using System.Collections.Concurrent;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using StyloFlow.Manifests;

namespace StyloFlow.Configuration;

/// <summary>
/// Default config provider implementation with hierarchy:
/// 1. appsettings.json overrides (highest priority)
/// 2. YAML manifest defaults
/// 3. Code defaults (fallback)
/// </summary>
public class ConfigProvider : IConfigProvider
{
    private readonly IManifestLoader _manifestLoader;
    private readonly IConfiguration? _configuration;
    private readonly ILogger<ConfigProvider> _logger;
    private readonly string _configSectionPath;
    private readonly ConcurrentDictionary<string, ComponentDefaults> _defaultsCache = new();

    public ConfigProvider(
        IManifestLoader manifestLoader,
        ILogger<ConfigProvider> logger,
        IConfiguration? configuration = null,
        string configSectionPath = "Components")
    {
        _manifestLoader = manifestLoader;
        _configuration = configuration;
        _logger = logger;
        _configSectionPath = configSectionPath;
    }

    public ComponentManifest? GetManifest(string componentName)
    {
        return _manifestLoader.GetManifest(componentName);
    }

    public ComponentDefaults GetDefaults(string componentName)
    {
        return _defaultsCache.GetOrAdd(componentName, name =>
        {
            var manifest = GetManifest(name);
            if (manifest == null)
            {
                _logger.LogDebug("No manifest found for {ComponentName}, using code defaults", name);
                return new ComponentDefaults();
            }

            // Start with YAML defaults
            var defaults = manifest.Defaults;

            // Apply appsettings overrides if configuration is available
            if (_configuration != null)
            {
                var section = _configuration.GetSection($"{_configSectionPath}:{name}");
                if (section.Exists())
                {
                    ApplyConfigurationOverrides(defaults, section);
                }
            }

            return defaults;
        });
    }

    public T GetParameter<T>(string componentName, string parameterName, T defaultValue)
    {
        // 1. Check appsettings override first
        if (_configuration != null)
        {
            var configPath = $"{_configSectionPath}:{componentName}:Parameters:{parameterName}";
            var configValue = _configuration.GetValue<T>(configPath);
            if (configValue != null && !EqualityComparer<T>.Default.Equals(configValue, default))
            {
                return configValue;
            }
        }

        // 2. Check YAML manifest
        var manifest = GetManifest(componentName);
        if (manifest?.Defaults.Parameters.TryGetValue(parameterName, out var yamlValue) == true)
        {
            try
            {
                return ConvertValue<T>(yamlValue);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to convert parameter {Parameter} for {Component}",
                    parameterName, componentName);
            }
        }

        // 3. Fall back to code default
        return defaultValue;
    }

    public IReadOnlyDictionary<string, ComponentManifest> GetAllManifests()
    {
        return _manifestLoader.GetAllManifests();
    }

    private void ApplyConfigurationOverrides(ComponentDefaults defaults, IConfigurationSection section)
    {
        // Override weights
        var weightsSection = section.GetSection("Weights");
        if (weightsSection.Exists())
        {
            defaults.Weights.Base = weightsSection.GetValue("Base", defaults.Weights.Base);
            defaults.Weights.BotSignal = weightsSection.GetValue("BotSignal", defaults.Weights.BotSignal);
            defaults.Weights.HumanSignal = weightsSection.GetValue("HumanSignal", defaults.Weights.HumanSignal);
            defaults.Weights.Verified = weightsSection.GetValue("Verified", defaults.Weights.Verified);
            defaults.Weights.EarlyExit = weightsSection.GetValue("EarlyExit", defaults.Weights.EarlyExit);
        }

        // Override confidence
        var confidenceSection = section.GetSection("Confidence");
        if (confidenceSection.Exists())
        {
            defaults.Confidence.Neutral = confidenceSection.GetValue("Neutral", defaults.Confidence.Neutral);
            defaults.Confidence.BotDetected = confidenceSection.GetValue("BotDetected", defaults.Confidence.BotDetected);
            defaults.Confidence.HumanIndicated = confidenceSection.GetValue("HumanIndicated", defaults.Confidence.HumanIndicated);
            defaults.Confidence.StrongSignal = confidenceSection.GetValue("StrongSignal", defaults.Confidence.StrongSignal);
            defaults.Confidence.HighThreshold = confidenceSection.GetValue("HighThreshold", defaults.Confidence.HighThreshold);
            defaults.Confidence.LowThreshold = confidenceSection.GetValue("LowThreshold", defaults.Confidence.LowThreshold);
            defaults.Confidence.EscalationThreshold = confidenceSection.GetValue("EscalationThreshold", defaults.Confidence.EscalationThreshold);
        }

        // Override timing
        var timingSection = section.GetSection("Timing");
        if (timingSection.Exists())
        {
            defaults.Timing.TimeoutMs = timingSection.GetValue("TimeoutMs", defaults.Timing.TimeoutMs);
            defaults.Timing.CacheRefreshSec = timingSection.GetValue("CacheRefreshSec", defaults.Timing.CacheRefreshSec);
        }

        // Override features
        var featuresSection = section.GetSection("Features");
        if (featuresSection.Exists())
        {
            defaults.Features.DetailedLogging = featuresSection.GetValue("DetailedLogging", defaults.Features.DetailedLogging);
            defaults.Features.EnableCache = featuresSection.GetValue("EnableCache", defaults.Features.EnableCache);
            defaults.Features.CanEarlyExit = featuresSection.GetValue("CanEarlyExit", defaults.Features.CanEarlyExit);
            defaults.Features.CanEscalate = featuresSection.GetValue("CanEscalate", defaults.Features.CanEscalate);
        }

        // Override individual parameters
        var parametersSection = section.GetSection("Parameters");
        if (parametersSection.Exists())
        {
            foreach (var child in parametersSection.GetChildren())
            {
                defaults.Parameters[child.Key] = child.Value ?? "";
            }
        }
    }

    private static T ConvertValue<T>(object value)
    {
        if (value is T typed)
            return typed;

        var targetType = typeof(T);

        // Handle nullable types
        var underlyingType = Nullable.GetUnderlyingType(targetType) ?? targetType;

        // Handle common conversions
        if (underlyingType == typeof(int))
            return (T)(object)Convert.ToInt32(value);
        if (underlyingType == typeof(double))
            return (T)(object)Convert.ToDouble(value);
        if (underlyingType == typeof(bool))
            return (T)(object)Convert.ToBoolean(value);
        if (underlyingType == typeof(string))
            return (T)(object)value.ToString()!;
        if (underlyingType == typeof(TimeSpan) && value is string timeString)
            return (T)(object)TimeSpan.Parse(timeString);

        // Handle lists
        if (value is IEnumerable<object> enumerable && targetType.IsGenericType)
        {
            var elementType = targetType.GetGenericArguments()[0];
            if (elementType == typeof(string))
            {
                var list = enumerable.Select(x => x.ToString()!).ToList();
                return (T)(object)list;
            }
        }

        throw new InvalidCastException($"Cannot convert {value.GetType()} to {typeof(T)}");
    }
}
