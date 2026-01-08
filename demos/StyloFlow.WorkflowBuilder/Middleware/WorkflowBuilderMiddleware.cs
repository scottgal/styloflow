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
            },
            // Coordinator composition demo - shows how atoms can spawn coordinators
            new WorkflowDefinition
            {
                Id = "sample-coordinator-composition",
                Name = "Coordinator Composition",
                Description = "Demonstrates single-keyed concurrency: HTTP requests spawn per-user coordinators",
                Nodes = new List<WorkflowNode>
                {
                    // Entry point - receives HTTP requests
                    new() { Id = "1", ManifestName = "http-receiver", X = 50, Y = 200, Config = new() { ["description"] = "Receives user requests" } },
                    // Spawns a coordinator per unique user-id
                    new() { Id = "2", ManifestName = "threshold-filter", X = 350, Y = 200, Config = new() { ["description"] = "Routes by user-id (single-key coordinator)" } },
                    // Parallel processing branch A
                    new() { Id = "3", ManifestName = "text-analyzer", X = 650, Y = 100, Config = new() { ["description"] = "Process text (keyed by user)" } },
                    // Parallel processing branch B
                    new() { Id = "4", ManifestName = "sentiment-detector", X = 650, Y = 300, Config = new() { ["description"] = "Detect sentiment (keyed by user)" } },
                    // Merge results
                    new() { Id = "5", ManifestName = "log-writer", X = 950, Y = 200, Config = new() { ["description"] = "Aggregate results per user" } }
                },
                Edges = new List<WorkflowEdge>
                {
                    new() { Id = "e1", SourceNodeId = "1", SignalKey = "http.body", TargetNodeId = "2" },
                    // Fan-out: coordinator spawns parallel atoms
                    new() { Id = "e2", SourceNodeId = "2", SignalKey = "filter.passed", TargetNodeId = "3" },
                    new() { Id = "e3", SourceNodeId = "2", SignalKey = "filter.passed", TargetNodeId = "4" },
                    // Fan-in: results merge back
                    new() { Id = "e4", SourceNodeId = "3", SignalKey = "text.analyzed", TargetNodeId = "5" },
                    new() { Id = "e5", SourceNodeId = "4", SignalKey = "sentiment.score", TargetNodeId = "5" }
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
    <script src=""https://cdn.jsdelivr.net/npm/@microsoft/signalr@8.0.0/dist/browser/signalr.min.js""></script>
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
            border-color: #4a6fa5;
            box-shadow: 0 6px 24px rgba(74, 111, 165, 0.25);
            transform: translateY(-2px);
        }}

        .drawflow .drawflow-node.selected {{
            border-color: #06b6d4;
            box-shadow: 0 0 16px rgba(6, 182, 212, 0.4);
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
            padding: 3px 8px 3px 6px;
            font-family: 'Monaco', 'Menlo', monospace;
            white-space: nowrap;
            cursor: grab;
            transition: all 0.15s ease;
            user-select: none;
            position: relative;
            display: flex;
            align-items: center;
            gap: 4px;
        }}

        .port-label::before {{
            content: '';
            display: inline-block;
            width: 0;
            height: 0;
            transition: all 0.15s ease;
        }}

        .port-label:hover {{
            transform: translateX(3px);
            box-shadow: 0 2px 8px currentColor;
        }}

        .port-label:active {{
            cursor: grabbing;
            transform: scale(0.95);
        }}

        .port-label.dragging {{
            opacity: 0.7;
            transform: scale(0.9) translateX(5px);
            box-shadow: 0 0 12px currentColor;
        }}

        /* Input signals - arrow pointing into the node (left side tab) */
        .port-label.input {{
            background: linear-gradient(90deg, transparent 0px, rgba(99, 102, 241, 0.3) 8px);
            color: #a5b4fc;
            border-radius: 0 6px 6px 0;
            margin-left: -8px;
            padding-left: 12px;
        }}

        .port-label.input::before {{
            content: '◂';
            color: #6366f1;
            font-size: 10px;
            margin-right: 2px;
        }}

        .port-label.input:hover {{
            background: linear-gradient(90deg, transparent 0px, rgba(99, 102, 241, 0.5) 8px);
            transform: translateX(-3px);
        }}

        /* Output signals - arrow pointing out of the node (right side tab) */
        .port-label.output {{
            background: linear-gradient(270deg, transparent 0px, rgba(34, 197, 94, 0.3) 8px);
            color: #86efac;
            border-radius: 6px 0 0 6px;
            margin-right: -8px;
            padding-right: 12px;
            flex-direction: row-reverse;
        }}

        .port-label.output::before {{
            content: '▸';
            color: #22c55e;
            font-size: 10px;
            margin-left: 2px;
        }}

        .port-label.output:hover {{
            background: linear-gradient(270deg, transparent 0px, rgba(34, 197, 94, 0.5) 8px);
            transform: translateX(3px);
        }}

        /* Data type color coding for signals */
        .port-label[data-signal*=""string""], .port-label[data-signal*=""text""], .port-label[data-signal*=""body""], .port-label[data-signal*=""label""] {{
            border-bottom: 2px solid #3b82f6;  /* blue for strings */
        }}

        .port-label[data-signal*=""score""], .port-label[data-signal*=""count""], .port-label[data-signal*=""value""], .port-label[data-signal*=""number""] {{
            border-bottom: 2px solid #22c55e;  /* green for numbers */
        }}

        .port-label[data-signal*=""passed""], .port-label[data-signal*=""exceeded""], .port-label[data-signal*=""bool""], .port-label[data-signal*=""is_""] {{
            border-bottom: 2px solid #a855f7;  /* purple for booleans */
        }}

        .port-label[data-signal*=""received""], .port-label[data-signal*=""triggered""], .port-label[data-signal*=""record""], .port-label[data-signal*=""document""] {{
            border-bottom: 2px solid #f59e0b;  /* amber for objects/events */
        }}

        .port-label[data-signal*=""config""], .port-label[data-signal*=""secret""] {{
            border-bottom: 2px solid #84cc16;  /* lime for config */
        }}

        /* Untyped / object signals - grey */
        .port-label:not([data-signal*=""string""]):not([data-signal*=""text""]):not([data-signal*=""body""]):not([data-signal*=""label""]):not([data-signal*=""score""]):not([data-signal*=""count""]):not([data-signal*=""value""]):not([data-signal*=""number""]):not([data-signal*=""passed""]):not([data-signal*=""exceeded""]):not([data-signal*=""bool""]):not([data-signal*=""is_""]):not([data-signal*=""received""]):not([data-signal*=""triggered""]):not([data-signal*=""record""]):not([data-signal*=""document""]):not([data-signal*=""config""]):not([data-signal*=""secret""]) {{
            border-bottom: 2px solid #6b7280;  /* grey for untyped/object */
        }}

        /* Signal connection line while dragging */
        .signal-drag-line {{
            position: fixed;
            pointer-events: none;
            z-index: 9999;
        }}

        .signal-drag-line line {{
            stroke: #22c55e;
            stroke-width: 3;
            stroke-dasharray: 8, 4;
            animation: dash-flow 0.5s linear infinite;
        }}

        @keyframes dash-flow {{
            from {{ stroke-dashoffset: 12; }}
            to {{ stroke-dashoffset: 0; }}
        }}

        /* Drop target highlighting for signals - subtle fill changes */
        .port-label.drop-target-valid {{
            background: rgba(34, 197, 94, 0.35) !important;
            border-color: #22c55e !important;
        }}

        .port-label.drop-target-adaptable {{
            background: rgba(245, 158, 11, 0.35) !important;
            border-color: #f59e0b !important;
        }}

        .port-label.drop-target-invalid {{
            background: rgba(100, 100, 100, 0.2) !important;
            opacity: 0.4;
        }}

        .port-label.drop-hover {{
            background: rgba(34, 197, 94, 0.5) !important;
            transform: scale(1.05);
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

        /* Connection lines - base styling */
        .drawflow .connection .main-path {{
            stroke: #6b7280;
            stroke-width: 3px;
            stroke-linecap: round;
            filter: drop-shadow(0 0 4px rgba(107, 114, 128, 0.4));
            transition: stroke 0.2s ease, stroke-width 0.2s ease;
        }}

        .drawflow .connection .main-path:hover {{
            stroke-width: 4px;
            filter: drop-shadow(0 0 8px currentColor);
        }}

        /* Signal type colored connections */
        .drawflow .connection.signal-type-string .main-path {{
            stroke: #3b82f6;
            filter: drop-shadow(0 0 4px rgba(59, 130, 246, 0.5));
        }}

        .drawflow .connection.signal-type-number .main-path {{
            stroke: #22c55e;
            filter: drop-shadow(0 0 4px rgba(34, 197, 94, 0.5));
        }}

        .drawflow .connection.signal-type-boolean .main-path {{
            stroke: #a855f7;
            filter: drop-shadow(0 0 4px rgba(168, 85, 247, 0.5));
        }}

        .drawflow .connection.signal-type-object .main-path {{
            stroke: #f59e0b;
            filter: drop-shadow(0 0 4px rgba(245, 158, 11, 0.5));
        }}

        .drawflow .connection.signal-type-config .main-path {{
            stroke: #84cc16;
            filter: drop-shadow(0 0 4px rgba(132, 204, 22, 0.5));
        }}

        .drawflow .connection.signal-type-any .main-path {{
            stroke: #6b7280;
            filter: drop-shadow(0 0 4px rgba(107, 114, 128, 0.4));
        }}

        /* Signal-matched connection (compatible signals) */
        .drawflow .connection.signal-matched .main-path {{
            stroke: #22c55e;
            stroke-width: 3px;
            filter: drop-shadow(0 0 8px rgba(34, 197, 94, 0.8));
        }}

        /* Signal-any connection (target accepts any) */
        .drawflow .connection.signal-any .main-path {{
            stroke: #8b5cf6;
            stroke-width: 3px;
            filter: drop-shadow(0 0 6px rgba(139, 92, 246, 0.6));
        }}

        /* Signal-warning connection (incompatible) */
        .drawflow .connection.signal-warning .main-path {{
            stroke: #ef4444;
            stroke-width: 3px;
            stroke-dasharray: 8, 4;
            filter: drop-shadow(0 0 6px rgba(239, 68, 68, 0.6));
        }}

        /* Signal-adaptable connection (needs adapter) */
        .drawflow .connection.signal-adaptable .main-path {{
            stroke: #f59e0b;
            stroke-width: 3px;
            stroke-dasharray: 12, 4;
            filter: drop-shadow(0 0 6px rgba(245, 158, 11, 0.6));
            cursor: pointer;
            animation: pulse-adaptable 2s infinite;
        }}

        @keyframes pulse-adaptable {{
            0%, 100% {{ filter: drop-shadow(0 0 6px rgba(245, 158, 11, 0.6)); }}
            50% {{ filter: drop-shadow(0 0 12px rgba(245, 158, 11, 0.9)); }}
        }}

        .drawflow .connection.signal-adaptable:hover .main-path {{
            stroke: #fbbf24;
            stroke-width: 4px;
        }}

        /* Inhibitor connection (blocks when signal fires) */
        .drawflow .connection.signal-inhibitor .main-path {{
            stroke: #dc2626;
            stroke-width: 2px;
            stroke-dasharray: 4, 4;
            filter: drop-shadow(0 0 4px rgba(220, 38, 38, 0.6));
        }}

        .drawflow .connection.signal-inhibitor::after {{
            content: '⊘';
            position: absolute;
            color: #dc2626;
            font-size: 16px;
        }}

        /* Context menu for inhibitors */
        .context-menu {{
            position: fixed;
            background: linear-gradient(135deg, #16213e 0%, #1a1a2e 100%);
            border: 1px solid #0f3460;
            border-radius: 8px;
            padding: 4px 0;
            min-width: 180px;
            z-index: 1000;
            box-shadow: 0 4px 20px rgba(0,0,0,0.5);
        }}

        .context-menu-item {{
            padding: 8px 16px;
            cursor: pointer;
            display: flex;
            align-items: center;
            gap: 8px;
            font-size: 13px;
            color: #e0e0e0;
        }}

        .context-menu-item:hover {{
            background: rgba(233, 69, 96, 0.2);
        }}

        .context-menu-item.danger {{
            color: #ef4444;
        }}

        .context-menu-divider {{
            height: 1px;
            background: #0f3460;
            margin: 4px 0;
        }}

        /* Adapter Panel Popup */
        .adapter-panel {{
            position: fixed;
            background: linear-gradient(135deg, #16213e 0%, #1a1a2e 100%);
            border: 2px solid #f59e0b;
            border-radius: 12px;
            padding: 16px;
            min-width: 280px;
            max-width: 360px;
            z-index: 1001;
            box-shadow: 0 8px 32px rgba(245, 158, 11, 0.3);
        }}

        .adapter-panel-header {{
            display: flex;
            align-items: center;
            gap: 8px;
            margin-bottom: 12px;
            padding-bottom: 8px;
            border-bottom: 1px solid #0f3460;
        }}

        .adapter-panel-header h4 {{
            margin: 0;
            color: #f59e0b;
            font-size: 14px;
            font-weight: 600;
        }}

        .adapter-panel-body {{
            margin-bottom: 12px;
        }}

        .adapter-signal-pair {{
            display: flex;
            align-items: center;
            justify-content: space-between;
            gap: 12px;
            padding: 8px;
            background: rgba(0,0,0,0.2);
            border-radius: 8px;
            margin-bottom: 8px;
        }}

        .adapter-signal {{
            font-family: 'Monaco', 'Menlo', monospace;
            font-size: 11px;
            padding: 4px 8px;
            border-radius: 4px;
        }}

        .adapter-signal.source {{
            background: rgba(34, 197, 94, 0.2);
            color: #4ade80;
        }}

        .adapter-signal.target {{
            background: rgba(99, 102, 241, 0.2);
            color: #818cf8;
        }}

        .adapter-arrow {{
            color: #f59e0b;
            font-size: 16px;
        }}

        .adapter-type-info {{
            display: flex;
            align-items: center;
            gap: 8px;
            font-size: 11px;
            color: #888;
            margin-top: 8px;
        }}

        .adapter-type-badge {{
            padding: 2px 6px;
            border-radius: 4px;
            font-size: 10px;
            font-weight: 600;
        }}

        .adapter-type-badge.string {{ background: #3b82f6; color: white; }}
        .adapter-type-badge.number {{ background: #22c55e; color: white; }}
        .adapter-type-badge.boolean {{ background: #8b5cf6; color: white; }}
        .adapter-type-badge.object {{ background: #f59e0b; color: white; }}

        .adapter-options {{
            display: flex;
            flex-direction: column;
            gap: 6px;
            margin-top: 8px;
        }}

        .adapter-option {{
            display: flex;
            align-items: center;
            gap: 8px;
            padding: 8px;
            background: rgba(0,0,0,0.15);
            border: 1px solid #0f3460;
            border-radius: 6px;
            cursor: pointer;
            transition: all 0.2s ease;
        }}

        .adapter-option:hover {{
            background: rgba(245, 158, 11, 0.1);
            border-color: #f59e0b;
        }}

        .adapter-option.selected {{
            background: rgba(245, 158, 11, 0.2);
            border-color: #f59e0b;
        }}

        .adapter-option-icon {{
            font-size: 16px;
        }}

        .adapter-option-info {{
            flex: 1;
        }}

        .adapter-option-name {{
            font-size: 12px;
            font-weight: 600;
            color: #e0e0e0;
        }}

        .adapter-option-desc {{
            font-size: 10px;
            color: #888;
        }}

        .adapter-panel-footer {{
            display: flex;
            gap: 8px;
            justify-content: flex-end;
            padding-top: 12px;
            border-top: 1px solid #0f3460;
        }}

        /* Inhibitor target highlighting */
        .drawflow .drawflow-node.inhibitor-target {{
            border-color: #dc2626 !important;
            animation: pulse-inhibitor 1s infinite;
            cursor: crosshair;
        }}

        @keyframes pulse-inhibitor {{
            0%, 100% {{ box-shadow: 0 0 10px rgba(220, 38, 38, 0.4); }}
            50% {{ box-shadow: 0 0 20px rgba(220, 38, 38, 0.8); }}
        }}

        /* Coordinator node styling */
        .kind-coordinator .node-header {{
            background: linear-gradient(135deg, #22c55e 0%, #16a34a 100%);
            color: white;
        }}

        .kind-adapter .node-header {{
            background: linear-gradient(135deg, #f59e0b 0%, #d97706 100%);
            color: white;
        }}

        /* Target highlight during connection */
        .drawflow .drawflow-node.connection-target-valid {{
            border-color: #22c55e !important;
            box-shadow: 0 0 20px rgba(34, 197, 94, 0.5) !important;
        }}

        .drawflow .drawflow-node.connection-target-invalid {{
            border-color: #ef4444 !important;
            opacity: 0.5;
        }}

        /* Zoom level indicator */
        .zoom-level-badge {{
            font-size: 10px;
            padding: 2px 8px;
            border-radius: 4px;
            text-transform: uppercase;
            font-weight: 600;
            letter-spacing: 0.5px;
        }}
        .zoom-level-badge.atom {{ background: #3b82f6; color: white; }}
        .zoom-level-badge.molecule {{ background: #8b5cf6; color: white; }}
        .zoom-level-badge.wave {{ background: #22c55e; color: white; }}

        /* Molecule view - grouped nodes */
        .molecule-group {{
            border: 2px dashed #8b5cf6;
            border-radius: 16px;
            padding: 16px;
            background: rgba(139, 92, 246, 0.1);
        }}

        /* Wave view - simplified nodes */
        .wave-view .drawflow-node {{
            min-width: 120px !important;
        }}
        .wave-view .node-body,
        .wave-view .signal-ports {{
            display: none !important;
        }}

        /* Kind-specific colors - muted/dampened */
        .kind-sensor .node-header {{
            background: linear-gradient(135deg, #4a6fa5 0%, #3d5a80 100%);
            color: #e8edf2;
        }}

        .kind-analyzer .node-header {{
            background: linear-gradient(135deg, #7c6b9e 0%, #5e4d7a 100%);
            color: #e8edf2;
        }}

        .kind-proposer .node-header {{
            background: linear-gradient(135deg, #b8956e 0%, #96724d 100%);
            color: #e8edf2;
        }}

        .kind-emitter .node-header {{
            background: linear-gradient(135deg, #a86565 0%, #8b4f4f 100%);
            color: #e8edf2;
        }}

        /* Shaper - modular synth signal processors */
        .kind-shaper .node-header {{
            background: linear-gradient(135deg, #06b6d4 0%, #0891b2 100%);
            color: #e8edf2;
        }}

        /* Config source atoms */
        .kind-config .node-header {{
            background: linear-gradient(135deg, #84cc16 0%, #65a30d 100%);
            color: #1a1a2e;
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
            border-color: #9e6b78;
        }}

        .palette-header {{
            padding: 8px 12px;
            font-weight: 600;
            font-size: 12px;
        }}

        /* Muted palette colors */
        .palette-card .kind-sensor {{ background: #4a6fa5; }}
        .palette-card .kind-analyzer {{ background: #7c6b9e; }}
        .palette-card .kind-proposer {{ background: #b8956e; }}
        .palette-card .kind-emitter {{ background: #a86565; }}

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

        .kind-badge.sensor {{ background: #4a6fa5; color: #e8edf2; }}
        .kind-badge.analyzer {{ background: #7c6b9e; color: #e8edf2; }}
        .kind-badge.proposer {{ background: #b8956e; color: #e8edf2; }}
        .kind-badge.emitter {{ background: #a86565; color: #e8edf2; }}
        .kind-badge.shaper {{ background: #06b6d4; color: #e8edf2; }}
        .kind-badge.config {{ background: #84cc16; color: #1a1a2e; }}

        /* Scrollbar */
        ::-webkit-scrollbar {{ width: 6px; }}
        ::-webkit-scrollbar-track {{ background: #1a1a2e; }}
        ::-webkit-scrollbar-thumb {{ background: #0f3460; border-radius: 3px; }}
        ::-webkit-scrollbar-thumb:hover {{ background: #9e6b78; }}

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
            border-color: #9e6b78;
            transform: translateY(-2px);
        }}

        /* Loupe - hover detail popout */
        .loupe {{
            position: fixed;
            background: linear-gradient(135deg, #1a1a2e 0%, #16213e 100%);
            border: 2px solid #4a6fa5;
            border-radius: 12px;
            padding: 14px;
            min-width: 260px;
            max-width: 360px;
            z-index: 1100;
            box-shadow: 0 8px 32px rgba(0,0,0,0.5), 0 0 20px rgba(74, 111, 165, 0.2);
            pointer-events: none;
            opacity: 0;
            transform: scale(0.95) translateY(5px);
            transition: opacity 0.15s ease, transform 0.15s ease;
        }}

        .loupe.visible {{
            opacity: 1;
            transform: scale(1) translateY(0);
        }}

        .loupe-header {{
            display: flex;
            align-items: center;
            gap: 10px;
            margin-bottom: 10px;
            padding-bottom: 8px;
            border-bottom: 1px solid #0f3460;
        }}

        .loupe-icon {{
            width: 28px;
            height: 28px;
            display: flex;
            align-items: center;
            justify-content: center;
            border-radius: 6px;
            font-size: 14px;
        }}

        .loupe-title {{
            flex: 1;
        }}

        .loupe-title h4 {{
            margin: 0;
            color: #e0e0e0;
            font-size: 13px;
            font-weight: 600;
        }}

        .loupe-title .loupe-kind {{
            font-size: 10px;
            color: #888;
            text-transform: uppercase;
            letter-spacing: 0.5px;
        }}

        .loupe-section {{
            margin-top: 10px;
        }}

        .loupe-section-title {{
            font-size: 9px;
            color: #666;
            text-transform: uppercase;
            letter-spacing: 0.5px;
            margin-bottom: 6px;
        }}

        .loupe-signals {{
            display: flex;
            flex-wrap: wrap;
            gap: 4px;
        }}

        .loupe-signal {{
            font-size: 9px;
            padding: 3px 6px;
            border-radius: 4px;
            font-family: 'Monaco', 'Menlo', monospace;
        }}

        .loupe-signal.input {{
            background: rgba(99, 102, 241, 0.2);
            color: #a5b4fc;
            border-left: 2px solid #6366f1;
        }}

        .loupe-signal.output {{
            background: rgba(34, 197, 94, 0.2);
            color: #86efac;
            border-left: 2px solid #22c55e;
        }}

        .loupe-signal.config {{
            background: rgba(132, 204, 22, 0.2);
            color: #bef264;
            border-left: 2px solid #84cc16;
        }}

        .loupe-desc {{
            font-size: 11px;
            color: #a0a0a0;
            line-height: 1.4;
        }}

        .loupe-meta {{
            display: flex;
            gap: 12px;
            margin-top: 10px;
            padding-top: 8px;
            border-top: 1px solid #0f3460;
        }}

        .loupe-meta-item {{
            display: flex;
            align-items: center;
            gap: 4px;
            font-size: 9px;
            color: #666;
        }}

        .loupe-meta-item span {{
            color: #888;
        }}

        /* Loupe color variations by kind */
        .loupe.kind-sensor {{ border-color: #4a6fa5; }}
        .loupe.kind-sensor .loupe-icon {{ background: #4a6fa5; }}
        .loupe.kind-analyzer {{ border-color: #7c6b9e; }}
        .loupe.kind-analyzer .loupe-icon {{ background: #7c6b9e; }}
        .loupe.kind-proposer {{ border-color: #b8956e; }}
        .loupe.kind-proposer .loupe-icon {{ background: #b8956e; }}
        .loupe.kind-emitter {{ border-color: #a86565; }}
        .loupe.kind-emitter .loupe-icon {{ background: #a86565; }}
        .loupe.kind-shaper {{ border-color: #06b6d4; }}
        .loupe.kind-shaper .loupe-icon {{ background: #06b6d4; }}
        .loupe.kind-config {{ border-color: #84cc16; }}
        .loupe.kind-config .loupe-icon {{ background: #84cc16; color: #1a1a2e; }}

        /* Signal loupe variation */
        .loupe.signal-loupe {{
            min-width: 200px;
            border-color: #6366f1;
        }}

        .loupe.signal-loupe.type-string {{ border-color: #3b82f6; }}
        .loupe.signal-loupe.type-number {{ border-color: #22c55e; }}
        .loupe.signal-loupe.type-boolean {{ border-color: #a855f7; }}
        .loupe.signal-loupe.type-object {{ border-color: #f59e0b; }}
    </style>
</head>
<body class=""min-h-screen"" style=""background: #0f0f23;"">
    <div x-data=""workflowBuilder()"" x-init=""init()"" class=""h-screen flex flex-col"">
        <!-- Header -->
        <div class=""px-4 py-3 flex items-center justify-between"" style=""background: linear-gradient(90deg, #16213e 0%, #1a1a2e 100%); border-bottom: 1px solid #0f3460;"">
            <div class=""flex items-center gap-4"">
                <div class=""flex items-center gap-2"">
                    <div class=""w-8 h-8 rounded-lg flex items-center justify-center"" style=""background: linear-gradient(135deg, #9e6b78 0%, #d62850 100%);"">
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
                <button class=""btn btn-sm"" style=""background: #9e6b78; border-color: #9e6b78; color: white;"" @click=""saveWorkflow()"">
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

                <template x-for=""kind in ['sensor', 'analyzer', 'proposer', 'emitter', 'shaper', 'config']"" :key=""kind"">
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

                <!-- Signal Type Legend -->
                <div class=""absolute bottom-4 left-4 p-3 rounded-lg"" style=""background: rgba(22, 33, 62, 0.95); border: 1px solid #0f3460;"">
                    <div class=""text-xs font-semibold text-gray-500 uppercase tracking-wider mb-2"">Signal Types</div>
                    <div class=""flex flex-col gap-1"">
                        <div class=""flex items-center gap-2"">
                            <div class=""w-4 h-1 rounded"" style=""background: #3b82f6""></div>
                            <span class=""text-xs text-gray-400"">string</span>
                        </div>
                        <div class=""flex items-center gap-2"">
                            <div class=""w-4 h-1 rounded"" style=""background: #22c55e""></div>
                            <span class=""text-xs text-gray-400"">number</span>
                        </div>
                        <div class=""flex items-center gap-2"">
                            <div class=""w-4 h-1 rounded"" style=""background: #a855f7""></div>
                            <span class=""text-xs text-gray-400"">boolean</span>
                        </div>
                        <div class=""flex items-center gap-2"">
                            <div class=""w-4 h-1 rounded"" style=""background: #f59e0b""></div>
                            <span class=""text-xs text-gray-400"">object/event</span>
                        </div>
                        <div class=""flex items-center gap-2"">
                            <div class=""w-4 h-1 rounded"" style=""background: #84cc16""></div>
                            <span class=""text-xs text-gray-400"">config</span>
                        </div>
                        <div class=""flex items-center gap-2"">
                            <div class=""w-4 h-1 rounded"" style=""background: #6b7280""></div>
                            <span class=""text-xs text-gray-400"">any/untyped</span>
                        </div>
                    </div>
                </div>

                <!-- Zoom controls -->
                <div class=""absolute bottom-4 right-4 flex flex-col gap-2"">
                    <!-- Zoom level selector -->
                    <div class=""flex flex-col gap-1 mb-2 p-2 rounded-lg"" style=""background: #16213e; border: 1px solid #0f3460;"">
                        <template x-for=""level in zoomLevels"" :key=""level"">
                            <button class=""btn btn-xs""
                                    :class=""zoomLevel === level ? 'btn-primary' : 'btn-ghost text-gray-400'""
                                    @click=""setZoomLevel(level)"">
                                <span class=""zoom-level-badge"" :class=""level"" x-text=""level""></span>
                            </button>
                        </template>
                    </div>
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
        <div class=""toast toast-end toast-bottom"" x-show=""toast.show"" x-transition>
            <div class=""alert"" :class=""toast.type === 'success' ? 'alert-success' : toast.type === 'error' ? 'alert-error' : 'alert-info'"">
                <span x-text=""toast.message""></span>
            </div>
        </div>

        <!-- Context menu for nodes/connections -->
        <div x-show=""contextMenu.show""
             x-transition
             class=""context-menu""
             :style=""`left: ${{contextMenu.x}}px; top: ${{contextMenu.y}}px`""
             @click.away=""contextMenu.show = false"">
            <template x-if=""contextMenu.type === 'node'"">
                <div>
                    <div class=""context-menu-item"" @click=""addInhibitorFromNode()"">
                        <span>⊘</span> Add Inhibitor Signal
                    </div>
                    <div class=""context-menu-item"" @click=""duplicateNode()"">
                        <span>📋</span> Duplicate Node
                    </div>
                    <div class=""context-menu-divider""></div>
                    <div class=""context-menu-item danger"" @click=""deleteContextNode()"">
                        <span>🗑️</span> Delete Node
                    </div>
                </div>
            </template>
            <template x-if=""contextMenu.type === 'connection'"">
                <div>
                    <div class=""context-menu-item"" @click=""toggleInhibitor()"">
                        <span>⊘</span> Toggle Inhibitor
                    </div>
                    <div class=""context-menu-item"" @click=""insertAdapterContextMenu()"">
                        <span>🔄</span> Insert Adapter
                    </div>
                    <div class=""context-menu-divider""></div>
                    <div class=""context-menu-item danger"" @click=""deleteConnection()"">
                        <span>✂️</span> Remove Connection
                    </div>
                </div>
            </template>
            <template x-if=""contextMenu.type === 'canvas'"">
                <div>
                    <div class=""context-menu-item"" @click=""addNodeAtPosition('sensor')"">
                        <span>📡</span> Add Sensor
                    </div>
                    <div class=""context-menu-item"" @click=""addNodeAtPosition('analyzer')"">
                        <span>🔬</span> Add Analyzer
                    </div>
                    <div class=""context-menu-item"" @click=""addNodeAtPosition('proposer')"">
                        <span>⚖️</span> Add Proposer
                    </div>
                    <div class=""context-menu-item"" @click=""addNodeAtPosition('emitter')"">
                        <span>📤</span> Add Emitter
                    </div>
                </div>
            </template>
        </div>

        <!-- Adapter Panel Popup -->
        <div x-show=""adapterPanel.show""
             x-transition
             class=""adapter-panel""
             :style=""`left: ${{adapterPanel.x}}px; top: ${{adapterPanel.y}}px`""
             @click.away=""adapterPanel.show = false"">
            <div class=""adapter-panel-header"">
                <span>🔄</span>
                <h4>Signal Adapter</h4>
            </div>
            <div class=""adapter-panel-body"">
                <div class=""adapter-signal-pair"">
                    <span class=""adapter-signal source"" x-text=""adapterPanel.source""></span>
                    <span class=""adapter-arrow"">→</span>
                    <span class=""adapter-signal target"" x-text=""adapterPanel.target""></span>
                </div>
                <div class=""adapter-type-info"">
                    <span>Type:</span>
                    <span class=""adapter-type-badge"" :class=""adapterPanel.sourceType"" x-text=""adapterPanel.sourceType""></span>
                    <span>→</span>
                    <span class=""adapter-type-badge"" :class=""adapterPanel.targetType"" x-text=""adapterPanel.targetType""></span>
                </div>
                <div class=""adapter-options"">
                    <template x-for=""opt in adapterPanel.options"" :key=""opt.name"">
                        <div class=""adapter-option"" :class=""{{ 'selected': adapterPanel.selected === opt.name }}"" @click=""adapterPanel.selected = opt.name"">
                            <span class=""adapter-option-icon"" x-text=""opt.icon""></span>
                            <div class=""adapter-option-info"">
                                <div class=""adapter-option-name"" x-text=""opt.name""></div>
                                <div class=""adapter-option-desc"" x-text=""opt.description""></div>
                            </div>
                        </div>
                    </template>
                </div>
            </div>
            <div class=""adapter-panel-footer"">
                <button class=""btn btn-sm btn-ghost"" @click=""adapterPanel.show = false"">Cancel</button>
                <button class=""btn btn-sm"" style=""background: #f59e0b; border-color: #f59e0b; color: white;"" @click=""applyAdapter()"">
                    Apply Adapter
                </button>
            </div>
        </div>

        <!-- Signal Drag Line SVG -->
        <svg x-show=""signalDrag.active"" class=""signal-drag-line"" style=""position: fixed; top: 0; left: 0; width: 100%; height: 100%; pointer-events: none; z-index: 9999;"">
            <line :x1=""signalDrag.startX"" :y1=""signalDrag.startY"" :x2=""signalDrag.endX"" :y2=""signalDrag.endY"" stroke=""#22c55e"" stroke-width=""3"" stroke-dasharray=""8,4"" />
            <circle :cx=""signalDrag.endX"" :cy=""signalDrag.endY"" r=""6"" fill=""#22c55e"" />
        </svg>

        <!-- Loupe (Hover Detail Popout) -->
        <div :class=""['loupe', 'kind-' + loupe.kind, loupe.show ? 'visible' : '', loupe.type === 'signal' ? 'signal-loupe type-' + loupe.kind : '']""
             :style=""`left: ${{loupe.x}}px; top: ${{loupe.y}}px`"">
            <div class=""loupe-header"">
                <div class=""loupe-icon"" x-text=""getKindIcon(loupe.kind)""></div>
                <div class=""loupe-title"">
                    <h4 x-text=""loupe.name""></h4>
                    <div class=""loupe-kind"" x-text=""loupe.type === 'signal' ? loupe.meta.direction + ' Signal' : loupe.kind""></div>
                </div>
            </div>
            <p class=""loupe-desc"" x-text=""loupe.description""></p>

            <template x-if=""loupe.inputSignals.length > 0"">
                <div class=""loupe-section"">
                    <div class=""loupe-section-title"">Requires</div>
                    <div class=""loupe-signals"">
                        <template x-for=""sig in loupe.inputSignals"" :key=""sig"">
                            <span class=""loupe-signal input"" x-text=""sig""></span>
                        </template>
                    </div>
                </div>
            </template>

            <template x-if=""loupe.outputSignals.length > 0"">
                <div class=""loupe-section"">
                    <div class=""loupe-section-title"">Emits</div>
                    <div class=""loupe-signals"">
                        <template x-for=""sig in loupe.outputSignals"" :key=""sig"">
                            <span class=""loupe-signal output"" x-text=""sig""></span>
                        </template>
                    </div>
                </div>
            </template>

            <div class=""loupe-meta"">
                <template x-for=""(val, key) in loupe.meta"" :key=""key"">
                    <div class=""loupe-meta-item"">
                        <span x-text=""key + ':'""></span>
                        <span x-text=""val""></span>
                    </div>
                </template>
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
                // Execution state
                isExecuting: false,
                showExecutionPanel: false,
                executionLogs: [],
                signalEvents: [],
                currentRunId: null,
                executionStatus: null,
                hubConnection: null,
                // Zoom levels: atom (detailed), molecule (grouped), wave (high-level)
                zoomLevel: 'atom',
                zoomLevels: ['atom', 'molecule', 'wave'],
                // Connection state for signal snapping
                pendingConnection: null,
                connectionValidation: null,
                pendingAdapter: null,
                // Context menu state
                contextMenu: {{ show: false, x: 0, y: 0, type: null, target: null }},
                // Inhibitor connections (signal → nodeId that it inhibits)
                inhibitors: [],
                pendingInhibitor: null,
                // Signal dragging state
                signalDrag: {{
                    active: false,
                    sourceNodeId: null,
                    sourceSignal: null,
                    sourceType: 'output',
                    startX: 0,
                    startY: 0,
                    endX: 0,
                    endY: 0
                }},
                // Adapter panel state
                adapterPanel: {{
                    show: false,
                    x: 0,
                    y: 0,
                    source: '',
                    target: '',
                    sourceType: 'string',
                    targetType: 'string',
                    sourceNodeId: null,
                    targetNodeId: null,
                    options: [],
                    selected: null
                }},
                // Loupe (hover detail) state
                loupe: {{
                    show: false,
                    x: 0,
                    y: 0,
                    type: 'atom', // 'atom', 'signal', 'config', 'wave'
                    kind: 'sensor',
                    name: '',
                    description: '',
                    inputSignals: [],
                    outputSignals: [],
                    configs: [],
                    meta: {{}}
                }},
                loupeTimer: null,

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

                    // Signal snapping: validate connections
                    this.editor.on('connectionCreated', (conn) => this.onConnectionCreated(conn));
                    this.editor.on('connectionRemoved', (conn) => this.onConnectionRemoved(conn));

                    // Right-click context menu
                    container.addEventListener('contextmenu', (e) => this.showContextMenu(e));

                    // Signal label drag events
                    container.addEventListener('mousedown', (e) => this.onSignalDragStart(e));
                    document.addEventListener('mousemove', (e) => this.onSignalDragMove(e));
                    document.addEventListener('mouseup', (e) => this.onSignalDragEnd(e));

                    // Loupe hover events
                    container.addEventListener('mouseover', (e) => this.onLoupeHoverStart(e));
                    container.addEventListener('mouseout', (e) => this.onLoupeHoverEnd(e));

                    await this.loadManifests();
                    await this.loadSamples();
                    await this.setupSignalR();

                    // Auto-load first sample
                    if (this.samples.length > 0) {{
                        setTimeout(() => this.loadSample(this.samples[0]), 500);
                    }}
                }},

                async setupSignalR() {{
                    this.hubConnection = new signalR.HubConnectionBuilder()
                        .withUrl(basePath + '/hub')
                        .withAutomaticReconnect()
                        .build();

                    // Listen for execution logs
                    this.hubConnection.on('ExecutionLog', (payload) => {{
                        const parts = payload.split(':');
                        const runId = parts[0];
                        const nodeId = parts[1];
                        const message = parts.slice(2).join(':');

                        if (this.currentRunId && runId === this.currentRunId) {{
                            this.addLog(nodeId, message);
                        }}
                    }});

                    // Listen for signal events
                    this.hubConnection.on('SignalEmitted', (payload) => {{
                        try {{
                            const sig = JSON.parse(payload);
                            this.signalEvents.push(sig);
                        }} catch (e) {{
                            console.log('Signal:', payload);
                        }}
                    }});

                    // Listen for workflow completion
                    this.hubConnection.on('WorkflowComplete', (payload) => {{
                        this.addLog('system', 'Workflow completed: ' + payload, 'complete');
                        this.isExecuting = false;
                        this.executionStatus = 'completed';
                    }});

                    // Listen for errors
                    this.hubConnection.on('ExecutionError', (payload) => {{
                        this.addLog('system', 'Error: ' + payload, 'error');
                        this.isExecuting = false;
                        this.executionStatus = 'failed';
                    }});

                    try {{
                        await this.hubConnection.start();
                        console.log('SignalR connected');
                    }} catch (err) {{
                        console.log('SignalR connection failed:', err);
                    }}
                }},

                addLog(nodeId, message, type = 'info') {{
                    const now = new Date();
                    const timestamp = now.toLocaleTimeString('en-US', {{ hour12: false }}) + '.' + String(now.getMilliseconds()).padStart(3, '0');
                    this.executionLogs.push({{ timestamp, nodeId, message, type }});

                    // Auto-scroll to bottom
                    this.$nextTick(() => {{
                        const container = this.$refs.logContainer;
                        if (container) container.scrollTop = container.scrollHeight;
                    }});
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

                getNodeHtml(atom, nodeId) {{
                    const inputSignals = atom.requiredSignals.slice(0, 4).map(s =>
                        `<div class=""port-label input"" data-signal=""${{s}}"" data-type=""input"" data-node-id=""${{nodeId || 'pending'}}"" draggable=""true"">${{s}}</div>`
                    ).join('');

                    const outputSignals = atom.emittedSignals.slice(0, 4).map(s =>
                        `<div class=""port-label output"" data-signal=""${{s}}"" data-type=""output"" data-node-id=""${{nodeId || 'pending'}}"" draggable=""true"">${{s}}</div>`
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
                        emitter: '📤',
                        shaper: '🎛️',
                        config: '⚙️',
                        adapter: '🔄'
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

                // Signal snapping: check if source signals are compatible with target
                onConnectionCreated(conn) {{
                    const sourceNode = this.editor.getNodeFromId(conn.output_id);
                    const targetNode = this.editor.getNodeFromId(conn.input_id);

                    if (!sourceNode || !targetNode) return;

                    const sourceEmits = sourceNode.data.emits || [];
                    const targetRequires = targetNode.data.requires || [];

                    // Check compatibility with type-based adaptation
                    const adaptResult = this.checkConnectionWithAdaptation(sourceEmits, targetRequires);

                    // Update connection styling based on compatibility
                    const connElement = document.querySelector(
                        `.connection.node_out_node-${{conn.output_id}}.node_in_node-${{conn.input_id}}`
                    );

                    if (connElement) {{
                        // Add signal type color class
                        const signalForColor = sourceEmits[0] || 'any';
                        const signalType = this.getSignalType(signalForColor);
                        const typeClass = this.getSignalTypeClass(signalType);
                        connElement.classList.add(typeClass);

                        if (adaptResult.compatible && !adaptResult.adapter) {{
                            // Direct signal match - perfect connection
                            connElement.classList.add('signal-matched');
                            connElement.setAttribute('data-signal', adaptResult.source);
                            this.showToast(`✓ Connected: ${{adaptResult.source}}`, 'success');
                        }} else if (adaptResult.compatible && adaptResult.adapter) {{
                            // Type-compatible but needs adapter
                            connElement.classList.add('signal-adaptable');
                            connElement.setAttribute('data-adapter', adaptResult.adapter.name);

                            // Store pending adapter info for this connection
                            this.pendingAdapter = {{
                                sourceId: conn.output_id,
                                targetId: conn.input_id,
                                info: adaptResult
                            }};

                            // Show adapter suggestion
                            const msg = adaptResult.rename
                                ? `Rename: ${{adaptResult.source}} → ${{adaptResult.target}}`
                                : `Adapt: ${{adaptResult.sourceType}} → ${{adaptResult.targetType}}`;

                            this.showToast(`🔄 ${{msg}} (dblclick to insert adapter)`, 'info');
                        }} else if (targetRequires.length === 0) {{
                            // Target accepts any signal (like log-writer)
                            connElement.classList.add('signal-any');
                            this.showToast(`Connected: any → ${{targetNode.name}}`, 'info');
                        }} else {{
                            // Truly incompatible signals
                            connElement.classList.add('signal-warning');
                            this.showToast(`⚠ No compatible signals`, 'error');
                        }}

                        // Add click handler for adapter insertion
                        connElement.addEventListener('dblclick', () => {{
                            if (this.pendingAdapter &&
                                this.pendingAdapter.sourceId === conn.output_id &&
                                this.pendingAdapter.targetId === conn.input_id) {{
                                this.insertAdapterForConnection(conn);
                            }}
                        }});
                    }}
                }},

                // Get CSS class for signal type coloring
                getSignalTypeClass(type) {{
                    const typeMap = {{
                        'string': 'signal-type-string',
                        'number': 'signal-type-number',
                        'boolean': 'signal-type-boolean',
                        'object': 'signal-type-object',
                        'config': 'signal-type-config'
                    }};
                    return typeMap[type] || 'signal-type-any';
                }},

                // Insert adapter for a connection
                async insertAdapterForConnection(conn) {{
                    if (!this.pendingAdapter) return;

                    const {{ sourceId, targetId, info }} = this.pendingAdapter;

                    // Remove the direct connection
                    this.editor.removeSingleConnection(sourceId, targetId, 'output_1', 'input_1');

                    // Insert the adapter
                    await this.insertAdapter(sourceId, targetId, info);

                    this.pendingAdapter = null;
                }},

                onConnectionRemoved(conn) {{
                    // Clean up any connection state if needed
                }},

                // Find signals that match between source and target using glob patterns
                findCompatibleSignals(sourceEmits, targetRequires) {{
                    const matches = [];
                    for (const emitted of sourceEmits) {{
                        for (const required of targetRequires) {{
                            // Support glob patterns: * matches any segment
                            if (this.signalMatches(emitted, required)) {{
                                matches.push(emitted);
                            }}
                        }}
                    }}
                    return matches;
                }},

                // Check if emitted signal matches required pattern
                signalMatches(emitted, pattern) {{
                    // Direct match
                    if (emitted === pattern) return true;

                    // Glob pattern matching (* = any segment, ** = any depth)
                    const patternParts = pattern.split('.');
                    const emittedParts = emitted.split('.');

                    if (patternParts.length !== emittedParts.length && !pattern.includes('**')) {{
                        return false;
                    }}

                    for (let i = 0; i < patternParts.length; i++) {{
                        if (patternParts[i] === '*') continue;
                        if (patternParts[i] === '**') return true;
                        if (patternParts[i] !== emittedParts[i]) return false;
                    }}

                    return true;
                }},

                // Highlight compatible targets when starting a connection
                highlightCompatibleTargets(sourceNodeId) {{
                    const sourceNode = this.editor.getNodeFromId(sourceNodeId);
                    if (!sourceNode) return;

                    const sourceEmits = sourceNode.data.emits || [];

                    // Check all nodes for compatibility
                    const exportData = this.editor.export();
                    for (const [moduleKey, module] of Object.entries(exportData.drawflow)) {{
                        for (const [nodeId, node] of Object.entries(module.data)) {{
                            if (nodeId === sourceNodeId) continue;

                            const targetRequires = node.data.requires || [];
                            const compatible = this.findCompatibleSignals(sourceEmits, targetRequires);

                            const nodeElement = document.querySelector(`#node-${{nodeId}}`);
                            if (nodeElement) {{
                                if (compatible.length > 0 || targetRequires.length === 0) {{
                                    nodeElement.classList.add('connection-target-valid');
                                }} else {{
                                    nodeElement.classList.add('connection-target-invalid');
                                }}
                            }}
                        }}
                    }}
                }},

                clearTargetHighlights() {{
                    document.querySelectorAll('.connection-target-valid, .connection-target-invalid')
                        .forEach(el => el.classList.remove('connection-target-valid', 'connection-target-invalid'));
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

                async executeWorkflow() {{
                    const workflow = this.getWorkflowData();
                    if (workflow.nodes.length === 0) {{
                        this.showToast('Add nodes to the workflow first', 'error');
                        return;
                    }}

                    // Clear previous execution
                    this.executionLogs = [];
                    this.signalEvents = [];
                    this.executionStatus = null;
                    this.isExecuting = true;
                    this.showExecutionPanel = true;

                    this.addLog('system', 'Starting workflow execution...');

                    try {{
                        const res = await fetch(`${{basePath}}/api/execute`, {{
                            method: 'POST',
                            headers: {{ 'Content-Type': 'application/json' }},
                            body: JSON.stringify({{
                                workflow: workflow,
                                input: {{
                                    text: 'This is a sample text for analysis. The product is amazing and I love it! Great quality.'
                                }}
                            }})
                        }});

                        if (res.ok) {{
                            const result = await res.json();
                            this.currentRunId = result.runId;
                            this.executionStatus = result.status;
                            this.addLog('system', `Run completed with status: ${{result.status}}`, result.status === 'completed' ? 'complete' : 'error');

                            // Show final signals
                            if (result.finalSignals) {{
                                for (const [key, value] of Object.entries(result.finalSignals)) {{
                                    this.addLog('system', `Signal: ${{key}} = ${{JSON.stringify(value)}}`);
                                }}
                            }}
                        }} else {{
                            this.addLog('system', 'Execution failed: ' + await res.text(), 'error');
                            this.executionStatus = 'failed';
                        }}
                    }} catch (err) {{
                        this.addLog('system', 'Error: ' + err.message, 'error');
                        this.executionStatus = 'failed';
                    }} finally {{
                        this.isExecuting = false;
                    }}
                }},

                showToast(message, type = 'info') {{
                    this.toast = {{ show: true, message, type }};
                    setTimeout(() => this.toast.show = false, 3000);
                }},

                zoomIn() {{ this.editor.zoom_in(); }},
                zoomOut() {{ this.editor.zoom_out(); }},
                resetZoom() {{ this.editor.zoom_reset(); }},

                // Zoom level changes view abstraction
                setZoomLevel(level) {{
                    this.zoomLevel = level;
                    const container = document.getElementById('drawflow');

                    container.classList.remove('atom-view', 'molecule-view', 'wave-view');
                    container.classList.add(`${{level}}-view`);

                    if (level === 'wave') {{
                        this.editor.zoom_out();
                        this.editor.zoom_out();
                    }} else if (level === 'molecule') {{
                        this.editor.zoom_reset();
                    }} else {{
                        this.editor.zoom_reset();
                    }}

                    this.showToast(`View: ${{level}}`, 'info');
                }},

                // ==========================================
                // AUTO-ADAPTERS: Type-based signal conversion
                // ==========================================

                // Signal type registry - infer type from signal name patterns
                signalTypes: {{
                    // Text/String types
                    'text.*': 'string',
                    'http.body': 'string',
                    'http.path': 'string',
                    'http.method': 'string',
                    'log.*': 'string',
                    'email.*': 'string',
                    'sentiment.label': 'string',

                    // Numeric types
                    'sentiment.score': 'number',
                    'sentiment.confidence': 'number',
                    'text.word_count': 'number',
                    'text.char_count': 'number',
                    'filter.value': 'number',

                    // Boolean types
                    'filter.passed': 'boolean',
                    'filter.exceeded': 'boolean',
                    'filter.action_required': 'boolean',
                    'sentiment.is_positive': 'boolean',

                    // Object types
                    'http.received': 'object',
                    'timer.triggered': 'object'
                }},

                // Get inferred type for a signal
                getSignalType(signal) {{
                    // Direct match
                    if (this.signalTypes[signal]) return this.signalTypes[signal];

                    // Pattern match
                    for (const [pattern, type] of Object.entries(this.signalTypes)) {{
                        if (this.signalMatches(signal, pattern)) return type;
                    }}

                    // Default to string
                    return 'string';
                }},

                // Check if two signal types are adaptable
                areTypesAdaptable(sourceType, targetType) {{
                    if (sourceType === targetType) return true;

                    // Adaptable type pairs
                    const adaptable = {{
                        'number': ['string', 'boolean'],
                        'string': ['number', 'boolean'],
                        'boolean': ['string', 'number'],
                        'object': ['string']
                    }};

                    return adaptable[sourceType]?.includes(targetType) || false;
                }},

                // Get adapter for type conversion
                getAdapter(sourceType, targetType) {{
                    const adapters = {{
                        'number→string': {{ name: 'format-number', description: 'Formats number as string' }},
                        'string→number': {{ name: 'parse-number', description: 'Parses string to number' }},
                        'boolean→string': {{ name: 'format-bool', description: 'Formats boolean as string' }},
                        'string→boolean': {{ name: 'parse-bool', description: 'Parses string to boolean' }},
                        'object→string': {{ name: 'json-stringify', description: 'Serializes object to JSON string' }},
                        'number→boolean': {{ name: 'number-to-bool', description: 'Non-zero = true' }}
                    }};
                    return adapters[`${{sourceType}}→${{targetType}}`];
                }},

                // Check connection compatibility with type adaptation
                checkConnectionWithAdaptation(sourceEmits, targetRequires) {{
                    for (const emitted of sourceEmits) {{
                        for (const required of targetRequires) {{
                            const sourceType = this.getSignalType(emitted);
                            const targetType = this.getSignalType(required);

                            // Direct signal match
                            if (this.signalMatches(emitted, required)) {{
                                return {{ compatible: true, adapter: null, source: emitted, target: required }};
                            }}

                            // Type-compatible but different names - need adapter
                            if (sourceType === targetType) {{
                                return {{
                                    compatible: true,
                                    adapter: {{ name: 'signal-rename', description: `Renames ${{emitted}} → ${{required}}` }},
                                    source: emitted,
                                    target: required,
                                    rename: true
                                }};
                            }}

                            // Different types but adaptable
                            if (this.areTypesAdaptable(sourceType, targetType)) {{
                                const adapter = this.getAdapter(sourceType, targetType);
                                return {{
                                    compatible: true,
                                    adapter,
                                    source: emitted,
                                    target: required,
                                    sourceType,
                                    targetType
                                }};
                            }}
                        }}
                    }}

                    return {{ compatible: false }};
                }},

                // Insert adapter node between two nodes
                async insertAdapter(sourceNodeId, targetNodeId, adapterInfo) {{
                    // Get positions
                    const sourceNode = this.editor.getNodeFromId(sourceNodeId);
                    const targetNode = this.editor.getNodeFromId(targetNodeId);

                    if (!sourceNode || !targetNode) return;

                    const midX = (sourceNode.pos_x + targetNode.pos_x) / 2;
                    const midY = (sourceNode.pos_y + targetNode.pos_y) / 2;

                    // Create adapter node
                    const adapterId = ++this.nodeCounter;
                    const adapterHtml = `
                        <div class=""kind-adapter"">
                            <div class=""node-header"" style=""background: linear-gradient(135deg, #f59e0b 0%, #d97706 100%);"">
                                <span class=""icon"">🔄</span>
                                <span>${{adapterInfo.adapter.name}}</span>
                            </div>
                            <div class=""node-body"">${{adapterInfo.adapter.description}}</div>
                            <div class=""signal-ports"">
                                <div class=""port-group inputs""><div class=""port-label input"">${{adapterInfo.source}}</div></div>
                                <div class=""port-group outputs""><div class=""port-label output"">${{adapterInfo.target}}</div></div>
                            </div>
                        </div>
                    `;

                    this.editor.addNode(
                        adapterInfo.adapter.name,
                        1, 1,
                        midX, midY,
                        'kind-adapter',
                        {{
                            manifest: 'adapter',
                            emits: [adapterInfo.target],
                            requires: [adapterInfo.source],
                            adapterConfig: adapterInfo
                        }},
                        adapterHtml
                    );

                    // Remove old connection
                    // Add source → adapter connection
                    this.editor.addConnection(sourceNodeId, adapterId, 'output_1', 'input_1');
                    // Add adapter → target connection
                    this.editor.addConnection(adapterId, targetNodeId, 'output_1', 'input_1');

                    this.showToast(`Inserted adapter: ${{adapterInfo.adapter.name}}`, 'success');
                }},

                // ==========================================
                // CONTEXT MENU & INHIBITORS
                // ==========================================

                showContextMenu(e) {{
                    e.preventDefault();

                    // Check what was right-clicked
                    const nodeEl = e.target.closest('.drawflow-node');
                    const connEl = e.target.closest('.connection');

                    if (nodeEl) {{
                        const nodeId = nodeEl.id.replace('node-', '');
                        this.contextMenu = {{
                            show: true,
                            x: e.clientX,
                            y: e.clientY,
                            type: 'node',
                            target: nodeId
                        }};
                    }} else if (connEl) {{
                        this.contextMenu = {{
                            show: true,
                            x: e.clientX,
                            y: e.clientY,
                            type: 'connection',
                            target: connEl
                        }};
                    }} else {{
                        // Canvas right-click
                        this.contextMenu = {{
                            show: true,
                            x: e.clientX,
                            y: e.clientY,
                            type: 'canvas',
                            target: {{ x: e.clientX, y: e.clientY }}
                        }};
                    }}
                }},

                // Add inhibitor from a node's output
                addInhibitorFromNode() {{
                    const nodeId = this.contextMenu.target;
                    const node = this.editor.getNodeFromId(nodeId);

                    if (node) {{
                        this.showToast(`Click target node to add inhibitor from ${{node.name}}`, 'info');

                        // Store pending inhibitor source
                        this.pendingInhibitor = {{
                            sourceId: nodeId,
                            sourceSignals: node.data.emits || []
                        }};

                        // Highlight potential targets
                        document.querySelectorAll('.drawflow-node').forEach(el => {{
                            if (el.id !== `node-${{nodeId}}`) {{
                                el.classList.add('inhibitor-target');
                                el.addEventListener('click', this.completeInhibitor.bind(this), {{ once: true }});
                            }}
                        }});
                    }}

                    this.contextMenu.show = false;
                }},

                completeInhibitor(e) {{
                    if (!this.pendingInhibitor) return;

                    const targetEl = e.target.closest('.drawflow-node');
                    if (!targetEl) return;

                    const targetId = targetEl.id.replace('node-', '');
                    const sourceId = this.pendingInhibitor.sourceId;

                    // Add inhibitor connection
                    this.inhibitors.push({{
                        sourceId,
                        targetId,
                        signal: this.pendingInhibitor.sourceSignals[0] || 'any'
                    }});

                    // Draw inhibitor connection (visual only)
                    this.drawInhibitorConnection(sourceId, targetId);

                    this.showToast(`⊘ Inhibitor added: blocks ${{targetId}} when source fires`, 'success');

                    // Clean up
                    document.querySelectorAll('.inhibitor-target').forEach(el => {{
                        el.classList.remove('inhibitor-target');
                    }});
                    this.pendingInhibitor = null;
                }},

                drawInhibitorConnection(sourceId, targetId) {{
                    // Add visual inhibitor using SVG
                    const sourceNode = this.editor.getNodeFromId(sourceId);
                    const targetNode = this.editor.getNodeFromId(targetId);

                    if (!sourceNode || !targetNode) return;

                    // Get positions
                    const svg = document.querySelector('#drawflow svg');
                    if (!svg) return;

                    const path = document.createElementNS('http://www.w3.org/2000/svg', 'path');
                    const x1 = sourceNode.pos_x + 110;
                    const y1 = sourceNode.pos_y + 50;
                    const x2 = targetNode.pos_x;
                    const y2 = targetNode.pos_y + 50;

                    // Bezier curve
                    const midX = (x1 + x2) / 2;
                    path.setAttribute('d', `M${{x1}},${{y1}} C${{midX}},${{y1}} ${{midX}},${{y2}} ${{x2}},${{y2}}`);
                    path.setAttribute('class', 'inhibitor-path');
                    path.setAttribute('stroke', '#dc2626');
                    path.setAttribute('stroke-width', '2');
                    path.setAttribute('stroke-dasharray', '4,4');
                    path.setAttribute('fill', 'none');
                    path.setAttribute('data-inhibitor', `${{sourceId}}-${{targetId}}`);

                    svg.appendChild(path);
                }},

                toggleInhibitor() {{
                    const connEl = this.contextMenu.target;
                    if (!connEl) return;

                    connEl.classList.toggle('signal-inhibitor');

                    if (connEl.classList.contains('signal-inhibitor')) {{
                        this.showToast('⊘ Connection marked as inhibitor', 'success');
                    }} else {{
                        this.showToast('Connection restored to normal', 'info');
                    }}

                    this.contextMenu.show = false;
                }},

                duplicateNode() {{
                    const nodeId = this.contextMenu.target;
                    const node = this.editor.getNodeFromId(nodeId);

                    if (node) {{
                        const atom = this.manifests.find(m => m.name === node.name);
                        if (atom) {{
                            this.addNode(atom, node.pos_x + 50, node.pos_y + 50);
                            this.showToast(`Duplicated: ${{node.name}}`, 'success');
                        }}
                    }}

                    this.contextMenu.show = false;
                }},

                deleteContextNode() {{
                    const nodeId = this.contextMenu.target;
                    this.editor.removeNodeId(`node-${{nodeId}}`);
                    this.showToast('Node deleted', 'info');
                    this.contextMenu.show = false;
                }},

                insertAdapterContextMenu() {{
                    // Get connection info and insert adapter
                    if (this.pendingAdapter) {{
                        this.insertAdapterForConnection(this.pendingAdapter);
                    }} else {{
                        this.showToast('No adapter available for this connection', 'error');
                    }}
                    this.contextMenu.show = false;
                }},

                deleteConnection() {{
                    const connEl = this.contextMenu.target;
                    if (connEl) {{
                        // Parse connection info from classes
                        const classes = connEl.className.baseVal;
                        const outMatch = classes.match(/node_out_node-(\d+)/);
                        const inMatch = classes.match(/node_in_node-(\d+)/);

                        if (outMatch && inMatch) {{
                            this.editor.removeSingleConnection(outMatch[1], inMatch[1], 'output_1', 'input_1');
                            this.showToast('Connection removed', 'info');
                        }}
                    }}
                    this.contextMenu.show = false;
                }},

                addNodeAtPosition(kind) {{
                    const atom = this.manifests.find(m => m.kind === kind);
                    if (atom) {{
                        const rect = document.getElementById('drawflow').getBoundingClientRect();
                        const x = this.contextMenu.target.x - rect.left;
                        const y = this.contextMenu.target.y - rect.top;
                        this.addNode(atom, x, y);
                    }}
                    this.contextMenu.show = false;
                }},

                // ==========================================
                // SIGNAL LABEL DRAGGING
                // ==========================================

                onSignalDragStart(e) {{
                    const portLabel = e.target.closest('.port-label[data-signal]');
                    if (!portLabel) return;

                    e.preventDefault();
                    e.stopPropagation();

                    const rect = portLabel.getBoundingClientRect();
                    const nodeEl = portLabel.closest('.drawflow-node');
                    const nodeId = nodeEl ? nodeEl.id.replace('node-', '') : null;

                    portLabel.classList.add('dragging');

                    this.signalDrag = {{
                        active: true,
                        sourceNodeId: nodeId,
                        sourceSignal: portLabel.dataset.signal,
                        sourceType: portLabel.dataset.type, // 'input' or 'output'
                        startX: rect.left + rect.width / 2,
                        startY: rect.top + rect.height / 2,
                        endX: e.clientX,
                        endY: e.clientY
                    }};

                    // Highlight compatible targets
                    this.highlightSignalTargets();
                }},

                onSignalDragMove(e) {{
                    if (!this.signalDrag.active) return;

                    this.signalDrag.endX = e.clientX;
                    this.signalDrag.endY = e.clientY;

                    // Update hover target highlighting
                    const targetEl = document.elementFromPoint(e.clientX, e.clientY);
                    const portLabel = targetEl?.closest('.port-label[data-signal]');

                    document.querySelectorAll('.port-label.drop-hover').forEach(el => el.classList.remove('drop-hover'));
                    if (portLabel && portLabel.dataset.type !== this.signalDrag.sourceType) {{
                        portLabel.classList.add('drop-hover');
                    }}
                }},

                onSignalDragEnd(e) {{
                    if (!this.signalDrag.active) return;

                    // Remove dragging class
                    document.querySelectorAll('.port-label.dragging').forEach(el => el.classList.remove('dragging'));

                    // Find target
                    const targetEl = document.elementFromPoint(e.clientX, e.clientY);
                    const targetPortLabel = targetEl?.closest('.port-label[data-signal]');

                    if (targetPortLabel && targetPortLabel.dataset.type !== this.signalDrag.sourceType) {{
                        const targetNodeEl = targetPortLabel.closest('.drawflow-node');
                        const targetNodeId = targetNodeEl ? targetNodeEl.id.replace('node-', '') : null;
                        const targetSignal = targetPortLabel.dataset.signal;

                        // Determine source and target based on drag direction
                        let sourceNodeId, targetNodeIdFinal, sourceSignal, targetSignalFinal;

                        if (this.signalDrag.sourceType === 'output') {{
                            sourceNodeId = this.signalDrag.sourceNodeId;
                            targetNodeIdFinal = targetNodeId;
                            sourceSignal = this.signalDrag.sourceSignal;
                            targetSignalFinal = targetSignal;
                        }} else {{
                            sourceNodeId = targetNodeId;
                            targetNodeIdFinal = this.signalDrag.sourceNodeId;
                            sourceSignal = targetSignal;
                            targetSignalFinal = this.signalDrag.sourceSignal;
                        }}

                        // Check compatibility and show adapter panel if needed
                        this.handleSignalConnection(sourceNodeId, targetNodeIdFinal, sourceSignal, targetSignalFinal, e.clientX, e.clientY);
                    }}

                    // Clear highlights
                    this.clearSignalHighlights();
                    this.signalDrag.active = false;
                }},

                highlightSignalTargets() {{
                    const sourceType = this.signalDrag.sourceType;
                    const sourceSignal = this.signalDrag.sourceSignal;
                    const sourceSignalType = this.getSignalType(sourceSignal);

                    document.querySelectorAll('.port-label[data-signal]').forEach(el => {{
                        // Skip same-type ports (output→output or input→input)
                        if (el.dataset.type === sourceType) return;

                        const targetSignal = el.dataset.signal;
                        const targetSignalType = this.getSignalType(targetSignal);

                        // Check compatibility
                        if (this.signalMatches(sourceSignal, targetSignal)) {{
                            el.classList.add('drop-target-valid');
                        }} else if (sourceSignalType === targetSignalType || this.areTypesAdaptable(sourceSignalType, targetSignalType)) {{
                            el.classList.add('drop-target-adaptable');
                        }} else {{
                            el.classList.add('drop-target-invalid');
                        }}
                    }});
                }},

                clearSignalHighlights() {{
                    document.querySelectorAll('.port-label').forEach(el => {{
                        el.classList.remove('drop-target-valid', 'drop-target-adaptable', 'drop-target-invalid', 'drop-hover', 'dragging');
                    }});
                }},

                handleSignalConnection(sourceNodeId, targetNodeId, sourceSignal, targetSignal, x, y) {{
                    const sourceType = this.getSignalType(sourceSignal);
                    const targetType = this.getSignalType(targetSignal);

                    // Direct match - create connection immediately
                    if (this.signalMatches(sourceSignal, targetSignal)) {{
                        this.editor.addConnection(sourceNodeId, targetNodeId, 'output_1', 'input_1');
                        this.showToast(`✓ Connected: ${{sourceSignal}}`, 'success');
                        return;
                    }}

                    // Same type but different names - offer rename adapter
                    if (sourceType === targetType) {{
                        this.showAdapterPanel(x, y, {{
                            sourceNodeId,
                            targetNodeId,
                            source: sourceSignal,
                            target: targetSignal,
                            sourceType,
                            targetType,
                            options: [
                                {{ name: 'signal-rename', icon: '📝', description: `Rename ${{sourceSignal}} → ${{targetSignal}}` }},
                                {{ name: 'direct-connect', icon: '🔗', description: 'Connect directly (ignore names)' }}
                            ]
                        }});
                        return;
                    }}

                    // Different adaptable types - offer type conversion
                    if (this.areTypesAdaptable(sourceType, targetType)) {{
                        const adapter = this.getAdapter(sourceType, targetType);
                        this.showAdapterPanel(x, y, {{
                            sourceNodeId,
                            targetNodeId,
                            source: sourceSignal,
                            target: targetSignal,
                            sourceType,
                            targetType,
                            options: [
                                {{ name: adapter.name, icon: '🔄', description: adapter.description }},
                                {{ name: 'signal-rename', icon: '📝', description: `Rename + convert` }},
                                {{ name: 'direct-connect', icon: '🔗', description: 'Connect directly (may fail at runtime)' }}
                            ]
                        }});
                        return;
                    }}

                    // Incompatible - show warning but allow forced connection
                    this.showAdapterPanel(x, y, {{
                        sourceNodeId,
                        targetNodeId,
                        source: sourceSignal,
                        target: targetSignal,
                        sourceType,
                        targetType,
                        options: [
                            {{ name: 'force-connect', icon: '⚠️', description: 'Force connection (incompatible types)' }},
                            {{ name: 'cancel', icon: '❌', description: 'Cancel - types are incompatible' }}
                        ]
                    }});
                }},

                showAdapterPanel(x, y, config) {{
                    this.adapterPanel = {{
                        show: true,
                        x: Math.min(x, window.innerWidth - 360),
                        y: Math.min(y, window.innerHeight - 300),
                        source: config.source,
                        target: config.target,
                        sourceType: config.sourceType,
                        targetType: config.targetType,
                        sourceNodeId: config.sourceNodeId,
                        targetNodeId: config.targetNodeId,
                        options: config.options,
                        selected: config.options[0]?.name
                    }};
                }},

                applyAdapter() {{
                    const {{ sourceNodeId, targetNodeId, source, target, sourceType, targetType, selected }} = this.adapterPanel;

                    if (selected === 'cancel') {{
                        this.adapterPanel.show = false;
                        return;
                    }}

                    if (selected === 'direct-connect' || selected === 'force-connect') {{
                        // Direct connection without adapter
                        this.editor.addConnection(sourceNodeId, targetNodeId, 'output_1', 'input_1');
                        this.showToast(`Connected: ${{source}} → ${{target}}`, 'success');
                    }} else {{
                        // Insert adapter node
                        this.insertAdapter(sourceNodeId, targetNodeId, {{
                            adapter: {{ name: selected, description: `${{sourceType}} → ${{targetType}}` }},
                            source,
                            target,
                            sourceType,
                            targetType
                        }});
                    }}

                    this.adapterPanel.show = false;
                }},

                // ==========================================
                // LOUPE (HOVER DETAIL POPOUT)
                // ==========================================

                onLoupeHoverStart(e) {{
                    // Don't show loupe while dragging
                    if (this.signalDrag.active) return;

                    // Check what we're hovering over
                    const node = e.target.closest('.drawflow-node');
                    const portLabel = e.target.closest('.port-label[data-signal]');
                    const paletteItem = e.target.closest('.palette-item');

                    clearTimeout(this.loupeTimer);

                    if (portLabel) {{
                        // Signal label hover
                        this.loupeTimer = setTimeout(() => {{
                            this.showSignalLoupe(portLabel, e);
                        }}, 400);
                    }} else if (node) {{
                        // Node hover
                        this.loupeTimer = setTimeout(() => {{
                            this.showNodeLoupe(node, e);
                        }}, 500);
                    }} else if (paletteItem) {{
                        // Palette item hover
                        this.loupeTimer = setTimeout(() => {{
                            this.showPaletteLoupe(paletteItem, e);
                        }}, 400);
                    }}
                }},

                onLoupeHoverEnd(e) {{
                    clearTimeout(this.loupeTimer);
                    this.loupe.show = false;
                }},

                showSignalLoupe(portLabel, e) {{
                    const signal = portLabel.dataset.signal;
                    const isInput = portLabel.dataset.type === 'input';
                    const signalType = this.getSignalType(signal);

                    const rect = portLabel.getBoundingClientRect();

                    this.loupe = {{
                        show: true,
                        x: Math.min(rect.right + 10, window.innerWidth - 280),
                        y: Math.max(10, rect.top - 30),
                        type: 'signal',
                        kind: signalType,
                        name: signal,
                        description: isInput
                            ? 'This signal is required to trigger the atom'
                            : 'This signal is emitted when the atom completes',
                        inputSignals: [],
                        outputSignals: [],
                        configs: [],
                        meta: {{
                            direction: isInput ? 'Input' : 'Output',
                            dataType: this.getSignalTypeLabel(signalType)
                        }}
                    }};
                }},

                showNodeLoupe(nodeEl, e) {{
                    const nodeId = nodeEl.id.replace('node-', '');
                    const nodeData = this.editor.getNodeFromId(nodeId);
                    if (!nodeData || !nodeData.data) return;

                    const atomData = nodeData.data;
                    const manifest = this.manifests.find(m => m.name === atomData.manifest);
                    if (!manifest) return;

                    const rect = nodeEl.getBoundingClientRect();

                    this.loupe = {{
                        show: true,
                        x: Math.min(rect.right + 10, window.innerWidth - 280),
                        y: Math.max(10, rect.top),
                        type: 'atom',
                        kind: manifest.kind?.toLowerCase() || 'sensor',
                        name: manifest.name,
                        description: manifest.description || 'No description available',
                        inputSignals: manifest.requiredSignals || [],
                        outputSignals: manifest.emittedSignals || [],
                        configs: [], // Would come from manifest if defined
                        meta: {{
                            kind: manifest.kind || 'Unknown',
                            determinism: manifest.determinism || 'Unknown',
                            persistence: manifest.persistence || 'Unknown'
                        }}
                    }};
                }},

                showPaletteLoupe(paletteItem, e) {{
                    const manifestName = paletteItem.dataset.manifest;
                    const manifest = this.manifests.find(m => m.name === manifestName);
                    if (!manifest) return;

                    const rect = paletteItem.getBoundingClientRect();

                    this.loupe = {{
                        show: true,
                        x: Math.min(rect.right + 10, window.innerWidth - 280),
                        y: Math.max(10, rect.top),
                        type: 'atom',
                        kind: manifest.kind?.toLowerCase() || 'sensor',
                        name: manifest.name,
                        description: manifest.description || 'Drag onto canvas to add',
                        inputSignals: manifest.requiredSignals || [],
                        outputSignals: manifest.emittedSignals || [],
                        configs: [],
                        meta: {{
                            kind: manifest.kind || 'Unknown',
                            determinism: manifest.determinism || 'Unknown',
                            persistence: manifest.persistence || 'Unknown'
                        }}
                    }};
                }},

                getSignalTypeLabel(type) {{
                    const labels = {{
                        'string': 'String',
                        'number': 'Number',
                        'boolean': 'Boolean',
                        'object': 'Object',
                        'config': 'Config',
                        'any': 'Any'
                    }};
                    return labels[type] || 'Unknown';
                }},

                getKindIcon(kind) {{
                    const icons = {{
                        'sensor': '📡',
                        'analyzer': '🔬',
                        'extractor': '🔬',
                        'proposer': '🎯',
                        'constrainer': '🛡️',
                        'emitter': '📤',
                        'renderer': '📤',
                        'shaper': '🎛️',
                        'config': '⚙️'
                    }};
                    return icons[kind?.toLowerCase()] || '📦';
                }}
            }};
        }}
    </script>
</body>
</html>";
    }
}
