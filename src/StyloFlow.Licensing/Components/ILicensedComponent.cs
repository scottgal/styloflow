namespace StyloFlow.Licensing.Components;

/// <summary>
/// Interface for components that have license-aware behavior.
/// Allows fine-grained control over which signals are emitted in free vs licensed mode.
/// Can be configured via attributes, code, or YAML manifests.
/// </summary>
public interface ILicensedComponent
{
    /// <summary>
    /// The unique identifier for this component type.
    /// Used to match manifests and configuration.
    /// </summary>
    string ComponentId { get; }

    /// <summary>
    /// Gets the license requirements for this component.
    /// </summary>
    LicenseRequirements Requirements { get; }

    /// <summary>
    /// Signals to defer on until license is validated (licensed mode only).
    /// Component will wait for these signals before starting.
    /// </summary>
    IReadOnlyList<string> DeferOnSignals { get; }

    /// <summary>
    /// Signals that resume this component (licensed mode only).
    /// </summary>
    IReadOnlyList<string> ResumeOnSignals { get; }

    /// <summary>
    /// Signals emitted when running in free tier mode.
    /// These are typically warning/degraded signals.
    /// </summary>
    IReadOnlyList<string> FreeTierSignals { get; }

    /// <summary>
    /// Signals emitted when running in licensed mode.
    /// These are typically the full-feature signals.
    /// </summary>
    IReadOnlyList<string> LicensedSignals { get; }

    /// <summary>
    /// Whether this component is currently operating in licensed mode.
    /// </summary>
    bool IsLicensed { get; }
}

/// <summary>
/// License requirements for a component.
/// </summary>
public sealed record LicenseRequirements
{
    /// <summary>
    /// Minimum license tier required (free, starter, professional, enterprise).
    /// </summary>
    public string MinimumTier { get; init; } = "free";

    /// <summary>
    /// Required features that must be enabled in the license.
    /// </summary>
    public IReadOnlyList<string> RequiredFeatures { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Base work units consumed per operation.
    /// </summary>
    public double WorkUnits { get; init; } = 1.0;

    /// <summary>
    /// Work units per KB of data processed (for data-proportional metering).
    /// </summary>
    public double WorkUnitsPerKb { get; init; } = 0.0;

    /// <summary>
    /// Whether this component requires the System Coordinator to be running.
    /// </summary>
    public bool RequiresSystemCoordinator { get; init; } = true;

    /// <summary>
    /// Whether to allow degraded operation in free tier.
    /// If false, component will refuse to run without a valid license.
    /// </summary>
    public bool AllowFreeTierDegradation { get; init; } = true;

    /// <summary>
    /// Create requirements for a free-tier component.
    /// </summary>
    public static LicenseRequirements FreeTier => new()
    {
        MinimumTier = "free",
        RequiresSystemCoordinator = false,
        AllowFreeTierDegradation = true
    };

    /// <summary>
    /// Create requirements for a licensed component.
    /// </summary>
    public static LicenseRequirements Licensed(string tier = "starter", double workUnits = 1.0) => new()
    {
        MinimumTier = tier,
        WorkUnits = workUnits,
        RequiresSystemCoordinator = true,
        AllowFreeTierDegradation = true
    };
}

/// <summary>
/// YAML manifest model for licensed component configuration.
/// Allows specifying license-aware behavior in declarative YAML.
/// </summary>
public sealed record LicensedComponentManifest
{
    /// <summary>
    /// Component identifier.
    /// </summary>
    public required string Id { get; init; }

    /// <summary>
    /// Human-readable name.
    /// </summary>
    public string? Name { get; init; }

    /// <summary>
    /// Component description.
    /// </summary>
    public string? Description { get; init; }

    /// <summary>
    /// License requirements section.
    /// </summary>
    public LicenseSection? License { get; init; }

    /// <summary>
    /// Signals section.
    /// </summary>
    public SignalsSection? Signals { get; init; }
}

/// <summary>
/// License section in YAML manifest.
/// </summary>
public sealed record LicenseSection
{
    /// <summary>
    /// Minimum tier required.
    /// </summary>
    public string? Tier { get; init; }

    /// <summary>
    /// Required feature flags.
    /// </summary>
    public List<string>? Features { get; init; }

    /// <summary>
    /// Work unit configuration.
    /// </summary>
    public WorkUnitSection? WorkUnits { get; init; }

    /// <summary>
    /// Whether System Coordinator is required.
    /// </summary>
    public bool RequiresSystem { get; init; } = true;

    /// <summary>
    /// Allow degraded free-tier operation.
    /// </summary>
    public bool AllowDegradation { get; init; } = true;
}

/// <summary>
/// Work unit configuration in YAML manifest.
/// </summary>
public sealed record WorkUnitSection
{
    /// <summary>
    /// Base work units per operation.
    /// </summary>
    public double Base { get; init; } = 1.0;

    /// <summary>
    /// Additional work units per KB of data.
    /// </summary>
    public double PerKb { get; init; } = 0.0;
}

/// <summary>
/// Signals section in YAML manifest.
/// </summary>
public sealed record SignalsSection
{
    /// <summary>
    /// Signals to defer on (wait for before starting).
    /// </summary>
    public List<string>? DeferOn { get; init; }

    /// <summary>
    /// Signals to resume on.
    /// </summary>
    public List<string>? ResumeOn { get; init; }

    /// <summary>
    /// Signals emitted in free tier mode.
    /// </summary>
    public List<string>? FreeTier { get; init; }

    /// <summary>
    /// Signals emitted in licensed mode.
    /// </summary>
    public List<string>? Licensed { get; init; }
}
