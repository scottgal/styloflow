# LucidRAG Pipeline Architecture

## Overview

The LucidRAG system uses a **signal-based pipeline architecture** with:

- **Unified summarization**: Documents, images, data files all summarized the same way
- **Entity-centric storage**: All outputs stored with the entity, embeddings stay pure
- **Sentinel LLM**: Small model (1-15B) decomposes queries into filters + sub-embeddings
- **GraphRAG integration**: Entity relationships as core part of retrieval
- **Signal-driven molecules**: Components activate only when relevant signals are detected

## Architecture Overview

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                              INGESTION FLOW                                  │
├─────────────────────────────────────────────────────────────────────────────┤
│                                                                              │
│   ┌──────────┐     ┌──────────────┐     ┌─────────────────────────────────┐ │
│   │  Source  │────▶│  Ingestor    │────▶│  ingestion:content_stored       │ │
│   │(Dir/Git) │     │(IngestionSvc)│     │  {contentType, mimeType, path}  │ │
│   └──────────┘     └──────────────┘     └─────────────────────────────────┘ │
│                                                    │                         │
│                    ┌───────────────────────────────┼───────────────────────┐ │
│                    │                               │                       │ │
│                    ▼                               ▼                       ▼ │
│   ┌────────────────────┐    ┌────────────────────────┐    ┌──────────────┐  │
│   │  contentType=doc   │    │  contentType=image     │    │ contentType  │  │
│   │  ───────────────   │    │  ──────────────────    │    │ =data        │  │
│   │  DocSummarizer     │    │  ImageSummarizer       │    │ ──────────── │  │
│   │  (molecule)        │    │  (molecule)            │    │ DataSumm.    │  │
│   └────────────────────┘    └────────────────────────┘    └──────────────┘  │
│            │                         │                          │            │
│            ▼                         ▼                          ▼            │
│   ┌────────────────┐        ┌────────────────┐        ┌────────────────┐    │
│   │ Evidence Store │        │ Evidence Store │        │ Evidence Store │    │
│   │ - Summary      │        │ - OCR text     │        │ - Profile      │    │
│   │ - Entities     │        │ - Caption      │        │ - Schema       │    │
│   │ - Claims       │        │ - Signals      │        │ - Insights     │    │
│   └────────────────┘        └────────────────┘        └────────────────┘    │
│            │                         │                          │            │
│            └─────────────────────────┼──────────────────────────┘            │
│                                      ▼                                       │
│                        ┌───────────────────────┐                            │
│                        │   Unified Entity      │                            │
│                        │   - Source modalities │                            │
│                        │   - Multi-embeddings  │                            │
│                        │   - GraphRAG links    │                            │
│                        └───────────────────────┘                            │
└─────────────────────────────────────────────────────────────────────────────┘

┌─────────────────────────────────────────────────────────────────────────────┐
│                              QUERY FLOW                                      │
├─────────────────────────────────────────────────────────────────────────────┤
│                                                                              │
│   ┌──────────────┐                                                          │
│   │ User Query   │  "What was the difference between Q1 and Q2 revenue?"   │
│   └──────────────┘                                                          │
│          │                                                                   │
│          ▼                                                                   │
│   ┌────────────────────────────────────────────────────────────────────┐    │
│   │                    SENTINEL LLM (1-15B)                            │    │
│   │  Decomposes query into:                                            │    │
│   │  1. Sub-queries: ["Q1 revenue", "Q2 revenue"]                      │    │
│   │  2. Filters: {contentType: "data", columns: ["revenue", "quarter"]}│    │
│   │  3. Comparison: {type: "difference", fields: ["total"]}            │    │
│   └────────────────────────────────────────────────────────────────────┘    │
│          │                                                                   │
│          ├─────────────────────────────────────────────────────────┐        │
│          │                                                         │        │
│          ▼                                                         ▼        │
│   ┌────────────────────┐                           ┌────────────────────┐   │
│   │ Sub-Embeddings     │                           │  Filter Assembly   │   │
│   │ - "Q1 revenue"     │                           │  - WHERE quarter   │   │
│   │ - "Q2 revenue"     │                           │  - JOIN entities   │   │
│   │ - Entity refs      │                           │  - Graph traversal │   │
│   └────────────────────┘                           └────────────────────┘   │
│          │                                                   │              │
│          └───────────────────────┬───────────────────────────┘              │
│                                  ▼                                          │
│                    ┌───────────────────────────┐                            │
│                    │      Vector Store         │                            │
│                    │  + Graph relationships    │                            │
│                    │  + Evidence retrieval     │                            │
│                    └───────────────────────────┘                            │
│                                  │                                          │
│                                  ▼                                          │
│                    ┌───────────────────────────┐                            │
│                    │   Response Assembly       │                            │
│                    │   - Combine evidence      │                            │
│                    │   - Execute comparison    │                            │
│                    │   - Generate answer       │                            │
│                    └───────────────────────────┘                            │
└─────────────────────────────────────────────────────────────────────────────┘
```

## Core Concepts

### Content Types & Modalities

| Content Type | Molecule | Evidence Stored | Embedding Type |
|--------------|----------|-----------------|----------------|
| `document` | DocSummarizer | summary, claims, entities | text |
| `image` | ImageSummarizer | ocr_text, caption, signals | clip_visual, clip_text |
| `data` | DataSummarizer | profile, schema, insights, correlations | data |
| `audio` | AudioTranscriber | transcript, segments | audio, speech |
| `video` | VideoProcessor | frames, transcript | video_frame |

### Signals

Signals are typed messages that flow through the pipeline:

```csharp
// Signal types (LucidRAG.Entities.SignalTypes)
public static class SignalTypes
{
    // Ingestion
    public const string JobStarted = "ingestion:job_started";
    public const string ContentStored = "ingestion:content_stored";
    public const string JobCompleted = "ingestion:job_completed";

