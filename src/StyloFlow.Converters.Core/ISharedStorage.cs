namespace StyloFlow.Converters;

/// <summary>
/// Shared storage for converter outputs.
///
/// Converters write their output here, and downstream processors read from here.
/// This allows efficient memory usage - files are stored once and referenced by path.
///
/// Storage can be backed by filesystem, S3, Azure Blob, etc.
/// </summary>
public interface ISharedStorage
{
    /// <summary>
    /// Storage provider identifier.
    /// </summary>
    string ProviderId { get; }

    /// <summary>
    /// Store content and return a path reference.
    /// </summary>
    /// <param name="content">Content stream to store.</param>
    /// <param name="suggestedPath">Suggested path/name for storage.</param>
    /// <param name="mimeType">MIME type of content.</param>
    /// <param name="metadata">Optional metadata to store with content.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Path to stored content that can be used in signals.</returns>
    Task<StoredContent> StoreAsync(
        Stream content,
        string suggestedPath,
        string mimeType,
        Dictionary<string, string>? metadata = null,
        CancellationToken ct = default);

    /// <summary>
    /// Store content from a local file.
    /// </summary>
    Task<StoredContent> StoreFromFileAsync(
        string localPath,
        string suggestedPath,
        string? mimeType = null,
        Dictionary<string, string>? metadata = null,
        CancellationToken ct = default);

    /// <summary>
    /// Store text content directly.
    /// </summary>
    Task<StoredContent> StoreTextAsync(
        string content,
        string suggestedPath,
        string mimeType = "text/plain",
        Dictionary<string, string>? metadata = null,
        CancellationToken ct = default);

    /// <summary>
    /// Get a readable stream for stored content.
    /// </summary>
    Task<Stream> GetStreamAsync(string path, CancellationToken ct = default);

    /// <summary>
    /// Read stored content as text.
    /// </summary>
    Task<string> GetTextAsync(string path, CancellationToken ct = default);

    /// <summary>
    /// Read stored content as bytes.
    /// </summary>
    Task<byte[]> GetBytesAsync(string path, CancellationToken ct = default);

    /// <summary>
    /// Check if content exists.
    /// </summary>
    Task<bool> ExistsAsync(string path, CancellationToken ct = default);

    /// <summary>
    /// Delete stored content.
    /// </summary>
    Task DeleteAsync(string path, CancellationToken ct = default);

    /// <summary>
    /// Get metadata for stored content.
    /// </summary>
    Task<StoredContent?> GetInfoAsync(string path, CancellationToken ct = default);

    /// <summary>
    /// List contents at a path prefix.
    /// </summary>
    IAsyncEnumerable<StoredContent> ListAsync(
        string pathPrefix,
        CancellationToken ct = default);

    /// <summary>
    /// Get a local file path for content (may download if remote).
    /// Use this when you need to pass a file path to external tools.
    /// Caller should dispose the returned handle when done.
    /// </summary>
    Task<LocalFileHandle> GetLocalPathAsync(string path, CancellationToken ct = default);
}

/// <summary>
/// Information about stored content.
/// </summary>
public record StoredContent
{
    /// <summary>
    /// Path to content in shared storage.
    /// This is what should be passed in signals.
    /// </summary>
    public required string Path { get; init; }

    /// <summary>
    /// MIME type of content.
    /// </summary>
    public required string MimeType { get; init; }

    /// <summary>
    /// Size in bytes.
    /// </summary>
    public long SizeBytes { get; init; }

    /// <summary>
    /// Content hash for deduplication/verification.
    /// </summary>
    public string? ContentHash { get; init; }

    /// <summary>
    /// When content was stored.
    /// </summary>
    public DateTimeOffset CreatedAt { get; init; }

    /// <summary>
    /// Custom metadata.
    /// </summary>
    public IReadOnlyDictionary<string, string>? Metadata { get; init; }
}

/// <summary>
/// Handle to a local file path for shared storage content.
/// Dispose when done to clean up any temporary files.
/// </summary>
public class LocalFileHandle : IAsyncDisposable, IDisposable
{
    private readonly string _path;
    private readonly bool _isTemporary;
    private bool _disposed;

    public LocalFileHandle(string path, bool isTemporary = false)
    {
        _path = path;
        _isTemporary = isTemporary;
    }

    /// <summary>
    /// Local filesystem path to the content.
    /// </summary>
    public string Path => _path;

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (_isTemporary && File.Exists(_path))
        {
            try { File.Delete(_path); } catch { }
        }
    }

    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }
}
