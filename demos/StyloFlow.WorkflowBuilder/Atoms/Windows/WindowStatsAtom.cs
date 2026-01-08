using Mostlylucid.Ephemeral.Atoms.Taxonomy;

namespace StyloFlow.WorkflowBuilder.Atoms.Windows;

/// <summary>
/// Window Stats - Emits statistics about a sliding window.
/// Useful for monitoring, alerting, and flow control based on window state.
/// </summary>
public sealed class WindowStatsAtom
{
    public static AtomContract Contract => AtomContract.Create(
        AtomKind.Extractor,
        AtomDeterminism.Deterministic,
        AtomPersistence.EphemeralOnly,
        name: "window-stats",
        reads: ["*"],
        writes: ["stats.count", "stats.oldest", "stats.newest", "stats.span_seconds", "stats.rate_per_minute"]);

    public static Task ExecuteAsync(WorkflowAtomContext ctx)
    {
        var windowName = ctx.Config.TryGetValue("window_name", out var wn) ? wn?.ToString() ?? "default" : "default";

        var stats = ctx.Signals.WindowStats(windowName);

        // Calculate rate per minute
        var ratePerMinute = stats.TimeSpan.TotalMinutes > 0
            ? stats.Count / stats.TimeSpan.TotalMinutes
            : 0;

        ctx.Log($"Window '{windowName}': count={stats.Count}, span={stats.TimeSpan.TotalSeconds:F1}s, rate={ratePerMinute:F1}/min");

        ctx.Emit("stats.count", stats.Count);
        ctx.Emit("stats.oldest", stats.OldestEntry?.ToString("O"));
        ctx.Emit("stats.newest", stats.NewestEntry?.ToString("O"));
        ctx.Emit("stats.span_seconds", stats.TimeSpan.TotalSeconds);
        ctx.Emit("stats.rate_per_minute", ratePerMinute);

        return Task.CompletedTask;
    }
}
