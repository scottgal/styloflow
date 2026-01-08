using System.Reflection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using StyloFlow.Configuration;
using StyloFlow.Entities;
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

    /// <summary>
    /// Add StyloFlow entity type services (registry, loader, validator).
    /// </summary>
    public static IServiceCollection AddStyloFlowEntities(
        this IServiceCollection services,
        Action<EntityTypeRegistry>? configureRegistry = null)
    {
        // Register entity type registry as singleton
        services.TryAddSingleton(sp =>
        {
            var registry = new EntityTypeRegistry();
            configureRegistry?.Invoke(registry);
            return registry;
        });

        // Register entity type loader
        services.TryAddSingleton(sp =>
        {
            var logger = sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<EntityTypeLoader>>();
            return new EntityTypeLoader(logger);
        });

        // Register manifest entity validator
        services.TryAddSingleton(sp =>
        {
            var registry = sp.GetRequiredService<EntityTypeRegistry>();
            var logger = sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<ManifestEntityValidator>>();
            return new ManifestEntityValidator(registry, logger);
        });

        return services;
    }

    /// <summary>
    /// Add StyloFlow entity types from YAML files in a directory.
    /// </summary>
    public static IServiceCollection AddStyloFlowEntitiesFromDirectory(
        this IServiceCollection services,
        string directory,
        string pattern = "*.entity.yaml")
    {
        services.AddStyloFlowEntities(registry =>
        {
            var loader = new EntityTypeLoader(
                Microsoft.Extensions.Logging.Abstractions.NullLogger<EntityTypeLoader>.Instance);

            foreach (var entityType in loader.LoadFromDirectory(directory, pattern))
            {
                registry.Register(entityType);
            }
        });

        return services;
    }

    /// <summary>
    /// Add StyloFlow entity types from embedded resources in assemblies.
    /// </summary>
    public static IServiceCollection AddStyloFlowEntitiesFromAssemblies(
        this IServiceCollection services,
        Assembly[] assemblies,
        string pattern = ".entity.yaml")
    {
        services.AddStyloFlowEntities(registry =>
        {
            var loader = new EntityTypeLoader(
                Microsoft.Extensions.Logging.Abstractions.NullLogger<EntityTypeLoader>.Instance);

            foreach (var assembly in assemblies)
            {
                foreach (var entityType in loader.LoadFromAssembly(assembly, pattern))
                {
                    registry.Register(entityType);
                }
            }
        });

        return services;
    }
}
