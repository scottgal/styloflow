using Mostlylucid.Ephemeral.Atoms.Taxonomy;

namespace StyloFlow.WorkflowBuilder.Atoms.Shapers;

/// <summary>
/// Signal Clamp - Limits values to min/max range (like audio limiter).
/// </summary>
public sealed class SignalClampShaper
{
    public static AtomContract Contract => AtomContract.Create(
        AtomKind.Constrainer,
        AtomDeterminism.Deterministic,
        AtomPersistence.EphemeralOnly,
        name: "signal-clamp",
        reads: ["*"],
        writes: ["clamp.value", "clamp.limited"]);

    public static Task ExecuteAsync(WorkflowAtomContext ctx)
    {
        var min = ctx.Config.TryGetValue("min", out var minVal) ? ShaperHelper.ToDouble(minVal) : 0.0;
        var max = ctx.Config.TryGetValue("max", out var maxVal) ? ShaperHelper.ToDouble(maxVal) : 1.0;

        var value = ShaperHelper.GetNumericSignal(ctx.Signals);
        if (value == 0)
        {
            ctx.Log("No numeric signal to clamp");
            return Task.CompletedTask;
        }

        var clamped = Math.Clamp(value, min, max);
        var wasLimited = Math.Abs(clamped - value) > 0.0001;

        ctx.Log($"Clamp: {value:F3} -> {clamped:F3} (range {min}-{max}){(wasLimited ? " LIMITED" : "")}");

        ctx.Emit("clamp.value", clamped);
        ctx.Emit("clamp.limited", wasLimited);

        return Task.CompletedTask;
    }
}
