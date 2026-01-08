using System.Reflection;
using Microsoft.Extensions.Logging;
using StyloFlow.Manifests;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace StyloFlow.Entities;

/// <summary>
/// Loads entity type definitions from YAML files.
/// Supports file system and embedded resources.
/// </summary>
public class EntityTypeLoader
{
    private readonly IDeserializer _deserializer;
    private readonly ILogger<EntityTypeLoader> _logger;

    public EntityTypeLoader(ILogger<EntityTypeLoader> logger)
    {
        _logger = logger;
        _deserializer = new DeserializerBuilder()
            .WithNamingConvention(UnderscoredNamingConvention.Instance)
            .IgnoreUnmatchedProperties()
            .Build();
    }

    /// <summary>
    /// Load entity types from a directory of YAML files.
    /// </summary>
    public IEnumerable<EntityTypeDefinition> LoadFromDirectory(string directory, string pattern = "*.entity.yaml")
    {
        if (!Directory.Exists(directory))
        {
            _logger.LogWarning("Entity type directory not found: {Directory}", directory);
            yield break;
        }

        var files = Directory.GetFiles(directory, pattern, SearchOption.AllDirectories);
        foreach (var file in files)
        {
            EntityTypeDefinitionFile? definitions = null;
            try
            {
                var yaml = File.ReadAllText(file);
                definitions = _deserializer.Deserialize<EntityTypeDefinitionFile>(yaml);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to parse entity type file: {File}", file);
            }

            if (definitions?.EntityTypes == null) continue;

            foreach (var def in definitions.EntityTypes)
            {
                _logger.LogDebug("Loaded entity type: {Type} from {File}", def.Type, file);
                yield return def;
            }
        }
    }

    /// <summary>
    /// Load entity types from embedded resources in an assembly.
    /// </summary>
    public IEnumerable<EntityTypeDefinition> LoadFromAssembly(Assembly assembly, string pattern = ".entity.yaml")
    {
        var resourceNames = assembly.GetManifestResourceNames()
            .Where(r => r.EndsWith(pattern, StringComparison.OrdinalIgnoreCase));

        foreach (var resourceName in resourceNames)
        {
            EntityTypeDefinitionFile? definitions = null;
            try
            {
                using var stream = assembly.GetManifestResourceStream(resourceName);
                if (stream == null) continue;

                using var reader = new StreamReader(stream);
                var yaml = reader.ReadToEnd();
                definitions = _deserializer.Deserialize<EntityTypeDefinitionFile>(yaml);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to parse entity type resource: {Resource}", resourceName);
            }

            if (definitions?.EntityTypes == null) continue;

            foreach (var def in definitions.EntityTypes)
            {
                _logger.LogDebug("Loaded entity type: {Type} from {Resource}", def.Type, resourceName);
                yield return def;
            }
        }
    }

    /// <summary>
    /// Load a single entity type from YAML string.
    /// </summary>
    public EntityTypeDefinition? LoadFromYaml(string yaml)
    {
        try
        {
            return _deserializer.Deserialize<EntityTypeDefinition>(yaml);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to parse entity type YAML");
            return null;
        }
    }
}

/// <summary>
/// YAML file structure for entity type definitions.
/// </summary>
public class EntityTypeDefinitionFile
{
    /// <summary>
    /// List of entity type definitions in this file.
    /// </summary>
    public List<EntityTypeDefinition> EntityTypes { get; set; } = [];
}
