using Mostlylucid.Ephemeral.Atoms.Taxonomy;

namespace StyloFlow.WorkflowBuilder.Atoms.Config;

/// <summary>
/// Config from Environment Variables - pure deterministic read.
/// </summary>
public sealed class ConfigEnvSensor
{
    public static AtomContract Contract => AtomContract.Create(
        AtomKind.Sensor,
        AtomDeterminism.Deterministic,
        AtomPersistence.EphemeralOnly,
        name: "config-env",
        writes: ["config.loaded", "config.value", "config.key"]);

    public static Task ExecuteAsync(WorkflowAtomContext ctx)
    {
        var key = ctx.Config.TryGetValue("key", out var keyVal) ? keyVal?.ToString() : "MY_CONFIG_VAR";
        var defaultValue = ctx.Config.TryGetValue("defaultValue", out var defVal) ? defVal?.ToString() : "";

        var value = Environment.GetEnvironmentVariable(key ?? "") ?? defaultValue;
        var loaded = !string.IsNullOrEmpty(value);

        ctx.Log($"Config env: {key} = {(loaded ? value?[..Math.Min(value.Length, 50)] : "(not set)")}");

        ctx.Emit("config.loaded", loaded);
        ctx.Emit("config.value", value);
        ctx.Emit("config.key", key);

        return Task.CompletedTask;
    }
}
