using Mostlylucid.Ephemeral.Atoms.Taxonomy;

namespace StyloFlow.WorkflowBuilder.Atoms.Config;

/// <summary>
/// Config from Database query - deterministic query execution.
/// Result may change between calls but execution is deterministic.
/// </summary>
public sealed class ConfigDbSensor
{
    public static AtomContract Contract => AtomContract.Create(
        AtomKind.Sensor,
        AtomDeterminism.Deterministic,
        AtomPersistence.EphemeralOnly,
        name: "config-db",
        writes: ["config.loaded", "config.value", "config.record", "config.timestamp"]);

    public static Task ExecuteAsync(WorkflowAtomContext ctx)
    {
        // In production this would query a database using the connection string and query from config
        var key = ctx.Config.TryGetValue("keyColumn", out var keyVal) ? keyVal?.ToString() : "config_key";

        ctx.Log($"Config DB: Would query for {key} (demo mode - returning placeholder)");

        ctx.Emit("config.loaded", true);
        ctx.Emit("config.value", "demo-db-value");
        ctx.Emit("config.record", new { key, value = "demo-db-value" });
        ctx.Emit("config.timestamp", DateTimeOffset.UtcNow.ToString("O"));

        return Task.CompletedTask;
    }
}
