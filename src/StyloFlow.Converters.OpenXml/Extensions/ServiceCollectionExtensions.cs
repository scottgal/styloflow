using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using StyloFlow.Converters.Extensions;

namespace StyloFlow.Converters.OpenXml.Extensions;

/// <summary>
/// Extension methods for registering OpenXml converter.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds OpenXml document converter with default settings.
    /// </summary>
    public static IServiceCollection AddOpenXmlConverter(this IServiceCollection services)
    {
        return services.AddOpenXmlConverter(_ => { });
    }

    /// <summary>
    /// Adds OpenXml document converter with configuration.
    /// </summary>
    public static IServiceCollection AddOpenXmlConverter(
        this IServiceCollection services,
        Action<OpenXmlConfig> configure)
    {
        services.AddConverterCore();

        var config = new OpenXmlConfig();
        configure(config);
        services.TryAddSingleton(config);

        services.AddTransient<OpenXmlConverter>();
        services.AddTransient<IContentConverter>(sp => sp.GetRequiredService<OpenXmlConverter>());

        return services;
    }

    /// <summary>
    /// Adds OpenXml converter bound to configuration section.
    /// </summary>
    public static IServiceCollection AddOpenXmlConverter(
        this IServiceCollection services,
        IConfigurationSection configSection)
    {
        services.AddConverterCore();

        var config = new OpenXmlConfig();
        configSection.Bind(config);
        services.TryAddSingleton(config);

        services.AddTransient<OpenXmlConverter>();
        services.AddTransient<IContentConverter>(sp => sp.GetRequiredService<OpenXmlConverter>());

        return services;
    }
}
