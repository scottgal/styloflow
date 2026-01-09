using System.Reflection;
using System.Text;

namespace StyloFlow.Retrieval.Orchestration;

/// <summary>
/// Loads wave manifests from YAML files for dynamic composition.
/// Supports loading from embedded resources, directories, or strings.
/// </summary>
public sealed class WaveManifestLoader
{
    private readonly Dictionary<string, WaveManifest> _manifests = new();

    /// <summary>
    /// Load all wave manifests from embedded resources in an assembly.
    /// </summary>
    public IReadOnlyDictionary<string, WaveManifest> LoadEmbeddedManifests(Assembly? assembly = null)
    {
        assembly ??= Assembly.GetExecutingAssembly();
        var resourceNames = assembly.GetManifestResourceNames()
            .Where(n => n.EndsWith(".wave.yaml", StringComparison.OrdinalIgnoreCase));

        foreach (var resourceName in resourceNames)
        {
            using var stream = assembly.GetManifestResourceStream(resourceName);
            if (stream == null) continue;

            using var reader = new StreamReader(stream);
            var yaml = reader.ReadToEnd();
            var manifest = ParseYaml(yaml);

            if (manifest != null)
            {
                _manifests[manifest.Name] = manifest;
            }
        }

        return _manifests;
    }

    /// <summary>
    /// Load wave manifests from a directory.
    /// </summary>
    public IReadOnlyDictionary<string, WaveManifest> LoadFromDirectory(string directory)
    {
        if (!Directory.Exists(directory))
            return _manifests;

        var files = Directory.GetFiles(directory, "*.wave.yaml", SearchOption.TopDirectoryOnly);

        foreach (var file in files)
        {
            var yaml = File.ReadAllText(file);
            var manifest = ParseYaml(yaml);

            if (manifest != null)
            {
                _manifests[manifest.Name] = manifest;
            }
        }

        return _manifests;
    }

    /// <summary>
    /// Parse a single manifest from YAML string.
    /// Uses simple key-value parsing (no external YAML dependency).
    /// </summary>
    public WaveManifest? ParseYaml(string yaml)
    {
        // Simple YAML parser for manifest format
        // For production, consider using YamlDotNet
        var lines = yaml.Split('\n').Select(l => l.TrimEnd('\r')).ToArray();

        string? name = null;
        int priority = 50;
        bool enabled = true;
        string? description = null;
        string domain = "any";
        var tags = new List<string>();

        foreach (var line in lines)
        {
            var trimmed = line.TrimStart();
            if (trimmed.StartsWith("#") || string.IsNullOrWhiteSpace(trimmed))
                continue;

            if (trimmed.StartsWith("name:"))
                name = trimmed.Substring(5).Trim().Trim('"', '\'');
            else if (trimmed.StartsWith("priority:"))
                int.TryParse(trimmed.Substring(9).Trim(), out priority);
            else if (trimmed.StartsWith("enabled:"))
                bool.TryParse(trimmed.Substring(8).Trim(), out enabled);
            else if (trimmed.StartsWith("description:"))
                description = trimmed.Substring(12).Trim().Trim('"', '\'');
            else if (trimmed.StartsWith("domain:"))
                domain = trimmed.Substring(7).Trim().Trim('"', '\'');
            else if (trimmed.StartsWith("- ") && lines.Any(l => l.TrimStart().StartsWith("tags:")))
            {
                // Simple tag parsing (assumes we're in tags section)
                var tag = trimmed.Substring(2).Trim().Trim('"', '\'');
                if (!string.IsNullOrEmpty(tag))
                    tags.Add(tag);
            }
        }

        if (string.IsNullOrEmpty(name))
            return null;

        return new WaveManifest
        {
            Name = name,
            Priority = priority,
            Enabled = enabled,
            Description = description,
            Domain = domain,
            Tags = tags
        };
    }

    /// <summary>
    /// Get a specific manifest by wave name.
    /// </summary>
    public WaveManifest? GetManifest(string waveName)
    {
        return _manifests.TryGetValue(waveName, out var manifest) ? manifest : null;
    }

    /// <summary>
    /// Get all loaded manifests.
    /// </summary>
    public IReadOnlyDictionary<string, WaveManifest> GetAllManifests() => _manifests;

    /// <summary>
    /// Get manifests for a specific domain.
    /// </summary>
    public IReadOnlyList<WaveManifest> GetManifestsForDomain(string domain)
    {
        return _manifests.Values
            .Where(m => m.Enabled && (m.Domain == domain || m.Domain == "any"))
            .OrderByDescending(m => m.Priority)
            .ToList();
    }

    /// <summary>
    /// Get all manifests sorted by priority.
    /// </summary>
    public IReadOnlyList<WaveManifest> GetOrderedManifests()
    {
        return _manifests.Values
            .Where(m => m.Enabled)
            .OrderByDescending(m => m.Priority)
            .ToList();
    }

    /// <summary>
    /// Build a dependency graph of waves based on signal requirements.
    /// </summary>
    public Dictionary<string, HashSet<string>> BuildDependencyGraph()
    {
        var graph = new Dictionary<string, HashSet<string>>();
        var signalToWave = new Dictionary<string, string>();

        // First pass: map signals to producing waves
        foreach (var manifest in _manifests.Values)
        {
            foreach (var signal in manifest.Emits.OnComplete)
            {
                signalToWave[signal.Key] = manifest.Name;
            }
        }

        // Second pass: build dependencies
        foreach (var manifest in _manifests.Values)
        {
            graph[manifest.Name] = new HashSet<string>();

            foreach (var req in manifest.Triggers.Requires)
            {
                if (signalToWave.TryGetValue(req.Signal, out var producer) && producer != manifest.Name)
                {
                    graph[manifest.Name].Add(producer);
                }
            }

            foreach (var signal in manifest.Listens.Required)
            {
                if (signalToWave.TryGetValue(signal, out var producer) && producer != manifest.Name)
                {
                    graph[manifest.Name].Add(producer);
                }
            }
        }

        return graph;
    }

    /// <summary>
    /// Get signal contracts summary for documentation.
    /// </summary>
    public string GetSignalContractsSummary()
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Wave Signal Contracts");
        sb.AppendLine();

        foreach (var manifest in GetOrderedManifests())
        {
            sb.AppendLine($"## {manifest.Name}");
            sb.AppendLine($"Priority: {manifest.Priority} | Domain: {manifest.Domain}");
            if (!string.IsNullOrEmpty(manifest.Description))
                sb.AppendLine($"Description: {manifest.Description}");

            if (manifest.Emits.OnComplete.Count > 0)
            {
                sb.AppendLine("**Emits:**");
                foreach (var signal in manifest.Emits.OnComplete)
                    sb.AppendLine($"  - {signal.Key}");
            }

            if (manifest.Listens.Required.Count > 0 || manifest.Listens.Optional.Count > 0)
            {
                sb.AppendLine("**Listens:**");
                foreach (var signal in manifest.Listens.Required)
                    sb.AppendLine($"  - {signal} (required)");
                foreach (var signal in manifest.Listens.Optional)
                    sb.AppendLine($"  - {signal} (optional)");
            }

            if (manifest.Tags.Count > 0)
                sb.AppendLine($"**Tags:** {string.Join(", ", manifest.Tags)}");

            sb.AppendLine();
        }

        return sb.ToString();
    }
}
