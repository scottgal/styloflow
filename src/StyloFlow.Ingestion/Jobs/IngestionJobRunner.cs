using System.Diagnostics;
using Microsoft.Extensions.Logging;
using StyloFlow.Converters;

namespace StyloFlow.Ingestion.Jobs;

/// <summary>
/// Executes ingestion jobs and emits signals.
/// </summary>
public class IngestionJobRunner
{
    private readonly IngestionSourceRegistry _sourceRegistry;
    private readonly ISharedStorage _storage;
    private readonly ILogger<IngestionJobRunner>? _logger;

    public IngestionJobRunner(
        IngestionSourceRegistry sourceRegistry,
        ISharedStorage storage,
        ILogger<IngestionJobRunner>? logger = null)
    {
        _sourceRegistry = sourceRegistry;
        _storage = storage;
        _logger = logger;
    }

    /// <summary>
    /// Event raised when a job starts.
    /// </summary>
    public event EventHandler<IngestionJob>? JobStarted;

    /// <summary>
    /// Event raised when a job completes.
    /// </summary>
    public event EventHandler<IngestionJobCompletedArgs>? JobCompleted;

    /// <summary>
    /// Event raised when an item is processed.
    /// </summary>
    public event EventHandler<ItemProcessedArgs>? ItemProcessed;

    /// <summary>
    /// Event raised when a signal should be emitted.
    /// Connect this to your signal/wave pipeline.
    /// </summary>
    public event EventHandler<SignalEmittedArgs>? SignalEmitted;

