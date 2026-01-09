# MapReduce Patterns

StyloFlow includes a suite of MapReduce atoms for document scoring, ranking, and reduction - essential for RAG (Retrieval Augmented Generation) and search pipelines.

## Overview

```
[Accumulator] --> [Scorer] --> [Reducer/Selector] --> [Output]
     |               |                |
     |               |                +-- Final ranked results
     |               +-- Score each item (BM25, TF-IDF, RRF, MMR)
     +-- Collect items into window
```

## Scoring Algorithms

### BM25 (Best Match 25)
Classic sparse retrieval algorithm for text search:

```yaml
name: bm25-scorer
config:
  query: "machine learning neural networks"
  k1: 1.2      # Term saturation (default 1.5)
  b: 0.75      # Length normalization (0-1)
  text_field: "content"
  id_field: "id"
```

Use for: Traditional keyword search, document retrieval

### TF-IDF (Term Frequency-Inverse Document Frequency)
Statistical measure of term importance:

```yaml
name: tfidf-scorer
config:
  text_field: "content"
  top_n: 20    # Number of top terms to extract
```

Use for: Term extraction, document characterization

### RRF (Reciprocal Rank Fusion)
Combines multiple ranked lists:

```yaml
name: rrf-scorer
config:
  k: 60           # Ranking constant (default 60)
  rank_fields:
    - "bm25_rank"
    - "semantic_rank"
    - "recency_rank"
```

Use for: Hybrid search (combine keyword + semantic + other signals)

### MMR (Maximal Marginal Relevance)
Balances relevance and diversity:

```yaml
name: mmr-scorer
config:
  query_embedding: [0.1, 0.2, ...]  # Query vector
  embedding_field: "embedding"
  lambda: 0.7     # Balance: 1.0=pure relevance, 0.0=pure diversity
  top_k: 10
```

Use for: Diverse result sets, avoiding redundancy

## Reduction Operations

### Sum Reducer
```yaml
name: reduce-sum
config:
  window_name: scores
  value_field: score
```

### Average Reducer
```yaml
name: reduce-avg
config:
  window_name: ratings
  value_field: rating
```

### General Reducer
```yaml
name: reducer
config:
  window_name: data
  value_field: value
  operation: median   # sum, avg, max, min, median, stddev
```

## Selection

### Top-K Selector
Select top K items by score:

```yaml
name: topk-selector
config:
  window_name: scored_docs
  score_field: score
  k: 10
```

### Deduplicator
Remove near-duplicates:

```yaml
name: deduplicator
config:
  window_name: documents
  text_field: content
  similarity_threshold: 0.85   # 0-1, higher = stricter
  algorithm: jaro_winkler      # jaro_winkler, levenshtein
```

### Iterative Reducer
Reduce window size iteratively:

```yaml
name: iterative-reducer
config:
  window_name: candidates
  target_count: 5     # Final output size
  batch_size: 10      # Items per reduction step
  score_field: score
```

## Example: RAG Pipeline

```
+------------------+
| http-fetch       |
| (fetch docs)     |
+------------------+
         |
         | fetch.response
         v
+------------------+
| accumulator      |
| window: docs     |
+------------------+
         |
         | accumulator.entries
         v
+------------------+     +------------------+
| bm25-scorer      |     | embedding-scorer |
| (keyword match)  |     | (semantic match) |
+------------------+     +------------------+
         |                        |
         | bm25.scores            | embed.scores
         v                        v
+------------------+
| rrf-scorer       |
| (fuse rankings)  |
+------------------+
         |
         | rrf.ranked
         v
+------------------+
| mmr-scorer       |
| (diversify)      |
+------------------+
         |
         | mmr.selected
         v
+------------------+
| topk-selector    |
| k: 5             |
+------------------+
         |
         | topk.selected
         v
[LLM with context]
```

## Accumulator Patterns

### Simple Accumulation
Collect all items:

```yaml
name: accumulator
config:
  window_name: items
  max_items: 1000
```

### Grouped Accumulation
Group by key:

```yaml
name: accumulator
config:
  window_name: items
  group_by_field: category
  max_items_per_group: 100
```

## Signal Flow

```
accumulator.entries --> scorer --> topk.selected
         |                |               |
         |                |               +-- Array of top items
         |                +-- Array with scores
         +-- All accumulated items
```

## Performance Tips

1. **Limit window size** - Use `max_items` to prevent memory issues
2. **Stream large datasets** - Process in batches with iterative-reducer
3. **Use appropriate k** - Higher k in RRF for more sources
4. **Tune BM25** - k1=1.2-2.0, b=0.5-0.75 for most use cases
5. **Balance MMR lambda** - Start at 0.7, adjust based on diversity needs

## Sample Workflow JSON

```json
{
  "id": "rag-pipeline",
  "name": "RAG Document Pipeline",
  "nodes": [
    {
      "id": "fetch",
      "manifestName": "http-fetch",
      "config": { "url": "https://api.docs.com/search" }
    },
    {
      "id": "accumulate",
      "manifestName": "accumulator",
      "config": { "window_name": "docs" }
    },
    {
      "id": "score",
      "manifestName": "bm25-scorer",
      "config": { "query": "user query here", "text_field": "content" }
    },
    {
      "id": "select",
      "manifestName": "topk-selector",
      "config": { "k": 5 }
    }
  ],
  "edges": [
    { "sourceNodeId": "fetch", "signalKey": "fetch.response", "targetNodeId": "accumulate" },
    { "sourceNodeId": "accumulate", "signalKey": "accumulator.entries", "targetNodeId": "score" },
    { "sourceNodeId": "score", "signalKey": "bm25.ranked", "targetNodeId": "select" }
  ]
}
```
