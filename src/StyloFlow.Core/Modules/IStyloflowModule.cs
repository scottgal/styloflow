using Microsoft.Extensions.DependencyInjection;

namespace StyloFlow.Modules;

/// <summary>
/// Contract for StyloFlow plugin modules.
/// Each plugin assembly exposes one entry type implementing this interface.
/// </summary>
/// <remarks>
/// Discovery options:
/// - Compile-time registration: NuGet package + services.AddMyPlugin()
/// - Attribute scan: scan assemblies already referenced
/// - Dynamic load: load from plugins folder (signed + allowlisted by sfpkg manifest hash)
/// </remarks>
public interface IStyloflowModule
{
    /// <summary>
    /// Unique identifier for this module (e.g., "mostlylucid.botdetection").
    /// </summary>
    string Id { get; }

    /// <summary>
    /// Module version.
    /// </summary>
    Version Version { get; }

    /// <summary>
    /// Human-readable name for display in UI.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Brief description of the module's functionality.
    /// </summary>
    string Description { get; }

    /// <summary>
    /// Register services with dependency injection.
    /// </summary>
    void ConfigureServices(IServiceCollection services, IStyloflowModuleContext context);
}

/// <summary>
/// Extended module interface for modules that want to map HTTP endpoints.
/// Implement this if your module needs to register API endpoints.
/// The endpointRouteBuilder parameter is an IEndpointRouteBuilder when used with ASP.NET Core.
/// </summary>
public interface IStyloflowWebModule : IStyloflowModule
{
    /// <summary>
    /// Map API endpoints. The builder parameter is IEndpointRouteBuilder.
    /// </summary>
    /// <param name="endpointRouteBuilder">The endpoint route builder (IEndpointRouteBuilder)</param>
    /// <param name="context">The module context</param>
    void MapEndpoints(object endpointRouteBuilder, IStyloflowModuleContext context);
}

/// <summary>
/// Context provided to modules during configuration.
/// </summary>
public interface IStyloflowModuleContext
{
    /// <summary>
    /// The service provider (available during endpoint mapping).
    /// </summary>
    IServiceProvider? ServiceProvider { get; }

    /// <summary>
    /// The host environment name (Development, Production, etc.).
    /// </summary>
    string EnvironmentName { get; }

    /// <summary>
    /// Whether the module is running in licensed mode.
    /// </summary>
    bool IsLicensed { get; }

    /// <summary>
    /// The license tier (free, starter, professional, enterprise).
    /// </summary>
    string LicenseTier { get; }

    /// <summary>
    /// Check if a specific feature is available.
    /// </summary>
    bool HasFeature(string featureId);
}

/// <summary>
/// Default implementation of IStyloflowModuleContext.
/// </summary>
public sealed class StyloflowModuleContext : IStyloflowModuleContext
{
    public IServiceProvider? ServiceProvider { get; init; }
    public string EnvironmentName { get; init; } = "Production";
    public bool IsLicensed { get; init; }
    public string LicenseTier { get; init; } = "free";

    private readonly HashSet<string> _features = new(StringComparer.OrdinalIgnoreCase);

    public void AddFeature(string featureId) => _features.Add(featureId);

    public bool HasFeature(string featureId)
    {
        // Support wildcard matching (e.g., "botdetection.*")
        if (_features.Contains(featureId))
            return true;

        // Check for wildcard patterns
        foreach (var feature in _features)
        {
            if (feature.EndsWith(".*") &&
                featureId.StartsWith(feature[..^2], StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }
}