    // Document processing
    public const string DocumentConverted = "document:converted";
    public const string DocumentSummarized = "document:summarized";
    public const string EntitiesExtracted = "document:entities_extracted";

    // Data processing (DataSummarizer)
    public const string DataProfiled = "data:profiled";
    public const string DataSummarized = "data:summarized";
    public const string DataSchemaDetected = "data:schema_detected";
    public const string DataInsightsGenerated = "data:insights_generated";

    // Image processing
    public const string ImageAnalyzed = "image:analyzed";
    public const string OcrCompleted = "ocr:completed";
}
```

### Molecules (Processing Units)

Molecules are self-contained processing units that:
- Subscribe to specific content types or signal types
- Process content uniformly regardless of source
- Store outputs as evidence artifacts
- Generate embeddings for their modality
- Emit new signals for downstream processing

**Available Molecules:**

| Molecule | Subscribes To | Evidence Outputs | Signals Emitted |
|----------|---------------|------------------|-----------------|
| DocSummarizer | `document`, `ocr:completed` | summary, claims, entities | `document:summarized`, `entities:extracted` |
| ImageSummarizer | `image`, `pdf:scanned_page` | ocr_text, caption, signals | `image:described`, `ocr:completed` |
| DataSummarizer | `data`, `pdf:table_detected` | profile, schema, insights, correlations | `data:profiled`, `data:insights_generated` |
| AudioTranscriber | `audio` | transcript, segments | `audio:transcribed` |

## Unified Summarization Pattern

All content types follow the same summarization pattern:

```
1. INGEST: Source → Shared Storage → content_stored signal
2. ROUTE:  Signal dispatcher routes by contentType
3. ANALYZE: Molecule processes content (OCR, profile, transcribe)
4. EVIDENCE: Store artifacts (never in embeddings)
5. EMBED: Generate modality-specific embedding
6. ENTITY: Create/update unified entity with multi-embeddings
7. GRAPH: Extract entities and relationships (GraphRAG)
```

### Example: Ingesting a CSV File

```
1. IngestionService stores CSV → emits: content_stored(contentType="data")
2. DataSummarizer molecule activates
3. Profiles data: row count, column types, correlations, PII detection
4. Stores evidence:
   - data_profile (full statistical profile)
   - data_schema (column definitions)
   - data_insights (LLM-generated observations)
   - data_correlations (inter-column relationships)
5. Generates data embedding from schema + insights
6. Creates entity with modality=["tabular"]
7. Links to related entities via GraphRAG
```

### Example: Ingesting a Scanned PDF

```
1. IngestionService stores PDF → emits: content_stored(contentType="document")
2. PdfPigConverter detects scanned pages → emits: pdf:scanned_page (per page)
3. ImageSummarizer FAN-OUT: spawns per page
   └── Each page: OCR → stores evidence → emits: ocr:completed
4. Text aggregated → DocSummarizer activates
5. Stores evidence:
   - ocr_text (raw OCR output)
   - llm_summary (unified summary)
   - llm_entities (extracted entities)
6. Generates text embedding
7. Creates entity with modalities=["visual", "text"]
```

## Evidence Storage

All processing outputs go to evidence stores, **never into embeddings**:

```csharp
// Evidence types (LucidRAG.Entities.EvidenceTypes)
public static class EvidenceTypes
{
    // Document evidence
    public const string OcrText = "ocr_text";
    public const string LlmSummary = "llm_summary";
    public const string LlmEntities = "llm_entities";

    // Data evidence (DataSummarizer outputs)
    public const string DataProfile = "data_profile";
    public const string DataSchema = "data_schema";
    public const string DataInsights = "data_insights";
    public const string DataCorrelations = "data_correlations";
    public const string DataAnomalies = "data_anomalies";
    public const string DataPiiReport = "data_pii_report";
    public const string TableCsv = "table_csv";
    public const string TableParquet = "table_parquet";