    /// <summary>
    /// Execute an ingestion job.
    /// </summary>
    public async Task<IngestionJobResult> ExecuteAsync(
        IngestionJob job,
        CancellationToken ct = default)
    {
        job.CancellationSource = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var linkedCt = job.CancellationSource.Token;

        var sw = Stopwatch.StartNew();
        var signals = new List<object>();

        try
        {
            // Get the source handler
            var source = _sourceRegistry.GetSource(job.SourceConfig.SourceType);
            if (source == null)
            {
                job.Status = IngestionJobStatus.Failed;
                job.ErrorMessage = $"Unknown source type: {job.SourceConfig.SourceType}";
                return CreateResult(job, sw.Elapsed, signals);
            }

            // Validate configuration
            var validation = await source.ValidateAsync(job.SourceConfig, linkedCt);
            if (!validation.IsValid)
            {
                job.Status = IngestionJobStatus.Failed;
                job.ErrorMessage = $"Invalid configuration: {validation.ErrorMessage}";
                return CreateResult(job, sw.Elapsed, signals);
            }

            // Start the job
            job.Status = IngestionJobStatus.Discovering;
            job.StartedAt = DateTimeOffset.UtcNow;
            JobStarted?.Invoke(this, job);

            EmitSignal(signals, new JobStartedSignal
            {
                JobId = job.Id,
                SourceType = job.SourceConfig.SourceType,
                SourceName = job.SourceConfig.Name,
                Location = job.SourceConfig.Location
            });

            // Discover items
            var discoveryOptions = new DiscoveryOptions
            {
                ModifiedSince = job.IncrementalSync ? job.LastSyncTime : null,
                MaxItems = job.MaxItems > 0 ? job.MaxItems : null
            };

            var items = new List<IngestionItem>();
            await foreach (var item in source.DiscoverAsync(job.SourceConfig, discoveryOptions, linkedCt))
            {
                items.Add(item);
                job.ItemsDiscovered++;

                if (job.MaxItems > 0 && items.Count >= job.MaxItems)
                    break;
            }

            _logger?.LogInformation(
                "Job {JobId}: Discovered {Count} items from {Source}",
                job.Id, items.Count, job.SourceConfig.Name);

            // Process items
            job.Status = IngestionJobStatus.Processing;
            var processedResults = new List<ItemProcessingResult>();

            foreach (var item in items)
            {
                linkedCt.ThrowIfCancellationRequested();

                job.CurrentItem = item.Path;
                var result = await ProcessItemAsync(job, source, item, linkedCt);
                processedResults.Add(result);

                // Update counters
                if (result.Success)
                    job.ItemsProcessed++;
                else if (result.Skipped)
                    job.ItemsSkipped++;
                else
                {
                    job.ItemsFailed++;
                    if (!string.IsNullOrEmpty(result.Error))
                    {
                        job.Errors.Add(new ItemError
                        {
                            ItemPath = item.Path,
                            Message = result.Error
                        });
                    }
                }

                // Emit signals from item processing
                if (result.Signals != null)
                {
                    foreach (var signal in result.Signals)
                    {
                        EmitSignal(signals, signal);
                    }
                }

                // Raise item processed event
                ItemProcessed?.Invoke(this, new ItemProcessedArgs
                {
                    Job = job,
                    Item = item,
                    Result = result
                });

                // Check if we should stop on error
                if (!result.Success && !result.Skipped && !job.ContinueOnError)
                {
                    job.Status = IngestionJobStatus.Failed;
                    job.ErrorMessage = $"Stopped on error: {result.Error}";
                    break;
                }
            }

            // Complete the job
            job.CurrentItem = null;
            job.CompletedAt = DateTimeOffset.UtcNow;

            if (job.Status != IngestionJobStatus.Failed && job.Status != IngestionJobStatus.Cancelling)
            {
                job.Status = job.ItemsFailed > 0
                    ? IngestionJobStatus.CompletedWithErrors
                    : IngestionJobStatus.Completed;
            }

            // Emit completion signal
            EmitSignal(signals, new JobCompletedSignal
            {
                JobId = job.Id,
                SourceType = job.SourceConfig.SourceType,
                SourceName = job.SourceConfig.Name,
                Status = job.Status.ToString(),
                ItemsDiscovered = job.ItemsDiscovered,
                ItemsProcessed = job.ItemsProcessed,
                ItemsFailed = job.ItemsFailed,
                ItemsSkipped = job.ItemsSkipped,
                Duration = sw.Elapsed
            });

            _logger?.LogInformation(
                "Job {JobId} completed: {Status}, {Processed}/{Discovered} items, {Duration}ms",
                job.Id, job.Status, job.ItemsProcessed, job.ItemsDiscovered, sw.ElapsedMilliseconds);

            return CreateResult(job, sw.Elapsed, signals);
        }
        catch (OperationCanceledException)
        {
            job.Status = IngestionJobStatus.Cancelled;
            job.CompletedAt = DateTimeOffset.UtcNow;
            _logger?.LogInformation("Job {JobId} was cancelled", job.Id);
            return CreateResult(job, sw.Elapsed, signals);
        }
        catch (Exception ex)
        {
            job.Status = IngestionJobStatus.Failed;
            job.ErrorMessage = ex.Message;
            job.CompletedAt = DateTimeOffset.UtcNow;
            _logger?.LogError(ex, "Job {JobId} failed", job.Id);
            return CreateResult(job, sw.Elapsed, signals);
        }
        finally
        {
            JobCompleted?.Invoke(this, new IngestionJobCompletedArgs
            {
                Job = job,
                Duration = sw.Elapsed
            });
        }
    }

