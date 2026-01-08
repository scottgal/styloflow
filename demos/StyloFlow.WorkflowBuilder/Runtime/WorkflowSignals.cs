using Mostlylucid.Ephemeral;

namespace StyloFlow.WorkflowBuilder.Runtime;

/// <summary>
/// Wrapper around Ephemeral's SignalSink providing workflow-specific helpers.
/// Uses the real SignalSink from Ephemeral - not a custom reimplementation.
/// </summary>
public sealed class WorkflowSignals : IDisposable
{
    private readonly SignalSink _sink;
    private readonly TypedSignalSink<WorkflowPayload> _typedSink;
    private readonly List<IDisposable> _subscriptions = [];
    private readonly string _runId;
    private long _operationId;

    public SignalSink Sink => _sink;
    public TypedSignalSink<WorkflowPayload> TypedSink => _typedSink;
    public string RunId => _runId;

    public WorkflowSignals(string runId, SignalSink? sharedSink = null)
    {
        _runId = runId;
        _sink = sharedSink ?? new SignalSink(maxCapacity: 1000, maxAge: TimeSpan.FromMinutes(5));
        _typedSink = new TypedSignalSink<WorkflowPayload>(_sink);
        _operationId = DateTime.UtcNow.Ticks;
    }

    /// <summary>
    /// Emit a signal with a value. Uses Ephemeral's SignalSink under the hood.
    /// </summary>
    public void Emit(string signal, object? value, string sourceNode, double confidence = 1.0)
    {
        // Create typed payload
        var payload = new WorkflowPayload(signal, value, sourceNode, confidence, _runId);

        // Emit to typed sink (which also raises on untyped sink)
        _typedSink.Raise(signal, payload, key: $"{_runId}:{sourceNode}");
    }

    /// <summary>
    /// Get a signal value by pattern matching recent signals.
    /// </summary>
    public T? Get<T>(string signalPattern)
    {
        var signals = _sink.Sense(e => e.Is(signalPattern) || e.StartsWith(signalPattern));
        if (signals.Count == 0) return default;

        // Get most recent matching signal
        var latest = signals.OrderByDescending(s => s.Timestamp).FirstOrDefault();

        // For typed signals, we need to look in the typed sink
        var typedSignals = _typedSink.Sense(e => e.Signal == signalPattern);
        if (typedSignals.Count > 0)
        {
            var typedLatest = typedSignals.OrderByDescending(s => s.Timestamp).First();
            if (typedLatest.Payload.Value is T typed)
                return typed;
        }

        return default;
    }

    /// <summary>
    /// Check if a signal exists in the window.
    /// </summary>
    public bool Has(string signalPattern) => _sink.Detect(signalPattern);

    /// <summary>
    /// Subscribe to all signals for broadcasting (e.g., to SignalR).
    /// </summary>
    public IDisposable Subscribe(Action<SignalEvent> handler)
    {
        var sub = _sink.Subscribe(handler);
        _subscriptions.Add(sub);
        return sub;
    }

    /// <summary>
    /// Subscribe to typed workflow signals.
    /// </summary>
    public void SubscribeTyped(Action<SignalEvent<WorkflowPayload>> handler)
    {
        _typedSink.TypedSignalRaised += handler;
    }

    /// <summary>
    /// Get all signals in the current window.
    /// </summary>
    public IReadOnlyList<SignalEvent> GetAll() => _sink.Sense();

    /// <summary>
    /// Get signals for a specific operation/run.
    /// </summary>
    public IReadOnlyList<SignalEvent> GetRunSignals()
    {
        return _sink.Sense(e => e.Key?.StartsWith(_runId) == true);
    }

    public void Dispose()
    {
        foreach (var sub in _subscriptions)
        {
            sub.Dispose();
        }
        _subscriptions.Clear();
    }
}

/// <summary>
/// Typed payload for workflow signals, carrying metadata alongside the value.
/// </summary>
public sealed record WorkflowPayload(
    string Signal,
    object? Value,
    string SourceNode,
    double Confidence,
    string RunId)
{
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
}
