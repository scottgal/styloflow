namespace StyloFlow.Manifests;

/// <summary>
/// Base manifest for any orchestrated component (detector, processor, handler, etc.).
/// YAML-driven configuration with taxonomy metadata and signal declarations.
/// </summary>
public class ComponentManifest
{
    public string Name { get; set; } = "";
    public int Priority { get; set; }
    public bool Enabled { get; set; } = true;
    public string Description { get; set; } = "";

    public SignalScope Scope { get; set; } = new();
    public TaxonomyInfo Taxonomy { get; set; } = new();
    public TriggerConfig Triggers { get; set; } = new();
    public EmitConfig Emits { get; set; } = new();
    public ListenConfig Listens { get; set; } = new();
    public LaneConfig Lane { get; set; } = new();
    public EscalationConfig? Escalation { get; set; }
    public BudgetConfig? Budget { get; set; }
    public ComponentDefaults Defaults { get; set; } = new();
    public ConfigBindings? Config { get; set; }
    public List<string> Tags { get; set; } = [];
}

/// <summary>
/// Signal scope defining where signals are published/consumed.
/// </summary>
public class SignalScope
{
    public string Sink { get; set; } = "";
    public string Coordinator { get; set; } = "";
    public string Atom { get; set; } = "";
}

/// <summary>
/// Taxonomy classification for the component.
/// </summary>
public class TaxonomyInfo
{
    public string Kind { get; set; } = "sensor";
    public string Determinism { get; set; } = "deterministic";
    public string Persistence { get; set; } = "ephemeral";
}

/// <summary>
/// Trigger conditions for when the component should run.
/// </summary>
public class TriggerConfig
{
    public List<TriggerRequirement> Requires { get; set; } = [];
    public List<string> SkipWhen { get; set; } = [];
    public List<string> When { get; set; } = [];
}

/// <summary>
/// A single trigger requirement with optional condition.
/// </summary>
public class TriggerRequirement
{
    public string Signal { get; set; } = "";
    public string? Condition { get; set; }
    public object? Value { get; set; }
}

/// <summary>
/// Signals emitted by the component.
/// </summary>
public class EmitConfig
{
    public List<string> OnStart { get; set; } = [];
    public List<SignalDefinition> OnComplete { get; set; } = [];
    public List<string> OnFailure { get; set; } = [];
    public List<ConditionalSignal> Conditional { get; set; } = [];
}

/// <summary>
/// Definition of a signal that can be emitted.
/// </summary>
public class SignalDefinition
{
    public string Key { get; set; } = "";
    public string Type { get; set; } = "string";
    public string Description { get; set; } = "";
    public double[]? ConfidenceRange { get; set; }
}

/// <summary>
/// Conditional signal emitted based on state.
/// </summary>
public class ConditionalSignal
{
    public string Key { get; set; } = "";
    public string Type { get; set; } = "bool";
    public string When { get; set; } = "";
    public string Description { get; set; } = "";
}

/// <summary>
/// Signals the component listens for.
/// </summary>
public class ListenConfig
{
    public List<string> Required { get; set; } = [];
    public List<string> Optional { get; set; } = [];
}

/// <summary>
/// Execution lane configuration.
/// </summary>
public class LaneConfig
{
    public string Name { get; set; } = "default";
    public int MaxConcurrency { get; set; } = 4;
    public int Priority { get; set; } = 50;
}

/// <summary>
/// Escalation configuration for forwarding to more expensive analysis.
/// </summary>
public class EscalationConfig
{
    public Dictionary<string, EscalationTarget> Targets { get; set; } = [];
}

/// <summary>
/// A single escalation target.
/// </summary>
public class EscalationTarget
{
    public List<TriggerRequirement> When { get; set; } = [];
    public List<TriggerRequirement> SkipWhen { get; set; } = [];
    public string Description { get; set; } = "";
}

/// <summary>
/// Resource budget constraints.
/// </summary>
public class BudgetConfig
{
    public string? MaxDuration { get; set; }
    public int? MaxTokens { get; set; }
    public double? MaxCost { get; set; }
}

/// <summary>
/// Default configuration values for the component.
/// </summary>
public class ComponentDefaults
{
    public WeightConfig Weights { get; set; } = new();
    public ConfidenceConfig Confidence { get; set; } = new();
    public TimingConfig Timing { get; set; } = new();
    public FeatureConfig Features { get; set; } = new();
    public Dictionary<string, object> Parameters { get; set; } = [];
}

/// <summary>
/// Weight configuration for signal contributions.
/// </summary>
public class WeightConfig
{
    public double Base { get; set; } = 1.0;
    public double BotSignal { get; set; } = 1.2;
    public double HumanSignal { get; set; } = 0.9;
    public double Verified { get; set; } = 1.5;
    public double EarlyExit { get; set; } = 2.0;
}

/// <summary>
/// Confidence thresholds and values.
/// </summary>
public class ConfidenceConfig
{
    public double Neutral { get; set; } = 0.0;
    public double BotDetected { get; set; } = 0.3;
    public double HumanIndicated { get; set; } = -0.1;
    public double StrongSignal { get; set; } = 0.6;
    public double HighThreshold { get; set; } = 0.7;
    public double LowThreshold { get; set; } = 0.2;
    public double EscalationThreshold { get; set; } = 0.5;
}

/// <summary>
/// Timing configuration for execution.
/// </summary>
public class TimingConfig
{
    public int TimeoutMs { get; set; } = 100;
    public int CacheRefreshSec { get; set; } = 300;
}

/// <summary>
/// Feature flags for the component.
/// </summary>
public class FeatureConfig
{
    public bool DetailedLogging { get; set; }
    public bool EnableCache { get; set; }
    public bool CanEarlyExit { get; set; }
    public bool CanEscalate { get; set; }
}

/// <summary>
/// Configuration bindings for appsettings integration.
/// </summary>
public class ConfigBindings
{
    public List<ConfigBinding> Bindings { get; set; } = [];
}

/// <summary>
/// A single configuration binding.
/// </summary>
public class ConfigBinding
{
    public string ConfigKey { get; set; } = "";
    public bool SkipIfFalse { get; set; }
    public string? OverrideKey { get; set; }
}
