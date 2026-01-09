using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace StyloFlow.Ingestion;

/// <summary>
/// Registry for ingestion sources.
/// Provides plugin-style extensibility - new sources can be registered at runtime.
/// </summary>
public interface IIngestionSourceRegistry
{
    /// <summary>
    /// Get all registered source types.
    /// </summary>
    IReadOnlyList<string> SourceTypes { get; }

    /// <summary>
    /// Get a source instance by type.
    /// </summary>
    IIngestionSource? GetSource(string sourceType);

    /// <summary>
    /// Check if a source type is registered.
    /// </summary>
    bool IsRegistered(string sourceType);

    /// <summary>
    /// Get metadata about a source type.
    /// </summary>
    SourceTypeInfo? GetSourceInfo(string sourceType);
}

/// <summary>
/// Metadata about a registered source type.
/// </summary>
public record SourceTypeInfo
{
    public required string SourceType { get; init; }
    public required string DisplayName { get; init; }
    public string? Description { get; init; }
    public string[]? RequiredOptions { get; init; }
    public string[]? OptionalOptions { get; init; }
}

/// <summary>
/// Default implementation of ingestion source registry.
/// </summary>
public class IngestionSourceRegistry : IIngestionSourceRegistry
{
    private readonly Dictionary<string, Func<IServiceProvider, IIngestionSource>> _factories = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, SourceTypeInfo> _sourceInfo = new(StringComparer.OrdinalIgnoreCase);
    private readonly IServiceProvider _services;
    private readonly ILogger<IngestionSourceRegistry> _logger;

    public IngestionSourceRegistry(
        IServiceProvider services,
        ILogger<IngestionSourceRegistry> logger)
    {
        _services = services;
        _logger = logger;
    }

    public IReadOnlyList<string> SourceTypes => _factories.Keys.ToList();

    public IIngestionSource? GetSource(string sourceType)
    {
        if (!_factories.TryGetValue(sourceType, out var factory))
        {
            _logger.LogWarning("Ingestion source type not found: {SourceType}", sourceType);
            return null;
        }

        return factory(_services);
    }

    public bool IsRegistered(string sourceType) => _factories.ContainsKey(sourceType);

    public SourceTypeInfo? GetSourceInfo(string sourceType) =>
        _sourceInfo.TryGetValue(sourceType, out var info) ? info : null;

    /// <summary>
    /// Register a source type with a factory function.
    /// </summary>
    internal void Register<T>(SourceTypeInfo info) where T : IIngestionSource
    {
        _factories[info.SourceType] = sp => sp.GetRequiredService<T>();
        _sourceInfo[info.SourceType] = info;
        _logger.LogDebug("Registered ingestion source: {SourceType}", info.SourceType);
    }

    /// <summary>
    /// Register a source type with an instance factory.
    /// </summary>
    internal void Register(string sourceType, Func<IServiceProvider, IIngestionSource> factory, SourceTypeInfo info)
    {
        _factories[sourceType] = factory;
        _sourceInfo[sourceType] = info;
        _logger.LogDebug("Registered ingestion source: {SourceType}", sourceType);
    }
}

/// <summary>
/// Builder for configuring ingestion sources.
/// </summary>
public class IngestionSourceBuilder
{
    private readonly IServiceCollection _services;
    private readonly List<Action<IServiceProvider, IngestionSourceRegistry>> _registrations = [];

    public IngestionSourceBuilder(IServiceCollection services)
    {
        _services = services;
    }

    /// <summary>
    /// Register a source type.
    /// </summary>
    public IngestionSourceBuilder AddSource<T>(SourceTypeInfo? info = null) where T : class, IIngestionSource
    {
        _services.AddTransient<T>();

        _registrations.Add((sp, registry) =>
        {
            var source = sp.GetRequiredService<T>();
            var sourceInfo = info ?? new SourceTypeInfo
            {
                SourceType = source.SourceType,
                DisplayName = source.DisplayName
            };
            registry.Register<T>(sourceInfo);
        });

        return this;
    }

    /// <summary>
    /// Register a source type with a custom factory.
    /// </summary>
    public IngestionSourceBuilder AddSource(
        string sourceType,
        Func<IServiceProvider, IIngestionSource> factory,
        SourceTypeInfo info)
    {
        _registrations.Add((sp, registry) =>
        {
            registry.Register(sourceType, factory, info);
        });

        return this;
    }

    /// <summary>
    /// Build and configure the registry.
    /// </summary>
    internal void Configure(IServiceProvider services, IngestionSourceRegistry registry)
    {
        foreach (var registration in _registrations)
        {
            registration(services, registry);
        }
    }
}
