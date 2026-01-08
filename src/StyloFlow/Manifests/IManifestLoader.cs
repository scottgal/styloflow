namespace StyloFlow.Manifests;

/// <summary>
/// Interface for loading YAML manifests from various sources.
/// </summary>
public interface IManifestLoader
{
    /// <summary>
    /// Get a manifest by component name.
    /// </summary>
    ComponentManifest? GetManifest(string componentName);

    /// <summary>
    /// Get all loaded manifests.
    /// </summary>
    IReadOnlyDictionary<string, ComponentManifest> GetAllManifests();

    /// <summary>
    /// Reload manifests from sources.
    /// </summary>
    Task ReloadAsync(CancellationToken cancellationToken = default);
}