    // Image/Video evidence
    public const string Thumbnail = "thumbnail";
    public const string Filmstrip = "filmstrip";
    public const string KeyFrame = "key_frame";

    // Audio evidence
    public const string Transcript = "transcript";
    public const string SpeakerDiarization = "speaker_diarization";
}
```

**Embeddings stay pure** - only the semantic vector, nothing else.

## Non-Leaking IDs

All chunk and segment IDs are non-reversible hashes:

```csharp
// Generate unique-per-tenant, non-reversible ID
var itemHash = GenerateSecureHash(tenantId, sourceId, path);
// Result: "xK9mN2pL4qR7..." (16 chars, base64url)
```

This prevents:
- Leaking file paths in embeddings
- Cross-tenant ID collision
- Reverse engineering source locations

## Query Decomposition (Sentinel LLM)

The Sentinel LLM (small 1-15B model) decomposes user queries:

```
User: "What was the difference between Q1 and Q2 revenue?"

Sentinel Output:
{
  "sub_queries": [
    {"query": "Q1 revenue total", "type": "data_lookup"},
    {"query": "Q2 revenue total", "type": "data_lookup"}
  ],
  "filters": {
    "content_type": ["data"],
    "columns": ["revenue", "quarter", "period"],
    "entity_types": ["financial_report", "revenue_data"]
  },
  "operation": {
    "type": "comparison",
    "function": "difference",
    "fields": ["total_revenue"]
  },
  "graph_traversal": {
    "from_entities": ["revenue_report"],
    "relationships": ["contains", "references"]
  }
}
```

The decomposed query drives:
1. **Sub-embeddings**: Each sub-query gets its own embedding for vector search
2. **Filter assembly**: SQL/filter conditions for pre-filtering
3. **Graph traversal**: Follow GraphRAG relationships
4. **Operation execution**: Apply comparison/aggregation on results

## GraphRAG Integration

GraphRAG is a core part of the system, not an add-on:

```
Entity Storage:
┌─────────────────────────────────────────────────────────────┐
│ RetrievalEntityRecord                                       │
│ - Id, ContentType, Source                                   │
│ - SourceModalities: ["text", "tabular"]                     │
│ - ExtractedEntities: [{name, type, mentions}]               │
│ - Relationships: [{source, target, type, weight}]           │
│ - Embeddings: [text, data, clip_visual]                     │
│ - EvidenceArtifacts: [summary, profile, insights]           │
└─────────────────────────────────────────────────────────────┘
           │
           │ Relationships
           ▼
┌─────────────────────────────────────────────────────────────┐
│ Knowledge Graph                                             │
│                                                             │
│   [Company A] ──reports_to──▶ [Financial Report Q1]         │
│        │                              │                     │
│        │                              │ contains            │
│        │                              ▼                     │
│        └────────────────────▶ [Revenue Data CSV]            │
│                                       │                     │
│                                       │ compared_with       │
│                                       ▼                     │
│                               [Revenue Data Q2]             │
└─────────────────────────────────────────────────────────────┘
```

## Pipeline Configuration

```yaml
pipeline:
  molecules:
    doc_summarizer:
      enabled: true
      subscribes: ["document", "ocr:completed"]
      embedding_model: "all-MiniLM-L6-v2"

    image_summarizer:
      enabled: true
      subscribes: ["image", "pdf:scanned_page", "pdf:image_extracted"]
      max_parallel: 4  # Fan-out limit
      embedding_model: "clip-vit-base-patch32"

    data_summarizer:
      enabled: true
      subscribes: ["data", "pdf:table_detected"]
      output_formats: ["parquet", "csv", "json"]
      profile_depth: "standard"  # initial, standard, background
      embedding_model: "all-MiniLM-L6-v2"

    audio_transcriber:
      enabled: true
      subscribes: ["audio", "video"]
      model: "whisper-base"

routing:
  content_stored:
    document: ["doc_summarizer"]
    image: ["image_summarizer"]
    data: ["data_summarizer"]
    audio: ["audio_transcriber"]
    video: ["audio_transcriber", "video_processor"]

sentinel:
  model: "llama-3.2-3b"  # Small, fast for decomposition
  max_sub_queries: 5
  enable_filters: true
  enable_graph_traversal: true
