using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using Octokit;

namespace StyloFlow.Ingestion.Sources;

/// <summary>
/// Ingestion source for GitHub repositories.
/// Supports public repos, private repos with PAT, and incremental sync via commits.
/// </summary>
public class GitHubIngestionSource : IIngestionSource
{
    private readonly ILogger<GitHubIngestionSource> _logger;
    private readonly HttpClient _httpClient;

    public GitHubIngestionSource(
        ILogger<GitHubIngestionSource> logger,
        HttpClient httpClient)
    {
        _logger = logger;
        _httpClient = httpClient;
    }

    public string SourceType => "github";
    public string DisplayName => "GitHub Repository";

    public async Task<SourceValidationResult> ValidateAsync(
        IngestionSourceConfig config,
        CancellationToken ct = default)
    {
        var (owner, repo, path) = ParseLocation(config.Location);

        if (string.IsNullOrEmpty(owner) || string.IsNullOrEmpty(repo))
        {
            return SourceValidationResult.Failure(
                "Invalid GitHub URL. Expected format: owner/repo or https://github.com/owner/repo");
        }

        try
        {
            var client = CreateGitHubClient(config.Credentials);
            var repository = await client.Repository.Get(owner, repo);

            return SourceValidationResult.Success();
        }
        catch (AuthorizationException)
        {
            return SourceValidationResult.Failure(
                "Access denied. For private repos, provide a Personal Access Token in credentials.");
        }
        catch (NotFoundException)
        {
            return SourceValidationResult.Failure(
                $"Repository not found: {owner}/{repo}");
        }
        catch (Exception ex)
        {
            return SourceValidationResult.Failure($"Error validating repository: {ex.Message}");
        }
    }

    public async IAsyncEnumerable<IngestionItem> DiscoverAsync(
        IngestionSourceConfig config,
        DiscoveryOptions? options = null,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        var (owner, repo, basePath) = ParseLocation(config.Location);
        var client = CreateGitHubClient(config.Credentials);

        // Get the default branch
        var repository = await client.Repository.Get(owner, repo);
        var defaultBranch = repository.DefaultBranch;

        // Get branch reference from options or use default
        var branch = config.Options?.TryGetValue("branch", out var branchObj) == true
            ? branchObj?.ToString() ?? defaultBranch
            : defaultBranch;

        _logger.LogInformation("Discovering files in {Owner}/{Repo} branch={Branch} path={Path}",
            owner, repo, branch, basePath ?? "/");

        var itemCount = 0;
        var maxItems = options?.MaxItems ?? int.MaxValue;

        await foreach (var item in DiscoverRecursiveAsync(client, owner, repo, branch, basePath ?? "", config, options, ct))
        {
            if (ct.IsCancellationRequested) yield break;
            if (itemCount >= maxItems) yield break;

            itemCount++;
            yield return item;
        }

        _logger.LogInformation("Discovered {Count} items in {Owner}/{Repo}", itemCount, owner, repo);
    }

    private async IAsyncEnumerable<IngestionItem> DiscoverRecursiveAsync(
        GitHubClient client,
        string owner,
        string repo,
        string branch,
        string path,
        IngestionSourceConfig config,
        DiscoveryOptions? options,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        IReadOnlyList<RepositoryContent> contents;

        try
        {
            contents = string.IsNullOrEmpty(path)
                ? await client.Repository.Content.GetAllContentsByRef(owner, repo, branch)
                : await client.Repository.Content.GetAllContentsByRef(owner, repo, path, branch);
        }
        catch (NotFoundException)
        {
            _logger.LogWarning("Path not found: {Path}", path);
            yield break;
        }

        foreach (var content in contents)
        {
            if (ct.IsCancellationRequested) yield break;

            if (content.Type == ContentType.Dir)
            {
                if (config.Recursive)
                {
                    // Skip excluded directories
                    if (ShouldExclude(content.Path, config.ExcludePatterns))
                        continue;

                    await foreach (var item in DiscoverRecursiveAsync(
                        client, owner, repo, branch, content.Path, config, options, ct))
                    {
                        yield return item;
                    }
                }
            }
            else if (content.Type == ContentType.File)
            {
                // Apply file pattern filter
                if (!MatchesPattern(content.Path, config.FilePattern, config.ExcludePatterns))
                    continue;

                // Skip if hash is excluded
                if (options?.ExcludeHashes?.Contains(content.Sha) ?? false)
                    continue;

                yield return new IngestionItem
                {
                    Path = content.Path,
                    Name = content.Name,
                    SizeBytes = content.Size,
                    ContentHash = content.Sha,
                    MimeType = GetMimeType(content.Name),
                    Metadata = new Dictionary<string, object>
                    {
                        ["downloadUrl"] = content.DownloadUrl?.ToString() ?? "",
                        ["htmlUrl"] = content.HtmlUrl?.ToString() ?? "",
                        ["gitUrl"] = content.GitUrl?.ToString() ?? "",
                        ["sha"] = content.Sha,
                        ["encoding"] = content.Encoding ?? "",
                        ["owner"] = owner,
                        ["repo"] = repo,
                        ["branch"] = branch
                    }
                };
            }
        }
    }

