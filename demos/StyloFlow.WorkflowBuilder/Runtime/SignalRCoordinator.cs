using Microsoft.AspNetCore.SignalR;
using Mostlylucid.Ephemeral;
using StyloFlow.WorkflowBuilder.Hubs;

namespace StyloFlow.WorkflowBuilder.Runtime;

/// <summary>
/// Singleton coordinator that listens for signalr.* signals and broadcasts them via SignalR.
/// Uses Ephemeral's EphemeralWorkCoordinator for proper signal-driven coordination.
///
/// Signal patterns:
/// - signalr.{userId}.log     → Sends to specific user
/// - signalr.all.log          → Broadcasts to all
/// - signalr.{userId}.signal  → Signal event to specific user
/// - signalr.all.signal       → Signal event to all
/// </summary>
public sealed class SignalRCoordinator : IAsyncDisposable
{
    private readonly IHubContext<WorkflowHub> _hubContext;
    private readonly SignalSink _globalSink;
    private readonly EphemeralWorkCoordinator<SignalRMessage> _coordinator;
    private readonly IDisposable _subscription;
    private readonly string _signalPattern;

    /// <summary>
    /// Create a SignalR coordinator with configurable signal pattern.
    /// Default pattern: "signalr.*.*" matches signalr.{target}.{method}
    /// Use "signalr.**" to match any depth.
    /// </summary>
    public SignalRCoordinator(
        IHubContext<WorkflowHub> hubContext,
        SignalSink globalSink,
        string signalPattern = "signalr.*.*")
    {
        _hubContext = hubContext;
        _globalSink = globalSink;
        _signalPattern = signalPattern;

        // Create coordinator that processes SignalR messages
        _coordinator = new EphemeralWorkCoordinator<SignalRMessage>(
            ProcessMessageAsync,
            new EphemeralOptions
            {
                MaxConcurrency = 4,
                Signals = globalSink,
                MaxTrackedOperations = 100,
                MaxOperationLifetime = TimeSpan.FromMinutes(1)
            });

        // Subscribe to all signals and filter for signalr.* pattern
        _subscription = _globalSink.Subscribe(OnSignalRaised);
    }

    private void OnSignalRaised(SignalEvent signal)
    {
        // Use Ephemeral's StringPatternMatcher for glob matching
        // Pattern: signalr.*.* matches signalr.{target}.{method}
        if (!StringPatternMatcher.Matches(signal.Signal, _signalPattern))
            return;

        // Parse: signalr.{target}.{method}
        var parts = signal.Signal.Split('.', 3);
        if (parts.Length < 3) return;

        var target = parts[1]; // userId or "all"
        var method = parts[2]; // "log", "signal", etc.

        var message = new SignalRMessage(
            Target: target,
            Method: method,
            Payload: signal.Key,
            Timestamp: signal.Timestamp,
            OriginalSignal: signal.Signal,
            OperationId: signal.OperationId);

        // Enqueue for processing (non-blocking)
        // Each send is an ephemeral operation - the coordinator handles it
        _coordinator.TryEnqueue(message);
    }

    private async Task ProcessMessageAsync(SignalRMessage message, CancellationToken ct)
    {
        try
        {
            if (message.Target.Equals("all", StringComparison.OrdinalIgnoreCase))
            {
                // Broadcast to all connected clients
                await _hubContext.Clients.All.SendAsync(
                    GetMethodName(message.Method),
                    message.Payload,
                    ct);
            }
            else
            {
                // Send to specific user/connection
                await _hubContext.Clients.User(message.Target).SendAsync(
                    GetMethodName(message.Method),
                    message.Payload,
                    ct);
            }

            // Trim the processed signal to avoid accumulating billions
            // ClearPattern removes all signals matching the exact pattern that was sent
            _globalSink.ClearOperation(message.OperationId);
        }
        catch (Exception ex)
        {
            // Emit failure signal (these are transient, will be trimmed on next cycle)
            _globalSink.Raise($"signalr.failed.{message.Target}", ex.Message);
        }
    }

    private static string GetMethodName(string method) => method.ToLowerInvariant() switch
    {
        "log" => "ExecutionLog",
        "signal" => "SignalEmitted",
        "status" => "WorkflowStatus",
        "error" => "ExecutionError",
        "complete" => "WorkflowComplete",
        _ => method
    };

    /// <summary>
    /// Emit a log message to SignalR via the signal system.
    /// </summary>
    public void EmitLog(string runId, string nodeId, string message, string? userId = null)
    {
        var target = userId ?? "all";
        _globalSink.Raise($"signalr.{target}.log", $"{runId}:{nodeId}:{message}");
    }

    /// <summary>
    /// Emit a signal event to SignalR via the signal system.
    /// </summary>
    public void EmitSignal(string signalKey, object? value, string sourceNode, double confidence, string? userId = null)
    {
        var target = userId ?? "all";
        var payload = new { Key = signalKey, Value = value, SourceNode = sourceNode, Confidence = confidence };
        _globalSink.Raise($"signalr.{target}.signal", System.Text.Json.JsonSerializer.Serialize(payload));
    }

    public async ValueTask DisposeAsync()
    {
        _subscription.Dispose();
        _coordinator.Complete();
        await _coordinator.DrainAsync();
    }
}

public sealed record SignalRMessage(
    string Target,
    string Method,
    string? Payload,
    DateTimeOffset Timestamp,
    string OriginalSignal,
    long OperationId);
