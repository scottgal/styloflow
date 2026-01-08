using StyloFlow.WorkflowBuilder.Models;

namespace StyloFlow.WorkflowBuilder.Services;

/// <summary>
/// Storage interface for workflow definitions.
/// </summary>
public interface IWorkflowStore
{
    Task<IReadOnlyList<WorkflowDefinition>> GetAllAsync();
    Task<WorkflowDefinition?> GetByIdAsync(string id);
    Task<WorkflowDefinition> SaveAsync(WorkflowDefinition workflow);
    Task<bool> DeleteAsync(string id);
}
