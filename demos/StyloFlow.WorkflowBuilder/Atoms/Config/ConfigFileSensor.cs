using Mostlylucid.Ephemeral.Atoms.Taxonomy;

namespace StyloFlow.WorkflowBuilder.Atoms.Config;

/// <summary>
/// Config from File (JSON/YAML) - deterministic file read.
/// </summary>
public sealed class ConfigFileSensor
{
    public static AtomContract Contract => AtomContract.Create(
        AtomKind.Sensor,
        AtomDeterminism.Deterministic,
        AtomPersistence.EphemeralOnly,
        name: "config-file",
        writes: ["config.loaded", "config.value", "config.document"]);

    public static async Task ExecuteAsync(WorkflowAtomContext ctx)
    {
        var path = ctx.Config.TryGetValue("path", out var pathVal) ? pathVal?.ToString() : "config.json";

        if (!File.Exists(path))
        {
            ctx.Log($"Config file not found: {path}");
            ctx.Emit("config.loaded", false);
            return;
        }

        var content = await File.ReadAllTextAsync(path!);

        ctx.Log($"Config file loaded: {path} ({content.Length} bytes)");

        ctx.Emit("config.loaded", true);
        ctx.Emit("config.value", content);
        ctx.Emit("config.document", content); // Would parse JSON in production

        return;
    }
}
