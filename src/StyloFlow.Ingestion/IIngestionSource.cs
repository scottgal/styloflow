namespace StyloFlow.Ingestion;

/// <summary>
/// Represents an item discovered by an ingestion source.
/// Contains metadata about the item before content is fetched.
/// </summary>
public record IngestionItem
{
    /// <summary>
    /// Relative path or identifier within the source.
    /// </summary>
    public required string Path { get; init; }

    /// <summary>
    /// Display name (filename, title, etc.)
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Size in bytes, if known.
    /// </summary>
    public long? SizeBytes { get; init; }

    /// <summary>
    /// Last modified timestamp, if known.
    /// </summary>
    public DateTimeOffset? ModifiedAt { get; init; }

    /// <summary>
    /// Content hash for deduplication, if available.
    /// </summary>
    public string? ContentHash { get; init; }

    /// <summary>
    /// MIME type if known.
    /// </summary>
    public string? MimeType { get; init; }

    /// <summary>
    /// Source-specific metadata.
    /// </summary>
    public Dictionary<string, object>? Metadata { get; init; }
}

/// <summary>
/// Result of fetching an item's content.
/// </summary>
public record IngestionContent
{
    /// <summary>
    /// The item this content belongs to.
    /// </summary>
    public required IngestionItem Item { get; init; }

    /// <summary>
    /// Content stream. Caller is responsible for disposal.
    /// </summary>
    public required Stream Content { get; init; }

    /// <summary>
    /// Actual MIME type of the content.
    /// </summary>
    public required string MimeType { get; init; }

    /// <summary>
    /// Content hash (computed during fetch if not available).
    /// </summary>
    public string? ContentHash { get; init; }
}

/// <summary>
/// Interface for content ingestion sources.
/// Implement this interface to add new source types (GitHub, S3, FTP, etc.)
/// </summary>
public interface IIngestionSource
{
    /// <summary>
    /// Unique identifier for this source type (e.g., "directory", "github", "s3").
    /// </summary>
    string SourceType { get; }

    /// <summary>
    /// Display name for this source type.
    /// </summary>
    string DisplayName { get; }

    /// <summary>
    /// Validate the source configuration.
    /// </summary>
    Task<SourceValidationResult> ValidateAsync(
        IngestionSourceConfig config,
        CancellationToken ct = default);

    /// <summary>
    /// Discover items available for ingestion.
    /// Returns an async enumerable for efficient streaming of large result sets.
    /// </summary>
    IAsyncEnumerable<IngestionItem> DiscoverAsync(
        IngestionSourceConfig config,
        DiscoveryOptions? options = null,
        CancellationToken ct = default);

    /// <summary>
    /// Fetch the content of a specific item.
    /// </summary>
    Task<IngestionContent> FetchAsync(
        IngestionSourceConfig config,
        IngestionItem item,
        CancellationToken ct = default);

    /// <summary>
    /// Check if an item has changed since last sync.
    /// Used for incremental ingestion.
    /// </summary>
    Task<bool> HasChangedAsync(
        IngestionSourceConfig config,
        IngestionItem item,
        string? lastKnownHash,
        DateTimeOffset? lastSyncTime,
        CancellationToken ct = default);
}

/// <summary>
/// Configuration for an ingestion source.
/// </summary>
public record IngestionSourceConfig
{
    /// <summary>
    /// Unique identifier for this source instance.
    /// </summary>
    public Guid Id { get; init; } = Guid.NewGuid();

    /// <summary>
    /// Source type (must match IIngestionSource.SourceType).
    /// </summary>
    public required string SourceType { get; init; }

    /// <summary>
    /// Human-readable name for this source.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Primary location (path, URL, bucket name, etc.)
    /// </summary>
    public required string Location { get; init; }

    /// <summary>
    /// Credentials or connection string (encrypted in production).
    /// </summary>
    public string? Credentials { get; init; }

    /// <summary>
    /// File pattern filter (glob pattern, e.g., "*.md", "**/*.pdf").
    /// </summary>
    public string? FilePattern { get; init; }

    /// <summary>
    /// Patterns to exclude.
    /// </summary>
    public string[]? ExcludePatterns { get; init; }

    /// <summary>
    /// Whether to recurse into subdirectories.
    /// </summary>
    public bool Recursive { get; init; } = true;

    /// <summary>
    /// Target collection ID for ingested content.
    /// </summary>
    public Guid? CollectionId { get; init; }

    /// <summary>
    /// Tenant ID for multi-tenant scenarios.
    /// </summary>
    public string? TenantId { get; init; }

    /// <summary>
    /// Source-specific options.
    /// </summary>
    public Dictionary<string, object>? Options { get; init; }

    /// <summary>
    /// Tags to apply to ingested content.
    /// </summary>
    public string[]? Tags { get; init; }

    /// <summary>
    /// Whether this source is enabled.
    /// </summary>
    public bool Enabled { get; init; } = true;

    /// <summary>
    /// Sync schedule (cron expression or interval).
    /// </summary>
    public string? Schedule { get; init; }
}

/// <summary>
/// Options for item discovery.
/// </summary>
public record DiscoveryOptions
{
    /// <summary>
    /// Only discover items modified since this time.
    /// </summary>
    public DateTimeOffset? ModifiedSince { get; init; }

    /// <summary>
    /// Maximum number of items to discover.
    /// </summary>
    public int? MaxItems { get; init; }

    /// <summary>
    /// Skip items with these hashes (for incremental sync).
    /// </summary>
    public HashSet<string>? ExcludeHashes { get; init; }

    /// <summary>
    /// Include hidden files/directories.
    /// </summary>
    public bool IncludeHidden { get; init; } = false;
}

/// <summary>
/// Result of source validation.
/// </summary>
public record SourceValidationResult
{
    public bool IsValid { get; init; }
    public string? ErrorMessage { get; init; }
    public Dictionary<string, string>? Details { get; init; }

    public static SourceValidationResult Success() =>
        new() { IsValid = true };

    public static SourceValidationResult Failure(string message, Dictionary<string, string>? details = null) =>
        new() { IsValid = false, ErrorMessage = message, Details = details };
}
