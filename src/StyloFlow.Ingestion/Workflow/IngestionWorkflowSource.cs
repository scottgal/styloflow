using StyloFlow.Ingestion.Configuration;

namespace StyloFlow.Ingestion.Workflow;

/// <summary>
/// Represents an item flowing through the ingestion workflow.
/// This is the unit that gets processed by waves.
/// </summary>
public class IngestionWorkItem
{
    /// <summary>
    /// Unique identifier for this work item.
    /// </summary>
    public Guid Id { get; init; } = Guid.NewGuid();

    /// <summary>
    /// Source configuration this item came from.
    /// </summary>
    public required string SourceName { get; init; }

    /// <summary>
    /// The discovered item metadata.
    /// </summary>
    public required IngestionItem Item { get; init; }

    /// <summary>
    /// Processing configuration for this item.
    /// </summary>
    public ProcessingConfig? Processing { get; init; }

    /// <summary>
    /// Tags inherited from source.
    /// </summary>
    public string[]? Tags { get; init; }

    /// <summary>
    /// Target collection ID.
    /// </summary>
    public Guid? CollectionId { get; init; }

    /// <summary>
    /// Tenant ID for multi-tenant.
    /// </summary>
    public string? TenantId { get; init; }

    /// <summary>
    /// Job ID this item belongs to.
    /// </summary>
    public Guid JobId { get; init; }

    /// <summary>
    /// Creation timestamp.
    /// </summary>
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Current processing state.
    /// </summary>
    public WorkItemState State { get; set; } = WorkItemState.Pending;

    /// <summary>
    /// Error message if failed.
    /// </summary>
    public string? Error { get; set; }

    /// <summary>
    /// Number of retry attempts.
    /// </summary>
    public int RetryCount { get; set; }

    /// <summary>
    /// Processing signals collected during workflow.
    /// </summary>
    public Dictionary<string, object> Signals { get; } = [];
}

/// <summary>
/// State of a work item.
/// </summary>
public enum WorkItemState
{
    Pending,
    Fetching,
    Processing,
    Completed,
    Failed,
    Skipped
}

/// <summary>
/// Interface for workflow sources that produce work items.
/// Ingestion sources implement this to feed the wave pipeline.
/// </summary>
public interface IWorkflowSource
{
    /// <summary>
    /// Source identifier.
    /// </summary>
    string SourceId { get; }

    /// <summary>
    /// Start producing work items.
    /// </summary>
    IAsyncEnumerable<IngestionWorkItem> ProduceAsync(
        IngestionJobContext context,
        CancellationToken ct = default);

    /// <summary>
    /// Get the total count of items (if known).
    /// Used for progress reporting.
    /// </summary>
    Task<int?> GetTotalCountAsync(
        IngestionJobContext context,
        CancellationToken ct = default);
}

/// <summary>
/// Context for an ingestion job.
/// </summary>
public class IngestionJobContext
{
    /// <summary>
    /// Unique job identifier.
    /// </summary>
    public Guid JobId { get; init; } = Guid.NewGuid();

    /// <summary>
    /// Source configuration.
    /// </summary>
    public required SourceConfig Source { get; init; }

    /// <summary>
    /// Merged defaults and source-specific config.
    /// </summary>
    public required IngestionDefaults EffectiveDefaults { get; init; }

    /// <summary>
    /// Last successful sync time for incremental.
    /// </summary>
    public DateTimeOffset? LastSyncTime { get; init; }

    /// <summary>
    /// Hashes of previously ingested items.
    /// </summary>
    public HashSet<string>? ExistingHashes { get; init; }

    /// <summary>
    /// Progress callback.
    /// </summary>
    public IProgress<IngestionProgress>? Progress { get; init; }

    /// <summary>
    /// Whether this is a dry run (discover only, don't process).
    /// </summary>
    public bool DryRun { get; init; }

