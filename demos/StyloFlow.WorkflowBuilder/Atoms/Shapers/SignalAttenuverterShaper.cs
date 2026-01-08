using Mostlylucid.Ephemeral.Atoms.Taxonomy;

namespace StyloFlow.WorkflowBuilder.Atoms.Shapers;

/// <summary>
/// Signal Attenuverter - Scales and inverts signal values.
/// </summary>
public sealed class SignalAttenuverterShaper
{
    public static AtomContract Contract => AtomContract.Create(
        AtomKind.Constrainer,
        AtomDeterminism.Deterministic,
        AtomPersistence.EphemeralOnly,
        name: "signal-attenuverter",
        reads: ["*"],
        writes: ["atten.value", "atten.clipped"]);

    public static Task ExecuteAsync(WorkflowAtomContext ctx)
    {
        var scale = ctx.Config.TryGetValue("scale", out var scaleVal) ? ShaperHelper.ToDouble(scaleVal) : 1.0;
        var offset = ctx.Config.TryGetValue("offset", out var offsetVal) ? ShaperHelper.ToDouble(offsetVal) : 0.0;
        var clipEnabled = ctx.Config.TryGetValue("clip", out var clipVal) && clipVal is bool clipB ? clipB : true;

        var value = ShaperHelper.GetNumericSignal(ctx.Signals);
        if (value == 0)
        {
            ctx.Log("No numeric signal to attenuvert");
            return Task.CompletedTask;
        }

        var scaled = value * scale + offset;
        var clipped = clipEnabled ? Math.Clamp(scaled, -1.0, 1.0) : scaled;
        var wasClipped = Math.Abs(clipped - scaled) > 0.0001;

        ctx.Log($"Attenuvert: {value:F3} * {scale:F2} + {offset:F2} = {clipped:F3}{(wasClipped ? " CLIPPED" : "")}");

        ctx.Emit("atten.value", clipped);
        ctx.Emit("atten.clipped", wasClipped);

        return Task.CompletedTask;
    }
}
