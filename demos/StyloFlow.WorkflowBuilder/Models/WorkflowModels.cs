using System.Text.Json.Serialization;

namespace StyloFlow.WorkflowBuilder.Models;

/// <summary>
/// A complete workflow definition with nodes and edges.
/// </summary>
public record WorkflowDefinition
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public string Description { get; init; } = "";
    public List<WorkflowNode> Nodes { get; init; } = [];
    public List<WorkflowEdge> Edges { get; init; } = [];
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; init; } = DateTime.UtcNow;
}

/// <summary>
/// A node in the workflow representing an atom instance.
/// </summary>
public record WorkflowNode
{
    public required string Id { get; init; }
    public required string ManifestName { get; init; }
    public double X { get; init; }
    public double Y { get; init; }
    public Dictionary<string, object> Config { get; init; } = [];
}

/// <summary>
/// An edge connecting two nodes via a signal.
/// </summary>
public record WorkflowEdge
{
    public required string Id { get; init; }
    public required string SourceNodeId { get; init; }
    public required string SignalKey { get; init; }
    public required string TargetNodeId { get; init; }
}

/// <summary>
/// Summary of an atom manifest for the palette.
/// </summary>
public record AtomSummary
{
    public required string Name { get; init; }
    public string Description { get; init; } = "";
    public string Kind { get; init; } = "sensor";
    public List<string> EmittedSignals { get; init; } = [];
    public List<string> RequiredSignals { get; init; } = [];
    public List<string> Tags { get; init; } = [];
}

/// <summary>
/// Result of validating a workflow.
/// </summary>
public record ValidationResult
{
    public bool IsValid { get; init; }
    public List<ValidationIssue> Issues { get; init; } = [];
}

/// <summary>
/// A single validation issue.
/// </summary>
public record ValidationIssue
{
    public required string NodeId { get; init; }
    public string? EdgeId { get; init; }
    public required string Message { get; init; }
    public ValidationSeverity Severity { get; init; } = ValidationSeverity.Error;
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ValidationSeverity
{
    Warning,
    Error
}

/// <summary>
/// Request to execute a workflow.
/// </summary>
public record ExecuteRequest
{
    public string? WorkflowId { get; init; }
    public WorkflowDefinition? Workflow { get; init; }
    public Dictionary<string, object>? Input { get; init; }
}
