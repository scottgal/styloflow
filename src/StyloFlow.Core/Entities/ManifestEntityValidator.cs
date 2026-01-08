using Microsoft.Extensions.Logging;
using StyloFlow.Manifests;

namespace StyloFlow.Entities;

/// <summary>
/// Validates that entity types referenced in manifests exist in the registry.
/// </summary>
public class ManifestEntityValidator
{
    private readonly EntityTypeRegistry _registry;
    private readonly ILogger<ManifestEntityValidator> _logger;

    public ManifestEntityValidator(
        EntityTypeRegistry registry,
        ILogger<ManifestEntityValidator> logger)
    {
        _registry = registry;
        _logger = logger;
    }

    /// <summary>
    /// Validate all entity type references in a manifest.
    /// </summary>
    public ManifestValidationResult Validate(ComponentManifest manifest)
    {
        var result = new ManifestValidationResult
        {
            ManifestName = manifest.Name
        };

        // Validate input contract
        if (manifest.Input?.Accepts != null)
        {
            foreach (var spec in manifest.Input.Accepts)
            {
                ValidateEntityTypeSpec(spec, "input.accepts", result);
            }
        }

        // Validate output contract
        if (manifest.Output?.Produces != null)
        {
            foreach (var spec in manifest.Output.Produces)
            {
                ValidateEntityTypeSpec(spec, "output.produces", result);
            }
        }

        // Validate output signals with entity types
        if (manifest.Output?.Signals != null)
        {
            foreach (var signal in manifest.Output.Signals)
            {
                if (!string.IsNullOrEmpty(signal.EntityType))
                {
                    // EntityType in SignalSpec is a primitive type name, not a registry type
                    // Skip validation for primitives
                    if (!IsPrimitiveType(signal.EntityType))
                    {
                        if (!_registry.IsRegistered(signal.EntityType))
                        {
                            result.Warnings.Add(
                                $"Signal '{signal.Key}' references unknown entity type '{signal.EntityType}'");
                        }
                    }
                }
            }
        }

        result.IsValid = result.Errors.Count == 0;

        if (!result.IsValid)
        {
            _logger.LogWarning(
                "Manifest '{Name}' has {ErrorCount} validation errors: {Errors}",
                manifest.Name, result.Errors.Count, string.Join("; ", result.Errors));
        }
        else if (result.Warnings.Count > 0)
        {
            _logger.LogDebug(
                "Manifest '{Name}' has {WarningCount} warnings: {Warnings}",
                manifest.Name, result.Warnings.Count, string.Join("; ", result.Warnings));
        }

        return result;
    }

    /// <summary>
    /// Validate all manifests from a loader.
    /// </summary>
    public IReadOnlyList<ManifestValidationResult> ValidateAll(IManifestLoader loader)
    {
        var results = new List<ManifestValidationResult>();

        foreach (var (name, manifest) in loader.GetAllManifests())
        {
            results.Add(Validate(manifest));
        }

        var errorCount = results.Count(r => !r.IsValid);
        var warningCount = results.Sum(r => r.Warnings.Count);

        if (errorCount > 0)
        {
            _logger.LogWarning(
                "Validated {Total} manifests: {Errors} errors, {Warnings} warnings",
                results.Count, errorCount, warningCount);
        }
        else
        {
            _logger.LogInformation(
                "Validated {Total} manifests: all valid, {Warnings} warnings",
                results.Count, warningCount);
        }

        return results;
    }

    private void ValidateEntityTypeSpec(EntityTypeSpec spec, string location, ManifestValidationResult result)
    {
        if (string.IsNullOrEmpty(spec.Type))
        {
            result.Errors.Add($"{location}: Entity type spec has empty type");
            return;
        }

        // Check if type is registered (exact or wildcard match)
        var definition = _registry.Get(spec.Type);
        if (definition == null)
        {
            // Check if it's a wildcard pattern that could match
            if (spec.Type.Contains('*'))
            {
                var matches = _registry.GetByPattern(spec.Type).ToList();
                if (matches.Count == 0)
                {
                    result.Warnings.Add(
                        $"{location}: Wildcard pattern '{spec.Type}' matches no registered types");
                }
            }
            else
            {
                result.Warnings.Add(
                    $"{location}: Entity type '{spec.Type}' is not registered (will be validated at runtime)");
            }
        }

        // Validate constraints if present
        if (spec.Constraints != null && definition?.DefaultConstraints != null)
        {
            // Check for constraint conflicts
            if (spec.Constraints.MaxSizeBytes.HasValue &&
                definition.DefaultConstraints.MaxSizeBytes.HasValue &&
                spec.Constraints.MaxSizeBytes > definition.DefaultConstraints.MaxSizeBytes)
            {
                result.Warnings.Add(
                    $"{location}: Constraint MaxSizeBytes ({spec.Constraints.MaxSizeBytes}) exceeds " +
                    $"type default ({definition.DefaultConstraints.MaxSizeBytes})");
            }
        }
    }

    private static bool IsPrimitiveType(string typeName)
    {
        return typeName.ToLowerInvariant() switch
        {
            "string" => true,
            "int" => true,
            "integer" => true,
            "long" => true,
            "double" => true,
            "float" => true,
            "number" => true,
            "bool" => true,
            "boolean" => true,
            "object" => true,
            "array" => true,
            "enum" => true,
            _ => false
        };
    }
}

/// <summary>
/// Result of manifest entity type validation.
/// </summary>
public class ManifestValidationResult
{
    public string ManifestName { get; set; } = "";
    public bool IsValid { get; set; }
    public List<string> Errors { get; set; } = [];
    public List<string> Warnings { get; set; } = [];
}