    /// <summary>
    /// Job start time.
    /// </summary>
    public DateTimeOffset StartedAt { get; init; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// Progress information for an ingestion job.
/// </summary>
public record IngestionProgress
{
    public int Discovered { get; init; }
    public int Processed { get; init; }
    public int Succeeded { get; init; }
    public int Failed { get; init; }
    public int Skipped { get; init; }
    public int? Total { get; init; }
    public string? CurrentItem { get; init; }
    public TimeSpan Elapsed { get; init; }
    public double? ItemsPerSecond { get; init; }
}

/// <summary>
/// Adapter that wraps IIngestionSource as a workflow source.
/// </summary>
public class IngestionSourceWorkflowAdapter : IWorkflowSource
{
    private readonly IIngestionSource _source;
    private readonly SourceConfig _config;

    public IngestionSourceWorkflowAdapter(IIngestionSource source, SourceConfig config)
    {
        _source = source;
        _config = config;
    }

    public string SourceId => _config.Collection ?? _source.SourceType;

    public async IAsyncEnumerable<IngestionWorkItem> ProduceAsync(
        IngestionJobContext context,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        var sourceConfig = BuildSourceConfig(context);
        var discoveryOptions = BuildDiscoveryOptions(context);

        await foreach (var item in _source.DiscoverAsync(sourceConfig, discoveryOptions, ct))
        {
            // Apply filters
            if (!PassesFilters(item, context))
            {
                continue;
            }

            yield return new IngestionWorkItem
            {
                SourceName = _config.Collection ?? _source.SourceType,
                Item = item,
                Processing = context.EffectiveDefaults.ToProcessingConfig(),
                Tags = _config.Tags,
                CollectionId = _config.Collection != null ? Guid.TryParse(_config.Collection, out var cid) ? cid : null : null,
                TenantId = _config.Tenant,
                JobId = context.JobId
            };
        }
    }

    public async Task<int?> GetTotalCountAsync(IngestionJobContext context, CancellationToken ct = default)
    {
        // Most sources don't know the total count ahead of time
        // Could implement for specific sources that support it
        return await Task.FromResult<int?>(null);
    }

    private IngestionSourceConfig BuildSourceConfig(IngestionJobContext context)
    {
        return new IngestionSourceConfig
        {
            SourceType = _config.Type,
            Name = _config.Collection ?? _config.Type,
            Location = _config.Location,
            Credentials = ResolveCredentials(_config.Credentials),
            FilePattern = _config.Filters?.Include?.FirstOrDefault(),
            ExcludePatterns = _config.Filters?.Exclude,
            Recursive = _config.Filters?.Recursive ?? true,
            CollectionId = context.Source.Collection != null && Guid.TryParse(context.Source.Collection, out var cid) ? cid : null,
            TenantId = _config.Tenant,
            Options = _config.Options,
            Tags = _config.Tags
        };
    }

    private DiscoveryOptions BuildDiscoveryOptions(IngestionJobContext context)
    {
        return new DiscoveryOptions
        {
            ModifiedSince = _config.Filters?.ModifiedAfter ?? context.LastSyncTime,
            ExcludeHashes = context.ExistingHashes,
            IncludeHidden = _config.Filters?.IncludeHidden ?? context.EffectiveDefaults.IncludeHidden
        };
    }

    private bool PassesFilters(IngestionItem item, IngestionJobContext context)
    {
        var filters = _config.Filters;
        if (filters == null) return true;

        // Size filters
        if (item.SizeBytes.HasValue)
        {
            if (filters.MinSizeBytes.HasValue && item.SizeBytes < filters.MinSizeBytes)
                return false;
            if (filters.MaxSizeBytes.HasValue && item.SizeBytes > filters.MaxSizeBytes)
                return false;
        }

        // Time filters
        if (item.ModifiedAt.HasValue)
        {
            if (filters.ModifiedAfter.HasValue && item.ModifiedAt < filters.ModifiedAfter)
                return false;
            if (filters.ModifiedBefore.HasValue && item.ModifiedAt > filters.ModifiedBefore)
                return false;
        }

        // Extension filter
        if (filters.Extensions is { Length: > 0 })
        {
            var ext = Path.GetExtension(item.Name);
            if (!filters.Extensions.Contains(ext, StringComparer.OrdinalIgnoreCase))
                return false;
        }

        // Hash deduplication
        if (context.ExistingHashes != null && item.ContentHash != null)
        {
            if (context.ExistingHashes.Contains(item.ContentHash))
                return false;
        }

        return true;
    }

    private static string? ResolveCredentials(string? credentials)
    {
        if (string.IsNullOrEmpty(credentials))
            return null;

        // Resolve environment variable references
        if (credentials.StartsWith("${") && credentials.EndsWith("}"))
        {
            var envVar = credentials[2..^1];
            return Environment.GetEnvironmentVariable(envVar);
        }

        // TODO: Resolve secret manager references
        if (credentials.StartsWith("secret:"))
        {
            // Would integrate with Azure Key Vault, AWS Secrets Manager, etc.
            return credentials;
        }

        return credentials;
    }
}

/// <summary>
/// Extension methods.
/// </summary>
public static class ConfigExtensions
{
    public static ProcessingConfig ToProcessingConfig(this IngestionDefaults defaults)
    {
        return new ProcessingConfig
        {
            Concurrency = defaults.Concurrency,
            BatchSize = defaults.BatchSize,
            TimeoutSeconds = defaults.TimeoutSeconds,
            Retry = defaults.Retry,
            Deduplication = defaults.Deduplication
        };
    }
}
