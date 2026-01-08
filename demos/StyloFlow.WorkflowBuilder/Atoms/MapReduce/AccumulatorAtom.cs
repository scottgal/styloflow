using System.Text.Json;
using Mostlylucid.Ephemeral.Atoms.Taxonomy;

namespace StyloFlow.WorkflowBuilder.Atoms.MapReduce;

/// <summary>
/// Accumulator - Collects and groups signals by key for map-reduce style processing.
/// Groups incoming values by a key field, building up collections for reduction.
/// </summary>
public sealed class AccumulatorAtom
{
    public static AtomContract Contract => AtomContract.Create(
        AtomKind.Sensor,
        AtomDeterminism.Deterministic,
        AtomPersistence.EphemeralOnly,
        name: "accumulator",
        reads: ["*"],
        writes: ["accumulator.added", "accumulator.group_key", "accumulator.group_count", "accumulator.total_count"]);

    public static Task ExecuteAsync(WorkflowAtomContext ctx)
    {
        var windowName = ctx.Config.TryGetValue("window_name", out var wn) ? wn?.ToString() ?? "accumulator" : "accumulator";
        var inputSignal = ctx.Config.TryGetValue("input_signal", out var inp) ? inp?.ToString() ?? "*" : "*";
        var keyField = ctx.Config.TryGetValue("key_field", out var kf) ? kf?.ToString() ?? "key" : "key";
        var valueField = ctx.Config.TryGetValue("value_field", out var vf) ? vf?.ToString() : null;
        var maxGroups = GetIntConfig(ctx.Config, "max_groups", 100);

        // Get input value
        var inputValue = ctx.Signals.Get<object>(inputSignal);

        // Extract key from input
        var groupKey = ExtractField(inputValue, keyField)?.ToString() ?? "default";
        var value = valueField != null ? ExtractField(inputValue, valueField) : inputValue;

        // Get or create window for this accumulator
        var window = ctx.Signals.GetWindow(windowName, maxGroups * 100, TimeSpan.FromMinutes(30));

        // Create accumulator entry with group key
        var entry = new AccumulatorEntry(groupKey, value, DateTimeOffset.UtcNow);
        var entryKey = $"{groupKey}:{Guid.NewGuid():N}"[..24];
        ctx.Signals.WindowAdd(windowName, entryKey, entry);

        // Count entries in this group
        var allEntries = ctx.Signals.WindowQuery(windowName);
        var groupCount = allEntries.Count(e => e.Entity is AccumulatorEntry ae && ae.GroupKey == groupKey);
        var totalCount = allEntries.Count;

        ctx.Log($"Accumulator '{windowName}': key={groupKey}, group_count={groupCount}, total={totalCount}");

        ctx.Emit("accumulator.added", entryKey);
        ctx.Emit("accumulator.group_key", groupKey);
        ctx.Emit("accumulator.group_count", groupCount);
        ctx.Emit("accumulator.total_count", totalCount);

        return Task.CompletedTask;
    }

    private static object? ExtractField(object? value, string field)
    {
        if (value == null) return null;

        if (value is IDictionary<string, object> dict)
        {
            return dict.TryGetValue(field, out var v) ? v : null;
        }

        if (value is JsonElement je && je.ValueKind == JsonValueKind.Object)
        {
            return je.TryGetProperty(field, out var prop) ? prop.ToString() : null;
        }

        return value.ToString();
    }

    private static int GetIntConfig(Dictionary<string, object> config, string key, int defaultValue)
    {
        if (!config.TryGetValue(key, out var val)) return defaultValue;
        return val switch
        {
            int i => i,
            long l => (int)l,
            double d => (int)d,
            string s when int.TryParse(s, out var p) => p,
            JsonElement je when je.ValueKind == JsonValueKind.Number => je.GetInt32(),
            _ => defaultValue
        };
    }
}

public sealed record AccumulatorEntry(string GroupKey, object? Value, DateTimeOffset Timestamp);
