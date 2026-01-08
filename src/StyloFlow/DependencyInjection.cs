using System.Reflection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using StyloFlow.Configuration;
using StyloFlow.Manifests;

namespace StyloFlow;

/// <summary>
/// Extension methods for registering StyloFlow services with dependency injection.
/// </summary>
public static class DependencyInjection
{
    /// <summary>
    /// Add StyloFlow services with file system manifest loading.
    /// </summary>
    public static IServiceCollection AddStyloFlow(
        this IServiceCollection services,
        string[] manifestDirectories,
        string configSectionPath = "Components")
    {
        // Register manifest loader
        services.TryAddSingleton<IManifestLoader>(sp =>
        {
            var logger = sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<FileSystemManifestLoader>>();
            return new FileSystemManifestLoader(logger, manifestDirectories);
        });

        // Register config provider
        services.TryAddSingleton<IConfigProvider>(sp =>
        {
            var manifestLoader = sp.GetRequiredService<IManifestLoader>();
            var logger = sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<ConfigProvider>>();
            var configuration = sp.GetService<IConfiguration>();
            return new ConfigProvider(manifestLoader, logger, configuration, configSectionPath);
        });

        return services;
    }

    /// <summary>
    /// Add StyloFlow services with embedded resource manifest loading.
    /// </summary>
    public static IServiceCollection AddStyloFlowFromAssemblies(
        this IServiceCollection services,
        Assembly[] sourceAssemblies,
        string manifestPattern = ".yaml",
        string configSectionPath = "Components")
    {
        // Register manifest loader
        services.TryAddSingleton<IManifestLoader>(sp =>
        {
            var logger = sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<EmbeddedManifestLoader>>();
            return new EmbeddedManifestLoader(logger, sourceAssemblies, manifestPattern);
        });

        // Register config provider
        services.TryAddSingleton<IConfigProvider>(sp =>
        {
            var manifestLoader = sp.GetRequiredService<IManifestLoader>();
            var logger = sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<ConfigProvider>>();
            var configuration = sp.GetService<IConfiguration>();
            return new ConfigProvider(manifestLoader, logger, configuration, configSectionPath);
        });

        return services;
    }

    /// <summary>
    /// Add StyloFlow services with a custom manifest loader.
    /// </summary>
    public static IServiceCollection AddStyloFlow(
        this IServiceCollection services,
        Func<IServiceProvider, IManifestLoader> manifestLoaderFactory,
        string configSectionPath = "Components")
    {
        services.TryAddSingleton(manifestLoaderFactory);

        services.TryAddSingleton<IConfigProvider>(sp =>
        {
            var manifestLoader = sp.GetRequiredService<IManifestLoader>();
            var logger = sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<ConfigProvider>>();
            var configuration = sp.GetService<IConfiguration>();
            return new ConfigProvider(manifestLoader, logger, configuration, configSectionPath);
        });

        return services;
    }
}
