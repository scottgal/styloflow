using System.Text.Json;
using Microsoft.AspNetCore.Http;
using StyloFlow.WorkflowBuilder.Models;
using StyloFlow.WorkflowBuilder.Runtime;
using StyloFlow.WorkflowBuilder.Services;

namespace StyloFlow.WorkflowBuilder.Middleware;

public class WorkflowBuilderMiddleware
{
    private readonly RequestDelegate _next;
    private readonly string _basePath;
    private readonly ManifestService _manifestService;
    private readonly IWorkflowStore _workflowStore;
    private readonly WorkflowValidator _validator;
    private readonly WorkflowOrchestrator _orchestrator;
    private readonly WorkflowStorage _storage;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    public WorkflowBuilderMiddleware(
        RequestDelegate next,
        string basePath,
        ManifestService manifestService,
        IWorkflowStore workflowStore,
        WorkflowValidator validator,
        WorkflowOrchestrator orchestrator,
        WorkflowStorage storage)
    {
        _next = next;
        _basePath = basePath.TrimEnd('/');
        _manifestService = manifestService;
        _workflowStore = workflowStore;
        _validator = validator;
        _orchestrator = orchestrator;
        _storage = storage;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var path = context.Request.Path.Value ?? "";

        if (!path.StartsWith(_basePath, StringComparison.OrdinalIgnoreCase))
        {
            await _next(context);
            return;
        }

        var relativePath = path[_basePath.Length..].TrimStart('/');

        var handled = relativePath switch
        {
            "" or "index.html" => await HandleUI(context),
            "api/manifests" => await HandleGetManifests(context),
            "api/workflows" => await HandleWorkflows(context),
            "api/samples" => await HandleGetSamples(context),
            "api/execute" => await HandleExecute(context),
            "api/runs" => await HandleGetRuns(context),
            _ when relativePath.StartsWith("api/workflows/") => await HandleWorkflowById(context, relativePath),
            _ => false
        };

        if (!handled)
        {
            await _next(context);
        }
    }

    private async Task<bool> HandleUI(HttpContext context)
    {
        context.Response.ContentType = "text/html";
        await context.Response.WriteAsync(GenerateHtml());
        return true;
    }

    private async Task<bool> HandleGetManifests(HttpContext context)
    {
        if (context.Request.Method != "GET")
            return false;

        var summaries = _manifestService.GetAtomSummaries();
        context.Response.ContentType = "application/json";
        await JsonSerializer.SerializeAsync(context.Response.Body, summaries, JsonOptions);
        return true;
    }

    private async Task<bool> HandleGetSamples(HttpContext context)
    {
        if (context.Request.Method != "GET")
            return false;

        var samples = GetSampleWorkflows();
        context.Response.ContentType = "application/json";
        await JsonSerializer.SerializeAsync(context.Response.Body, samples, JsonOptions);
        return true;
    }

    private async Task<bool> HandleWorkflows(HttpContext context)
    {
        if (context.Request.Method == "GET")
        {
            var workflows = await _workflowStore.GetAllAsync();
            context.Response.ContentType = "application/json";
            await JsonSerializer.SerializeAsync(context.Response.Body, workflows, JsonOptions);
            return true;
        }

        if (context.Request.Method == "POST")
        {
            var workflow = await JsonSerializer.DeserializeAsync<WorkflowDefinition>(
                context.Request.Body, JsonOptions);

            if (workflow == null)
            {
                context.Response.StatusCode = 400;
                return true;
            }

            var saved = await _workflowStore.SaveAsync(workflow);
            context.Response.ContentType = "application/json";
            await JsonSerializer.SerializeAsync(context.Response.Body, saved, JsonOptions);
            return true;
        }

        return false;
    }

    private async Task<bool> HandleWorkflowById(HttpContext context, string relativePath)
    {
        var parts = relativePath.Split('/');
        if (parts.Length < 3)
            return false;

        var workflowId = parts[2];

        if (parts.Length == 4 && parts[3] == "validate")
        {
            return await HandleValidate(context, workflowId);
        }

        if (context.Request.Method == "GET")
        {
            var workflow = await _workflowStore.GetByIdAsync(workflowId);
            if (workflow == null)
            {
                context.Response.StatusCode = 404;
                return true;
            }

            context.Response.ContentType = "application/json";
            await JsonSerializer.SerializeAsync(context.Response.Body, workflow, JsonOptions);
            return true;
        }

        if (context.Request.Method == "DELETE")
        {
            var deleted = await _workflowStore.DeleteAsync(workflowId);
            context.Response.StatusCode = deleted ? 204 : 404;
            return true;
        }

        return false;
    }

    private async Task<bool> HandleValidate(HttpContext context, string workflowId)
    {
        if (context.Request.Method != "POST")
            return false;

        var workflow = await _workflowStore.GetByIdAsync(workflowId);
        if (workflow == null)
        {
            context.Response.StatusCode = 404;
            return true;
        }

        var result = _validator.Validate(workflow);
        context.Response.ContentType = "application/json";
        await JsonSerializer.SerializeAsync(context.Response.Body, result, JsonOptions);
        return true;
    }

    private async Task<bool> HandleExecute(HttpContext context)
    {
        if (context.Request.Method != "POST")
            return false;

        var request = await JsonSerializer.DeserializeAsync<ExecuteRequest>(context.Request.Body, JsonOptions);
        if (request == null)
        {
            context.Response.StatusCode = 400;
            return true;
        }

        // Get the workflow (from store or from request)
        WorkflowDefinition? workflow = null;
        if (!string.IsNullOrEmpty(request.WorkflowId))
        {
            workflow = await _workflowStore.GetByIdAsync(request.WorkflowId);
        }
        else if (request.Workflow != null)
        {
            workflow = request.Workflow;
        }

        if (workflow == null)
        {
            context.Response.StatusCode = 404;
            await context.Response.WriteAsync("Workflow not found");
            return true;
        }

        // Execute the workflow using the orchestrator
        // SignalR broadcasting is handled automatically by SignalRCoordinator via signal patterns
        var result = await _orchestrator.ExecuteAsync(workflow, request.Input);

        context.Response.ContentType = "application/json";
        await JsonSerializer.SerializeAsync(context.Response.Body, result, JsonOptions);
        return true;
    }

