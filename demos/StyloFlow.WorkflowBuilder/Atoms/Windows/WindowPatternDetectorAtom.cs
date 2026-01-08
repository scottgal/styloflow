using System.Text.Json;
using Mostlylucid.Ephemeral.Atoms.Taxonomy;
using StyloFlow.WorkflowBuilder.Runtime;

namespace StyloFlow.WorkflowBuilder.Atoms.Windows;

/// <summary>
/// Window Pattern Detector - Detects behavioral patterns in windowed data.
/// Identifies bursts, periodic behavior, and anomalies for bot detection, abuse prevention, etc.
/// </summary>
public sealed class WindowPatternDetectorAtom
{
    public static AtomContract Contract => AtomContract.Create(
        AtomKind.Extractor,
        AtomDeterminism.Deterministic,
        AtomPersistence.EphemeralOnly,
        name: "window-pattern-detector",
        reads: ["*"],
        writes: ["pattern.detected", "pattern.type", "pattern.confidence", "pattern.count", "pattern.details"]);

    public static Task ExecuteAsync(WorkflowAtomContext ctx)
    {
        var windowName = ctx.Config.TryGetValue("window_name", out var wn) ? wn?.ToString() ?? "default" : "default";
        var patternTypeStr = ctx.Config.TryGetValue("pattern_type", out var pt) ? pt?.ToString() ?? "all" : "all";
        var minConfidence = GetDoubleConfig(ctx.Config, "min_confidence", 0.5);

        var patternType = patternTypeStr.ToLowerInvariant() switch
        {
            "burst" => PatternType.Burst,
            "periodic" => PatternType.Periodic,
            "anomaly" => PatternType.Anomaly,
            _ => PatternType.All
        };

        var patterns = ctx.Signals.DetectPatterns(windowName, patternType);

        // Filter by confidence threshold
        var significantPatterns = patterns
            .Where(p => p.Confidence >= minConfidence)
            .OrderByDescending(p => p.Confidence)
            .ToList();

        var stats = ctx.Signals.WindowStats(windowName);

        ctx.Log($"Window '{windowName}': detected {significantPatterns.Count} patterns (threshold={minConfidence:F2})");

        // Emit pattern detection results
        var detected = significantPatterns.Count > 0;
        ctx.Emit("pattern.detected", detected);
        ctx.Emit("pattern.count", significantPatterns.Count);

        if (significantPatterns.Count > 0)
        {
            var topPattern = significantPatterns.First();
            ctx.Emit("pattern.type", topPattern.Type.ToString().ToLowerInvariant());
            ctx.Emit("pattern.confidence", topPattern.Confidence);
            ctx.Emit("pattern.details", significantPatterns.Select(p => new
            {
                Type = p.Type.ToString(),
                p.Description,
                p.Confidence,
                p.DetectedAt
            }).ToList());

            foreach (var pattern in significantPatterns)
            {
                ctx.Log($"  {pattern.Type}: {pattern.Description} (confidence={pattern.Confidence:F2})");
            }
        }

        return Task.CompletedTask;
    }

    private static double GetDoubleConfig(Dictionary<string, object> config, string key, double defaultValue)
    {
        if (!config.TryGetValue(key, out var val)) return defaultValue;

        return val switch
        {
            double d => d,
            float f => f,
            int i => i,
            long l => l,
            decimal dec => (double)dec,
            string s when double.TryParse(s, out var parsed) => parsed,
            JsonElement je when je.ValueKind == JsonValueKind.Number => je.GetDouble(),
            JsonElement je when je.ValueKind == JsonValueKind.String && double.TryParse(je.GetString(), out var p) => p,
            _ => defaultValue
        };
    }
}
