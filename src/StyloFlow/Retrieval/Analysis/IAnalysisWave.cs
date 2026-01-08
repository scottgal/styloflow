namespace StyloFlow.Retrieval.Analysis;

/// <summary>
/// Interface for pluggable analysis components that produce signals.
/// Waves are composable analyzers that run in priority order.
/// This is the base interface - domain-specific packages extend it.
/// </summary>
public interface IAnalysisWave
{
    /// <summary>
    /// Unique name identifying this analysis wave.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Human-readable description of what this wave analyzes.
    /// </summary>
    string Description { get; }

    /// <summary>
    /// Priority for execution order. Higher priority waves run first (100 = high, 0 = low).
    /// Allows dependencies between waves.
    /// </summary>
    int Priority { get; }

    /// <summary>
    /// Tags describing what category of analysis this wave provides.
    /// </summary>
    IReadOnlyList<string> Tags { get; }

    /// <summary>
    /// Whether this wave is enabled.
    /// </summary>
    bool Enabled { get; set; }
}

/// <summary>
/// Analysis wave for generic content (path-based).
/// </summary>
public interface IContentAnalysisWave : IAnalysisWave
{
    /// <summary>
    /// Check if this wave should run based on cheap preconditions.
    /// </summary>
    bool ShouldRun(string contentPath, AnalysisContext context) => true;

    /// <summary>
    /// Analyze content and produce signals.
    /// </summary>
    Task<IEnumerable<Signal>> AnalyzeAsync(
        string contentPath,
        AnalysisContext context,
        CancellationToken ct = default);
}

/// <summary>
/// Analysis wave for in-memory content.
/// </summary>
/// <typeparam name="T">Type of content to analyze.</typeparam>
public interface ITypedAnalysisWave<T> : IAnalysisWave
{
    /// <summary>
    /// Check if this wave should run.
    /// </summary>
    bool ShouldRun(T content, AnalysisContext context) => true;

    /// <summary>
    /// Analyze content and produce signals.
    /// </summary>
    Task<IEnumerable<Signal>> AnalyzeAsync(
        T content,
        AnalysisContext context,
        CancellationToken ct = default);
}

/// <summary>
/// Result from wave execution with success/failure tracking.
/// </summary>
public record WaveResult
{
    public bool IsSuccess { get; init; }
    public string? Error { get; init; }
    public IReadOnlyList<Signal> Signals { get; init; } = Array.Empty<Signal>();
    public TimeSpan Duration { get; init; }

    public static WaveResult Success(IEnumerable<Signal> signals, TimeSpan duration) =>
        new() { IsSuccess = true, Signals = signals.ToList(), Duration = duration };

    public static WaveResult Failure(string error, TimeSpan duration) =>
        new() { IsSuccess = false, Error = error, Duration = duration };
}
