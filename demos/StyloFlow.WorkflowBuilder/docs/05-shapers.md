# Signal Shapers

Signal shapers transform and route signals like modules in a modular synthesizer. They provide low-level control over signal flow and values.

## Modular Synth Analogy

Just as a modular synth processes audio signals through VCAs, filters, and mixers, StyloFlow shapers process data signals:

| Synth Module | StyloFlow Shaper | Function |
|--------------|------------------|----------|
| VCA | signal-attenuverter | Amplify/attenuate values |
| Limiter | signal-clamp | Constrain to range |
| Gate | signal-filter | Pass/block by condition |
| Mixer | signal-mixer | Combine signals |
| Quantizer | signal-quantizer | Snap to discrete values |
| Switch | signal-switch | Route between inputs |
| S&H | signal-delay | Sample and hold |
| Slew | signal-slew | Smooth transitions |
| Comparator | signal-comparator | Compare values |

## Signal Clamp

Limits values to a range (like an audio limiter):

```yaml
name: signal-clamp
config:
  input_signal: sentiment.score
  min: 0.0
  max: 1.0
```

**Use for:**
- Normalizing scores to 0-1 range
- Preventing outliers
- Constraining user inputs

## Signal Filter

Gates signals based on conditions (like a VCA gate):

```yaml
name: signal-filter
config:
  input_signal: score
  condition: gte    # gte, gt, lte, lt, eq, neq
  threshold: 0.5
```

**Use for:**
- Conditional routing
- Quality gates
- Threshold-based filtering

## Signal Mixer

Combines multiple signals with weights (like an audio mixer):

```yaml
name: signal-mixer
config:
  inputs:
    - signal: bm25.score
      weight: 0.4
    - signal: semantic.score
      weight: 0.6
  operation: weighted_sum   # weighted_sum, max, min, avg
```

**Use for:**
- Hybrid scoring
- Ensemble methods
- Signal blending

## Signal Quantizer

Snaps values to discrete steps (like a pitch quantizer):

```yaml
name: signal-quantizer
config:
  input_signal: confidence
  steps: [0.0, 0.25, 0.5, 0.75, 1.0]
```

Or with automatic step size:

```yaml
name: signal-quantizer
config:
  input_signal: value
  step_size: 0.1   # Round to nearest 0.1
```

**Use for:**
- Discretizing continuous values
- Rating systems (1-5 stars)
- Bucketing

## Signal Attenuverter

Scales, inverts, or offsets values:

```yaml
name: signal-attenuverter
config:
  input_signal: score
  scale: 2.0      # Multiply by this
  offset: 0.1     # Add this
  invert: false   # Flip sign?
```

**Use for:**
- Normalizing ranges
- Inverting confidence (1-x)
- Calibrating scores

## Signal Switch

Routes between inputs based on control signal (like a mux):

```yaml
name: signal-switch
config:
  control_signal: route.select
  inputs:
    - signal: path_a.value
    - signal: path_b.value
    - signal: path_c.value
  default_input: 0
```

**Use for:**
- A/B testing
- Feature flags
- Dynamic routing

## Signal Delay

Holds or delays signal propagation (like sample & hold):

```yaml
name: signal-delay
config:
  input_signal: sensor.value
  mode: hold          # hold, delay, gate
  delay_ms: 1000      # For delay mode
  trigger_signal: clock.tick   # For hold mode
```

**Use for:**
- Rate limiting
- Debouncing
- Synchronization

## Signal Slew

Smooths transitions (like portamento/glide):

```yaml
name: signal-slew
config:
  input_signal: target.value
  rise_rate: 0.1    # Max change per step (rising)
  fall_rate: 0.1    # Max change per step (falling)
```

**Use for:**
- Smooth score transitions
- Preventing sudden changes
- Trend dampening

## Signal Comparator

Compares two signals:

```yaml
name: signal-comparator
config:
  input_a: user.score
  input_b: threshold.value
  output_on_true: 1.0
  output_on_false: 0.0
```

**Use for:**
- Binary decisions
- Threshold crossing detection
- Value comparison

## Combining Shapers

Shapers can be chained for complex signal processing:

```
Raw Score --> Clamp (0-1) --> Quantize (0.1 steps) --> Mix (with other scores) --> Filter (>0.5)
```

Example workflow:

```json
{
  "nodes": [
    { "id": "source", "manifestName": "sensor", "config": {} },
    { "id": "clamp", "manifestName": "signal-clamp", "config": { "min": 0, "max": 1 } },
    { "id": "quant", "manifestName": "signal-quantizer", "config": { "step_size": 0.1 } },
    { "id": "filter", "manifestName": "signal-filter", "config": { "threshold": 0.5 } }
  ],
  "edges": [
    { "sourceNodeId": "source", "signalKey": "raw.value", "targetNodeId": "clamp" },
    { "sourceNodeId": "clamp", "signalKey": "clamp.value", "targetNodeId": "quant" },
    { "sourceNodeId": "quant", "signalKey": "quantize.value", "targetNodeId": "filter" }
  ]
}
```

## Real-World Examples

### Score Normalization Pipeline
```
[BM25 Scorer] --> [Clamp 0-100] --> [Attenuverter /100] --> [Clamp 0-1]
```

### Hybrid Scoring with Gating
```
[Keyword Score] ----\
                     +--> [Mixer] --> [Filter >0.6] --> [Output]
[Semantic Score] ---/
```

### Rate-Limited Output
```
[Fast Source] --> [Slew (smooth)] --> [Delay (100ms)] --> [Slow Consumer]
```

## Best Practices

1. **Chain for clarity** - Multiple simple shapers > one complex atom
2. **Document signal ranges** - Know your input/output bounds
3. **Test edge cases** - What happens at min/max?
4. **Use quantizers for UI** - Discrete values are easier to display
5. **Slew for stability** - Prevent UI flicker with smooth transitions
