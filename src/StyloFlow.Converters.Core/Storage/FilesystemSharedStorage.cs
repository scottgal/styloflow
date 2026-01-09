using System.Security.Cryptography;
using Microsoft.Extensions.Logging;

namespace StyloFlow.Converters.Storage;

/// <summary>
/// Filesystem-backed shared storage.
/// Stores converted content on local disk for efficient access.
/// </summary>
public class FilesystemSharedStorage : ISharedStorage
{
    private readonly string _basePath;
    private readonly ILogger<FilesystemSharedStorage>? _logger;

    public FilesystemSharedStorage(string basePath, ILogger<FilesystemSharedStorage>? logger = null)
    {
        _basePath = basePath;
        _logger = logger;
        Directory.CreateDirectory(_basePath);
    }

    public string ProviderId => "filesystem";

    public async Task<StoredContent> StoreAsync(
        Stream content,
        string suggestedPath,
        string mimeType,
        Dictionary<string, string>? metadata = null,
        CancellationToken ct = default)
    {
        var fullPath = GetFullPath(suggestedPath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);

        await using var fileStream = new FileStream(fullPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true);
        await content.CopyToAsync(fileStream, ct);

        var fileInfo = new FileInfo(fullPath);
        var hash = await ComputeHashAsync(fullPath, ct);

        // Store metadata sidecar if provided
        if (metadata is { Count: > 0 })
        {
            await StoreMetadataAsync(fullPath, metadata, ct);
        }

        _logger?.LogDebug("Stored {Size} bytes to {Path}", fileInfo.Length, suggestedPath);

        return new StoredContent
        {
            Path = suggestedPath,
            MimeType = mimeType,
            SizeBytes = fileInfo.Length,
            ContentHash = hash,
            CreatedAt = fileInfo.CreationTimeUtc,
            Metadata = metadata?.AsReadOnly()
        };
    }

    public async Task<StoredContent> StoreFromFileAsync(
        string localPath,
        string suggestedPath,
        string? mimeType = null,
        Dictionary<string, string>? metadata = null,
        CancellationToken ct = default)
    {
        var fullPath = GetFullPath(suggestedPath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);

        File.Copy(localPath, fullPath, overwrite: true);

        var fileInfo = new FileInfo(fullPath);
        var hash = await ComputeHashAsync(fullPath, ct);

        // Store metadata sidecar if provided
        if (metadata is { Count: > 0 })
        {
            await StoreMetadataAsync(fullPath, metadata, ct);
        }

        return new StoredContent
        {
            Path = suggestedPath,
            MimeType = mimeType ?? GetMimeType(suggestedPath),
            SizeBytes = fileInfo.Length,
            ContentHash = hash,
            CreatedAt = fileInfo.CreationTimeUtc,
            Metadata = metadata?.AsReadOnly()
        };
    }

    public async Task<StoredContent> StoreTextAsync(
        string content,
        string suggestedPath,
        string mimeType = "text/plain",
        Dictionary<string, string>? metadata = null,
        CancellationToken ct = default)
    {
        var fullPath = GetFullPath(suggestedPath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);

        await File.WriteAllTextAsync(fullPath, content, ct);

        var fileInfo = new FileInfo(fullPath);
        var hash = await ComputeHashAsync(fullPath, ct);

        if (metadata is { Count: > 0 })
        {
            await StoreMetadataAsync(fullPath, metadata, ct);
        }

        return new StoredContent
        {
            Path = suggestedPath,
            MimeType = mimeType,
            SizeBytes = fileInfo.Length,
            ContentHash = hash,
            CreatedAt = fileInfo.CreationTimeUtc,
            Metadata = metadata?.AsReadOnly()
        };
    }

    public Task<Stream> GetStreamAsync(string path, CancellationToken ct = default)
    {
        var fullPath = GetFullPath(path);
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException($"Content not found: {path}", path);
        }

