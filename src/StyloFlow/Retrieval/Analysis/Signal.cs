namespace StyloFlow.Retrieval.Analysis;

/// <summary>
/// Represents a single analytical signal produced by an analysis wave.
/// Signals are atomic units of information with confidence and provenance.
/// This is the universal output type for all domain-specific analyzers.
/// </summary>
public record Signal
{
    /// <summary>
    /// Unique key identifying what this signal measures.
    /// Convention: "domain.category.metric" (e.g., "image.color.dominant", "doc.structure.heading_count")
    /// </summary>
    public required string Key { get; init; }

    /// <summary>
    /// The measured value. Can be any serializable type.
    /// </summary>
    public object? Value { get; init; }

    /// <summary>
    /// Confidence score (0.0 - 1.0) indicating reliability of this signal.
    /// </summary>
    public double Confidence { get; init; } = 1.0;

    /// <summary>
    /// Source analyzer that produced this signal.
    /// </summary>
    public required string Source { get; init; }

    /// <summary>
    /// When this signal was generated.
    /// </summary>
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;

    /// <summary>
    /// Optional metadata about how this signal was computed.
    /// </summary>
    public Dictionary<string, object>? Metadata { get; init; }

    /// <summary>
    /// Data type of the value for serialization/deserialization.
    /// </summary>
    public string? ValueType { get; init; }

    /// <summary>
    /// Optional tags for categorization.
    /// </summary>
    public List<string>? Tags { get; init; }

    /// <summary>
    /// Get typed value or default.
    /// </summary>
    public T? GetValue<T>() => Value is T typed ? typed : default;
}

/// <summary>
/// Aggregation strategy for combining multiple signals for the same key.
/// </summary>
public enum AggregationStrategy
{
    /// <summary>Take the signal with highest confidence.</summary>
    HighestConfidence,
    /// <summary>Take the most recent signal.</summary>
    MostRecent,
    /// <summary>Average numeric values weighted by confidence.</summary>
    WeightedAverage,
    /// <summary>Majority vote for categorical values.</summary>
    MajorityVote,
    /// <summary>Merge all signals into a collection.</summary>
    Collect
}

/// <summary>
/// Standard tags for categorizing signals across domains.
/// </summary>
public static class SignalTags
{
    // Cross-domain
    public const string Quality = "quality";
    public const string Identity = "identity";
    public const string Metadata = "metadata";
    public const string Content = "content";
    public const string Structure = "structure";
    public const string Embedding = "embedding";

    // Document-specific
    public const string Semantic = "semantic";
    public const string Lexical = "lexical";
    public const string Position = "position";

    // Image-specific
    public const string Visual = "visual";
    public const string Color = "color";
    public const string Forensic = "forensic";

    // Audio-specific
    public const string Acoustic = "acoustic";
    public const string Speech = "speech";
    public const string Music = "music";

    // Video-specific
    public const string Motion = "motion";
    public const string Temporal = "temporal";
    public const string Scene = "scene";
}
