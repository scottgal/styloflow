using Mostlylucid.Ephemeral.Atoms.Taxonomy;

namespace StyloFlow.WorkflowBuilder.Atoms.Sensors;

/// <summary>
/// Timer trigger sensor - fires immediately for demo, would be scheduled in production.
/// Taxonomy: sensor, deterministic, ephemeral
/// </summary>
public sealed class TimerTriggerSensor
{
    public static AtomContract Contract => AtomContract.Create(
        AtomKind.Sensor,
        AtomDeterminism.Deterministic,
        AtomPersistence.EphemeralOnly,
        name: "timer-trigger",
        writes: ["timer.triggered", "timer.timestamp"]);

    public static Task ExecuteAsync(WorkflowAtomContext ctx)
    {
        ctx.Log("Timer fired!");
        ctx.Emit("timer.triggered", true);
        ctx.Emit("timer.timestamp", DateTimeOffset.UtcNow);
        return Task.CompletedTask;
    }
}
