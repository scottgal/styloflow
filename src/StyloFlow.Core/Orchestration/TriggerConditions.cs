namespace StyloFlow.Orchestration;

/// <summary>
/// Condition that must be met for a component to run.
/// Ported from BotDetection's mature trigger infrastructure.
/// </summary>
public abstract record TriggerCondition
{
    /// <summary>
    /// Human-readable description of this condition.
    /// </summary>
    public abstract string Description { get; }

    /// <summary>
    /// Check if this condition is satisfied given the current signals.
    /// </summary>
    public abstract bool IsSatisfied(IReadOnlyDictionary<string, object> signals);
}

/// <summary>
/// Trigger when a specific signal key exists.
/// </summary>
public sealed record SignalExistsTrigger(string SignalKey) : TriggerCondition
{
    public override string Description => $"Signal '{SignalKey}' exists";

    public override bool IsSatisfied(IReadOnlyDictionary<string, object> signals)
        => signals.ContainsKey(SignalKey);
}

/// <summary>
/// Trigger when a signal has a specific value.
/// </summary>
public sealed record SignalValueTrigger<T>(string SignalKey, T ExpectedValue) : TriggerCondition
{
    public override string Description => $"Signal '{SignalKey}' == {ExpectedValue}";

    public override bool IsSatisfied(IReadOnlyDictionary<string, object> signals)
        => signals.TryGetValue(SignalKey, out var value) &&
           value is T typed &&
           EqualityComparer<T>.Default.Equals(typed, ExpectedValue);
}

/// <summary>
/// Trigger when a signal satisfies a predicate.
/// </summary>
public sealed record SignalPredicateTrigger<T>(
    string SignalKey,
    Func<T, bool> Predicate,
    string PredicateDescription) : TriggerCondition
{
    public override string Description => $"Signal '{SignalKey}' {PredicateDescription}";

    public override bool IsSatisfied(IReadOnlyDictionary<string, object> signals)
        => signals.TryGetValue(SignalKey, out var value) &&
           value is T typed &&
           Predicate(typed);
}

/// <summary>
/// Trigger when any of the sub-conditions are met (OR logic).
/// </summary>
public sealed record AnyOfTrigger(IReadOnlyList<TriggerCondition> Conditions) : TriggerCondition
{
    public override string Description => $"Any of: [{string.Join(", ", Conditions.Select(c => c.Description))}]";

    public override bool IsSatisfied(IReadOnlyDictionary<string, object> signals)
        => Conditions.Any(c => c.IsSatisfied(signals));
}

/// <summary>
/// Trigger when all of the sub-conditions are met (AND logic).
/// </summary>
public sealed record AllOfTrigger(IReadOnlyList<TriggerCondition> Conditions) : TriggerCondition
{
    public override string Description => $"All of: [{string.Join(", ", Conditions.Select(c => c.Description))}]";

    public override bool IsSatisfied(IReadOnlyDictionary<string, object> signals)
        => Conditions.All(c => c.IsSatisfied(signals));
}

/// <summary>
/// Trigger when a certain number of components have completed.
/// </summary>
public sealed record ComponentCountTrigger(int MinComponents) : TriggerCondition
{
    public const string CompletedComponentsSignal = "_system.completed_components";

    public override string Description => $"At least {MinComponents} components completed";

    public override bool IsSatisfied(IReadOnlyDictionary<string, object> signals)
        => signals.TryGetValue(CompletedComponentsSignal, out var value) &&
           value is int count &&
           count >= MinComponents;
}

/// <summary>
/// Trigger when a score exceeds a threshold.
/// </summary>
public sealed record ScoreThresholdTrigger(string ScoreKey, double MinScore) : TriggerCondition
{
    public override string Description => $"{ScoreKey} >= {MinScore:F2}";

    public override bool IsSatisfied(IReadOnlyDictionary<string, object> signals)
        => signals.TryGetValue(ScoreKey, out var value) &&
           value is double score &&
           score >= MinScore;
}

/// <summary>
/// Helper class for building trigger conditions fluently.
/// </summary>
public static class Triggers
{
    /// <summary>
    /// Trigger when a signal exists.
    /// </summary>
    public static TriggerCondition WhenSignalExists(string signalKey)
        => new SignalExistsTrigger(signalKey);

    /// <summary>
    /// Trigger when a signal has a specific value.
    /// </summary>
    public static TriggerCondition WhenSignalEquals<T>(string signalKey, T value)
        => new SignalValueTrigger<T>(signalKey, value);

    /// <summary>
    /// Trigger when a signal satisfies a predicate.
    /// </summary>
    public static TriggerCondition WhenSignal<T>(
        string signalKey,
        Func<T, bool> predicate,
        string description)
        => new SignalPredicateTrigger<T>(signalKey, predicate, description);

    /// <summary>
    /// Trigger when any condition is met.
    /// </summary>
    public static TriggerCondition AnyOf(params TriggerCondition[] conditions)
        => new AnyOfTrigger(conditions);

    /// <summary>
    /// Trigger when all conditions are met.
    /// </summary>
    public static TriggerCondition AllOf(params TriggerCondition[] conditions)
        => new AllOfTrigger(conditions);

    /// <summary>
    /// Trigger when enough components have completed.
    /// </summary>
    public static TriggerCondition WhenComponentCount(int min)
        => new ComponentCountTrigger(min);

    /// <summary>
    /// Trigger when a score exceeds threshold.
    /// </summary>
    public static TriggerCondition WhenScoreExceeds(string scoreKey, double threshold)
        => new ScoreThresholdTrigger(scoreKey, threshold);
}
