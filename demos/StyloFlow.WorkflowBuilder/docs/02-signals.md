# Understanding Signals

Signals are the communication backbone of StyloFlow workflows. They carry data between atoms and enable loosely-coupled, reactive pipelines.

## Signal Anatomy

```
sentiment.score = 0.85
   |         |      |
   |         |      +-- Value (any type)
   |         +--------- Key (dot-separated path)
   +------------------- Namespace
```

## Signal Namespaces

Signals use dot-notation to organize by domain:

```yaml
# Text processing
text.analyzed
text.word_count
text.char_count

# Sentiment
sentiment.score
sentiment.label
sentiment.confidence

# HTTP
http.body
http.method
http.path

# Patterns
burst.detected
pattern.type
pattern.confidence
```

## Triggering and Emission

### Triggers (Inputs)
Atoms declare which signals they can process:

```yaml
signals:
  triggers:
    - signal: http.body
      required: true
    - signal: timer.triggered
      required: false
```

### Emits (Outputs)
Atoms declare which signals they produce:

```yaml
signals:
  emits:
    - key: text.analyzed
      type: string
    - key: text.word_count
      type: integer
```

## Signal Flow Example

```
Timer fires every 60s
        |
        v
+-------------------+
| timer-trigger     |
+-------------------+
        |
        | timer.triggered
        | timer.timestamp
        v
+-------------------+
| http-fetch        |
| url: "api.com"    |
+-------------------+
        |
        | fetch.response
        | fetch.status
        v
+-------------------+
| text-analyzer     |
+-------------------+
        |
        | text.analyzed
        | text.word_count
        v
+-------------------+
| sentiment-detector|
+-------------------+
        |
        | sentiment.score
        | sentiment.label
        v
+-------------------+
| threshold-filter  |
| threshold: 0.7    |
+-------------------+
       / \
      /   \
filter.passed  filter.exceeded
      |             |
      v             v
[email-sender]  [log-writer]
```

## Reading Signals in Atoms

```csharp
public static Task ExecuteAsync(WorkflowAtomContext ctx)
{
    // Read a string signal
    var text = ctx.Signals.Get<string>("http.body");

    // Read a numeric signal
    var score = ctx.Signals.Get<double>("sentiment.score");

    // Check if signal exists
    if (ctx.Signals.Has("timer.triggered"))
    {
        // Process timer event
    }

    // Emit new signals
    ctx.Emit("text.analyzed", processedText);
    ctx.Emit("text.word_count", wordCount);

    return Task.CompletedTask;
}
```

## Signal Windows

Signals exist in a sliding window with configurable capacity and age:

```csharp
// Get recent signals
var recent = ctx.Signals.GetAll();

// Get signals for this run only
var runSignals = ctx.Signals.GetRunSignals();
```

## Wildcard Triggers

Atoms can trigger on any signal using wildcards:

```yaml
signals:
  triggers:
    - signal: "*"
      description: Accepts any signal
```

This is useful for:
- Log writers (capture everything)
- Routers (inspect and forward)
- Accumulators (collect for reduction)

## Signal Shapers

Shapers transform signals like modular synth modules:

| Shaper | Function |
|--------|----------|
| `signal-clamp` | Limit to range |
| `signal-filter` | Gate/pass conditions |
| `signal-mixer` | Combine multiple signals |
| `signal-quantizer` | Discrete steps |
| `signal-attenuverter` | Scale/invert |
| `signal-delay` | Hold/delay values |

Example: Clamp sentiment to [0.0, 1.0]:

```yaml
config:
  input_signal: sentiment.score
  min: 0.0
  max: 1.0
```

## Best Practices

1. **Namespace signals** - Use dot-notation for clarity
2. **Be specific** - `user.email_verified` not `verified`
3. **Use types consistently** - Don't emit both string and number for same signal
4. **Document signals** - Add descriptions in manifests
5. **Minimize emissions** - Only emit what downstream atoms need

## Debugging Signals

Use the `log-writer` atom to capture signal values:

```yaml
name: debug-logger
config:
  signal_pattern: "*"
  log_level: debug
```

Or view real-time signals in the workflow builder's signal panel.
