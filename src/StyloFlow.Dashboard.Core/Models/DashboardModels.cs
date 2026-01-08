namespace StyloFlow.Dashboard.Models;

/// <summary>
/// Base record for dashboard events. Extend this for pack-specific events.
/// </summary>
public record DashboardEvent
{
    /// <summary>
    /// Unique identifier for the event.
    /// </summary>
    public required string EventId { get; init; }

    /// <summary>
    /// Timestamp when the event occurred.
    /// </summary>
    public required DateTime Timestamp { get; init; }

    /// <summary>
    /// Type/category of the event.
    /// </summary>
    public required string EventType { get; init; }

    /// <summary>
    /// Severity level (Info, Warning, Error, Critical).
    /// </summary>
    public string Severity { get; init; } = "Info";

    /// <summary>
    /// Source component or pack that generated this event.
    /// </summary>
    public string? Source { get; init; }

    /// <summary>
    /// Human-readable message describing the event.
    /// </summary>
    public string? Message { get; init; }

    /// <summary>
    /// Additional metadata as key-value pairs.
    /// </summary>
    public Dictionary<string, object> Metadata { get; init; } = new();

    /// <summary>
    /// Tags for filtering and categorization.
    /// </summary>
    public List<string> Tags { get; init; } = new();

    /// <summary>
    /// Processing time in milliseconds (if applicable).
    /// </summary>
    public double? ProcessingTimeMs { get; init; }
}

/// <summary>
/// Base record for dashboard summaries. Extend this for pack-specific summaries.
/// </summary>
public record DashboardSummary
{
    /// <summary>
    /// Timestamp when the summary was generated.
    /// </summary>
    public required DateTime Timestamp { get; init; }

    /// <summary>
    /// Total number of events in the time window.
    /// </summary>
    public required int TotalEvents { get; init; }

    /// <summary>
    /// Events grouped by type.
    /// </summary>
    public Dictionary<string, int> EventsByType { get; init; } = new();

    /// <summary>
    /// Events grouped by severity.
    /// </summary>
    public Dictionary<string, int> EventsBySeverity { get; init; } = new();

    /// <summary>
    /// Events grouped by source/pack.
    /// </summary>
    public Dictionary<string, int> EventsBySource { get; init; } = new();

    /// <summary>
    /// Average processing time in milliseconds.
    /// </summary>
    public double AverageProcessingTimeMs { get; init; }

    /// <summary>
    /// Events per second rate.
    /// </summary>
    public double EventsPerSecond { get; init; }

    /// <summary>
    /// Time window for this summary in seconds.
    /// </summary>
    public int TimeWindowSeconds { get; init; }
}

/// <summary>
/// Filter options for querying dashboard events.
/// </summary>
public sealed record DashboardFilter
{
    /// <summary>
    /// Start time for filtering events.
    /// </summary>
    public DateTime? StartTime { get; init; }

    /// <summary>
    /// End time for filtering events.
    /// </summary>
    public DateTime? EndTime { get; init; }

    /// <summary>
    /// Filter by event types.
    /// </summary>
    public List<string>? EventTypes { get; init; }

    /// <summary>
    /// Filter by severity levels.
    /// </summary>
    public List<string>? Severities { get; init; }

    /// <summary>
    /// Filter by source/pack.
    /// </summary>
    public List<string>? Sources { get; init; }

    /// <summary>
    /// Filter by tags (any match).
    /// </summary>
    public List<string>? Tags { get; init; }

    /// <summary>
    /// Text search in message field.
    /// </summary>
    public string? MessageContains { get; init; }

    /// <summary>
    /// Maximum number of results to return.
    /// </summary>
    public int Limit { get; init; } = 100;

    /// <summary>
    /// Number of results to skip (for pagination).
    /// </summary>
    public int Offset { get; init; } = 0;
}

/// <summary>
/// Time series data point for charts.
/// </summary>
public sealed record TimeSeriesPoint
{
    /// <summary>
    /// Timestamp for this bucket.
    /// </summary>
    public required DateTime Timestamp { get; init; }

    /// <summary>
    /// Total count in this bucket.
    /// </summary>
    public int TotalCount { get; init; }

    /// <summary>
    /// Counts by event type.
    /// </summary>
    public Dictionary<string, int> CountsByType { get; init; } = new();

    /// <summary>
    /// Counts by severity.
    /// </summary>
    public Dictionary<string, int> CountsBySeverity { get; init; } = new();

    /// <summary>
    /// Average processing time in this bucket.
    /// </summary>
    public double AverageProcessingTimeMs { get; init; }
}
