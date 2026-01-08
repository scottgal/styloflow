using Mostlylucid.Ephemeral.Atoms.Taxonomy;

namespace StyloFlow.WorkflowBuilder.Atoms.Config;

/// <summary>
/// Config from Secret Vault (Azure Key Vault, HashiCorp Vault, etc.).
/// Deterministic fetch - result is stable for a given secret version.
/// </summary>
public sealed class ConfigVaultSensor
{
    public static AtomContract Contract => AtomContract.Create(
        AtomKind.Sensor,
        AtomDeterminism.Deterministic,
        AtomPersistence.EphemeralOnly,
        name: "config-vault",
        writes: ["config.loaded", "config.secret", "config.version", "config.expiry"]);

    public static Task ExecuteAsync(WorkflowAtomContext ctx)
    {
        var secretName = ctx.Config.TryGetValue("secretName", out var nameVal) ? nameVal?.ToString() : "my-secret";
        var provider = ctx.Config.TryGetValue("provider", out var provVal) ? provVal?.ToString() : "azure";

        // In production this would call Azure Key Vault / HashiCorp Vault
        ctx.Log($"Config Vault ({provider}): Would fetch secret '{secretName}' (demo mode)");

        ctx.Emit("config.loaded", true);
        ctx.Emit("config.secret", "***MASKED***"); // Never log actual secrets!
        ctx.Emit("config.version", "v1");
        ctx.Emit("config.expiry", DateTimeOffset.UtcNow.AddDays(30).ToString("O"));

        return Task.CompletedTask;
    }
}
