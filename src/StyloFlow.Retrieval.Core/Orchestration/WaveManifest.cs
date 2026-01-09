namespace StyloFlow.Retrieval.Orchestration;

/// <summary>
/// Declarative wave manifest loaded from YAML.
/// Defines signal contracts for dynamic composition across all content types.
/// Universal manifest for documents, images, audio, video analysis.
/// </summary>
public sealed class WaveManifest
{
    public required string Name { get; init; }
    public int Priority { get; init; } = 50;
    public bool Enabled { get; init; } = true;
    public string? Description { get; init; }

    /// <summary>
    /// Content domain this wave applies to (document, image, audio, video, or "any").
    /// </summary>
    public string Domain { get; init; } = "any";

    public SignalScope Scope { get; init; } = new();
    public TaxonomyConfig Taxonomy { get; init; } = new();
    public TriggerConfig Triggers { get; init; } = new();
    public EmissionConfig Emits { get; init; } = new();
    public ListenConfig Listens { get; init; } = new();
    public EscalationConfig? Escalation { get; init; }
    public LaneConfig Lane { get; init; } = new();
    public CacheConfig Cache { get; init; } = new();
    public BudgetConfig? Budget { get; init; }
    public List<string> Tags { get; init; } = new();
    public WaveDefaults Defaults { get; init; } = new();
}

/// <summary>
/// Signal scope context (three-level hierarchy).
/// </summary>
public sealed class SignalScope
{
    public string Sink { get; init; } = "retrieval";
    public string Coordinator { get; init; } = "analysis";
    public string Atom { get; init; } = "";
}

/// <summary>
/// Taxonomy classification for the wave.
/// </summary>
public sealed class TaxonomyConfig
{
    /// <summary>
    /// Wave kind: sensor, extractor, proposer, constrainer, ranker, synthesizer.
    /// </summary>
    public string Kind { get; init; } = "sensor";

    /// <summary>
    /// Determinism: deterministic or probabilistic.
    /// </summary>
    public string Determinism { get; init; } = "deterministic";

    /// <summary>
    /// Persistence: ephemeral, escalatable, or direct_write.
    /// </summary>
    public string Persistence { get; init; } = "ephemeral";
}

/// <summary>
/// Trigger conditions - when should this wave run?
/// </summary>
public sealed class TriggerConfig
{
    /// <summary>
    /// Required signals - ALL must be satisfied before wave runs.
    /// </summary>
    public List<SignalRequirement> Requires { get; init; } = new();

    /// <summary>
    /// Run when ANY of these signals exist.
    /// </summary>
    public List<string> Signals { get; init; } = new();

    /// <summary>
    /// Skip if ANY of these signals exist.
    /// </summary>
    public List<string> SkipWhen { get; init; } = new();
}

/// <summary>
/// Signal requirement with optional condition.
/// </summary>
public sealed class SignalRequirement
{
    public required string Signal { get; init; }
    public string? Condition { get; init; }
    public object? Value { get; init; }
}

/// <summary>
/// Signal emissions - what signals does this wave produce?
/// </summary>
public sealed class EmissionConfig
{
    public List<string> OnStart { get; init; } = new();
    public List<SignalDefinition> OnComplete { get; init; } = new();
    public List<string> OnFailure { get; init; } = new();
    public List<ConditionalSignal> Conditional { get; init; } = new();
}

/// <summary>
/// Signal definition with type and metadata.
/// </summary>
public class SignalDefinition
{
    public required string Key { get; init; }
    public string Type { get; init; } = "string";
    public string? Description { get; init; }
    public double[]? ConfidenceRange { get; init; }
}

/// <summary>
/// Conditional signal emitted based on runtime conditions.
/// </summary>
public sealed class ConditionalSignal : SignalDefinition
{
    public string? When { get; init; }
}

/// <summary>
/// Dependencies - signals this wave reads from other waves.
/// </summary>
public sealed class ListenConfig
{
    public List<string> Required { get; init; } = new();
    public List<string> Optional { get; init; } = new();
}

/// <summary>
/// Escalation rules - when to defer to more powerful processing.
/// </summary>
public sealed class EscalationConfig
{
    /// <summary>
    /// Named escalation targets with conditions.
    /// </summary>
    public Dictionary<string, EscalationRule> Targets { get; init; } = new();
}

/// <summary>
/// Escalation rule with conditions.
/// </summary>
public sealed class EscalationRule
{
    public List<EscalationCondition> When { get; init; } = new();
    public List<EscalationCondition> SkipWhen { get; init; } = new();
    public string? Description { get; init; }
}

/// <summary>
/// Escalation condition.
/// </summary>
public sealed class EscalationCondition
{
    public required string Signal { get; init; }
    public object? Value { get; init; }
    public string? Condition { get; init; }
}

/// <summary>
/// Lane configuration for concurrency control.
/// </summary>
public sealed class LaneConfig
{
    public string Name { get; init; } = "fast";
    public int MaxConcurrency { get; init; } = 4;
    public int Priority { get; init; } = 0;
}

/// <summary>
/// Cache configuration for wave results.
/// </summary>
public sealed class CacheConfig
{
    public List<string> Uses { get; init; } = new();
    public List<SignalDefinition> Emits { get; init; } = new();
    public List<string> Invalidates { get; init; } = new();
    public int? TtlSeconds { get; init; }
}

/// <summary>
/// Budget constraints for the wave.
/// </summary>
public sealed class BudgetConfig
{
    public string? MaxDuration { get; init; }
    public int? MaxTokens { get; init; }
    public decimal? MaxCost { get; init; }
}

/// <summary>
/// Default parameter values for a wave.
/// </summary>
public sealed class WaveDefaults
{
    public WeightDefaults Weights { get; init; } = new();
    public ConfidenceDefaults Confidence { get; init; } = new();
    public TimingDefaults Timing { get; init; } = new();
    public FeatureDefaults Features { get; init; } = new();
    public Dictionary<string, object> Parameters { get; init; } = new();
}

public sealed class WeightDefaults
{
    public double Base { get; init; } = 1.0;
    public double HighConfidence { get; init; } = 1.5;
    public double LowConfidence { get; init; } = 0.5;
    public double Verified { get; init; } = 2.0;
}

public sealed class ConfidenceDefaults
{
    public double Neutral { get; init; } = 0.5;
    public double High { get; init; } = 0.9;
    public double Medium { get; init; } = 0.7;
    public double Low { get; init; } = 0.5;
    public double HighThreshold { get; init; } = 0.8;
    public double EscalationThreshold { get; init; } = 0.5;
}

public sealed class TimingDefaults
{
    public int TimeoutMs { get; init; } = 10000;
    public int CacheTtlSec { get; init; } = 3600;
}

public sealed class FeatureDefaults
{
    public bool DetailedLogging { get; init; } = false;
    public bool EnableCache { get; init; } = true;
    public bool CanEarlyExit { get; init; } = false;
    public bool CanEscalate { get; init; } = false;
}
