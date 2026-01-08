using Mostlylucid.Ephemeral.Atoms.Taxonomy;

namespace StyloFlow.WorkflowBuilder.Atoms.Sensors;

/// <summary>
/// HTTP receiver sensor - receives webhook data.
/// Taxonomy: sensor, deterministic, ephemeral
/// </summary>
public sealed class HttpReceiverSensor
{
    public static AtomContract Contract => AtomContract.Create(
        AtomKind.Sensor,
        AtomDeterminism.Deterministic,
        AtomPersistence.EphemeralOnly,
        name: "http-receiver",
        writes: ["http.received", "http.body", "http.method", "http.path"]);

    public static Task ExecuteAsync(WorkflowAtomContext ctx)
    {
        var body = ctx.Config.TryGetValue("body", out var b) ? b?.ToString() : "Hello, this is a test message!";
        var method = ctx.Config.TryGetValue("method", out var m) ? m?.ToString() : "POST";
        var path = ctx.Config.TryGetValue("path", out var p) ? p?.ToString() : "/webhook";

        ctx.Log($"Received {method} request to {path}");
        ctx.Log($"Body: {body?[..Math.Min(body.Length, 100)]}...");

        ctx.Emit("http.received", true);
        ctx.Emit("http.body", body);
        ctx.Emit("http.method", method);
        ctx.Emit("http.path", path);

        return Task.CompletedTask;
    }
}
