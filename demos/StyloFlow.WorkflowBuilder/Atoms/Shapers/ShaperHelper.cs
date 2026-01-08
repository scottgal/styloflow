using StyloFlow.WorkflowBuilder.Runtime;

namespace StyloFlow.WorkflowBuilder.Atoms.Shapers;

/// <summary>
/// Helper methods for shaper atoms.
/// </summary>
internal static class ShaperHelper
{
    /// <summary>
    /// Try to get a numeric value from common signal patterns.
    /// </summary>
    public static double GetNumericSignal(WorkflowSignals signals)
    {
        // Try common numeric signal patterns in order of likelihood
        var value = signals.Get<double>("sentiment.score");
        if (value != 0) return value;

        value = signals.Get<double>("filter.value");
        if (value != 0) return value;

        value = signals.Get<double>("clamp.value");
        if (value != 0) return value;

        value = signals.Get<double>("quantize.value");
        if (value != 0) return value;

        value = signals.Get<double>("mix.output");
        if (value != 0) return value;

        value = signals.Get<double>("compare.difference");
        if (value != 0) return value;

        value = signals.Get<double>("atten.value");
        if (value != 0) return value;

        value = signals.Get<double>("text.word_count");
        if (value != 0) return value;

        return 0;
    }

    /// <summary>
    /// Convert config value to double.
    /// </summary>
    public static double ToDouble(object? val) => val switch
    {
        double d => d,
        int i => i,
        float f => f,
        long l => l,
        string s when double.TryParse(s, out var d) => d,
        _ => 0.0
    };

    /// <summary>
    /// Convert config value to int.
    /// </summary>
    public static int ToInt(object? val) => val switch
    {
        int i => i,
        long l => (int)l,
        double d => (int)d,
        float f => (int)f,
        string s when int.TryParse(s, out var i) => i,
        _ => 0
    };
}
