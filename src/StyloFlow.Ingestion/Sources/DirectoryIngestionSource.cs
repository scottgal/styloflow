using System.Security.Cryptography;
using Microsoft.Extensions.FileSystemGlobbing;
using Microsoft.Extensions.FileSystemGlobbing.Abstractions;
using Microsoft.Extensions.Logging;

namespace StyloFlow.Ingestion.Sources;

/// <summary>
/// Ingestion source for local filesystem directories.
/// Supports glob patterns, recursive traversal, and incremental sync.
/// </summary>
public class DirectoryIngestionSource : IIngestionSource
{
    private readonly ILogger<DirectoryIngestionSource> _logger;

    public DirectoryIngestionSource(ILogger<DirectoryIngestionSource> logger)
    {
        _logger = logger;
    }

    public string SourceType => "directory";
    public string DisplayName => "Local Directory";

    public Task<SourceValidationResult> ValidateAsync(
        IngestionSourceConfig config,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(config.Location))
        {
            return Task.FromResult(SourceValidationResult.Failure("Location is required"));
        }

        if (!Directory.Exists(config.Location))
        {
            return Task.FromResult(SourceValidationResult.Failure(
                $"Directory does not exist: {config.Location}"));
        }

        try
        {
            // Check we can list the directory
            Directory.GetFiles(config.Location).Take(1).ToArray();
            return Task.FromResult(SourceValidationResult.Success());
        }
        catch (UnauthorizedAccessException)
        {
            return Task.FromResult(SourceValidationResult.Failure(
                $"Access denied to directory: {config.Location}"));
        }
        catch (Exception ex)
        {
            return Task.FromResult(SourceValidationResult.Failure(
                $"Error accessing directory: {ex.Message}"));
        }
    }

    public async IAsyncEnumerable<IngestionItem> DiscoverAsync(
        IngestionSourceConfig config,
        DiscoveryOptions? options = null,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        var basePath = config.Location;
        if (!Directory.Exists(basePath))
        {
            _logger.LogWarning("Directory does not exist: {Path}", basePath);
            yield break;
        }

        var matcher = BuildMatcher(config);
        var searchOption = config.Recursive
            ? SearchOption.AllDirectories
            : SearchOption.TopDirectoryOnly;

        var directoryInfo = new DirectoryInfo(basePath);
        var matches = matcher.Execute(new DirectoryInfoWrapper(directoryInfo));

        var itemCount = 0;
        var maxItems = options?.MaxItems ?? int.MaxValue;

        foreach (var match in matches.Files)
        {
            if (ct.IsCancellationRequested) yield break;
            if (itemCount >= maxItems) yield break;

            var fullPath = Path.Combine(basePath, match.Path);
            var fileInfo = new FileInfo(fullPath);

            if (!fileInfo.Exists) continue;

            // Skip hidden files unless requested
            if (!options?.IncludeHidden ?? true)
            {
                if (IsHidden(fileInfo)) continue;
            }

            // Skip if modified before threshold
            if (options?.ModifiedSince.HasValue ?? false)
            {
                if (fileInfo.LastWriteTimeUtc < options.ModifiedSince.Value.UtcDateTime)
                    continue;
            }

            var item = await CreateIngestionItemAsync(fileInfo, basePath, ct);

            // Skip if hash is in exclude list
            if (options?.ExcludeHashes?.Contains(item.ContentHash ?? "") ?? false)
                continue;

            itemCount++;
            yield return item;
        }

        _logger.LogInformation("Discovered {Count} items in {Path}", itemCount, basePath);
    }

    public async Task<IngestionContent> FetchAsync(
        IngestionSourceConfig config,
        IngestionItem item,
        CancellationToken ct = default)
    {
        var fullPath = Path.Combine(config.Location, item.Path);

        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException($"File not found: {fullPath}");
        }

        var stream = new FileStream(
            fullPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 81920,
            useAsync: true);

        var mimeType = GetMimeType(item.Name);

        return new IngestionContent
        {
            Item = item,
            Content = stream,
            MimeType = mimeType,
            ContentHash = item.ContentHash
        };
    }

    public async Task<bool> HasChangedAsync(
        IngestionSourceConfig config,
        IngestionItem item,
        string? lastKnownHash,
        DateTimeOffset? lastSyncTime,
        CancellationToken ct = default)
    {
        var fullPath = Path.Combine(config.Location, item.Path);

        if (!File.Exists(fullPath))
        {
            return true; // File deleted = changed
        }

        var fileInfo = new FileInfo(fullPath);

        // Quick check by modification time
        if (lastSyncTime.HasValue && fileInfo.LastWriteTimeUtc > lastSyncTime.Value.UtcDateTime)
        {
            return true;
        }

        // Deep check by hash
        if (!string.IsNullOrEmpty(lastKnownHash))
        {
            var currentHash = await ComputeHashAsync(fullPath, ct);
            return currentHash != lastKnownHash;
        }

        return false;
    }

    private Matcher BuildMatcher(IngestionSourceConfig config)
    {
        var matcher = new Matcher();

        // Add include patterns
        if (!string.IsNullOrEmpty(config.FilePattern))
        {
            matcher.AddInclude(config.FilePattern);
        }
        else
        {
            matcher.AddInclude("**/*");
        }

        // Add exclude patterns
        if (config.ExcludePatterns is { Length: > 0 })
        {
            foreach (var pattern in config.ExcludePatterns)
            {
                matcher.AddExclude(pattern);
            }
        }

        // Default exclusions
        matcher.AddExclude("**/node_modules/**");
        matcher.AddExclude("**/.git/**");
        matcher.AddExclude("**/bin/**");
        matcher.AddExclude("**/obj/**");

        return matcher;
    }

    private async Task<IngestionItem> CreateIngestionItemAsync(
        FileInfo fileInfo,
        string basePath,
        CancellationToken ct)
    {
        var relativePath = Path.GetRelativePath(basePath, fileInfo.FullName)
            .Replace('\\', '/');

        string? hash = null;
        try
        {
            hash = await ComputeHashAsync(fileInfo.FullName, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to compute hash for {Path}", fileInfo.FullName);
        }

        return new IngestionItem
        {
            Path = relativePath,
            Name = fileInfo.Name,
            SizeBytes = fileInfo.Length,
            ModifiedAt = new DateTimeOffset(fileInfo.LastWriteTimeUtc, TimeSpan.Zero),
            ContentHash = hash,
            MimeType = GetMimeType(fileInfo.Name),
            Metadata = new Dictionary<string, object>
            {
                ["fullPath"] = fileInfo.FullName,
                ["createdAt"] = fileInfo.CreationTimeUtc,
                ["extension"] = fileInfo.Extension,
                ["directory"] = fileInfo.DirectoryName ?? ""
            }
        };
    }

    private static async Task<string> ComputeHashAsync(string filePath, CancellationToken ct)
    {
        await using var stream = new FileStream(
            filePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 81920,
            useAsync: true);

        var hashBytes = await SHA256.HashDataAsync(stream, ct);
        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }

    private static bool IsHidden(FileInfo fileInfo)
    {
        // Check file attributes
        if ((fileInfo.Attributes & FileAttributes.Hidden) == FileAttributes.Hidden)
            return true;

        // Check for dot-prefix (Unix convention)
        if (fileInfo.Name.StartsWith('.'))
            return true;

        // Check parent directories
        var dir = fileInfo.Directory;
        while (dir != null)
        {
            if (dir.Name.StartsWith('.'))
                return true;
            dir = dir.Parent;
        }

        return false;
    }

    private static string GetMimeType(string fileName)
    {
        var ext = Path.GetExtension(fileName).ToLowerInvariant();
        return ext switch
        {
            ".md" or ".markdown" => "text/markdown",
            ".txt" => "text/plain",
            ".html" or ".htm" => "text/html",
            ".xml" => "application/xml",
            ".json" => "application/json",
            ".yaml" or ".yml" => "application/x-yaml",
            ".pdf" => "application/pdf",
            ".doc" => "application/msword",
            ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            ".xls" => "application/vnd.ms-excel",
            ".xlsx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            ".ppt" => "application/vnd.ms-powerpoint",
            ".pptx" => "application/vnd.openxmlformats-officedocument.presentationml.presentation",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            ".gif" => "image/gif",
            ".webp" => "image/webp",
            ".svg" => "image/svg+xml",
            ".mp3" => "audio/mpeg",
            ".wav" => "audio/wav",
            ".mp4" => "video/mp4",
            ".webm" => "video/webm",
            ".cs" => "text/x-csharp",
            ".js" => "text/javascript",
            ".ts" => "text/typescript",
            ".py" => "text/x-python",
            ".java" => "text/x-java",
            ".go" => "text/x-go",
            ".rs" => "text/x-rust",
            ".css" => "text/css",
            ".scss" or ".sass" => "text/x-scss",
            ".sql" => "application/sql",
            ".sh" or ".bash" => "application/x-sh",
            ".ps1" => "application/x-powershell",
            ".csv" => "text/csv",
            ".tsv" => "text/tab-separated-values",
            _ => "application/octet-stream"
        };
    }
}
