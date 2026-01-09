using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using StyloFlow.Converters.Storage;

namespace StyloFlow.Converters.Extensions;

/// <summary>
/// Extension methods for registering converter services.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds the core converter infrastructure.
    /// </summary>
    public static IServiceCollection AddConverterCore(this IServiceCollection services)
    {
        services.TryAddSingleton<IConverterRegistry, ConverterRegistry>();
        return services;
    }

    /// <summary>
    /// Adds filesystem-based shared storage for converters.
    /// </summary>
    /// <param name="services">Service collection.</param>
    /// <param name="basePath">Base path for storage. Defaults to temp directory.</param>
    public static IServiceCollection AddFilesystemStorage(
        this IServiceCollection services,
        string? basePath = null)
    {
        var path = basePath ?? Path.Combine(Path.GetTempPath(), "styloflow", "shared");
        services.TryAddSingleton<ISharedStorage>(sp =>
            new FilesystemSharedStorage(path, sp.GetService<Microsoft.Extensions.Logging.ILogger<FilesystemSharedStorage>>()));
        return services;
    }

    /// <summary>
    /// Adds a content converter to the registry.
    /// </summary>
    public static IServiceCollection AddConverter<T>(this IServiceCollection services)
        where T : class, IContentConverter
    {
        services.AddTransient<T>();
        services.AddTransient<IContentConverter, T>();
        return services;
    }
}