```

## Runtime Architecture

### Ephemeral Application

LucidRAG is designed as an **ephemeral application**:
- Stateless web instances - can scale horizontally
- All state in PostgreSQL + vector store + evidence storage
- No local file dependencies
- Container-friendly deployment

### Background Coordinators

Processing is offloaded to **keyed background coordinators**:

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                         COORDINATOR ARCHITECTURE                             │
├─────────────────────────────────────────────────────────────────────────────┤
│                                                                              │
│  REQUEST PIPELINE (High Priority, Per-Request)                              │
│  ┌────────────────────────────────────────────────────────────────────┐     │
│  │ User Query → [Sentinel Atom] → [Sub-Embeddings] → [Vector Search] │     │
│  │           → [Evidence Fetch] → [GraphRAG Traverse] → Response     │     │
│  └────────────────────────────────────────────────────────────────────┘     │
│                                                                              │
│  INGESTION COORDINATORS (Medium Priority, Per-Tenant/Per-Doc Keyed)         │
│  ┌────────────────────────────────────────────────────────────────────┐     │
│  │ tenant_123_doc_abc → [DocSummarizer] → [Entity Create] → Done     │     │
│  │ tenant_123_doc_def → [ImageSummarizer] → [OCR] → [Entity] → Done  │     │
│  │ tenant_456_data_xyz → [DataSummarizer] → [Profile] → Done         │     │
│  └────────────────────────────────────────────────────────────────────┘     │
│                                                                              │
│  BACKGROUND COORDINATORS (Low Priority, Continual)                          │
│  ┌────────────────────────────────────────────────────────────────────┐     │
│  │ [Re-embedding] - Update embeddings when model improves             │     │
│  │ [Relationship Learning] - Discover new entity connections          │     │
│  │ [Quality Improvement] - Re-OCR low-confidence pages                │     │
│  │ [Profile Enrichment] - Add deeper data insights                    │     │
│  │ [Drift Detection] - Monitor data schema changes                    │     │
│  └────────────────────────────────────────────────────────────────────┘     │
│                                                                              │
└─────────────────────────────────────────────────────────────────────────────┘
```

**Keying Strategy:**
- `tenant_{id}_doc_{hash}` - Per-document ingestion
- `tenant_{id}_collection_{id}` - Collection-level operations
- `background_learning_{tenant_id}` - Continual improvement

### Sentinel Atom (Request Pipeline)

The Sentinel LLM is an **atom** in the request pipeline:

```csharp
public class SentinelAtom : IRequestAtom
{
    // Part of the request pipeline, not background
    public async Task<QueryPlan> DecomposeAsync(string userQuery)
    {
        return new QueryPlan
        {
            SubQueries = ExtractSubQueries(userQuery),
            Filters = BuildFilters(userQuery),
            GraphTraversal = PlanGraphTraversal(userQuery),
            Operation = DetectOperation(userQuery)  // compare, aggregate, etc.
        };
    }
}
```

### Future: Lenses

**Lenses** provide different views over the same underlying data:

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                           LENS ARCHITECTURE (Future)                         │
├─────────────────────────────────────────────────────────────────────────────┤
│                                                                              │
│                        ┌───────────────────────┐                            │
│                        │   Same Entities &     │                            │
│                        │   Evidence Store      │                            │
│                        └───────────────────────┘                            │
│                                   │                                          │
│               ┌───────────────────┼───────────────────┐                     │
│               │                   │                   │                     │
│               ▼                   ▼                   ▼                     │
│   ┌───────────────────┐ ┌───────────────────┐ ┌───────────────────┐        │
│   │   Research Lens   │ │   Compliance Lens │ │   Executive Lens  │        │
│   │   ─────────────   │ │   ─────────────── │ │   ──────────────  │        │
│   │ - Deep citations  │ │ - PII redaction   │ │ - High-level only │        │
│   │ - Cross-refs      │ │ - Audit trail     │ │ - KPI focus       │        │
│   │ - Contradiction   │ │ - Policy filters  │ │ - Trend summaries │        │
│   │   detection       │ │ - Access control  │ │ - Visualizations  │        │
│   └───────────────────┘ └───────────────────┘ └───────────────────┘        │
│                                                                              │
└─────────────────────────────────────────────────────────────────────────────┘
```

Each lens:
- Uses same underlying entity/evidence/embedding data
- Applies different filtering, ranking, and presentation
- Can have lens-specific prompts for the LLM
- Enables role-based views without data duplication

## Key Design Principles

1. **Ephemeral app**: Stateless, horizontally scalable
2. **Keyed coordinators**: Per-tenant/per-doc partitioned processing
3. **Unified summarization**: Same pattern for all content types
4. **Entity-centric**: Everything stored with the entity
5. **Evidence separation**: Artifacts in evidence stores, not embeddings
6. **Pure embeddings**: Only semantic vectors, no metadata leakage
7. **Non-reversible IDs**: Secure, unique-per-tenant identifiers
8. **Sentinel atom**: Small LLM in request pipeline for query decomposition
9. **GraphRAG core**: Entity relationships are first-class citizens
10. **Signal-driven**: Lazy activation based on content type detection
11. **Background learning**: Continual low-priority improvement
12. **Future lenses**: Different views over same data
