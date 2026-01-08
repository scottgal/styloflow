using System.Reflection;
using StyloFlow.Manifests;
using StyloFlow.WorkflowBuilder.Models;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace StyloFlow.WorkflowBuilder.Services;

/// <summary>
/// Service for loading and managing atom manifests.
/// </summary>
public class ManifestService
{
    private readonly Dictionary<string, ComponentManifest> _manifests = new();
    private readonly IDeserializer _deserializer;

    public ManifestService()
    {
        _deserializer = new DeserializerBuilder()
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .IgnoreUnmatchedProperties()
            .Build();
    }

    public void LoadEmbeddedManifests()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var manifestResources = assembly.GetManifestResourceNames()
            .Where(r => r.EndsWith(".yaml", StringComparison.OrdinalIgnoreCase));

        foreach (var resourceName in manifestResources)
        {
            using var stream = assembly.GetManifestResourceStream(resourceName);
            if (stream == null) continue;

            using var reader = new StreamReader(stream);
            var yaml = reader.ReadToEnd();

            try
            {
                var manifest = _deserializer.Deserialize<ComponentManifest>(yaml);
                if (!string.IsNullOrEmpty(manifest.Name))
                {
                    _manifests[manifest.Name] = manifest;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to load manifest {resourceName}: {ex.Message}");
            }
        }
    }

    public IReadOnlyDictionary<string, ComponentManifest> GetAllManifests() => _manifests;

    public ComponentManifest? GetManifest(string name)
    {
        _manifests.TryGetValue(name, out var manifest);
        return manifest;
    }

    public IReadOnlyList<AtomSummary> GetAtomSummaries()
    {
        return _manifests.Values.Select(m => new AtomSummary
        {
            Name = m.Name,
            Description = m.Description,
            Kind = m.Taxonomy.Kind,
            EmittedSignals = m.Emits.OnComplete.Select(s => s.Key).ToList(),
            RequiredSignals = m.Triggers.Requires.Select(r => r.Signal).ToList(),
            Tags = m.Tags
        }).ToList();
    }

    public IEnumerable<string> GetEmittedSignals(string manifestName)
    {
        if (!_manifests.TryGetValue(manifestName, out var manifest))
            return [];

        return manifest.Emits.OnComplete.Select(s => s.Key)
            .Concat(manifest.Emits.Conditional.Select(c => c.Key));
    }

    public IEnumerable<string> GetRequiredSignals(string manifestName)
    {
        if (!_manifests.TryGetValue(manifestName, out var manifest))
            return [];

        return manifest.Triggers.Requires.Select(r => r.Signal);
    }
}