    public async Task<IngestionContent> FetchAsync(
        IngestionSourceConfig config,
        IngestionItem item,
        CancellationToken ct = default)
    {
        var downloadUrl = item.Metadata?["downloadUrl"]?.ToString();

        if (string.IsNullOrEmpty(downloadUrl))
        {
            // Fallback: fetch via API
            var (owner, repo, _) = ParseLocation(config.Location);
            var client = CreateGitHubClient(config.Credentials);
            var branch = item.Metadata?["branch"]?.ToString() ?? "main";

            var contents = await client.Repository.Content.GetAllContentsByRef(owner, repo, item.Path, branch);
            var content = contents.FirstOrDefault();

            if (content?.Content == null)
            {
                throw new InvalidOperationException($"No content found for {item.Path}");
            }

            var bytes = Convert.FromBase64String(content.Content);
            return new IngestionContent
            {
                Item = item,
                Content = new MemoryStream(bytes),
                MimeType = item.MimeType ?? "application/octet-stream",
                ContentHash = content.Sha
            };
        }

        // Download via raw URL (faster for large files)
        var request = new HttpRequestMessage(HttpMethod.Get, downloadUrl);

        // Add auth header if we have credentials
        if (!string.IsNullOrEmpty(config.Credentials))
        {
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue(
                "Bearer", config.Credentials);
        }

        var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();

        var stream = await response.Content.ReadAsStreamAsync(ct);

        return new IngestionContent
        {
            Item = item,
            Content = stream,
            MimeType = item.MimeType ?? response.Content.Headers.ContentType?.MediaType ?? "application/octet-stream",
            ContentHash = item.ContentHash
        };
    }

    public Task<bool> HasChangedAsync(
        IngestionSourceConfig config,
        IngestionItem item,
        string? lastKnownHash,
        DateTimeOffset? lastSyncTime,
        CancellationToken ct = default)
    {
        // GitHub uses SHA for content hash - if the SHA changed, content changed
        if (!string.IsNullOrEmpty(lastKnownHash) && !string.IsNullOrEmpty(item.ContentHash))
        {
            return Task.FromResult(lastKnownHash != item.ContentHash);
        }

        // Without hash comparison, assume changed
        return Task.FromResult(true);
    }

    private GitHubClient CreateGitHubClient(string? credentials)
    {
        var client = new GitHubClient(new ProductHeaderValue("StyloFlow-Ingestion"));

        if (!string.IsNullOrEmpty(credentials))
        {
            // Resolve credential if it's an env var reference
            var token = credentials;
            if (credentials.StartsWith("${") && credentials.EndsWith("}"))
            {
                var envVar = credentials[2..^1];
                token = Environment.GetEnvironmentVariable(envVar) ?? "";
            }

            if (!string.IsNullOrEmpty(token))
            {
                client.Credentials = new Credentials(token);
            }
        }

        return client;
    }