    private async Task<bool> HandleGetRuns(HttpContext context)
    {
        if (context.Request.Method != "GET")
            return false;

        var runs = await _storage.GetRecentRunsAsync();
        context.Response.ContentType = "application/json";
        await JsonSerializer.SerializeAsync(context.Response.Body, runs, JsonOptions);
        return true;
    }

    private List<WorkflowDefinition> GetSampleWorkflows()
    {
        return new List<WorkflowDefinition>
        {
            new WorkflowDefinition
            {
                Id = "sample-sentiment-pipeline",
                Name = "Sentiment Analysis Pipeline",
                Description = "Analyzes incoming text for sentiment and sends alerts for negative content",
                Nodes = new List<WorkflowNode>
                {
                    new() { Id = "1", ManifestName = "http-receiver", X = 50, Y = 200 },
                    new() { Id = "2", ManifestName = "text-analyzer", X = 350, Y = 200 },
                    new() { Id = "3", ManifestName = "sentiment-detector", X = 650, Y = 200 },
                    new() { Id = "4", ManifestName = "threshold-filter", X = 950, Y = 150 },
                    new() { Id = "5", ManifestName = "email-sender", X = 1250, Y = 100 },
                    new() { Id = "6", ManifestName = "log-writer", X = 950, Y = 350 }
                },
                Edges = new List<WorkflowEdge>
                {
                    new() { Id = "e1", SourceNodeId = "1", SignalKey = "http.body", TargetNodeId = "2" },
                    new() { Id = "e2", SourceNodeId = "2", SignalKey = "text.analyzed", TargetNodeId = "3" },
                    new() { Id = "e3", SourceNodeId = "3", SignalKey = "sentiment.score", TargetNodeId = "4" },
                    new() { Id = "e4", SourceNodeId = "4", SignalKey = "filter.passed", TargetNodeId = "5" },
                    new() { Id = "e5", SourceNodeId = "3", SignalKey = "sentiment.label", TargetNodeId = "6" }
                }
            },
            new WorkflowDefinition
            {
                Id = "sample-scheduled-analysis",
                Name = "Scheduled Text Processing",
                Description = "Timer-triggered text analysis with logging",
                Nodes = new List<WorkflowNode>
                {
                    new() { Id = "1", ManifestName = "timer-trigger", X = 50, Y = 200 },
                    new() { Id = "2", ManifestName = "text-analyzer", X = 350, Y = 200 },
                    new() { Id = "3", ManifestName = "log-writer", X = 650, Y = 200 }
                },
                Edges = new List<WorkflowEdge>
                {
                    new() { Id = "e1", SourceNodeId = "1", SignalKey = "timer.triggered", TargetNodeId = "2" },
                    new() { Id = "e2", SourceNodeId = "2", SignalKey = "text.word_count", TargetNodeId = "3" }
                }
            },
            new WorkflowDefinition
            {
                Id = "sample-webhook-logger",
                Name = "Webhook Logger",
                Description = "Simple webhook receiver that logs all incoming requests",
                Nodes = new List<WorkflowNode>
                {
                    new() { Id = "1", ManifestName = "http-receiver", X = 100, Y = 200 },
                    new() { Id = "2", ManifestName = "log-writer", X = 450, Y = 200 }
                },
                Edges = new List<WorkflowEdge>
                {
                    new() { Id = "e1", SourceNodeId = "1", SignalKey = "http.body", TargetNodeId = "2" }
                }
            }
        };
    }

