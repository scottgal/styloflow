namespace StyloFlow.Ingestion.Configuration;

/// <summary>
/// Root configuration for ingestion pipelines.
/// Can be loaded from YAML/JSON configuration files.
/// </summary>
public class IngestionConfig
{
    /// <summary>
    /// Global defaults applied to all sources.
    /// </summary>
    public IngestionDefaults Defaults { get; set; } = new();

    /// <summary>
    /// Named source configurations.
    /// </summary>
    public Dictionary<string, SourceConfig> Sources { get; set; } = [];

    /// <summary>
    /// Named pipeline configurations.
    /// </summary>
    public Dictionary<string, PipelineConfig> Pipelines { get; set; } = [];

    /// <summary>
    /// Schedule configurations for automated sync.
    /// </summary>
    public Dictionary<string, ScheduleConfig> Schedules { get; set; } = [];
}

/// <summary>
/// Global defaults for ingestion.
/// </summary>
public class IngestionDefaults
{
    /// <summary>
    /// Default concurrency for parallel processing.
    /// </summary>
    public int Concurrency { get; set; } = 4;

    /// <summary>
    /// Default batch size for processing.
    /// </summary>
    public int BatchSize { get; set; } = 100;

    /// <summary>
    /// Maximum file size in bytes (default 100MB).
    /// </summary>
    public long MaxFileSizeBytes { get; set; } = 100 * 1024 * 1024;

    /// <summary>
    /// Default timeout per item in seconds.
    /// </summary>
    public int TimeoutSeconds { get; set; } = 300;

    /// <summary>
    /// Retry settings.
    /// </summary>
    public RetryConfig Retry { get; set; } = new();

    /// <summary>
    /// Default file patterns to include.
    /// </summary>
    public string[] IncludePatterns { get; set; } = ["**/*"];

    /// <summary>
    /// Default patterns to exclude.
    /// </summary>
    public string[] ExcludePatterns { get; set; } =
    [
        "**/node_modules/**",
        "**/.git/**",
        "**/bin/**",
        "**/obj/**",
        "**/.vs/**",
        "**/packages/**"
    ];

    /// <summary>
    /// Whether to process hidden files by default.
    /// </summary>
    public bool IncludeHidden { get; set; } = false;

    /// <summary>
    /// Default deduplication strategy.
    /// </summary>
    public DeduplicationStrategy Deduplication { get; set; } = DeduplicationStrategy.ContentHash;
}

/// <summary>
/// Configuration for a specific source.
/// </summary>
public class SourceConfig
{
    /// <summary>
    /// Source type (directory, github, s3, ftp, etc.)
    /// </summary>
    public required string Type { get; set; }

    /// <summary>
    /// Whether this source is enabled.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Source location (path, URL, bucket, etc.)
    /// </summary>
    public required string Location { get; set; }

    /// <summary>
    /// Credentials reference or inline value.
    /// Can be: "${ENV_VAR}", "secret:name", or literal value.
    /// </summary>
    public string? Credentials { get; set; }

    /// <summary>
    /// Target collection for ingested content.
    /// </summary>
    public string? Collection { get; set; }

    /// <summary>
    /// Tenant ID for multi-tenant scenarios.
    /// </summary>
    public string? Tenant { get; set; }

    /// <summary>
    /// Filter configuration (overrides defaults).
    /// </summary>
    public FilterConfig? Filters { get; set; }

    /// <summary>
    /// Processing options (overrides defaults).
    /// </summary>
    public ProcessingConfig? Processing { get; set; }

    /// <summary>
    /// Tags to apply to all content from this source.
    /// </summary>
    public string[]? Tags { get; set; }

    /// <summary>
    /// Custom metadata to attach.
    /// </summary>
    public Dictionary<string, object>? Metadata { get; set; }

    /// <summary>
    /// Source-specific options.
    /// </summary>
    public Dictionary<string, object>? Options { get; set; }
}

/// <summary>
/// Filter configuration for selecting content.
/// </summary>
public class FilterConfig
{
    /// <summary>
    /// Glob patterns to include.
    /// </summary>
    public string[]? Include { get; set; }

    /// <summary>
    /// Glob patterns to exclude.
    /// </summary>
    public string[]? Exclude { get; set; }

    /// <summary>
    /// Minimum file size in bytes.
    /// </summary>
    public long? MinSizeBytes { get; set; }

    /// <summary>
    /// Maximum file size in bytes.
    /// </summary>
    public long? MaxSizeBytes { get; set; }

    /// <summary>
    /// Only include files modified after this date.
    /// </summary>
    public DateTimeOffset? ModifiedAfter { get; set; }

    /// <summary>
    /// Only include files modified before this date.
    /// </summary>
    public DateTimeOffset? ModifiedBefore { get; set; }

    /// <summary>
    /// Include hidden files/directories.
    /// </summary>
    public bool? IncludeHidden { get; set; }

