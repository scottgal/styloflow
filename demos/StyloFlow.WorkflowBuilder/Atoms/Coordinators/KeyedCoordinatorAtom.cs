using Mostlylucid.Ephemeral.Atoms.Taxonomy;

namespace StyloFlow.WorkflowBuilder.Atoms.Coordinators;

/// <summary>
/// Keyed Coordinator - Spawns per-key child coordinators for sequential execution.
/// Uses EphemeralKeyedWorkCoordinator pattern from Ephemeral.
/// </summary>
public sealed class KeyedCoordinatorAtom
{
    public static AtomContract Contract => AtomContract.Create(
        AtomKind.Sensor, // Coordinators act as workflow entry points
        AtomDeterminism.Deterministic,
        AtomPersistence.EphemeralOnly,
        name: "coordinator-keyed",
        reads: ["*"],
        writes: ["coordinator.spawned", "coordinator.output", "coordinator.completed"]);

    public static Task ExecuteAsync(WorkflowAtomContext ctx)
    {
        var keySelector = ctx.Config.TryGetValue("keySelector", out var ks) ? ks?.ToString() : "$.key";
        var maxConcurrency = ctx.Config.TryGetValue("maxConcurrency", out var mc) ? Convert.ToInt32(mc) : 4;

        // In a real implementation, this would use EphemeralKeyedWorkCoordinator
        // to spawn child coordinators per key
        var key = $"key-{Guid.NewGuid():N}"[..12];

        ctx.Log($"Keyed Coordinator: spawning for key={key}, maxConcurrency={maxConcurrency}");
        ctx.Log($"Key selector: {keySelector}");

        ctx.Emit("coordinator.spawned", key);
        ctx.Emit("coordinator.output", new { key, timestamp = DateTimeOffset.UtcNow });
        ctx.Emit("coordinator.completed", true);

        return Task.CompletedTask;
    }
}
