using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using StyloFlow.Converters.Extensions;

namespace StyloFlow.Converters.Docling.Extensions;

/// <summary>
/// Extension methods for registering Docling converter.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds Docling document converter.
    /// </summary>
    public static IServiceCollection AddDoclingConverter(this IServiceCollection services)
    {
        return services.AddDoclingConverter(_ => { });
    }

    /// <summary>
    /// Adds Docling document converter with configuration.
    /// </summary>
    public static IServiceCollection AddDoclingConverter(
        this IServiceCollection services,
        Action<DoclingConfig> configure)
    {
        // Ensure core is registered
        services.AddConverterCore();

        // Configure Docling
        var config = new DoclingConfig();
        configure(config);
        services.TryAddSingleton(config);

        // Register converter
        services.AddTransient<DoclingConverter>();
        services.AddTransient<IContentConverter>(sp => sp.GetRequiredService<DoclingConverter>());

        return services;
    }

    /// <summary>
    /// Adds Docling converter bound to configuration section.
    /// </summary>
    public static IServiceCollection AddDoclingConverter(
        this IServiceCollection services,
        IConfigurationSection configSection)
    {
        services.AddConverterCore();

        // Bind configuration section
        var config = new DoclingConfig();
        configSection.Bind(config);
        services.TryAddSingleton(config);

        services.AddTransient<DoclingConverter>();
        services.AddTransient<IContentConverter>(sp => sp.GetRequiredService<DoclingConverter>());
        return services;
    }
}
