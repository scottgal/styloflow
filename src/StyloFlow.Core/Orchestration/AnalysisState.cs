namespace StyloFlow.Orchestration;

/// <summary>
/// Immutable snapshot of analysis state passed to components.
/// Contains all signals from prior components.
///
/// CRITICAL DATA HANDLING RULES (ported from BotDetection's BlackboardState):
/// - Sensitive data (PII, credentials, etc.) is accessed ONLY via direct properties
/// - Sensitive data must NEVER be placed in signal payloads
/// - Signals contain ONLY boolean indicators or hashed values
/// - Raw sensitive data exists in memory only as long as components need it
/// </summary>
public class AnalysisState
{
    /// <summary>
    /// All signals collected so far.
    /// IMPORTANT: Signals must NEVER contain raw sensitive data.
    /// </summary>
    public IReadOnlyDictionary<string, object> Signals { get; init; } = new Dictionary<string, object>();

    /// <summary>
    /// Current aggregated score (0.0 to 1.0).
    /// </summary>
    public double CurrentScore { get; init; }

    /// <summary>
    /// Which components have already run.
    /// </summary>
    public IReadOnlySet<string> CompletedComponents { get; init; } = new HashSet<string>();

    /// <summary>
    /// Which components failed.
    /// </summary>
    public IReadOnlySet<string> FailedComponents { get; init; } = new HashSet<string>();

    /// <summary>
    /// Request/analysis ID for correlation.
    /// </summary>
    public string RequestId { get; init; } = Guid.NewGuid().ToString("N");

    /// <summary>
    /// Time elapsed since analysis started.
    /// </summary>
    public TimeSpan Elapsed { get; init; }

    /// <summary>
    /// Metadata for the current analysis run.
    /// </summary>
    public IReadOnlyDictionary<string, object> Metadata { get; init; } = new Dictionary<string, object>();

    /// <summary>
    /// Get a typed signal value.
    /// IMPORTANT: Signals should NEVER contain raw sensitive data.
    /// </summary>
    public T? GetSignal<T>(string key)
        => Signals.TryGetValue(key, out var value) && value is T typed ? typed : default;

    /// <summary>
    /// Check if a signal exists.
    /// </summary>
    public bool HasSignal(string key)
        => Signals.ContainsKey(key);

    /// <summary>
    /// Check if all trigger conditions are satisfied.
    /// </summary>
    public bool AllTriggersSatisfied(IReadOnlyList<TriggerCondition> conditions)
        => conditions.All(c => c.IsSatisfied(Signals));

    /// <summary>
    /// Check if any trigger conditions are satisfied.
    /// </summary>
    public bool AnyTriggerSatisfied(IReadOnlyList<TriggerCondition> conditions)
        => conditions.Any(c => c.IsSatisfied(Signals));
}

/// <summary>
/// Builder for creating AnalysisState with proper lifecycle management.
/// </summary>
public class AnalysisStateBuilder
{
    private readonly Dictionary<string, object> _signals = new();
    private readonly HashSet<string> _completed = new();
    private readonly HashSet<string> _failed = new();
    private readonly Dictionary<string, object> _metadata = new();
    private double _score;
    private string _requestId = Guid.NewGuid().ToString("N");
    private TimeSpan _elapsed;

    public AnalysisStateBuilder WithSignal(string key, object value)
    {
        _signals[key] = value;
        return this;
    }

    public AnalysisStateBuilder WithScore(double score)
    {
        _score = score;
        return this;
    }

    public AnalysisStateBuilder WithCompletedComponent(string name)
    {
        _completed.Add(name);
        return this;
    }

    public AnalysisStateBuilder WithFailedComponent(string name)
    {
        _failed.Add(name);
        return this;
    }

    public AnalysisStateBuilder WithRequestId(string requestId)
    {
        _requestId = requestId;
        return this;
    }

    public AnalysisStateBuilder WithElapsed(TimeSpan elapsed)
    {
        _elapsed = elapsed;
        return this;
    }

    public AnalysisStateBuilder WithMetadata(string key, object value)
    {
        _metadata[key] = value;
        return this;
    }

    public AnalysisState Build() => new()
    {
        Signals = _signals,
        CurrentScore = _score,
        CompletedComponents = _completed,
        FailedComponents = _failed,
        RequestId = _requestId,
        Elapsed = _elapsed,
        Metadata = _metadata
    };
}

/// <summary>
/// Helper for emitting signals without accidentally including sensitive data.
/// </summary>
public static class SignalSafetyHelper
{
    /// <summary>
    /// Emit a boolean indicator signal (safe for logging/storage).
    /// </summary>
    public static KeyValuePair<string, object> Indicator(string key, bool value)
        => new(key, value);

    /// <summary>
    /// Emit a hashed value signal (safe for logging/storage).
    /// </summary>
    public static KeyValuePair<string, object> HashedValue(string key, string rawValue)
    {
        var hash = Convert.ToHexString(
            System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes(rawValue ?? "")));
        return new(key, hash[..16]); // Truncated hash is sufficient for matching
    }

    /// <summary>
    /// Emit a numeric score signal (safe for logging/storage).
    /// </summary>
    public static KeyValuePair<string, object> Score(string key, double value)
        => new(key, Math.Round(value, 4));

    /// <summary>
    /// Emit a category signal (safe for logging/storage).
    /// </summary>
    public static KeyValuePair<string, object> Category(string key, string category)
        => new(key, category);
}
