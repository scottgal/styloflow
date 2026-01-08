using System.Text.Json;
using Mostlylucid.Ephemeral;
using StyloFlow.WorkflowBuilder.Atoms;
using StyloFlow.WorkflowBuilder.Models;

namespace StyloFlow.WorkflowBuilder.Runtime;

/// <summary>
/// Workflow orchestrator using Ephemeral's EphemeralWorkCoordinator.
/// Replaces the manual topological sort with signal-driven execution.
/// </summary>
public sealed class WorkflowOrchestrator : IAsyncDisposable
{
    private readonly SignalSink _globalSink;
    private readonly OllamaService _ollama;
    private readonly WorkflowStorage _storage;
    private readonly SignalRCoordinator _signalRCoordinator;
    private readonly Dictionary<string, Func<WorkflowAtomContext, Task>> _atomExecutors;

    public WorkflowOrchestrator(
        SignalSink globalSink,
        OllamaService ollama,
        WorkflowStorage storage,
        SignalRCoordinator signalRCoordinator)
    {
        _globalSink = globalSink;
        _ollama = ollama;
        _storage = storage;
        _signalRCoordinator = signalRCoordinator;

        // Register atom executors by manifest name
        _atomExecutors = new Dictionary<string, Func<WorkflowAtomContext, Task>>
        {
            ["timer-trigger"] = TimerTriggerSensor.ExecuteAsync,
            ["http-receiver"] = HttpReceiverSensor.ExecuteAsync,
            ["text-analyzer"] = TextAnalyzerExtractor.ExecuteAsync,
            ["sentiment-detector"] = SentimentDetectorProposer.ExecuteAsync,
            ["threshold-filter"] = ThresholdFilterConstrainer.ExecuteAsync,
            ["email-sender"] = EmailSenderRenderer.ExecuteAsync,
            ["log-writer"] = LogWriterRenderer.ExecuteAsync
        };
    }

    /// <summary>
    /// Execute a workflow using Ephemeral's signal-driven coordination.
    /// </summary>
    public async Task<WorkflowExecutionResult> ExecuteAsync(
        WorkflowDefinition workflow,
        Dictionary<string, object>? input = null,
        CancellationToken cancellationToken = default)
    {
        // Create per-run signals backed by global sink
        using var runSignals = new WorkflowSignals(
            $"run-{Guid.NewGuid():N}"[..16],
            _globalSink);

        var runId = await _storage.StartRunAsync(workflow.Id, workflow.Name, JsonSerializer.Serialize(input));

        var result = new WorkflowExecutionResult
        {
            RunId = runId,
            WorkflowId = workflow.Id,
            WorkflowName = workflow.Name,
            StartedAt = DateTime.UtcNow
        };

        // Log start
        _signalRCoordinator.EmitLog(runId, "system", $"Starting workflow: {workflow.Name}");
        _signalRCoordinator.EmitLog(runId, "system", $"Nodes: {workflow.Nodes.Count}, Edges: {workflow.Edges.Count}");

        try
        {
            // Build execution order using topological sort
            var executionOrder = TopologicalSort(workflow);
            _signalRCoordinator.EmitLog(runId, "system",
                $"Execution order: {string.Join(" -> ", executionOrder.Select(n => n.ManifestName))}");

            // Create coordinator for this workflow run
            var coordinator = new EphemeralWorkCoordinator<WorkflowNodeExecution>(
                async (execution, ct) => await ExecuteNodeAsync(execution, runSignals, runId),
                new EphemeralOptions
                {
                    MaxConcurrency = 1, // Sequential for now, could be parallel
                    Signals = _globalSink,
                    MaxTrackedOperations = workflow.Nodes.Count * 2,
                    MaxOperationLifetime = TimeSpan.FromMinutes(5),
                    // Cancel on failure signals
                    CancelOnSignals = new HashSet<string> { "workflow.failed", "workflow.cancelled" }
                });

            // Merge input config for entry points
            foreach (var node in executionOrder)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    result.Status = "cancelled";
                    break;
                }

                var config = new Dictionary<string, object>(node.Config);
                if (input != null && IsEntryPoint(node, workflow))
                {
                    foreach (var kvp in input)
                    {
                        config[kvp.Key] = kvp.Value;
                    }
                }

                await coordinator.EnqueueAsync(new WorkflowNodeExecution(node, config), cancellationToken);
            }

