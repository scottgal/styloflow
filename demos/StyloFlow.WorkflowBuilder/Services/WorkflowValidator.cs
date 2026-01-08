using StyloFlow.WorkflowBuilder.Models;

namespace StyloFlow.WorkflowBuilder.Services;

/// <summary>
/// Validates workflow connections and configuration.
/// </summary>
public class WorkflowValidator
{
    private readonly ManifestService _manifestService;

    public WorkflowValidator(ManifestService manifestService)
    {
        _manifestService = manifestService;
    }

    public ValidationResult Validate(WorkflowDefinition workflow)
    {
        var issues = new List<ValidationIssue>();

        // Validate each node has a valid manifest
        foreach (var node in workflow.Nodes)
        {
            var manifest = _manifestService.GetManifest(node.ManifestName);
            if (manifest == null)
            {
                issues.Add(new ValidationIssue
                {
                    NodeId = node.Id,
                    Message = $"Unknown atom type: {node.ManifestName}",
                    Severity = ValidationSeverity.Error
                });
            }
        }

        // Validate each edge
        foreach (var edge in workflow.Edges)
        {
            var sourceNode = workflow.Nodes.FirstOrDefault(n => n.Id == edge.SourceNodeId);
            var targetNode = workflow.Nodes.FirstOrDefault(n => n.Id == edge.TargetNodeId);

            if (sourceNode == null)
            {
                issues.Add(new ValidationIssue
                {
                    NodeId = edge.SourceNodeId,
                    EdgeId = edge.Id,
                    Message = "Source node not found",
                    Severity = ValidationSeverity.Error
                });
                continue;
            }

            if (targetNode == null)
            {
                issues.Add(new ValidationIssue
                {
                    NodeId = edge.TargetNodeId,
                    EdgeId = edge.Id,
                    Message = "Target node not found",
                    Severity = ValidationSeverity.Error
                });
                continue;
            }

            // Check if source emits the signal
            var emittedSignals = _manifestService.GetEmittedSignals(sourceNode.ManifestName).ToList();
            if (!emittedSignals.Contains(edge.SignalKey))
            {
                issues.Add(new ValidationIssue
                {
                    NodeId = sourceNode.Id,
                    EdgeId = edge.Id,
                    Message = $"Atom '{sourceNode.ManifestName}' does not emit signal '{edge.SignalKey}'",
                    Severity = ValidationSeverity.Error
                });
            }

            // Check if target can receive the signal (or accepts any)
            var requiredSignals = _manifestService.GetRequiredSignals(targetNode.ManifestName).ToList();
            var manifest = _manifestService.GetManifest(targetNode.ManifestName);

            // Log writer accepts any signal
            if (manifest?.Listens.Optional.Contains("*") == true)
            {
                continue;
            }

            // Check if target requires or listens to this signal
            var allAcceptedSignals = requiredSignals
                .Concat(manifest?.Input.OptionalSignals ?? [])
                .Concat(manifest?.Listens.Required ?? [])
                .Concat(manifest?.Listens.Optional ?? [])
                .ToHashSet();

            if (allAcceptedSignals.Count > 0 && !allAcceptedSignals.Contains(edge.SignalKey))
            {
                issues.Add(new ValidationIssue
                {
                    NodeId = targetNode.Id,
                    EdgeId = edge.Id,
                    Message = $"Atom '{targetNode.ManifestName}' does not accept signal '{edge.SignalKey}'",
                    Severity = ValidationSeverity.Warning
                });
            }
        }

        // Check for unconnected required inputs
        foreach (var node in workflow.Nodes)
        {
            var requiredSignals = _manifestService.GetRequiredSignals(node.ManifestName).ToHashSet();
            var incomingSignals = workflow.Edges
                .Where(e => e.TargetNodeId == node.Id)
                .Select(e => e.SignalKey)
                .ToHashSet();

            foreach (var required in requiredSignals)
            {
                if (!incomingSignals.Contains(required))
                {
                    issues.Add(new ValidationIssue
                    {
                        NodeId = node.Id,
                        Message = $"Required signal '{required}' is not connected",
                        Severity = ValidationSeverity.Warning
                    });
                }
            }
        }

        return new ValidationResult
        {
            IsValid = !issues.Any(i => i.Severity == ValidationSeverity.Error),
            Issues = issues
        };
    }

    public bool IsValidConnection(string sourceManifest, string signalKey, string targetManifest)
    {
        var emittedSignals = _manifestService.GetEmittedSignals(sourceManifest).ToHashSet();
        if (!emittedSignals.Contains(signalKey))
            return false;

        var manifest = _manifestService.GetManifest(targetManifest);
        if (manifest == null)
            return false;

        // Log writer accepts any
        if (manifest.Listens.Optional.Contains("*"))
            return true;

        var acceptedSignals = _manifestService.GetRequiredSignals(targetManifest)
            .Concat(manifest.Input.OptionalSignals)
            .Concat(manifest.Listens.Required)
            .Concat(manifest.Listens.Optional)
            .ToHashSet();

        return acceptedSignals.Count == 0 || acceptedSignals.Contains(signalKey);
    }
}
