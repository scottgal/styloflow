namespace StyloFlow.Licensing.Attributes;

/// <summary>
/// Marks a job or class as requiring StyloFlow licensing.
/// When applied, the component will wait for the System Coordinator
/// to emit license validation signals before processing.
/// </summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = false)]
public sealed class StyloFlowLicensedAttribute : Attribute
{
    /// <summary>
    /// Work units consumed per invocation.
    /// Default: 1.0
    /// </summary>
    public double WorkUnits { get; set; } = 1.0;

    /// <summary>
    /// Additional work units per KB of input processed.
    /// Useful for data-intensive operations.
    /// Default: 0.0
    /// </summary>
    public double WorkUnitsPerKb { get; set; } = 0.0;

    /// <summary>
    /// Minimum license tier required to run this component.
    /// Values: "free", "starter", "professional", "enterprise"
    /// Default: "free"
    /// </summary>
    public string Tier { get; set; } = "free";

    /// <summary>
    /// If true (default), requires System Coordinator to be running and authenticated.
    /// If false, component runs without licensing checks (use for truly free components).
    /// </summary>
    public bool RequiresSystem { get; set; } = true;

    /// <summary>
    /// Feature flags required to run this component.
    /// Empty means no specific features required (just tier check).
    /// </summary>
    public string[] Features { get; set; } = Array.Empty<string>();
}

/// <summary>
/// Declares a capability that this component provides.
/// Used for capability-based discovery and routing.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
public sealed class ProvidesAttribute : Attribute
{
    /// <summary>
    /// The capability identifier (e.g., "detection.bot", "sensor.file").
    /// </summary>
    public string Capability { get; }

    /// <summary>
    /// Optional version constraint for this capability.
    /// </summary>
    public string? Version { get; set; }

    public ProvidesAttribute(string capability)
    {
        Capability = capability ?? throw new ArgumentNullException(nameof(capability));
    }
}

/// <summary>
/// Declares a capability that this component requires to function.
/// The runtime will ensure dependencies are available before starting.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
public sealed class RequiresCapabilityAttribute : Attribute
{
    /// <summary>
    /// The capability identifier required (e.g., "retrieval.core").
    /// </summary>
    public string Capability { get; }

    /// <summary>
    /// Version constraint (e.g., ">=1.0.0", "^2.0").
    /// </summary>
    public string? Version { get; set; }

    /// <summary>
    /// If true, component can function without this capability (degraded mode).
    /// Default: false (required).
    /// </summary>
    public bool Optional { get; set; } = false;

    public RequiresCapabilityAttribute(string capability)
    {
        Capability = capability ?? throw new ArgumentNullException(nameof(capability));
    }
}