    /// <summary>
    /// Recursive directory traversal.
    /// </summary>
    public bool? Recursive { get; set; }

    /// <summary>
    /// MIME types to include.
    /// </summary>
    public string[]? MimeTypes { get; set; }

    /// <summary>
    /// File extensions to include (with dot, e.g., ".md").
    /// </summary>
    public string[]? Extensions { get; set; }

    /// <summary>
    /// Maximum directory depth.
    /// </summary>
    public int? MaxDepth { get; set; }
}

/// <summary>
/// Processing configuration.
/// </summary>
public class ProcessingConfig
{
    /// <summary>
    /// Number of concurrent items to process.
    /// </summary>
    public int? Concurrency { get; set; }

    /// <summary>
    /// Batch size for bulk operations.
    /// </summary>
    public int? BatchSize { get; set; }

    /// <summary>
    /// Timeout per item in seconds.
    /// </summary>
    public int? TimeoutSeconds { get; set; }

    /// <summary>
    /// Retry configuration.
    /// </summary>
    public RetryConfig? Retry { get; set; }

    /// <summary>
    /// Deduplication strategy.
    /// </summary>
    public DeduplicationStrategy? Deduplication { get; set; }

    /// <summary>
    /// Whether to extract text from documents.
    /// </summary>
    public bool ExtractText { get; set; } = true;

    /// <summary>
    /// Whether to generate embeddings.
    /// </summary>
    public bool GenerateEmbeddings { get; set; } = true;

    /// <summary>
    /// Whether to extract entities.
    /// </summary>
    public bool ExtractEntities { get; set; } = false;

    /// <summary>
    /// Whether to generate thumbnails for images.
    /// </summary>
    public bool GenerateThumbnails { get; set; } = true;

    /// <summary>
    /// Whether to perform OCR on images.
    /// </summary>
    public bool PerformOcr { get; set; } = true;
}

/// <summary>
/// Retry configuration.
/// </summary>
public class RetryConfig
{
    /// <summary>
    /// Maximum number of retry attempts.
    /// </summary>
    public int MaxAttempts { get; set; } = 3;

    /// <summary>
    /// Initial delay between retries in milliseconds.
    /// </summary>
    public int InitialDelayMs { get; set; } = 1000;

    /// <summary>
    /// Maximum delay between retries in milliseconds.
    /// </summary>
    public int MaxDelayMs { get; set; } = 30000;

    /// <summary>
    /// Exponential backoff multiplier.
    /// </summary>
    public double BackoffMultiplier { get; set; } = 2.0;
}

/// <summary>
/// Pipeline configuration for composing multiple sources.
/// </summary>
public class PipelineConfig
{
    /// <summary>
    /// Whether this pipeline is enabled.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Sources included in this pipeline.
    /// Can reference named sources or define inline.
    /// </summary>
    public string[] Sources { get; set; } = [];

    /// <summary>
    /// Processing configuration for the pipeline.
    /// </summary>
    public ProcessingConfig? Processing { get; set; }

    /// <summary>
    /// Schedule for automated runs.
    /// </summary>
    public string? Schedule { get; set; }

    /// <summary>
    /// Notification settings.
    /// </summary>
    public NotificationConfig? Notifications { get; set; }
}

/// <summary>
/// Schedule configuration.
/// </summary>
public class ScheduleConfig
{
    /// <summary>
    /// Cron expression for scheduling.
    /// </summary>
    public string? Cron { get; set; }

    /// <summary>
    /// Interval in minutes (alternative to cron).
    /// </summary>
    public int? IntervalMinutes { get; set; }

    /// <summary>
    /// Whether the schedule is enabled.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Timezone for cron expression.
    /// </summary>
    public string? Timezone { get; set; }
}

/// <summary>
/// Notification configuration.
/// </summary>
public class NotificationConfig
{
    /// <summary>
    /// Notify on completion.
    /// </summary>
    public bool OnComplete { get; set; } = false;

    /// <summary>
    /// Notify on error.
    /// </summary>
    public bool OnError { get; set; } = true;

    /// <summary>
    /// Webhook URL for notifications.
    /// </summary>
    public string? WebhookUrl { get; set; }

    /// <summary>
    /// Email addresses for notifications.
    /// </summary>
    public string[]? Emails { get; set; }
}

/// <summary>
/// Deduplication strategies.
/// </summary>
public enum DeduplicationStrategy
{
    /// <summary>
    /// No deduplication - always process.
    /// </summary>
    None,

    /// <summary>
    /// Deduplicate by content hash.
    /// </summary>
    ContentHash,

    /// <summary>
    /// Deduplicate by path and modification time.
    /// </summary>
    PathAndTime,

    /// <summary>
    /// Deduplicate by path only (replace on re-ingest).
    /// </summary>
    PathOnly
}
