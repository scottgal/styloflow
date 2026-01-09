namespace StyloFlow.Ingestion.Jobs;

/// <summary>
/// Represents a job to ingest content from a source.
/// Jobs emit signals as they process items.
/// </summary>
public class IngestionJob
{
    /// <summary>
    /// Unique job identifier.
    /// </summary>
    public Guid Id { get; init; } = Guid.NewGuid();

    /// <summary>
    /// Source configuration for this job.
    /// </summary>
    public required IngestionSourceConfig SourceConfig { get; init; }

    /// <summary>
    /// Job status.
    /// </summary>
    public IngestionJobStatus Status { get; set; } = IngestionJobStatus.Pending;

    /// <summary>
    /// When the job was created.
    /// </summary>
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// When the job started processing.
    /// </summary>
    public DateTimeOffset? StartedAt { get; set; }

    /// <summary>
    /// When the job completed.
    /// </summary>
    public DateTimeOffset? CompletedAt { get; set; }

    /// <summary>
    /// Total items discovered.
    /// </summary>
    public int ItemsDiscovered { get; set; }

    /// <summary>
    /// Items successfully processed.
    /// </summary>
    public int ItemsProcessed { get; set; }

    /// <summary>
    /// Items that failed processing.
    /// </summary>
    public int ItemsFailed { get; set; }

    /// <summary>
    /// Items skipped (unchanged, filtered, etc.)
    /// </summary>
    public int ItemsSkipped { get; set; }

    /// <summary>
    /// Current item being processed.
    /// </summary>
    public string? CurrentItem { get; set; }

    /// <summary>
    /// Error message if job failed.
    /// </summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// Detailed errors per item.
    /// </summary>
    public List<ItemError> Errors { get; init; } = [];

    /// <summary>
    /// Job priority (lower = higher priority).
    /// </summary>
    public int Priority { get; init; } = 100;

    /// <summary>
    /// Whether to continue on item errors.
    /// </summary>
    public bool ContinueOnError { get; init; } = true;

    /// <summary>
    /// Maximum items to process (0 = unlimited).
    /// </summary>
    public int MaxItems { get; init; } = 0;

    /// <summary>
    /// Only process items modified since last sync.
    /// </summary>
    public bool IncrementalSync { get; init; } = true;

    /// <summary>
    /// Last sync timestamp for incremental.
    /// </summary>
    public DateTimeOffset? LastSyncTime { get; init; }

    /// <summary>
    /// Cancellation token source for this job.
    /// </summary>
    internal CancellationTokenSource? CancellationSource { get; set; }

    /// <summary>
    /// Progress percentage (0-100).
    /// </summary>
    public double Progress => ItemsDiscovered > 0
        ? (double)(ItemsProcessed + ItemsFailed + ItemsSkipped) / ItemsDiscovered * 100
        : 0;

    /// <summary>
    /// Duration of the job.
    /// </summary>
    public TimeSpan? Duration => StartedAt.HasValue
        ? (CompletedAt ?? DateTimeOffset.UtcNow) - StartedAt.Value
        : null;

    /// <summary>
    /// Cancel the job.
    /// </summary>
    public void Cancel()
    {
        CancellationSource?.Cancel();
        Status = IngestionJobStatus.Cancelling;
    }
}

/// <summary>
/// Status of an ingestion job.
/// </summary>
public enum IngestionJobStatus
{
    /// <summary>Job is pending, not yet started.</summary>
    Pending,

    /// <summary>Job is queued for processing.</summary>
    Queued,

    /// <summary>Job is discovering items.</summary>
    Discovering,

    /// <summary>Job is processing items.</summary>
    Processing,

    /// <summary>Job is being cancelled.</summary>
    Cancelling,

    /// <summary>Job was cancelled.</summary>
    Cancelled,

    /// <summary>Job completed successfully.</summary>
    Completed,

    /// <summary>Job completed with errors.</summary>
    CompletedWithErrors,

    /// <summary>Job failed.</summary>
    Failed
}

/// <summary>
/// Error details for a single item.
/// </summary>
public record ItemError
{
    /// <summary>Item path.</summary>
    public required string ItemPath { get; init; }

    /// <summary>Error message.</summary>
    public required string Message { get; init; }

    /// <summary>Exception type.</summary>
    public string? ExceptionType { get; init; }

    /// <summary>When the error occurred.</summary>
    public DateTimeOffset OccurredAt { get; init; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// Result of processing a single item.
/// </summary>
public record ItemProcessingResult
{
    /// <summary>The item that was processed.</summary>
    public required IngestionItem Item { get; init; }

    /// <summary>Whether processing was successful.</summary>
    public bool Success { get; init; }

    /// <summary>Path to stored content in shared storage.</summary>
    public string? StoredPath { get; init; }

    /// <summary>Content hash after storage.</summary>
    public string? ContentHash { get; init; }

    /// <summary>Error message if failed.</summary>
    public string? Error { get; init; }

    /// <summary>Whether the item was skipped.</summary>
    public bool Skipped { get; init; }

    /// <summary>Reason for skipping.</summary>
    public string? SkipReason { get; init; }

    /// <summary>Signals to emit for this item.</summary>
    public List<object>? Signals { get; init; }

    /// <summary>Processing duration.</summary>
    public TimeSpan Duration { get; init; }
}
