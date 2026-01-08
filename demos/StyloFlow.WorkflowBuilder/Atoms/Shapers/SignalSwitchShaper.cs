using Mostlylucid.Ephemeral.Atoms.Taxonomy;

namespace StyloFlow.WorkflowBuilder.Atoms.Shapers;

/// <summary>
/// Signal Switch - Routes between inputs based on control signal.
/// </summary>
public sealed class SignalSwitchShaper
{
    public static AtomContract Contract => AtomContract.Create(
        AtomKind.Constrainer,
        AtomDeterminism.Deterministic,
        AtomPersistence.EphemeralOnly,
        name: "signal-switch",
        reads: ["*"],
        writes: ["switch.output", "switch.selected"]);

    public static Task ExecuteAsync(WorkflowAtomContext ctx)
    {
        var mode = ctx.Config.TryGetValue("mode", out var modeVal) ? modeVal?.ToString() : "binary";

        // Look for a boolean control signal
        var filterPassed = ctx.Signals.Get<bool>("filter.passed");
        var useA = filterPassed;

        // Get numeric value as the signal to route
        var value = ShaperHelper.GetNumericSignal(ctx.Signals);

        var selected = useA ? "A" : "B";
        object? output = useA ? value : 0.0;

        ctx.Log($"Switch ({mode}): control={filterPassed}, selected={selected}, output={output}");

        ctx.Emit("switch.output", output);
        ctx.Emit("switch.selected", selected);

        return Task.CompletedTask;
    }
}