        return Task.FromResult<Stream>(new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, true));
    }

    public async Task<string> GetTextAsync(string path, CancellationToken ct = default)
    {
        var fullPath = GetFullPath(path);
        return await File.ReadAllTextAsync(fullPath, ct);
    }

    public async Task<byte[]> GetBytesAsync(string path, CancellationToken ct = default)
    {
        var fullPath = GetFullPath(path);
        return await File.ReadAllBytesAsync(fullPath, ct);
    }

    public Task<bool> ExistsAsync(string path, CancellationToken ct = default)
    {
        var fullPath = GetFullPath(path);
        return Task.FromResult(File.Exists(fullPath));
    }

    public Task DeleteAsync(string path, CancellationToken ct = default)
    {
        var fullPath = GetFullPath(path);
        if (File.Exists(fullPath))
        {
            File.Delete(fullPath);
        }

        var metaPath = fullPath + ".meta";
        if (File.Exists(metaPath))
        {
            File.Delete(metaPath);
        }

        return Task.CompletedTask;
    }

    public async Task<StoredContent?> GetInfoAsync(string path, CancellationToken ct = default)
    {
        var fullPath = GetFullPath(path);
        if (!File.Exists(fullPath))
        {
            return null;
        }

        var fileInfo = new FileInfo(fullPath);
        var hash = await ComputeHashAsync(fullPath, ct);
        var metadata = await LoadMetadataAsync(fullPath, ct);

        return new StoredContent
        {
            Path = path,
            MimeType = GetMimeType(path),
            SizeBytes = fileInfo.Length,
            ContentHash = hash,
            CreatedAt = fileInfo.CreationTimeUtc,
            Metadata = metadata?.AsReadOnly()
        };
    }

    public async IAsyncEnumerable<StoredContent> ListAsync(
        string pathPrefix,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        var fullPath = GetFullPath(pathPrefix);

        if (!Directory.Exists(fullPath))
        {
            yield break;
        }

        var files = Directory.GetFiles(fullPath, "*", SearchOption.AllDirectories)
            .Where(f => !f.EndsWith(".meta"));

        foreach (var file in files)
        {
            if (ct.IsCancellationRequested) yield break;

            var relativePath = Path.GetRelativePath(_basePath, file).Replace('\\', '/');
            var info = await GetInfoAsync(relativePath, ct);
            if (info != null)
            {
                yield return info;
            }
        }
    }

    public Task<LocalFileHandle> GetLocalPathAsync(string path, CancellationToken ct = default)
    {
        var fullPath = GetFullPath(path);
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException($"Content not found: {path}", path);
        }

        // For filesystem storage, just return the path directly (no temp file needed)
        return Task.FromResult(new LocalFileHandle(fullPath, isTemporary: false));
    }

    private string GetFullPath(string path)
    {
        // Normalize and prevent path traversal
        var normalized = path.Replace('\\', '/').TrimStart('/');
        if (normalized.Contains(".."))
        {
            throw new ArgumentException("Path traversal not allowed", nameof(path));
        }

        return Path.Combine(_basePath, normalized);
    }

    private static async Task<string> ComputeHashAsync(string filePath, CancellationToken ct)
    {
        await using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, true);
        var hashBytes = await SHA256.HashDataAsync(stream, ct);
        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }

    private async Task StoreMetadataAsync(string filePath, Dictionary<string, string> metadata, CancellationToken ct)
    {
        var metaPath = filePath + ".meta";
        var lines = metadata.Select(kv => $"{kv.Key}={kv.Value}");
        await File.WriteAllLinesAsync(metaPath, lines, ct);
    }

    private async Task<Dictionary<string, string>?> LoadMetadataAsync(string filePath, CancellationToken ct)
    {
        var metaPath = filePath + ".meta";
        if (!File.Exists(metaPath))
        {
            return null;
        }

        var lines = await File.ReadAllLinesAsync(metaPath, ct);
        var metadata = new Dictionary<string, string>();
        foreach (var line in lines)
        {
            var eq = line.IndexOf('=');
            if (eq > 0)
            {
                metadata[line[..eq]] = line[(eq + 1)..];
            }
        }
        return metadata;
    }

    private static string GetMimeType(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        return ext switch
        {
            ".md" or ".markdown" => "text/markdown",
            ".txt" => "text/plain",
            ".html" or ".htm" => "text/html",
            ".json" => "application/json",
            ".pdf" => "application/pdf",
            ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            ".doc" => "application/msword",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            ".gif" => "image/gif",
            ".svg" => "image/svg+xml",
            _ => "application/octet-stream"
        };
    }
}
