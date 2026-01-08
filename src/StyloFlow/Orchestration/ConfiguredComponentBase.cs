using StyloFlow.Configuration;
using StyloFlow.Manifests;

namespace StyloFlow.Orchestration;

/// <summary>
/// Base class for config-driven orchestrated components.
/// Provides convenient access to YAML manifest configuration with appsettings overrides.
/// Eliminates magic numbers by pulling all values from configuration.
/// </summary>
public abstract class ConfiguredComponentBase
{
    private readonly IConfigProvider _configProvider;
    private ComponentDefaults? _cachedConfig;
    private ComponentManifest? _cachedManifest;

    protected ConfiguredComponentBase(IConfigProvider configProvider)
    {
        _configProvider = configProvider;
    }

    /// <summary>
    /// The component name used to lookup manifest (typically class name without "Contributor" suffix).
    /// Override to customize the manifest lookup.
    /// </summary>
    public virtual string ManifestName => GetType().Name;

    /// <summary>
    /// The loaded manifest for this component, or null if not found.
    /// </summary>
    protected ComponentManifest? Manifest => _cachedManifest ??= _configProvider.GetManifest(ManifestName);

    /// <summary>
    /// The resolved configuration with hierarchy: appsettings > YAML > code defaults.
    /// </summary>
    protected ComponentDefaults Config => _cachedConfig ??= _configProvider.GetDefaults(ManifestName);

    // ===== Weight shortcuts =====

    /// <summary>Base weight from YAML: defaults.weights.base</summary>
    protected double WeightBase => Config.Weights.Base;

    /// <summary>Bot signal weight from YAML: defaults.weights.bot_signal</summary>
    protected double WeightBotSignal => Config.Weights.BotSignal;

    /// <summary>Human signal weight from YAML: defaults.weights.human_signal</summary>
    protected double WeightHumanSignal => Config.Weights.HumanSignal;

    /// <summary>Verified pattern weight from YAML: defaults.weights.verified</summary>
    protected double WeightVerified => Config.Weights.Verified;

    /// <summary>Early exit weight from YAML: defaults.weights.early_exit</summary>
    protected double WeightEarlyExit => Config.Weights.EarlyExit;

    // ===== Confidence shortcuts =====

    /// <summary>Neutral confidence from YAML: defaults.confidence.neutral</summary>
    protected double ConfidenceNeutral => Config.Confidence.Neutral;

    /// <summary>Bot detected confidence from YAML: defaults.confidence.bot_detected</summary>
    protected double ConfidenceBotDetected => Config.Confidence.BotDetected;

    /// <summary>Human indicated confidence from YAML: defaults.confidence.human_indicated</summary>
    protected double ConfidenceHumanIndicated => Config.Confidence.HumanIndicated;

    /// <summary>Strong signal confidence from YAML: defaults.confidence.strong_signal</summary>
    protected double ConfidenceStrongSignal => Config.Confidence.StrongSignal;

    /// <summary>High threshold from YAML: defaults.confidence.high_threshold</summary>
    protected double ConfidenceHighThreshold => Config.Confidence.HighThreshold;

    /// <summary>Low threshold from YAML: defaults.confidence.low_threshold</summary>
    protected double ConfidenceLowThreshold => Config.Confidence.LowThreshold;

    /// <summary>Escalation threshold from YAML: defaults.confidence.escalation_threshold</summary>
    protected double ConfidenceEscalationThreshold => Config.Confidence.EscalationThreshold;

    // ===== Timing shortcuts =====

    /// <summary>Timeout in milliseconds from YAML: defaults.timing.timeout_ms</summary>
    protected int TimeoutMs => Config.Timing.TimeoutMs;

    /// <summary>Cache refresh interval from YAML: defaults.timing.cache_refresh_sec</summary>
    protected int CacheRefreshSec => Config.Timing.CacheRefreshSec;

    // ===== Feature shortcuts =====

    /// <summary>Detailed logging enabled from YAML: defaults.features.detailed_logging</summary>
    protected bool DetailedLogging => Config.Features.DetailedLogging;

    /// <summary>Cache enabled from YAML: defaults.features.enable_cache</summary>
    protected bool CacheEnabled => Config.Features.EnableCache;

    /// <summary>Can trigger early exit from YAML: defaults.features.can_early_exit</summary>
    protected bool CanEarlyExit => Config.Features.CanEarlyExit;

    /// <summary>Can escalate to expensive analysis from YAML: defaults.features.can_escalate</summary>
    protected bool CanEscalate => Config.Features.CanEscalate;

    // ===== Parameter access =====

    /// <summary>
    /// Get a typed parameter from YAML with fallback hierarchy.
    /// </summary>
    protected T GetParam<T>(string name, T defaultValue)
    {
        return _configProvider.GetParameter(ManifestName, name, defaultValue);
    }

    /// <summary>
    /// Get a string list parameter from YAML.
    /// </summary>
    protected IReadOnlyList<string> GetStringListParam(string name)
    {
        return GetParam<List<string>>(name, []) ?? [];
    }

    /// <summary>
    /// Check if a feature flag is enabled in the manifest.
    /// </summary>
    protected bool IsFeatureEnabled(string featureName)
    {
        return GetParam(featureName, false);
    }
}
