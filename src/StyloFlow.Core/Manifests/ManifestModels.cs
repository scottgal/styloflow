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
    public InputContract Input { get; set; } = new();
    public OutputContract Output { get; set; } = new();
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
/// Input contract for a component - what entity types it accepts.
/// EntityType signals define the shape of acceptable inputs.
/// </summary>
public class InputContract
{
    /// <summary>
    /// Entity types this component can process.
    /// Examples: "http.request", "image/*", "behavioral.signature"
    /// </summary>
    public List<EntityTypeSpec> Accepts { get; set; } = [];

    /// <summary>
    /// Signal patterns required in the SignalSink before this component runs.
    /// </summary>
    public List<string> RequiredSignals { get; set; } = [];

    /// <summary>
    /// Optional signal patterns that enhance processing if present.
    /// </summary>
    public List<string> OptionalSignals { get; set; } = [];
}

/// <summary>
/// Output contract for a component - what entity types it produces.
/// </summary>
public class OutputContract
{
    /// <summary>
    /// Entity types this component produces.
    /// </summary>
    public List<EntityTypeSpec> Produces { get; set; } = [];

    /// <summary>
    /// Signal patterns emitted on success.
    /// </summary>
    public List<SignalSpec> Signals { get; set; } = [];
}

/// <summary>
/// Specification for an entity type - the fundamental contract for atom I/O.
/// Combines MIME types, schemas, and signal patterns into a unified contract.
/// </summary>
public class EntityTypeSpec
{
    /// <summary>
    /// Entity type identifier. Hierarchical dot-notation.
    /// Examples:
    ///   - "http.request" - HTTP request
    ///   - "http.response" - HTTP response
    ///   - "image.png", "image/*" - Image files
    ///   - "video.mp4", "video/*" - Video files
    ///   - "behavioral.signature" - User behavior pattern
    ///   - "document.pdf" - PDF document
    ///   - "audio.wav" - Audio file
    /// </summary>
    public string Type { get; set; } = "";

    /// <summary>
    /// MIME type for file-based entities.
    /// Examples: "image/png", "application/pdf", "video/mp4"
    /// </summary>
    public string? MimeType { get; set; }

    /// <summary>
    /// Reference to a schema definition (JSON Schema, YAML, etc.)
    /// Can be an inline definition or a path to an external file.
    /// </summary>
    public SchemaRef? Schema { get; set; }

    /// <summary>
    /// Signal pattern for extracting this entity from SignalSink.
    /// Uses glob patterns: "request.headers.*", "ip.geo.*"
    /// </summary>
    public string? SignalPattern { get; set; }

    /// <summary>
    /// Whether this entity type is required or optional.
    /// </summary>
    public bool Required { get; set; } = true;

    /// <summary>
    /// Human-readable description of this entity type.
    /// </summary>
    public string Description { get; set; } = "";

    /// <summary>
    /// Validation constraints for this entity type.
    /// </summary>
    public EntityConstraints? Constraints { get; set; }
}

/// <summary>
/// Reference to a schema definition for structured entities.
/// </summary>
public class SchemaRef
{
    /// <summary>
    /// Schema format: "json-schema", "yaml", "proto", "inline"
    /// </summary>
    public string Format { get; set; } = "json-schema";

    /// <summary>
    /// Schema location - can be a file path, URL, or inline definition.
    /// </summary>
    public string? Location { get; set; }

    /// <summary>
    /// Inline schema definition (for simple schemas).
    /// </summary>
    public Dictionary<string, object>? Inline { get; set; }

    /// <summary>
    /// Version of the schema.
    /// </summary>
    public string? Version { get; set; }
}

/// <summary>
/// Constraints for entity validation.
/// </summary>
public class EntityConstraints
{
    /// <summary>
    /// Maximum size in bytes (for files).
    /// </summary>
    public long? MaxSizeBytes { get; set; }

    /// <summary>
    /// Minimum size in bytes (for files).
    /// </summary>
    public long? MinSizeBytes { get; set; }

    /// <summary>
    /// Maximum duration in seconds (for video/audio).
    /// </summary>
    public double? MaxDurationSeconds { get; set; }

    /// <summary>
    /// Maximum width in pixels (for images/video).
    /// </summary>
    public int? MaxWidth { get; set; }

    /// <summary>
    /// Maximum height in pixels (for images/video).
    /// </summary>
    public int? MaxHeight { get; set; }

    /// <summary>
    /// Custom validation rules (pattern matching, range checks, etc.)
    /// </summary>
    public Dictionary<string, object> Rules { get; set; } = [];
}

/// <summary>
/// Specification for a signal emitted by a component.
/// </summary>
public class SignalSpec
{
    /// <summary>
    /// Signal key pattern.
    /// </summary>
    public string Key { get; set; } = "";

    /// <summary>
    /// Entity type of the signal value.
    /// </summary>
    public string? EntityType { get; set; }

    /// <summary>
    /// Signal salience (0.0-1.0).
    /// </summary>
    public double Salience { get; set; } = 0.5;

    /// <summary>
    /// Description of this signal.
    /// </summary>
    public string Description { get; set; } = "";
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
