using Mostlylucid.Ephemeral.Atoms.Taxonomy;

namespace StyloFlow.WorkflowBuilder.Atoms.Shapers;

/// <summary>
/// Signal Delay - Holds or delays signal propagation (sample &amp; hold).
/// </summary>
public sealed class SignalDelayShaper
{
    public static AtomContract Contract => AtomContract.Create(
        AtomKind.Constrainer,
        AtomDeterminism.Deterministic,
        AtomPersistence.EphemeralOnly,
        name: "signal-delay",
        reads: ["*"],
        writes: ["delay.output", "delay.trigger"]);

    private static double _heldValue;
    private static DateTimeOffset _holdTime = DateTimeOffset.MinValue;

    public static async Task ExecuteAsync(WorkflowAtomContext ctx)
    {
        var delayMs = ctx.Config.TryGetValue("delayMs", out var dVal) ? ShaperHelper.ToInt(dVal) : 100;
        var mode = ctx.Config.TryGetValue("mode", out var mVal) ? mVal?.ToString() : "delay";

        var value = ShaperHelper.GetNumericSignal(ctx.Signals);

        switch (mode)
        {
            case "sample-hold":
                // Sample and hold: capture value on trigger, hold until next trigger
                if (value != 0 && value != _heldValue)
                {
                    _heldValue = value;
                    _holdTime = DateTimeOffset.UtcNow;
                    ctx.Log($"Delay (sample-hold): captured {_heldValue:F3}");
                }
                ctx.Emit("delay.output", _heldValue);
                ctx.Emit("delay.trigger", value != _heldValue);
                break;

            case "debounce":
                // Debounce: only emit after stable for delayMs
                var elapsed = (DateTimeOffset.UtcNow - _holdTime).TotalMilliseconds;
                if (value != _heldValue)
                {
                    _heldValue = value;
                    _holdTime = DateTimeOffset.UtcNow;
                }
                else if (elapsed >= delayMs)
                {
                    ctx.Emit("delay.output", _heldValue);
                    ctx.Emit("delay.trigger", true);
                    ctx.Log($"Delay (debounce): {_heldValue:F3} after {elapsed:F0}ms");
                }
                break;

            default: // "delay"
                // Simple delay
                if (delayMs > 0)
                {
                    await Task.Delay(delayMs);
                }
                ctx.Emit("delay.output", value);
                ctx.Emit("delay.trigger", true);
                ctx.Log($"Delay: {value:F3} after {delayMs}ms");
                break;
        }
    }
}
