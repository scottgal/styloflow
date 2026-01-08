using Mostlylucid.Ephemeral.Atoms.Taxonomy;

namespace StyloFlow.WorkflowBuilder.Atoms.Shapers;

/// <summary>
/// Signal Quantizer - Snaps values to discrete steps.
/// </summary>
public sealed class SignalQuantizerShaper
{
    public static AtomContract Contract => AtomContract.Create(
        AtomKind.Constrainer,
        AtomDeterminism.Deterministic,
        AtomPersistence.EphemeralOnly,
        name: "signal-quantizer",
        reads: ["*"],
        writes: ["quantize.value", "quantize.step"]);

    public static Task ExecuteAsync(WorkflowAtomContext ctx)
    {
        var steps = ctx.Config.TryGetValue("steps", out var stepsVal) ? ShaperHelper.ToInt(stepsVal) : 8;
        if (steps < 2) steps = 8;
        var min = ctx.Config.TryGetValue("min", out var minVal) ? ShaperHelper.ToDouble(minVal) : 0.0;
        var max = ctx.Config.TryGetValue("max", out var maxVal) ? ShaperHelper.ToDouble(maxVal) : 1.0;

        var value = ShaperHelper.GetNumericSignal(ctx.Signals);
        if (value == 0)
        {
            ctx.Log("No numeric signal to quantize");
            return Task.CompletedTask;
        }

        var normalized = (value - min) / (max - min);
        var stepIndex = (int)Math.Round(normalized * (steps - 1));
        stepIndex = Math.Clamp(stepIndex, 0, steps - 1);
        var quantized = min + (stepIndex / (double)(steps - 1)) * (max - min);

        ctx.Log($"Quantize: {value:F3} -> step {stepIndex}/{steps-1} = {quantized:F3}");

        ctx.Emit("quantize.value", quantized);
        ctx.Emit("quantize.step", stepIndex);

        return Task.CompletedTask;
    }
}
