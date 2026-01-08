using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Mostlylucid.Ephemeral.Atoms.Taxonomy;

namespace StyloFlow.WorkflowBuilder.Atoms.Windows;

/// <summary>
/// Window Collector - Adds incoming signals/entities to a named sliding window.
/// Uses signal fingerprinting to match similar requests for behavioral accumulation.
/// Essential for building up behavioral data for analysis, caching, and pattern detection.
/// </summary>
public sealed class WindowCollectorAtom
{
    public static AtomContract Contract => AtomContract.Create(
        AtomKind.Sensor,
        AtomDeterminism.Deterministic,
        AtomPersistence.EphemeralOnly,
        name: "window-collector",
        reads: ["*"],
        writes: ["window.added", "window.count", "window.key", "window.fingerprint", "window.match_count"]);

    public static Task ExecuteAsync(WorkflowAtomContext ctx)
    {
        var windowName = ctx.Config.TryGetValue("window_name", out var wn) ? wn?.ToString() ?? "default" : "default";
        var inputSignal = ctx.Config.TryGetValue("input_signal", out var inp) ? inp?.ToString() ?? "*" : "*";
        var keyField = ctx.Config.TryGetValue("key_field", out var kf) ? kf?.ToString() : null;
        var maxItems = GetIntConfig(ctx.Config, "max_items", 100);
        var maxAgeMinutes = GetIntConfig(ctx.Config, "max_age_minutes", 10);

        // Fingerprint fields - which fields to use for matching similar requests
        var fingerprintFields = ctx.Config.TryGetValue("fingerprint_fields", out var ff)
            ? ff?.ToString()?.Split(',').Select(f => f.Trim()).ToArray() ?? []
            : Array.Empty<string>();

        // Get input value from signals
        var inputValue = ctx.Signals.Get<object>(inputSignal);

        // Generate fingerprint from specified fields (for grouping similar requests)
        var fingerprint = GenerateFingerprint(inputValue, fingerprintFields);

        // Generate a unique key for this entry
        string key;
        if (keyField != null && inputValue is IDictionary<string, object> dict && dict.TryGetValue(keyField, out var keyVal))
        {
            key = keyVal?.ToString() ?? Guid.NewGuid().ToString("N")[..12];
        }
        else
        {
            key = $"entry-{Guid.NewGuid():N}"[..16];
        }

        // Get or create the window and add the entity
        var window = ctx.Signals.GetWindow(windowName, maxItems, TimeSpan.FromMinutes(maxAgeMinutes));

        // Create an entry object with value, timestamp, and fingerprint
        var entry = new WindowedEntity(inputValue, fingerprint, DateTimeOffset.UtcNow);
        ctx.Signals.WindowAdd(windowName, key, entry);

        // Count how many entries have the same fingerprint (for behavioral analysis)
        var allEntries = ctx.Signals.WindowQuery(windowName);
        var matchCount = allEntries.Count(e => e.Entity is WindowedEntity we && we.Fingerprint == fingerprint);

        var stats = ctx.Signals.WindowStats(windowName);

        ctx.Log($"Window '{windowName}': added key={key}, fingerprint={fingerprint[..Math.Min(8, fingerprint.Length)]}, matches={matchCount}, total={stats.Count}");

        ctx.Emit("window.added", key);
        ctx.Emit("window.key", key);
        ctx.Emit("window.fingerprint", fingerprint);
        ctx.Emit("window.match_count", matchCount);
        ctx.Emit("window.count", stats.Count);

        return Task.CompletedTask;
    }

    /// <summary>
    /// Generate a fingerprint hash from specified fields of the input value.
    /// This allows matching similar requests based on selected characteristics.
    /// </summary>
    private static string GenerateFingerprint(object? value, string[] fields)
    {
        if (value == null) return "null";
        if (fields.Length == 0) return "default";

        var parts = new List<string>();

        if (value is IDictionary<string, object> dict)
        {
            foreach (var field in fields)
            {
                if (dict.TryGetValue(field, out var fieldValue))
                {
                    parts.Add($"{field}:{fieldValue}");
                }
            }
        }
        else if (value is JsonElement json)
        {
            foreach (var field in fields)
            {
                if (json.TryGetProperty(field, out var prop))
                {
                    parts.Add($"{field}:{prop}");
                }
            }
        }
        else
        {
            // Use string representation for simple types
            parts.Add(value.ToString() ?? "");
        }

        if (parts.Count == 0) return "default";

        // Hash the combined parts for a consistent fingerprint
        var combined = string.Join("|", parts);
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(combined));
        return Convert.ToHexString(hash)[..16].ToLowerInvariant();
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
            JsonElement je when je.ValueKind == JsonValueKind.Number => je.GetInt32(),
            JsonElement je when je.ValueKind == JsonValueKind.String && int.TryParse(je.GetString(), out var p) => p,
            _ => defaultValue
        };
    }
}

/// <summary>
/// Entity stored in a window with its fingerprint for matching.
/// </summary>
public sealed record WindowedEntity(object? Value, string Fingerprint, DateTimeOffset CollectedAt);
