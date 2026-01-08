using StyloFlow.Manifests;

namespace StyloFlow.Configuration;

/// <summary>
/// Provides configuration for orchestrated components with hierarchy:
/// 1. appsettings.json overrides (highest priority)
/// 2. YAML manifest defaults
/// 3. Code defaults (fallback)
/// </summary>
public interface IConfigProvider
{
    /// <summary>
    /// Get the manifest for a component by name.
    /// </summary>
    ComponentManifest? GetManifest(string componentName);

    /// <summary>
    /// Get the default configuration for a component.
    /// </summary>
    ComponentDefaults GetDefaults(string componentName);

    /// <summary>
    /// Get a typed parameter value with fallback hierarchy.
    /// </summary>
    T GetParameter<T>(string componentName, string parameterName, T defaultValue);

    /// <summary>
    /// Get all loaded manifests.
    /// </summary>
    IReadOnlyDictionary<string, ComponentManifest> GetAllManifests();
}
