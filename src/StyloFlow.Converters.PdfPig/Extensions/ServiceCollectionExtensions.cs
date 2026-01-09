using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using StyloFlow.Converters.Extensions;

namespace StyloFlow.Converters.PdfPig.Extensions;

/// <summary>
/// Extension methods for registering PdfPig converter.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds PdfPig PDF converter with default settings.
    /// </summary>
    public static IServiceCollection AddPdfPigConverter(this IServiceCollection services)
    {
        return services.AddPdfPigConverter(_ => { });
    }

    /// <summary>
    /// Adds PdfPig PDF converter with configuration.
    /// </summary>
    public static IServiceCollection AddPdfPigConverter(
        this IServiceCollection services,
        Action<PdfPigConfig> configure)
    {
        services.AddConverterCore();

        var config = new PdfPigConfig();
        configure(config);
        services.TryAddSingleton(config);

        services.AddTransient<PdfPigConverter>();
        services.AddTransient<IContentConverter>(sp => sp.GetRequiredService<PdfPigConverter>());

        // Also register the OCR layout analyzer
        services.TryAddTransient<OcrLayoutAnalyzer>();

        return services;
    }

    /// <summary>
    /// Adds PdfPig converter bound to configuration section.
    /// </summary>
    public static IServiceCollection AddPdfPigConverter(
        this IServiceCollection services,
        IConfigurationSection configSection)
    {
        services.AddConverterCore();

        var config = new PdfPigConfig();
        configSection.Bind(config);
        services.TryAddSingleton(config);

        services.AddTransient<PdfPigConverter>();
        services.AddTransient<IContentConverter>(sp => sp.GetRequiredService<PdfPigConverter>());
        services.TryAddTransient<OcrLayoutAnalyzer>();

        return services;
    }

    /// <summary>
    /// Adds just the OCR layout analyzer (without full converter).
    /// Use this when you only need OCR post-processing.
    /// </summary>
    public static IServiceCollection AddOcrLayoutAnalyzer(
        this IServiceCollection services,
        Action<PdfPigConfig>? configure = null)
    {
        var config = new PdfPigConfig();
        configure?.Invoke(config);
        services.TryAddSingleton(config);
        services.TryAddTransient<OcrLayoutAnalyzer>();
        return services;
    }
}
