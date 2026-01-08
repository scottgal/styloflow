using StyloFlow.WorkflowBuilder.Runtime;

namespace StyloFlow.WorkflowBuilder.Atoms;

/// <summary>
/// Base context for workflow atoms, providing access to signals and services.
/// </summary>
public sealed class WorkflowAtomContext
{
    public required string NodeId { get; init; }
    public required string RunId { get; init; }
    public required WorkflowSignals Signals { get; init; }
    public required OllamaService Ollama { get; init; }
    public required WorkflowStorage Storage { get; init; }
    public Dictionary<string, object> Config { get; init; } = [];

    public void Log(string message)
    {
        // Emit to SignalR coordinator via signal
        Signals.Emit($"signalr.all.log", $"{RunId}|{NodeId}|{message}", NodeId);
    }

    public void Emit(string signal, object? value, double confidence = 1.0)
    {
        Signals.Emit(signal, value, NodeId, confidence);
    }
}
