using System.Collections.Concurrent;
using System.Reflection;
using Microsoft.Extensions.Logging;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace StyloFlow.Manifests;

/// <summary>
/// Loads YAML manifests from embedded resources in assemblies.
/// Supports multiple manifest directories and hot reload.
/// </summary>
public class EmbeddedManifestLoader : IManifestLoader
{
    private readonly ConcurrentDictionary<string, ComponentManifest> _manifests = new();
    private readonly IDeserializer _deserializer;
    private readonly ILogger<EmbeddedManifestLoader> _logger;
    private readonly Assembly[] _sourceAssemblies;
    private readonly string _manifestPattern;

    public EmbeddedManifestLoader(
        ILogger<EmbeddedManifestLoader> logger,
        Assembly[] sourceAssemblies,
        string manifestPattern = ".yaml")
    {
        _logger = logger;
        _sourceAssemblies = sourceAssemblies;
        _manifestPattern = manifestPattern;

        // Use standard deserializer with underscore naming
        _deserializer = new DeserializerBuilder()
            .WithNamingConvention(UnderscoredNamingConvention.Instance)
            .IgnoreUnmatchedProperties()
            .Build();

        // Load manifests on construction
        LoadManifestsSync();
    }

    public ComponentManifest? GetManifest(string componentName)
    {
        // Try exact match first
        if (_manifests.TryGetValue(componentName, out var manifest))
            return manifest;

        // Try without "Contributor" suffix
        var baseName = componentName.Replace("Contributor", "");
        if (_manifests.TryGetValue(baseName, out manifest))
            return manifest;

        return null;
    }

    public IReadOnlyDictionary<string, ComponentManifest> GetAllManifests()
    {
        return _manifests;
    }

    public Task ReloadAsync(CancellationToken cancellationToken = default)
    {
        LoadManifestsSync();
        return Task.CompletedTask;
    }

    private void LoadManifestsSync()
    {
        _manifests.Clear();

        foreach (var assembly in _sourceAssemblies)
        {
            try
            {
                LoadFromAssembly(assembly);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to load manifests from assembly {Assembly}", assembly.FullName);
            }
        }

        _logger.LogInformation("Loaded {Count} manifests from {AssemblyCount} assemblies",
            _manifests.Count, _sourceAssemblies.Length);
    }

    private void LoadFromAssembly(Assembly assembly)
    {
        var resourceNames = assembly.GetManifestResourceNames()
            .Where(r => r.EndsWith(_manifestPattern, StringComparison.OrdinalIgnoreCase));

        foreach (var resourceName in resourceNames)
        {
            try
            {
                using var stream = assembly.GetManifestResourceStream(resourceName);
                if (stream == null)
                {
                    _logger.LogWarning("Could not open resource stream for {Resource}", resourceName);
                    continue;
                }

                using var reader = new StreamReader(stream);
                var yaml = reader.ReadToEnd();

                var manifest = _deserializer.Deserialize<ComponentManifest>(yaml);
                if (manifest != null && !string.IsNullOrEmpty(manifest.Name))
                {
                    _manifests[manifest.Name] = manifest;
                    _logger.LogDebug("Loaded manifest: {Name} from {Resource}", manifest.Name, resourceName);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to parse manifest from {Resource}", resourceName);
            }
        }
    }
}

/// <summary>
/// Loads YAML manifests from file system directories.
/// Supports file watching for hot reload.
/// </summary>
public class FileSystemManifestLoader : IManifestLoader
{
    private readonly ConcurrentDictionary<string, ComponentManifest> _manifests = new();
    private readonly IDeserializer _deserializer;
    private readonly ILogger<FileSystemManifestLoader> _logger;
    private readonly string[] _directories;
    private readonly string _filePattern;

    public FileSystemManifestLoader(
        ILogger<FileSystemManifestLoader> logger,
        string[] directories,
        string filePattern = "*.yaml")
    {
        _logger = logger;
        _directories = directories;
        _filePattern = filePattern;

        _deserializer = new DeserializerBuilder()
            .WithNamingConvention(UnderscoredNamingConvention.Instance)
            .IgnoreUnmatchedProperties()
            .Build();

        LoadManifestsSync();
    }

    public ComponentManifest? GetManifest(string componentName)
    {
        if (_manifests.TryGetValue(componentName, out var manifest))
            return manifest;

        var baseName = componentName.Replace("Contributor", "");
        if (_manifests.TryGetValue(baseName, out manifest))
            return manifest;

        return null;
    }

    public IReadOnlyDictionary<string, ComponentManifest> GetAllManifests()
    {
        return _manifests;
    }

    public async Task ReloadAsync(CancellationToken cancellationToken = default)
    {
        _manifests.Clear();

        foreach (var directory in _directories)
        {
            if (!Directory.Exists(directory))
            {
                _logger.LogWarning("Manifest directory not found: {Directory}", directory);
                continue;
            }

            var files = Directory.GetFiles(directory, _filePattern, SearchOption.AllDirectories);
            foreach (var file in files)
            {
                if (cancellationToken.IsCancellationRequested)
                    return;

                try
                {
                    var yaml = await File.ReadAllTextAsync(file, cancellationToken);
                    var manifest = _deserializer.Deserialize<ComponentManifest>(yaml);

                    if (manifest != null && !string.IsNullOrEmpty(manifest.Name))
                    {
                        _manifests[manifest.Name] = manifest;
                        _logger.LogDebug("Loaded manifest: {Name} from {File}", manifest.Name, file);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to parse manifest from {File}", file);
                }
            }
        }

        _logger.LogInformation("Loaded {Count} manifests from {DirectoryCount} directories",
            _manifests.Count, _directories.Length);
    }

    private void LoadManifestsSync()
    {
        ReloadAsync().GetAwaiter().GetResult();
    }
}
