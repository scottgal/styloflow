using Mostlylucid.Ephemeral.Atoms.Taxonomy;

namespace StyloFlow.WorkflowBuilder.Atoms.Shapers;

/// <summary>
/// Signal Mixer - Combines multiple signals (simplified to use primary numeric signal).
/// </summary>
public sealed class SignalMixerShaper
{
    public static AtomContract Contract => AtomContract.Create(
        AtomKind.Constrainer,
        AtomDeterminism.Deterministic,
        AtomPersistence.EphemeralOnly,
        name: "signal-mixer",
        reads: ["*"],
        writes: ["mix.output", "mix.peak"]);

    public static Task ExecuteAsync(WorkflowAtomContext ctx)
    {
        var mode = ctx.Config.TryGetValue("mode", out var modeVal) ? modeVal?.ToString() : "average";

        // Get available numeric signals
        var score = ctx.Signals.Get<double>("sentiment.score");
        var wordCount = ctx.Signals.Get<double>("text.word_count");

        var values = new List<double>();
        if (score != 0) values.Add(score);
        if (wordCount != 0) values.Add(wordCount / 100.0); // Normalize word count

        if (values.Count == 0)
        {
            ctx.Log("No numeric signals to mix");
            return Task.CompletedTask;
        }

        var result = mode switch
        {
            "sum" => values.Sum(),
            "max" => values.Max(),
            "min" => values.Min(),
            "average" => values.Average(),
            _ => values.Average()
        };

        var peak = values.Max();

        ctx.Log($"Mix ({mode}): {values.Count} signals -> {result:F3}, peak={peak:F3}");

        ctx.Emit("mix.output", result);
        ctx.Emit("mix.peak", peak);

        return Task.CompletedTask;
    }
}
