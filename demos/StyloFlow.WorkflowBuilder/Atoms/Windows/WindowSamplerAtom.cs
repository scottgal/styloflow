using System.Text.Json;
using Mostlylucid.Ephemeral.Atoms.Taxonomy;
using StyloFlow.WorkflowBuilder.Runtime;

namespace StyloFlow.WorkflowBuilder.Atoms.Windows;

/// <summary>
/// Window Sampler - Takes random samples from a sliding window for analysis.
/// Useful for behavioral analysis, cache warming, and representative sampling.
/// </summary>
public sealed class WindowSamplerAtom
{
    public static AtomContract Contract => AtomContract.Create(
        AtomKind.Extractor,
        AtomDeterminism.Deterministic,
        AtomPersistence.EphemeralOnly,
        name: "window-sampler",
        reads: ["*"],
        writes: ["sample.entries", "sample.count", "sample.window_size"]);

    public static Task ExecuteAsync(WorkflowAtomContext ctx)
    {
        var windowName = ctx.Config.TryGetValue("window_name", out var wn) ? wn?.ToString() ?? "default" : "default";
        var sampleCount = ctx.Config.TryGetValue("sample_count", out var sc) ? Convert.ToInt32(sc) : 5;
        var unprocessedOnly = ctx.Config.TryGetValue("unprocessed_only", out var uo) && Convert.ToBoolean(uo);

        IReadOnlyList<WindowEntry> entries;

        if (unprocessedOnly)
        {
            // Get only unprocessed entries
            entries = ctx.Signals.GetUnprocessed(windowName);
            if (entries.Count > sampleCount)
            {
                // Take random sample from unprocessed
                var allUnprocessed = entries.ToList();
                var random = new Random();
                entries = allUnprocessed.OrderBy(_ => random.Next()).Take(sampleCount).ToList();
            }
        }
        else
        {
            // Random sample from entire window
            entries = ctx.Signals.WindowSample(windowName, sampleCount);
        }

        var stats = ctx.Signals.WindowStats(windowName);

        ctx.Log($"Window '{windowName}': sampled {entries.Count} from {stats.Count} total");

        // Emit the sampled entries
        var entrySummaries = entries.Select(e => new
        {
            e.Key,
            e.Timestamp,
            Entity = e.Entity
        }).ToList();

        ctx.Emit("sample.entries", entrySummaries);
        ctx.Emit("sample.count", entries.Count);
        ctx.Emit("sample.window_size", stats.Count);

        return Task.CompletedTask;
    }
}
