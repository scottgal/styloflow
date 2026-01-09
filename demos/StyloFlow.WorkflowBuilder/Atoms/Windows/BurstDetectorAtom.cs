using System.Collections.Concurrent;
using System.Text.Json;
using Mostlylucid.Ephemeral.Atoms.BurstDetection;
using Mostlylucid.Ephemeral.Atoms.Taxonomy;
using StyloFlow.WorkflowBuilder.Runtime;

namespace StyloFlow.WorkflowBuilder.Atoms.Windows;

/// <summary>
/// Per-identity burst detection using Ephemeral's BurstDetectionAtom.
/// Detects when a specific identity (user, IP, session) is making requests
/// at an unusually high rate, indicating potential abuse or automated traffic.
/// </summary>
public sealed class BurstDetectorAtom
{
    // Shared instance per workflow run for proper state tracking
    private static readonly ConcurrentDictionary<string, BurstDetectionAtom> _detectors = new();

    public static AtomContract Contract => AtomContract.Create(
        AtomKind.Extractor, // Pattern detection/extraction
        AtomDeterminism.Probabilistic, // Non-deterministic due to time-based detection
        AtomPersistence.EphemeralOnly,
        name: "burst-detector",
        reads: ["identity.key", "*"],
        writes: ["burst.detected", "burst.count", "burst.duration", "burst.rate", "burst.description"]);

    public static Task ExecuteAsync(WorkflowAtomContext ctx)
    {
        // Get identity key from signal or config
        var identityKey = ctx.Signals.Get<string>("identity.key");
        if (string.IsNullOrEmpty(identityKey) && ctx.Config.TryGetValue("identity_key", out var ik))
        {
            identityKey = ik?.ToString();
        }
        identityKey ??= ctx.RunId; // Fall back to run ID if no identity specified

        // Configure detection parameters
        var windowSeconds = GetIntConfig(ctx.Config, "window_seconds", 30);
        var threshold = GetIntConfig(ctx.Config, "threshold", 10);

        // Get or create detector for this workflow run
        var detectorKey = $"{ctx.RunId}:{windowSeconds}:{threshold}";
        var detector = _detectors.GetOrAdd(detectorKey, _ =>
            new BurstDetectionAtom(
                window: TimeSpan.FromSeconds(windowSeconds),
                threshold: threshold,
                signals: ctx.Signals.Sink));

        // Record request and check for burst
        var result = detector.RecordAndCheck(identityKey!);

        ctx.Log($"Burst check for '{identityKey}': {result.RequestCount} requests in {windowSeconds}s window (threshold={threshold})");

        // Emit results
        ctx.Emit("burst.detected", result.IsBurst);
        ctx.Emit("burst.count", result.RequestCount);
        ctx.Emit("burst.duration", result.BurstDuration.TotalSeconds);
        ctx.Emit("burst.rate", detector.GetRequestRate(identityKey!));
        ctx.Emit("burst.description", result.Description);

        if (result.IsBurst)
        {
            ctx.Log($"  BURST DETECTED: {result.Description}");
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Cleanup detectors for finished workflow runs.
    /// </summary>
    public static void CleanupRun(string runId)
    {
        var keysToRemove = _detectors.Keys.Where(k => k.StartsWith($"{runId}:")).ToList();
        foreach (var key in keysToRemove)
        {
            if (_detectors.TryRemove(key, out var detector))
            {
                _ = detector.DisposeAsync();
            }
        }
    }

    private static int GetIntConfig(Dictionary<string, object> config, string key, int defaultValue)
    {
        if (!config.TryGetValue(key, out var val)) return defaultValue;

        return val switch
        {
            int i => i,
            long l => (int)l,
            double d => (int)d,
            string s when int.TryParse(s, out var parsed) => parsed,
            JsonElement { ValueKind: JsonValueKind.Number } je => je.GetInt32(),
            JsonElement { ValueKind: JsonValueKind.String } je when int.TryParse(je.GetString(), out var p) => p,
            _ => defaultValue
        };
    }
}