    private static (string owner, string repo, string? path) ParseLocation(string location)
    {
        // Handle various formats:
        // - owner/repo
        // - owner/repo/path/to/dir
        // - https://github.com/owner/repo
        // - https://github.com/owner/repo/tree/main/path

        var url = location.Trim();

        // Remove GitHub URL prefix
        if (url.StartsWith("https://github.com/", StringComparison.OrdinalIgnoreCase))
        {
            url = url["https://github.com/".Length..];
        }
        else if (url.StartsWith("http://github.com/", StringComparison.OrdinalIgnoreCase))
        {
            url = url["http://github.com/".Length..];
        }
        else if (url.StartsWith("github.com/", StringComparison.OrdinalIgnoreCase))
        {
            url = url["github.com/".Length..];
        }

        // Remove trailing slash
        url = url.TrimEnd('/');

        // Handle /tree/branch/path format
        var treePart = "/tree/";
        var treeIndex = url.IndexOf(treePart, StringComparison.OrdinalIgnoreCase);
        string? path = null;

        if (treeIndex > 0)
        {
            var afterTree = url[(treeIndex + treePart.Length)..];
            var slashIndex = afterTree.IndexOf('/');
            if (slashIndex > 0)
            {
                path = afterTree[(slashIndex + 1)..];
            }
            url = url[..treeIndex];
        }

        // Handle /blob/branch/path format
        var blobPart = "/blob/";
        var blobIndex = url.IndexOf(blobPart, StringComparison.OrdinalIgnoreCase);
        if (blobIndex > 0)
        {
            var afterBlob = url[(blobIndex + blobPart.Length)..];
            var slashIndex = afterBlob.IndexOf('/');
            if (slashIndex > 0)
            {
                path = afterBlob[(slashIndex + 1)..];
            }
            url = url[..blobIndex];
        }

        var parts = url.Split('/', StringSplitOptions.RemoveEmptyEntries);

        if (parts.Length < 2)
        {
            return (string.Empty, string.Empty, null);
        }

        var owner = parts[0];
        var repo = parts[1];

        // If there's a path in the owner/repo/path format
        if (parts.Length > 2 && path == null)
        {
            path = string.Join('/', parts.Skip(2));
        }

        return (owner, repo, path);
    }

    private static bool MatchesPattern(string path, string? includePattern, string[]? excludePatterns)
    {
        // Check excludes first
        if (excludePatterns != null)
        {
            foreach (var pattern in excludePatterns)
            {
                if (MatchGlob(path, pattern))
                    return false;
            }
        }

        // Check include
        if (string.IsNullOrEmpty(includePattern))
            return true;

        return MatchGlob(path, includePattern);
    }

    private static bool ShouldExclude(string path, string[]? excludePatterns)
    {
        if (excludePatterns == null) return false;

        foreach (var pattern in excludePatterns)
        {
            if (MatchGlob(path, pattern))
                return true;
        }

        return false;
    }

    private static bool MatchGlob(string path, string pattern)
    {
        // Simple glob matching for common patterns
        // Full implementation would use Microsoft.Extensions.FileSystemGlobbing

        if (pattern == "**/*" || pattern == "*")
            return true;

        // Handle extension patterns like "*.md"
        if (pattern.StartsWith("*."))
        {
            var ext = pattern[1..]; // .md
            return path.EndsWith(ext, StringComparison.OrdinalIgnoreCase);
        }

        // Handle directory patterns like "**/node_modules/**"
        if (pattern.Contains("**"))
        {
            var parts = pattern.Split("**", StringSplitOptions.RemoveEmptyEntries);
            var current = path;

            foreach (var part in parts)
            {
                var trimmed = part.Trim('/');
                if (string.IsNullOrEmpty(trimmed)) continue;

                var index = current.IndexOf(trimmed, StringComparison.OrdinalIgnoreCase);
                if (index < 0) return false;
                current = current[(index + trimmed.Length)..];
            }

            return true;
        }

        return path.Equals(pattern, StringComparison.OrdinalIgnoreCase);
    }

    private static string GetMimeType(string fileName)
    {
        var ext = Path.GetExtension(fileName).ToLowerInvariant();
        return ext switch
        {
            ".md" or ".markdown" => "text/markdown",
            ".txt" => "text/plain",
            ".html" or ".htm" => "text/html",
            ".json" => "application/json",
            ".yaml" or ".yml" => "application/x-yaml",
            ".xml" => "application/xml",
            ".pdf" => "application/pdf",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            ".gif" => "image/gif",
            ".svg" => "image/svg+xml",
            ".cs" => "text/x-csharp",
            ".js" => "text/javascript",
            ".ts" => "text/typescript",
            ".py" => "text/x-python",
            ".go" => "text/x-go",
            ".rs" => "text/x-rust",
            ".java" => "text/x-java",
            ".css" => "text/css",
            ".sql" => "application/sql",
            _ => "application/octet-stream"
        };
    }
}
