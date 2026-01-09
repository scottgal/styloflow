using Mostlylucid.Ephemeral;
using StyloFlow.WorkflowBuilder.Atoms;
using StyloFlow.WorkflowBuilder.Runtime;

namespace StyloFlow.WorkflowBuilder.Tests;

/// <summary>
/// Test helpers for creating contexts and services.
/// </summary>
public static class TestHelpers
{
    private static readonly string TestDataPath = Path.Combine(Path.GetTempPath(), "styloflow-tests");

    /// <summary>
    /// Creates a WorkflowAtomContext suitable for testing atoms.
    /// Uses real instances with test-friendly configurations.
    /// </summary>
    public static WorkflowAtomContext CreateTestContext(Dictionary<string, object>? config = null)
    {
        var signals = new WorkflowSignals("test-run");

        // Create real OllamaService - won't connect unless actually used
        var ollama = new OllamaService("http://localhost:11434", "test");

        // Create real WorkflowStorage with temp path
        var sink = new SignalSink(maxCapacity: 100, maxAge: TimeSpan.FromMinutes(1));
        var storage = new WorkflowStorage(sink, TestDataPath);

        return new WorkflowAtomContext
        {
            NodeId = "test-node",
            RunId = $"test-run-{Guid.NewGuid():N}"[..24],
            Signals = signals,
            Ollama = ollama,
            Storage = storage,
            Config = config ?? new Dictionary<string, object>()
        };
    }

    /// <summary>
    /// Creates a minimal context without storage initialization.
    /// Useful for tests that don't need persistent storage.
    /// </summary>
    public static WorkflowAtomContext CreateMinimalContext(Dictionary<string, object>? config = null)
    {
        var signals = new WorkflowSignals("test-run");
        var ollama = new OllamaService("http://localhost:11434", "test");
        var sink = new SignalSink(maxCapacity: 100, maxAge: TimeSpan.FromMinutes(1));
        var storage = new WorkflowStorage(sink, TestDataPath);

        return new WorkflowAtomContext
        {
            NodeId = "test-node",
            RunId = $"test-{Guid.NewGuid():N}"[..16],
            Signals = signals,
            Ollama = ollama,
            Storage = storage,
            Config = config ?? new Dictionary<string, object>()
        };
    }
}
