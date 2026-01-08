using Mostlylucid.Ephemeral.Atoms.Taxonomy;

namespace StyloFlow.WorkflowBuilder.Atoms.Shapers;

/// <summary>
/// Signal Slew Limiter - Smooths rapid changes (portamento/glide effect).
/// </summary>
public sealed class SignalSlewShaper
{
    public static AtomContract Contract => AtomContract.Create(
        AtomKind.Constrainer,
        AtomDeterminism.Deterministic,
        AtomPersistence.EphemeralOnly,
        name: "signal-slew",
        reads: ["*"],
        writes: ["slew.value", "slew.moving"]);

    private static double _lastValue;
    private static DateTimeOffset _lastUpdate = DateTimeOffset.MinValue;

    public static Task ExecuteAsync(WorkflowAtomContext ctx)
    {
        var riseTime = ctx.Config.TryGetValue("riseTime", out var rVal) ? ShaperHelper.ToDouble(rVal) : 100.0;
        var fallTime = ctx.Config.TryGetValue("fallTime", out var fVal) ? ShaperHelper.ToDouble(fVal) : 100.0;

        var targetValue = ShaperHelper.GetNumericSignal(ctx.Signals);
        if (targetValue == 0)
        {
            ctx.Log("No numeric signal to slew");
            return Task.CompletedTask;
        }

        var now = DateTimeOffset.UtcNow;
        var elapsed = (now - _lastUpdate).TotalMilliseconds;
        _lastUpdate = now;

        // Calculate slew rate
        var diff = targetValue - _lastValue;
        var maxChange = diff > 0
            ? elapsed / riseTime
            : elapsed / fallTime;

        var change = Math.Clamp(diff, -Math.Abs(maxChange), Math.Abs(maxChange));
        var newValue = _lastValue + change;
        var isMoving = Math.Abs(newValue - targetValue) > 0.001;

        _lastValue = newValue;

        ctx.Log($"Slew: target={targetValue:F3}, current={newValue:F3}, moving={isMoving}");

        ctx.Emit("slew.value", newValue);
        ctx.Emit("slew.moving", isMoving);

        return Task.CompletedTask;
    }
}
