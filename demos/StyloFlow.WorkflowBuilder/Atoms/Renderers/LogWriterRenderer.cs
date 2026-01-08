using Mostlylucid.Ephemeral.Atoms.Taxonomy;

namespace StyloFlow.WorkflowBuilder.Atoms.Renderers;

/// <summary>
/// Log writer renderer - writes workflow events to storage.
/// Taxonomy: renderer, deterministic, direct write allowed
/// </summary>
public sealed class LogWriterRenderer
{
    public static AtomContract Contract => AtomContract.Create(
        AtomKind.Renderer,
        AtomDeterminism.Deterministic,
        AtomPersistence.DirectWriteAllowed,
        name: "log-writer",
        reads: ["*"], // Reads any signal
        writes: ["log.written", "log.entry_id"]);

    public static async Task ExecuteAsync(WorkflowAtomContext ctx)
    {
        var logLevel = ctx.Config.TryGetValue("log_level", out var ll) ? ll?.ToString() : "info";
        var allSignals = ctx.Signals.GetAll();
        var entryId = $"log-{Guid.NewGuid():N}"[..12];

        ctx.Log($"Writing log entry {entryId}");
        ctx.Log($"  Level: {logLevel}");
        ctx.Log($"  Signals captured: {allSignals.Count}");

        // Log key signals
        var sentimentLabel = ctx.Signals.Get<string>("sentiment.label");
        if (sentimentLabel != null)
            ctx.Log($"  sentiment.label = {sentimentLabel}");

        var sentimentScore = ctx.Signals.Get<double>("sentiment.score");
        if (sentimentScore != 0)
            ctx.Log($"  sentiment.score = {sentimentScore:F2}");

        var wordCount = ctx.Signals.Get<int>("text.word_count");
        if (wordCount != 0)
            ctx.Log($"  text.word_count = {wordCount}");

        // Store signals via storage atom
        foreach (var signal in allSignals)
        {
            await ctx.Storage.LogSignalAsync(
                ctx.RunId,
                signal.Signal,
                signal.Key,
                ctx.NodeId,
                1.0);
        }

        ctx.Emit("log.written", true);
        ctx.Emit("log.entry_id", entryId);
    }
}
