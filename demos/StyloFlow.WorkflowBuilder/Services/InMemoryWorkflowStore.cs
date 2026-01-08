using System.Collections.Concurrent;
using StyloFlow.WorkflowBuilder.Models;

namespace StyloFlow.WorkflowBuilder.Services;

/// <summary>
/// In-memory workflow storage for demo purposes.
/// </summary>
public class InMemoryWorkflowStore : IWorkflowStore
{
    private readonly ConcurrentDictionary<string, WorkflowDefinition> _workflows = new();
    private bool _samplesLoaded;

    public InMemoryWorkflowStore()
    {
        LoadSampleWorkflows();
    }

    private void LoadSampleWorkflows()
    {
        if (_samplesLoaded) return;
        _samplesLoaded = true;

        // Try loading from embedded resources first
        var samples = SampleWorkflowLoader.LoadEmbeddedSamples().ToList();

        // If no embedded samples found, try file system (development mode)
        if (samples.Count == 0)
        {
            var devPath = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "SampleWorkflows");
            if (Directory.Exists(devPath))
            {
                samples = SampleWorkflowLoader.LoadFromDirectory(devPath).ToList();
            }
        }

        foreach (var sample in samples)
        {
            _workflows[sample.Id] = sample;
        }
    }

    public Task<IReadOnlyList<WorkflowDefinition>> GetAllAsync()
    {
        var workflows = _workflows.Values.OrderByDescending(w => w.UpdatedAt).ToList();
        return Task.FromResult<IReadOnlyList<WorkflowDefinition>>(workflows);
    }

    public Task<WorkflowDefinition?> GetByIdAsync(string id)
    {
        _workflows.TryGetValue(id, out var workflow);
        return Task.FromResult(workflow);
    }

    public Task<WorkflowDefinition> SaveAsync(WorkflowDefinition workflow)
    {
        var updated = workflow with { UpdatedAt = DateTime.UtcNow };
        _workflows[workflow.Id] = updated;
        return Task.FromResult(updated);
    }

    public Task<bool> DeleteAsync(string id)
    {
        return Task.FromResult(_workflows.TryRemove(id, out _));
    }
}