    private async Task<ItemProcessingResult> ProcessItemAsync(
        IngestionJob job,
        IIngestionSource source,
        IngestionItem item,
        CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        var signals = new List<object>();

        try
        {
            // Check if item has changed (for incremental sync)
            if (job.IncrementalSync && job.LastSyncTime.HasValue)
            {
                var hasChanged = await source.HasChangedAsync(
                    job.SourceConfig,
                    item,
                    item.ContentHash,
                    job.LastSyncTime,
                    ct);

                if (!hasChanged)
                {
                    return new ItemProcessingResult
                    {
                        Item = item,
                        Success = false,
                        Skipped = true,
                        SkipReason = "Unchanged since last sync",
                        Duration = sw.Elapsed
                    };
                }
            }

            // Fetch content
            var content = await source.FetchAsync(job.SourceConfig, item, ct);

            // Store in shared storage
            var storagePath = BuildStoragePath(job, item);
            var stored = await _storage.StoreAsync(
                content.Content,
                storagePath,
                content.MimeType,
                ct: ct);

            // Emit content stored signal
            signals.Add(new ContentStoredSignal
            {
                JobId = job.Id,
                ItemPath = item.Path,
                StoredPath = stored.Path,
                MimeType = content.MimeType,
                SizeBytes = stored.SizeBytes,
                ContentHash = stored.ContentHash
            });

            return new ItemProcessingResult
            {
                Item = item,
                Success = true,
                StoredPath = stored.Path,
                ContentHash = stored.ContentHash,
                Signals = signals,
                Duration = sw.Elapsed
            };
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to process item {Path}", item.Path);

            return new ItemProcessingResult
            {
                Item = item,
                Success = false,
                Error = ex.Message,
                Duration = sw.Elapsed
            };
        }
    }

    private string BuildStoragePath(IngestionJob job, IngestionItem item)
    {
        var sourceId = job.SourceConfig.Id.ToString("N")[..8];
        var timestamp = DateTimeOffset.UtcNow.ToString("yyyyMMdd");
        var fileName = Path.GetFileName(item.Path);

        return $"ingested/{sourceId}/{timestamp}/{fileName}";
    }

    private void EmitSignal(List<object> signals, object signal)
    {
        signals.Add(signal);
        SignalEmitted?.Invoke(this, new SignalEmittedArgs { Signal = signal });
    }

    private static IngestionJobResult CreateResult(IngestionJob job, TimeSpan duration, List<object> signals)
    {
        return new IngestionJobResult
        {
            Job = job,
            Duration = duration,
            EmittedSignals = signals
        };
    }
}

/// <summary>
/// Result of executing an ingestion job.
/// </summary>
public record IngestionJobResult
{
    public required IngestionJob Job { get; init; }
    public TimeSpan Duration { get; init; }
    public required IReadOnlyList<object> EmittedSignals { get; init; }

    public bool Success => Job.Status is IngestionJobStatus.Completed or IngestionJobStatus.CompletedWithErrors;
}

/// <summary>
/// Event args for job completion.
/// </summary>
public class IngestionJobCompletedArgs : EventArgs
{
    public required IngestionJob Job { get; init; }
    public TimeSpan Duration { get; init; }
}

/// <summary>
/// Event args for item processing.
/// </summary>
public class ItemProcessedArgs : EventArgs
{
    public required IngestionJob Job { get; init; }
    public required IngestionItem Item { get; init; }
    public required ItemProcessingResult Result { get; init; }
}

/// <summary>
/// Event args for signal emission.
/// </summary>
public class SignalEmittedArgs : EventArgs
{
    public required object Signal { get; init; }
}

// Signals emitted by the job runner

public record JobStartedSignal
{
    public const string SignalType = "ingestion:job_started";
    public Guid JobId { get; init; }
    public required string SourceType { get; init; }
    public required string SourceName { get; init; }
    public required string Location { get; init; }
}

public record JobCompletedSignal
{
    public const string SignalType = "ingestion:job_completed";
    public Guid JobId { get; init; }
    public required string SourceType { get; init; }
    public required string SourceName { get; init; }
    public required string Status { get; init; }
    public int ItemsDiscovered { get; init; }
    public int ItemsProcessed { get; init; }
    public int ItemsFailed { get; init; }
    public int ItemsSkipped { get; init; }
    public TimeSpan Duration { get; init; }
}

public record ContentStoredSignal
{
    public const string SignalType = "ingestion:content_stored";
    public Guid JobId { get; init; }
    public required string ItemPath { get; init; }
    public required string StoredPath { get; init; }
    public required string MimeType { get; init; }
    public long SizeBytes { get; init; }
    public string? ContentHash { get; init; }
}