            // Wait for all nodes to complete
            coordinator.Complete();
            await coordinator.DrainAsync(cancellationToken);

            result.Status = cancellationToken.IsCancellationRequested ? "cancelled" : "completed";
            result.FinalSignals = runSignals.GetAll()
                .GroupBy(s => s.Signal)
                .ToDictionary(
                    g => g.Key,
                    g => g.OrderByDescending(s => s.Timestamp).First().Key as object);

            _signalRCoordinator.EmitLog(runId, "system", $"Workflow completed with {result.FinalSignals.Count} signals");
        }
        catch (Exception ex)
        {
            result.Status = "failed";
            result.Error = ex.Message;
            _signalRCoordinator.EmitLog(runId, "system", $"Workflow failed: {ex.Message}");
            _globalSink.Raise("workflow.failed", runId);
        }

        result.CompletedAt = DateTime.UtcNow;
        await _storage.CompleteRunAsync(runId, result.Status, JsonSerializer.Serialize(result.FinalSignals));

        return result;
    }

    private async Task ExecuteNodeAsync(WorkflowNodeExecution execution, WorkflowSignals signals, string runId)
    {
        var node = execution.Node;

        if (!_atomExecutors.TryGetValue(node.ManifestName, out var executor))
        {
            _signalRCoordinator.EmitLog(runId, node.Id, $"No executor found for: {node.ManifestName}");
            return;
        }

        _signalRCoordinator.EmitLog(runId, node.Id, $"Executing: {node.ManifestName}");

        var context = new WorkflowAtomContext
        {
            NodeId = node.Id,
            RunId = runId,
            Signals = signals,
            Ollama = _ollama,
            Storage = _storage,
            Config = execution.Config
        };

        try
        {
            await executor(context);
            _signalRCoordinator.EmitLog(runId, node.Id, "Completed");
            _globalSink.Raise($"node.{node.Id}.completed", node.ManifestName);
        }
        catch (Exception ex)
        {
            _signalRCoordinator.EmitLog(runId, node.Id, $"Error: {ex.Message}");
            _globalSink.Raise($"node.{node.Id}.failed", ex.Message);
            throw;
        }
    }

    private static bool IsEntryPoint(WorkflowNode node, WorkflowDefinition workflow)
    {
        // A node is an entry point if no edges target it
        return !workflow.Edges.Any(e => e.TargetNodeId == node.Id);
    }

    private static List<WorkflowNode> TopologicalSort(WorkflowDefinition workflow)
    {
        var inDegree = workflow.Nodes.ToDictionary(n => n.Id, _ => 0);
        var adjacency = workflow.Nodes.ToDictionary(n => n.Id, _ => new List<string>());

        foreach (var edge in workflow.Edges)
        {
            if (adjacency.ContainsKey(edge.SourceNodeId) && inDegree.ContainsKey(edge.TargetNodeId))
            {
                adjacency[edge.SourceNodeId].Add(edge.TargetNodeId);
                inDegree[edge.TargetNodeId]++;
            }
        }

        // Kahn's algorithm
        var queue = new Queue<string>(inDegree.Where(kvp => kvp.Value == 0).Select(kvp => kvp.Key));
        var result = new List<WorkflowNode>();

        while (queue.Count > 0)
        {
            var nodeId = queue.Dequeue();
            var node = workflow.Nodes.First(n => n.Id == nodeId);
            result.Add(node);

            foreach (var neighbor in adjacency[nodeId])
            {
                inDegree[neighbor]--;
                if (inDegree[neighbor] == 0)
                {
                    queue.Enqueue(neighbor);
                }
            }
        }

        // If we couldn't sort all nodes, there's a cycle - return original order
        if (result.Count != workflow.Nodes.Count)
        {
            return workflow.Nodes.ToList();
        }

        return result;
    }

    public async ValueTask DisposeAsync()
    {
        await _storage.DisposeAsync();
        await _signalRCoordinator.DisposeAsync();
    }
}

public sealed record WorkflowNodeExecution(WorkflowNode Node, Dictionary<string, object> Config);

public class WorkflowExecutionResult
{
    public required string RunId { get; init; }
    public required string WorkflowId { get; init; }
    public required string WorkflowName { get; init; }
    public DateTime StartedAt { get; init; }
    public DateTime? CompletedAt { get; set; }
    public string Status { get; set; } = "running";
    public string? Error { get; set; }
    public Dictionary<string, object?> FinalSignals { get; set; } = [];
}