    private string GenerateHtml()
    {
        return $@"<!DOCTYPE html>
<html lang=""en"" data-theme=""dark"">
<head>
    <meta charset=""UTF-8"">
    <meta name=""viewport"" content=""width=device-width, initial-scale=1.0"">
    <title>StyloFlow Workflow Builder</title>
    <link href=""https://cdn.jsdelivr.net/npm/daisyui@4.12.14/dist/full.min.css"" rel=""stylesheet"" />
    <script src=""https://cdn.tailwindcss.com""></script>
    <script src=""https://cdn.jsdelivr.net/npm/alpinejs@3.x.x/dist/cdn.min.js"" defer></script>
    <link rel=""stylesheet"" href=""https://cdn.jsdelivr.net/npm/drawflow@0.0.60/dist/drawflow.min.css"">
    <script src=""https://cdn.jsdelivr.net/npm/drawflow@0.0.60/dist/drawflow.min.js""></script>
    <style>
        /* Canvas styling */
        .drawflow {{
            background: #1a1a2e;
            background-image:
                radial-gradient(circle, #ffffff08 1px, transparent 1px);
            background-size: 20px 20px;
            height: 100%;
        }}

        /* Node base styling */
        .drawflow .drawflow-node {{
            background: linear-gradient(135deg, #16213e 0%, #1a1a2e 100%);
            border: 2px solid #0f3460;
            border-radius: 12px;
            min-width: 220px;
            box-shadow: 0 4px 20px rgba(0,0,0,0.4);
            transition: all 0.2s ease;
        }}

        .drawflow .drawflow-node:hover {{
            border-color: #e94560;
            box-shadow: 0 6px 30px rgba(233,69,96,0.3);
            transform: translateY(-2px);
        }}

        .drawflow .drawflow-node.selected {{
            border-color: #e94560;
            box-shadow: 0 0 20px rgba(233,69,96,0.5);
        }}

        /* Node header */
        .node-header {{
            padding: 10px 14px;
            border-radius: 10px 10px 0 0;
            font-weight: 700;
            font-size: 13px;
            letter-spacing: 0.5px;
            display: flex;
            align-items: center;
            gap: 8px;
        }}

        .node-header .icon {{
            width: 20px;
            height: 20px;
            display: flex;
            align-items: center;
            justify-content: center;
            font-size: 14px;
        }}

        /* Node body */
        .node-body {{
            padding: 12px 14px;
            font-size: 11px;
            color: #a0a0a0;
        }}

        /* Signal ports section */
        .signal-ports {{
            padding: 8px 14px 12px;
            display: flex;
            justify-content: space-between;
            gap: 16px;
        }}

        .port-group {{
            display: flex;
            flex-direction: column;
            gap: 4px;
        }}

        .port-group.inputs {{ align-items: flex-start; }}
        .port-group.outputs {{ align-items: flex-end; }}

        .port-label {{
            font-size: 9px;
            padding: 2px 6px;
            border-radius: 4px;
            font-family: 'Monaco', 'Menlo', monospace;
            white-space: nowrap;
        }}

        .port-label.input {{
            background: rgba(99, 102, 241, 0.2);
            color: #818cf8;
            border-left: 2px solid #6366f1;
        }}

        .port-label.output {{
            background: rgba(34, 197, 94, 0.2);
            color: #4ade80;
            border-right: 2px solid #22c55e;
        }}

        /* Connection ports */
        .drawflow .drawflow-node .input,
        .drawflow .drawflow-node .output {{
            width: 14px;
            height: 14px;
            border: 2px solid;
            border-radius: 50%;
            background: #1a1a2e;
            transition: all 0.2s ease;
        }}

        .drawflow .drawflow-node .input {{
            border-color: #6366f1;
            left: -7px;
        }}

        .drawflow .drawflow-node .input:hover {{
            background: #6366f1;
            box-shadow: 0 0 10px #6366f1;
        }}

        .drawflow .drawflow-node .output {{
            border-color: #22c55e;
            right: -7px;
        }}

        .drawflow .drawflow-node .output:hover {{
            background: #22c55e;
            box-shadow: 0 0 10px #22c55e;
        }}

        /* Connection lines */
        .drawflow .connection .main-path {{
            stroke: #22c55e;
            stroke-width: 3px;
            stroke-linecap: round;
            filter: drop-shadow(0 0 4px rgba(34, 197, 94, 0.5));
        }}

        .drawflow .connection .main-path:hover {{
            stroke: #4ade80;
            stroke-width: 4px;
        }}

        /* Kind-specific colors */
        .kind-sensor .node-header {{
            background: linear-gradient(135deg, #3b82f6 0%, #1d4ed8 100%);
            color: white;
        }}

        .kind-analyzer .node-header {{
            background: linear-gradient(135deg, #8b5cf6 0%, #6d28d9 100%);
            color: white;
        }}

        .kind-proposer .node-header {{
            background: linear-gradient(135deg, #f59e0b 0%, #d97706 100%);
            color: white;
        }}

        .kind-emitter .node-header {{
            background: linear-gradient(135deg, #ef4444 0%, #dc2626 100%);
            color: white;
        }}

        /* Palette styling */
        .palette-item {{
            cursor: grab;
            transition: all 0.2s ease;
        }}

        .palette-item:hover {{
            transform: translateX(4px);
        }}

        .palette-item:active {{
            cursor: grabbing;
        }}

        .palette-card {{
            background: linear-gradient(135deg, #16213e 0%, #1a1a2e 100%);
            border: 1px solid #0f3460;
            border-radius: 8px;
            overflow: hidden;
        }}

        .palette-card:hover {{
            border-color: #e94560;
        }}

        .palette-header {{
            padding: 8px 12px;
            font-weight: 600;
            font-size: 12px;
        }}

        .palette-body {{
            padding: 6px 12px 10px;
            font-size: 10px;
            color: #888;
        }}

        .palette-signals {{
            display: flex;
            flex-wrap: wrap;
            gap: 3px;
            margin-top: 6px;
        }}

        .signal-badge {{
            font-size: 8px;
            padding: 1px 4px;
            border-radius: 3px;
            font-family: monospace;
        }}

        .signal-badge.emits {{
            background: rgba(34, 197, 94, 0.2);
            color: #4ade80;
        }}

        .signal-badge.requires {{
            background: rgba(99, 102, 241, 0.2);
            color: #818cf8;
        }}

        /* Kind badges */
        .kind-badge {{
            font-size: 9px;
            padding: 2px 6px;
            border-radius: 4px;
            text-transform: uppercase;
            font-weight: 600;
            letter-spacing: 0.5px;
        }}

        .kind-badge.sensor {{ background: #3b82f6; color: white; }}
        .kind-badge.analyzer {{ background: #8b5cf6; color: white; }}
        .kind-badge.proposer {{ background: #f59e0b; color: white; }}
        .kind-badge.emitter {{ background: #ef4444; color: white; }}

        /* Scrollbar */
        ::-webkit-scrollbar {{ width: 6px; }}
        ::-webkit-scrollbar-track {{ background: #1a1a2e; }}
        ::-webkit-scrollbar-thumb {{ background: #0f3460; border-radius: 3px; }}
        ::-webkit-scrollbar-thumb:hover {{ background: #e94560; }}

        /* Sample workflow cards */
        .sample-card {{
            background: linear-gradient(135deg, #16213e 0%, #1a1a2e 100%);
            border: 1px solid #0f3460;
            border-radius: 8px;
            padding: 12px;
            cursor: pointer;
            transition: all 0.2s ease;
        }}

        .sample-card:hover {{
            border-color: #e94560;
            transform: translateY(-2px);
        }}
    </style>
</head>
<body class=""min-h-screen"" style=""background: #0f0f23;"">
    <div x-data=""workflowBuilder()"" x-init=""init()"" class=""h-screen flex flex-col"">
        <!-- Header -->
        <div class=""px-4 py-3 flex items-center justify-between"" style=""background: linear-gradient(90deg, #16213e 0%, #1a1a2e 100%); border-bottom: 1px solid #0f3460;"">
            <div class=""flex items-center gap-4"">
                <div class=""flex items-center gap-2"">
                    <div class=""w-8 h-8 rounded-lg flex items-center justify-center"" style=""background: linear-gradient(135deg, #e94560 0%, #d62850 100%);"">
                        <svg class=""w-5 h-5 text-white"" fill=""none"" stroke=""currentColor"" viewBox=""0 0 24 24"">
                            <path stroke-linecap=""round"" stroke-linejoin=""round"" stroke-width=""2"" d=""M13 10V3L4 14h7v7l9-11h-7z""/>
                        </svg>
                    </div>
                    <span class=""text-lg font-bold text-white"">StyloFlow</span>
                </div>
                <div class=""h-6 w-px bg-gray-700""></div>
                <input type=""text"" x-model=""workflowName""
                    class=""bg-transparent border-none text-white text-sm font-medium focus:outline-none focus:ring-0 w-64""
                    placeholder=""Untitled Workflow"">
            </div>
            <div class=""flex items-center gap-2"">
                <button class=""btn btn-sm btn-ghost text-gray-400 hover:text-white"" @click=""clearCanvas()"">
                    <svg class=""w-4 h-4"" fill=""none"" stroke=""currentColor"" viewBox=""0 0 24 24""><path stroke-linecap=""round"" stroke-linejoin=""round"" stroke-width=""2"" d=""M19 7l-.867 12.142A2 2 0 0116.138 21H7.862a2 2 0 01-1.995-1.858L5 7m5 4v6m4-6v6m1-10V4a1 1 0 00-1-1h-4a1 1 0 00-1 1v3M4 7h16""/></svg>
                    Clear
                </button>
                <button class=""btn btn-sm"" style=""background: #0f3460; border-color: #0f3460; color: white;"" @click=""validateWorkflow()"">
                    <svg class=""w-4 h-4"" fill=""none"" stroke=""currentColor"" viewBox=""0 0 24 24""><path stroke-linecap=""round"" stroke-linejoin=""round"" stroke-width=""2"" d=""M9 12l2 2 4-4m6 2a9 9 0 11-18 0 9 9 0 0118 0z""/></svg>
                    Validate
                </button>
                <button class=""btn btn-sm"" style=""background: #e94560; border-color: #e94560; color: white;"" @click=""saveWorkflow()"">
                    <svg class=""w-4 h-4"" fill=""none"" stroke=""currentColor"" viewBox=""0 0 24 24""><path stroke-linecap=""round"" stroke-linejoin=""round"" stroke-width=""2"" d=""M8 7H5a2 2 0 00-2 2v9a2 2 0 002 2h14a2 2 0 002-2V9a2 2 0 00-2-2h-3m-1 4l-3 3m0 0l-3-3m3 3V4""/></svg>
                    Save
                </button>
                <button class=""btn btn-sm btn-ghost text-gray-400 hover:text-white"" @click=""exportWorkflow()"">
                    <svg class=""w-4 h-4"" fill=""none"" stroke=""currentColor"" viewBox=""0 0 24 24""><path stroke-linecap=""round"" stroke-linejoin=""round"" stroke-width=""2"" d=""M12 10v6m0 0l-3-3m3 3l3-3m2 8H7a2 2 0 01-2-2V5a2 2 0 012-2h5.586a1 1 0 01.707.293l5.414 5.414a1 1 0 01.293.707V19a2 2 0 01-2 2z""/></svg>
                    Export
                </button>
                <div class=""h-6 w-px bg-gray-700""></div>
                <button class=""btn btn-sm"" :class=""isExecuting ? 'btn-disabled' : ''"" style=""background: #22c55e; border-color: #22c55e; color: white;"" @click=""executeWorkflow()"" :disabled=""isExecuting"">
                    <svg x-show=""!isExecuting"" class=""w-4 h-4"" fill=""none"" stroke=""currentColor"" viewBox=""0 0 24 24""><path stroke-linecap=""round"" stroke-linejoin=""round"" stroke-width=""2"" d=""M14.752 11.168l-3.197-2.132A1 1 0 0010 9.87v4.263a1 1 0 001.555.832l3.197-2.132a1 1 0 000-1.664z""/><path stroke-linecap=""round"" stroke-linejoin=""round"" stroke-width=""2"" d=""M21 12a9 9 0 11-18 0 9 9 0 0118 0z""/></svg>
                    <span x-show=""isExecuting"" class=""loading loading-spinner loading-xs""></span>
                    <span x-text=""isExecuting ? 'Running...' : 'Execute'""></span>
                </button>
                <button class=""btn btn-sm btn-ghost"" :class=""executionLogs.length > 0 ? 'text-green-400' : 'text-gray-400'"" @click=""showExecutionPanel = !showExecutionPanel"">
                    <svg class=""w-4 h-4"" fill=""none"" stroke=""currentColor"" viewBox=""0 0 24 24""><path stroke-linecap=""round"" stroke-linejoin=""round"" stroke-width=""2"" d=""M9 17v-2m3 2v-4m3 4v-6m2 10H7a2 2 0 01-2-2V5a2 2 0 012-2h5.586a1 1 0 01.707.293l5.414 5.414a1 1 0 01.293.707V19a2 2 0 01-2 2z""/></svg>
                    <span x-show=""executionLogs.length > 0"" class=""badge badge-xs badge-success"" x-text=""executionLogs.length""></span>
                </button>
            </div>
        </div>

        <!-- Main content -->
        <div class=""flex-1 flex overflow-hidden"">
            <!-- Left sidebar - Palette -->
            <div class=""w-72 overflow-y-auto p-4"" style=""background: #16213e; border-right: 1px solid #0f3460;"">
                <!-- Sample workflows -->
                <div class=""mb-6"">
                    <h3 class=""text-xs font-semibold uppercase tracking-wider text-gray-500 mb-3"">Sample Workflows</h3>
                    <div class=""space-y-2"">
                        <template x-for=""sample in samples"" :key=""sample.id"">
                            <div class=""sample-card"" @click=""loadSample(sample)"">
                                <div class=""font-semibold text-white text-sm"" x-text=""sample.name""></div>
                                <div class=""text-xs text-gray-500 mt-1"" x-text=""sample.description""></div>
                                <div class=""flex items-center gap-2 mt-2"">
                                    <span class=""text-xs text-gray-600"" x-text=""sample.nodes.length + ' nodes'""></span>
                                    <span class=""text-xs text-gray-600"">•</span>
                                    <span class=""text-xs text-gray-600"" x-text=""sample.edges.length + ' connections'""></span>
                                </div>
                            </div>
                        </template>
                    </div>
                </div>

                <div class=""h-px bg-gray-700 my-4""></div>

                <!-- Atoms palette -->
                <h3 class=""text-xs font-semibold uppercase tracking-wider text-gray-500 mb-3"">Atoms</h3>

                <template x-for=""kind in ['sensor', 'analyzer', 'proposer', 'emitter']"" :key=""kind"">
                    <div class=""mb-4"">
                        <div class=""flex items-center gap-2 mb-2"">
                            <span class=""kind-badge"" :class=""kind"" x-text=""kind""></span>
                            <span class=""text-xs text-gray-600"" x-text=""manifests.filter(m => m.kind === kind).length""></span>
                        </div>
                        <div class=""space-y-2"">
                            <template x-for=""atom in manifests.filter(m => m.kind === kind)"" :key=""atom.name"">
                                <div class=""palette-item palette-card""
                                     draggable=""true""
                                     @dragstart=""onDragStart($event, atom)"">
                                    <div class=""palette-header"" :class=""{{
                                        'bg-blue-600': atom.kind === 'sensor',
                                        'bg-purple-600': atom.kind === 'analyzer',
                                        'bg-amber-600': atom.kind === 'proposer',
                                        'bg-red-600': atom.kind === 'emitter'
                                    }}"">
                                        <span class=""text-white"" x-text=""atom.name""></span>
                                    </div>
                                    <div class=""palette-body"">
                                        <p x-text=""atom.description.substring(0, 60) + (atom.description.length > 60 ? '...' : '')""></p>
                                        <div class=""palette-signals"">
                                            <template x-for=""sig in atom.emittedSignals.slice(0, 2)"" :key=""sig"">
                                                <span class=""signal-badge emits"" x-text=""sig""></span>
                                            </template>
                                            <template x-for=""sig in atom.requiredSignals.slice(0, 2)"" :key=""sig"">
                                                <span class=""signal-badge requires"" x-text=""sig""></span>
                                            </template>
                                        </div>
                                    </div>
                                </div>
                            </template>
                        </div>
                    </div>
                </template>
            </div>

            <!-- Canvas -->
            <div class=""flex-1 relative""
                 @dragover.prevent
                 @drop=""onDrop($event)"">
                <div id=""drawflow"" class=""h-full""></div>

                <!-- Zoom controls -->
                <div class=""absolute bottom-4 right-4 flex flex-col gap-2"">
                    <button class=""btn btn-sm btn-circle"" style=""background: #16213e; border-color: #0f3460;"" @click=""zoomIn()"">
                        <svg class=""w-4 h-4 text-white"" fill=""none"" stroke=""currentColor"" viewBox=""0 0 24 24""><path stroke-linecap=""round"" stroke-linejoin=""round"" stroke-width=""2"" d=""M12 6v6m0 0v6m0-6h6m-6 0H6""/></svg>
                    </button>
                    <button class=""btn btn-sm btn-circle"" style=""background: #16213e; border-color: #0f3460;"" @click=""zoomOut()"">
                        <svg class=""w-4 h-4 text-white"" fill=""none"" stroke=""currentColor"" viewBox=""0 0 24 24""><path stroke-linecap=""round"" stroke-linejoin=""round"" stroke-width=""2"" d=""M20 12H4""/></svg>
                    </button>
                    <button class=""btn btn-sm btn-circle"" style=""background: #16213e; border-color: #0f3460;"" @click=""resetZoom()"">
                        <svg class=""w-4 h-4 text-white"" fill=""none"" stroke=""currentColor"" viewBox=""0 0 24 24""><path stroke-linecap=""round"" stroke-linejoin=""round"" stroke-width=""2"" d=""M4 8V4m0 0h4M4 4l5 5m11-1V4m0 0h-4m4 0l-5 5M4 16v4m0 0h4m-4 0l5-5m11 5l-5-5m5 5v-4m0 4h-4""/></svg>
                    </button>
                </div>
            </div>

            <!-- Right sidebar - Properties -->
            <div class=""w-80 overflow-y-auto p-4"" style=""background: #16213e; border-left: 1px solid #0f3460;"" x-show=""selectedNode"" x-transition>
                <h3 class=""text-xs font-semibold uppercase tracking-wider text-gray-500 mb-4"">Node Properties</h3>

                <template x-if=""selectedNode"">
                    <div>
                        <div class=""mb-4"">
                            <span class=""kind-badge"" :class=""selectedAtom?.kind"" x-text=""selectedAtom?.kind""></span>
                        </div>

                        <div class=""mb-4"">
                            <label class=""text-xs text-gray-500 block mb-1"">Name</label>
                            <div class=""text-white font-semibold"" x-text=""selectedNode.name""></div>
                        </div>

                        <div class=""mb-4"">
                            <label class=""text-xs text-gray-500 block mb-1"">Description</label>
                            <div class=""text-gray-400 text-sm"" x-text=""selectedAtom?.description""></div>
                        </div>

                        <div class=""mb-4"">
                            <label class=""text-xs text-gray-500 block mb-2"">Output Signals</label>
                            <div class=""space-y-1"">
                                <template x-for=""signal in selectedNode.emits"" :key=""signal"">
                                    <div class=""flex items-center gap-2 p-2 rounded"" style=""background: rgba(34, 197, 94, 0.1);"">
                                        <div class=""w-2 h-2 rounded-full bg-green-500""></div>
                                        <span class=""text-green-400 text-sm font-mono"" x-text=""signal""></span>
                                    </div>
                                </template>
                                <p x-show=""selectedNode.emits.length === 0"" class=""text-xs text-gray-600"">No outputs</p>
                            </div>
                        </div>

                        <div class=""mb-4"">
                            <label class=""text-xs text-gray-500 block mb-2"">Required Signals</label>
                            <div class=""space-y-1"">
                                <template x-for=""signal in selectedNode.requires"" :key=""signal"">
                                    <div class=""flex items-center gap-2 p-2 rounded"" style=""background: rgba(99, 102, 241, 0.1);"">
                                        <div class=""w-2 h-2 rounded-full bg-indigo-500""></div>
                                        <span class=""text-indigo-400 text-sm font-mono"" x-text=""signal""></span>
                                    </div>
                                </template>
                                <p x-show=""selectedNode.requires.length === 0"" class=""text-xs text-gray-600 italic"">Entry point - no inputs required</p>
                            </div>
                        </div>

                        <div class=""mb-4"">
                            <label class=""text-xs text-gray-500 block mb-2"">Tags</label>
                            <div class=""flex flex-wrap gap-1"">
                                <template x-for=""tag in selectedAtom?.tags || []"" :key=""tag"">
                                    <span class=""text-xs px-2 py-1 rounded"" style=""background: #0f3460; color: #888;"" x-text=""tag""></span>
                                </template>
                            </div>
                        </div>

                        <button class=""btn btn-sm btn-error w-full mt-4"" @click=""deleteSelectedNode()"">
                            <svg class=""w-4 h-4"" fill=""none"" stroke=""currentColor"" viewBox=""0 0 24 24""><path stroke-linecap=""round"" stroke-linejoin=""round"" stroke-width=""2"" d=""M19 7l-.867 12.142A2 2 0 0116.138 21H7.862a2 2 0 01-1.995-1.858L5 7m5 4v6m4-6v6m1-10V4a1 1 0 00-1-1h-4a1 1 0 00-1 1v3M4 7h16""/></svg>
                            Delete Node
                        </button>
                    </div>
                </template>
            </div>
        </div>

        <!-- Execution Panel (Bottom drawer) -->
        <div class=""fixed bottom-0 left-0 right-0 z-50 transition-transform duration-300""
             :class=""showExecutionPanel ? 'translate-y-0' : 'translate-y-full'""
             style=""height: 320px; background: linear-gradient(180deg, #16213e 0%, #1a1a2e 100%); border-top: 2px solid #0f3460;"">
            <div class=""flex items-center justify-between px-4 py-2 border-b border-gray-700"">
                <div class=""flex items-center gap-3"">
                    <h3 class=""text-sm font-semibold text-white"">Execution Output</h3>
                    <span x-show=""currentRunId"" class=""text-xs text-gray-500 font-mono"" x-text=""'Run: ' + currentRunId""></span>
                    <span x-show=""isExecuting"" class=""flex items-center gap-1 text-xs text-green-400"">
                        <span class=""loading loading-dots loading-xs""></span>
                        Running
                    </span>
                    <span x-show=""executionStatus === 'completed'"" class=""badge badge-success badge-sm"">Completed</span>
                    <span x-show=""executionStatus === 'failed'"" class=""badge badge-error badge-sm"">Failed</span>
                </div>
                <div class=""flex items-center gap-2"">
                    <button class=""btn btn-xs btn-ghost text-gray-400"" @click=""executionLogs = []; signalEvents = []"">Clear</button>
                    <button class=""btn btn-xs btn-ghost text-gray-400"" @click=""showExecutionPanel = false"">
                        <svg class=""w-4 h-4"" fill=""none"" stroke=""currentColor"" viewBox=""0 0 24 24""><path stroke-linecap=""round"" stroke-linejoin=""round"" stroke-width=""2"" d=""M19 9l-7 7-7-7""/></svg>
                    </button>
                </div>
            </div>
            <div class=""flex h-full"" style=""height: calc(100% - 44px);"">
                <!-- Log output -->
                <div class=""flex-1 overflow-y-auto p-3 font-mono text-xs"" x-ref=""logContainer"">
                    <template x-if=""executionLogs.length === 0"">
                        <div class=""text-gray-600 text-center py-8"">
                            <svg class=""w-12 h-12 mx-auto mb-2 opacity-30"" fill=""none"" stroke=""currentColor"" viewBox=""0 0 24 24""><path stroke-linecap=""round"" stroke-linejoin=""round"" stroke-width=""2"" d=""M9 17v-2m3 2v-4m3 4v-6m2 10H7a2 2 0 01-2-2V5a2 2 0 012-2h5.586a1 1 0 01.707.293l5.414 5.414a1 1 0 01.293.707V19a2 2 0 01-2 2z""/></svg>
                            No execution logs yet. Click Execute to run the workflow.
                        </div>
                    </template>
                    <template x-for=""(log, index) in executionLogs"" :key=""index"">
                        <div class=""flex items-start gap-2 py-1 border-b border-gray-800"">
                            <span class=""text-gray-600 shrink-0"" x-text=""log.timestamp""></span>
                            <span class=""px-1.5 py-0.5 rounded text-xs shrink-0""
                                  :class=""{{
                                      'bg-blue-900/50 text-blue-300': log.nodeId === 'system',
                                      'bg-green-900/50 text-green-300': log.type === 'complete',
                                      'bg-red-900/50 text-red-300': log.type === 'error',
                                      'bg-purple-900/50 text-purple-300': log.nodeId !== 'system' && log.type !== 'complete' && log.type !== 'error'
                                  }}""
                                  x-text=""log.nodeId""></span>
                            <span class=""text-gray-300"" x-text=""log.message""></span>
                        </div>
                    </template>
                </div>
                <!-- Signal events -->
                <div class=""w-72 border-l border-gray-700 overflow-y-auto p-3"">
                    <h4 class=""text-xs font-semibold text-gray-500 uppercase mb-2"">Signals Emitted</h4>
                    <template x-if=""signalEvents.length === 0"">
                        <div class=""text-gray-600 text-xs"">No signals emitted</div>
                    </template>
                    <template x-for=""(sig, index) in signalEvents"" :key=""index"">
                        <div class=""flex items-center gap-2 py-1"">
                            <div class=""w-2 h-2 rounded-full"" :class=""sig.confidence > 0.7 ? 'bg-green-500' : 'bg-yellow-500'""></div>
                            <span class=""text-green-400 font-mono text-xs"" x-text=""sig.key""></span>
                            <span class=""text-gray-500 text-xs"" x-text=""sig.sourceNode""></span>
                        </div>
                    </template>
                </div>
            </div>
        </div>

        <!-- Toast notifications -->
        <div class=""toast toast-end toast-bottom"" x-show=""toast.show"" x-transition
            <div class=""alert"" :class=""toast.type === 'success' ? 'alert-success' : toast.type === 'error' ? 'alert-error' : 'alert-info'"">
                <span x-text=""toast.message""></span>
            </div>
        </div>
    </div>

    <script>
        const basePath = '{_basePath}';

        function workflowBuilder() {{
            return {{
                editor: null,
                manifests: [],
                samples: [],
                savedWorkflows: [],
                workflowId: crypto.randomUUID(),
                workflowName: 'Untitled Workflow',
                selectedNode: null,
                selectedAtom: null,
                nodeCounter: 0,
                toast: {{ show: false, message: '', type: 'info' }},

                async init() {{
                    const container = document.getElementById('drawflow');
                    this.editor = new Drawflow(container);
                    this.editor.reroute = true;
                    this.editor.curvature = 0.5;
                    this.editor.reroute_curvature_start_end = 0.5;
                    this.editor.reroute_curvature = 0.5;
                    this.editor.force_first_input = false;
                    this.editor.start();

                    this.editor.on('nodeSelected', (nodeId) => this.onNodeSelected(nodeId));
                    this.editor.on('nodeUnselected', () => {{ this.selectedNode = null; this.selectedAtom = null; }});
                    this.editor.on('nodeRemoved', (nodeId) => this.onNodeRemoved(nodeId));

                    await this.loadManifests();
                    await this.loadSamples();

                    // Auto-load first sample
                    if (this.samples.length > 0) {{
                        setTimeout(() => this.loadSample(this.samples[0]), 500);
                    }}
                }},

                async loadManifests() {{
                    const res = await fetch(`${{basePath}}/api/manifests`);
                    this.manifests = await res.json();
                }},

                async loadSamples() {{
                    const res = await fetch(`${{basePath}}/api/samples`);
                    this.samples = await res.json();
                }},

                onDragStart(event, atom) {{
                    event.dataTransfer.setData('application/json', JSON.stringify(atom));
                }},

                onDrop(event) {{
                    event.preventDefault();
                    const atomData = JSON.parse(event.dataTransfer.getData('application/json'));
                    const rect = document.getElementById('drawflow').getBoundingClientRect();
                    const x = event.clientX - rect.left;
                    const y = event.clientY - rect.top;
                    this.addNode(atomData, x, y);
                }},

                getNodeHtml(atom) {{
                    const inputSignals = atom.requiredSignals.slice(0, 3).map(s =>
                        `<div class=""port-label input"">${{s}}</div>`
                    ).join('');

                    const outputSignals = atom.emittedSignals.slice(0, 3).map(s =>
                        `<div class=""port-label output"">${{s}}</div>`
                    ).join('');

                    return `
                        <div class=""kind-${{atom.kind}}"">
                            <div class=""node-header"">
                                <span class=""icon"">${{this.getKindIcon(atom.kind)}}</span>
                                <span>${{atom.name}}</span>
                            </div>
                            <div class=""node-body"">${{atom.description.substring(0, 40)}}${{atom.description.length > 40 ? '...' : ''}}</div>
                            <div class=""signal-ports"">
                                <div class=""port-group inputs"">${{inputSignals || '<div class=""port-label input"" style=""opacity:0.3"">entry</div>'}}</div>
                                <div class=""port-group outputs"">${{outputSignals}}</div>
                            </div>
                        </div>
                    `;
                }},

                getKindIcon(kind) {{
                    const icons = {{
                        sensor: '📡',
                        analyzer: '🔬',
                        proposer: '⚖️',
                        emitter: '📤'
                    }};
                    return icons[kind] || '⚡';
                }},

                addNode(atom, x, y) {{
                    const nodeId = ++this.nodeCounter;
                    const inputs = atom.requiredSignals.length > 0 || atom.kind !== 'sensor' ? 1 : 0;
                    const outputs = atom.emittedSignals.length > 0 ? 1 : 0;

                    const editorX = (x - this.editor.precanvas.getBoundingClientRect().left + this.editor.canvas_x) / this.editor.zoom;
                    const editorY = (y - this.editor.precanvas.getBoundingClientRect().top + this.editor.canvas_y) / this.editor.zoom;

                    this.editor.addNode(
                        atom.name,
                        inputs,
                        outputs,
                        editorX,
                        editorY,
                        `kind-${{atom.kind}}`,
                        {{ manifest: atom.name, emits: atom.emittedSignals, requires: atom.requiredSignals }},
                        this.getNodeHtml(atom)
                    );
                }},

                onNodeSelected(nodeId) {{
                    const nodeData = this.editor.getNodeFromId(nodeId);
                    if (nodeData) {{
                        this.selectedNode = {{
                            id: nodeId,
                            name: nodeData.name,
                            emits: nodeData.data.emits || [],
                            requires: nodeData.data.requires || []
                        }};
                        this.selectedAtom = this.manifests.find(m => m.name === nodeData.name);
                    }}
                }},

                onNodeRemoved(nodeId) {{
                    if (this.selectedNode?.id === nodeId) {{
                        this.selectedNode = null;
                        this.selectedAtom = null;
                    }}
                }},

                deleteSelectedNode() {{
                    if (this.selectedNode) {{
                        this.editor.removeNodeId(`node-${{this.selectedNode.id}}`);
                        this.selectedNode = null;
                        this.selectedAtom = null;
                    }}
                }},

                clearCanvas() {{
                    this.editor.clear();
                    this.nodeCounter = 0;
                    this.selectedNode = null;
                    this.selectedAtom = null;
                    this.workflowId = crypto.randomUUID();
                    this.workflowName = 'Untitled Workflow';
                }},

                loadSample(sample) {{
                    this.editor.clear();
                    this.nodeCounter = 0;
                    this.workflowId = sample.id + '-' + Date.now();
                    this.workflowName = sample.name;

                    const nodeIdMap = {{}};
                    for (const node of sample.nodes) {{
                        const atom = this.manifests.find(m => m.name === node.manifestName);
                        if (!atom) continue;

                        const inputs = atom.requiredSignals.length > 0 || atom.kind !== 'sensor' ? 1 : 0;
                        const outputs = atom.emittedSignals.length > 0 ? 1 : 0;

                        const newId = this.editor.addNode(
                            atom.name,
                            inputs,
                            outputs,
                            node.x,
                            node.y,
                            `kind-${{atom.kind}}`,
                            {{ manifest: atom.name, emits: atom.emittedSignals, requires: atom.requiredSignals }},
                            this.getNodeHtml(atom)
                        );

                        nodeIdMap[node.id] = newId;
                        this.nodeCounter = Math.max(this.nodeCounter, newId);
                    }}

                    for (const edge of sample.edges) {{
                        const sourceId = nodeIdMap[edge.sourceNodeId];
                        const targetId = nodeIdMap[edge.targetNodeId];
                        if (sourceId && targetId) {{
                            this.editor.addConnection(sourceId, targetId, 'output_1', 'input_1');
                        }}
                    }}

                    this.showToast('Loaded: ' + sample.name, 'success');
                }},

                getWorkflowData() {{
                    const exportData = this.editor.export();
                    const nodes = [];
                    const edges = [];

                    for (const [moduleKey, module] of Object.entries(exportData.drawflow)) {{
                        for (const [nodeId, node] of Object.entries(module.data)) {{
                            nodes.push({{
                                id: nodeId,
                                manifestName: node.name,
                                x: node.pos_x,
                                y: node.pos_y,
                                config: {{}}
                            }});

                            for (const [outputKey, output] of Object.entries(node.outputs)) {{
                                for (const conn of output.connections) {{
                                    const emits = node.data.emits || [];
                                    edges.push({{
                                        id: `${{nodeId}}-${{conn.node}}-${{outputKey}}`,
                                        sourceNodeId: nodeId,
                                        signalKey: emits[0] || 'signal',
                                        targetNodeId: conn.node
                                    }});
                                }}
                            }}
                        }}
                    }}

                    return {{ id: this.workflowId, name: this.workflowName, nodes, edges }};
                }},

                async saveWorkflow() {{
                    const workflow = this.getWorkflowData();
                    const res = await fetch(`${{basePath}}/api/workflows`, {{
                        method: 'POST',
                        headers: {{ 'Content-Type': 'application/json' }},
                        body: JSON.stringify(workflow)
                    }});

                    if (res.ok) {{
                        this.showToast('Workflow saved!', 'success');
                    }} else {{
                        this.showToast('Failed to save', 'error');
                    }}
                }},

                async validateWorkflow() {{
                    const workflow = this.getWorkflowData();
                    await fetch(`${{basePath}}/api/workflows`, {{
                        method: 'POST',
                        headers: {{ 'Content-Type': 'application/json' }},
                        body: JSON.stringify(workflow)
                    }});

                    const res = await fetch(`${{basePath}}/api/workflows/${{this.workflowId}}/validate`, {{ method: 'POST' }});

                    if (res.ok) {{
                        const result = await res.json();
                        if (result.isValid) {{
                            this.showToast('Workflow is valid!', 'success');
                        }} else {{
                            this.showToast(`${{result.issues.length}} validation issue(s)`, 'error');
                        }}
                    }}
                }},

                exportWorkflow() {{
                    const workflow = this.getWorkflowData();
                    const blob = new Blob([JSON.stringify(workflow, null, 2)], {{ type: 'application/json' }});
                    const url = URL.createObjectURL(blob);
                    const a = document.createElement('a');
                    a.href = url;
                    a.download = `${{this.workflowName.replace(/\\s+/g, '-').toLowerCase()}}.json`;
                    a.click();
                    URL.revokeObjectURL(url);
                    this.showToast('Exported!', 'success');
                }},

                showToast(message, type = 'info') {{
                    this.toast = {{ show: true, message, type }};
                    setTimeout(() => this.toast.show = false, 3000);
                }},

                zoomIn() {{ this.editor.zoom_in(); }},
                zoomOut() {{ this.editor.zoom_out(); }},
                resetZoom() {{ this.editor.zoom_reset(); }}
            }};
        }}
    </script>
</body>
</html>";
    }
}
