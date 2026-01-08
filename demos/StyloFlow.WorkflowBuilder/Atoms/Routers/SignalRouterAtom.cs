using Mostlylucid.Ephemeral.Atoms.Taxonomy;

namespace StyloFlow.WorkflowBuilder.Atoms.Routers;

/// <summary>
/// Signal Router - Routes signals to different outputs based on conditions.
/// Supports multiple routing modes: threshold, match, range, contains.
/// Pure deterministic routing - no LLM involved.
/// </summary>
public sealed class SignalRouterAtom
{
    public static AtomContract Contract => AtomContract.Create(
        AtomKind.Sensor, // Routers act like signal transformers
        AtomDeterminism.Deterministic,
        AtomPersistence.EphemeralOnly,
        name: "signal-router",
        reads: ["*"],
        writes: ["router.route_a", "router.route_b", "router.route_c", "router.route_default", "router.decision", "router.input_value"]);

    public static Task ExecuteAsync(WorkflowAtomContext ctx)
    {
        // Get configuration
        var inputSignal = ctx.Config.TryGetValue("input_signal", out var inp) ? inp?.ToString() ?? "input" : "input";
        var mode = ctx.Config.TryGetValue("mode", out var m) ? m?.ToString() ?? "threshold" : "threshold";

        // Get the input value from signals
        var inputValue = ctx.Signals.Get<object>(inputSignal);

        // Also check common signal names if the configured one isn't found
        if (inputValue == null)
        {
            inputValue = ctx.Signals.Get<object>("timer.triggered")
                      ?? ctx.Signals.Get<object>("http.body")
                      ?? ctx.Signals.Get<object>("text.analyzed")
                      ?? ctx.Signals.Get<object>("sentiment.score")
                      ?? ctx.Signals.Get<object>("filter.passed");
        }

        ctx.Log($"Router: mode={mode}, input_signal={inputSignal}, value={inputValue}");
        ctx.Emit("router.input_value", inputValue);

        var (route, decision) = mode?.ToLowerInvariant() switch
        {
            "threshold" => EvaluateThreshold(ctx, inputValue),
            "match" => EvaluateMatch(ctx, inputValue),
            "range" => EvaluateRange(ctx, inputValue),
            "contains" => EvaluateContains(ctx, inputValue),
            _ => ("route_default", "unknown_mode")
        };

        ctx.Log($"Router decision: {decision} -> {route}");
        ctx.Emit("router.decision", decision);
        ctx.Emit("router.route_name", route);

        // Emit to the appropriate route signal with the input value passed through
        // This enables dynamic routing - downstream atoms listen to router.{route_name}
        var routeSignal = $"router.{route}";
        ctx.Emit(routeSignal, inputValue);

        return Task.CompletedTask;
    }

    private static (string route, string decision) EvaluateThreshold(WorkflowAtomContext ctx, object? value)
    {
        var threshold = GetDouble(ctx.Config, "threshold_value", 0.5);
        var aboveRoute = GetString(ctx.Config, "threshold_above", "route_a");
        var belowRoute = GetString(ctx.Config, "threshold_below", "route_b");

        var numericValue = ToDouble(value);

        if (numericValue >= threshold)
        {
            return (aboveRoute, $"value {numericValue:F2} >= threshold {threshold:F2}");
        }
        return (belowRoute, $"value {numericValue:F2} < threshold {threshold:F2}");
    }

    private static (string route, string decision) EvaluateMatch(WorkflowAtomContext ctx, object? value)
    {
        var matchValue = GetString(ctx.Config, "match_value", "");
        var matchRoute = GetString(ctx.Config, "match_route", "route_a");
        var noMatchRoute = GetString(ctx.Config, "no_match_route", "route_default");

        var stringValue = value?.ToString() ?? "";

        if (string.Equals(stringValue, matchValue, StringComparison.OrdinalIgnoreCase))
        {
            return (matchRoute, $"value '{stringValue}' matches '{matchValue}'");
        }
        return (noMatchRoute, $"value '{stringValue}' does not match '{matchValue}'");
    }

    private static (string route, string decision) EvaluateRange(WorkflowAtomContext ctx, object? value)
    {
        var rangeLow = GetDouble(ctx.Config, "range_low", 0);
        var rangeHigh = GetDouble(ctx.Config, "range_high", 100);
        var inRangeRoute = GetString(ctx.Config, "in_range_route", "route_a");
        var outRangeRoute = GetString(ctx.Config, "out_range_route", "route_b");

        var numericValue = ToDouble(value);

        if (numericValue >= rangeLow && numericValue <= rangeHigh)
        {
            return (inRangeRoute, $"value {numericValue:F2} in range [{rangeLow:F2}, {rangeHigh:F2}]");
        }
        return (outRangeRoute, $"value {numericValue:F2} outside range [{rangeLow:F2}, {rangeHigh:F2}]");
    }

    private static (string route, string decision) EvaluateContains(WorkflowAtomContext ctx, object? value)
    {
        var containsText = GetString(ctx.Config, "contains_text", "");
        var containsRoute = GetString(ctx.Config, "contains_route", "route_a");
        var notContainsRoute = GetString(ctx.Config, "not_contains_route", "route_b");

        var stringValue = value?.ToString() ?? "";

        if (stringValue.Contains(containsText, StringComparison.OrdinalIgnoreCase))
        {
            return (containsRoute, $"value contains '{containsText}'");
        }
        return (notContainsRoute, $"value does not contain '{containsText}'");
    }

    private static double GetDouble(Dictionary<string, object> config, string key, double defaultValue)
    {
        if (config.TryGetValue(key, out var val))
        {
            return ToDouble(val);
        }
        return defaultValue;
    }

    private static string GetString(Dictionary<string, object> config, string key, string defaultValue)
    {
        if (config.TryGetValue(key, out var val))
        {
            return val?.ToString() ?? defaultValue;
        }
        return defaultValue;
    }

    private static double ToDouble(object? value)
    {
        return value switch
        {
            null => 0,
            double d => d,
            float f => f,
            int i => i,
            long l => l,
            decimal dec => (double)dec,
            string s when double.TryParse(s, out var parsed) => parsed,
            bool b => b ? 1 : 0,
            _ => 0
        };
    }
}
