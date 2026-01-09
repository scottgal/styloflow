using System.Text.Json.Serialization;

namespace StyloFlow.Converters.Docling;

/// <summary>
/// OCR output from Docling conversion.
/// Stored as an artifact for downstream processing.
/// </summary>
public class OcrArtifact
{
    /// <summary>
    /// Source file this OCR came from.
    /// </summary>
    public required string SourcePath { get; init; }

    /// <summary>
    /// Page number (1-indexed).
    /// </summary>
    public int PageNumber { get; init; }

    /// <summary>
    /// Extracted text content.
    /// </summary>
    public string? Text { get; init; }

    /// <summary>
    /// Overall OCR confidence (0-1).
    /// </summary>
    public double? Confidence { get; init; }

    /// <summary>
    /// Quality assessment of the OCR result.
    /// </summary>
    public OcrQuality Quality { get; init; }

    /// <summary>
    /// Whether this needs re-OCR by another pipeline.
    /// </summary>
    public bool NeedsReocr { get; init; }

    /// <summary>
    /// Reason for re-OCR if needed.
    /// </summary>
    public string? ReocrReason { get; init; }

    /// <summary>
    /// Word-level bounding boxes (if available).
    /// </summary>
    public IReadOnlyList<OcrWord>? Words { get; init; }

    /// <summary>
    /// Producer (e.g., "docling", "tesseract", "easyocr").
    /// </summary>
    public string? Producer { get; init; }

    /// <summary>
    /// Time taken to OCR this page.
    /// </summary>
    public TimeSpan Duration { get; init; }
}

/// <summary>
/// Word-level OCR result with position.
/// </summary>
public record OcrWord
{
    public required string Text { get; init; }
    public double Confidence { get; init; }
    public OcrBoundingBox? BoundingBox { get; init; }
}

/// <summary>
/// Bounding box for OCR word position.
/// </summary>
public record OcrBoundingBox
{
    public double X { get; init; }
    public double Y { get; init; }
    public double Width { get; init; }
    public double Height { get; init; }
}

/// <summary>
/// Quality classification for OCR output.
/// </summary>
public enum OcrQuality
{
    /// <summary>
    /// Unknown quality (not assessed).
    /// </summary>
    Unknown,

    /// <summary>
    /// High quality - text is clean and readable.
    /// </summary>
    High,

    /// <summary>
    /// Medium quality - some issues but usable.
    /// </summary>
    Medium,

    /// <summary>
    /// Low quality - significant issues, consider re-OCR.
    /// </summary>
    Low,

    /// <summary>
    /// Garbage - text is garbled, needs re-OCR.
    /// </summary>
    Garbage,

    /// <summary>
    /// Empty - no text extracted.
    /// </summary>
    Empty
}

/// <summary>
/// Aggregated OCR results for a document.
/// </summary>
public class DocumentOcrResult
{
    /// <summary>
    /// Source document path.
    /// </summary>
    public required string SourcePath { get; init; }

    /// <summary>
    /// Total pages processed.
    /// </summary>
    public int TotalPages { get; init; }

    /// <summary>
    /// Pages with garbage/low quality that need re-OCR.
    /// </summary>
    public int PagesNeedingReocr { get; init; }

    /// <summary>
    /// Overall quality assessment.
    /// </summary>
    public OcrQuality OverallQuality { get; init; }

    /// <summary>
    /// Average confidence across pages.
    /// </summary>
    public double AverageConfidence { get; init; }

    /// <summary>
    /// Individual page results.
    /// </summary>
    public IReadOnlyList<OcrArtifact>? Pages { get; init; }

    /// <summary>
    /// Path to combined markdown in shared storage.
    /// </summary>
    public string? MarkdownPath { get; init; }

    /// <summary>
    /// Path to OCR artifacts directory in shared storage.
    /// </summary>
    public string? OcrArtifactsPath { get; init; }

    /// <summary>
    /// Whether any pages need re-OCR.
    /// </summary>
    public bool HasQualityIssues => PagesNeedingReocr > 0;
}

/// <summary>
/// Signal emitted when Docling conversion completes.
/// This allows downstream processors to decide on further processing.
/// </summary>
public class DoclingConversionSignal
{
    /// <summary>
    /// Path to converted markdown in shared storage.
    /// </summary>
    public required string MarkdownPath { get; init; }

    /// <summary>
    /// Path to OCR artifacts in shared storage.
    /// </summary>
    public string? OcrArtifactsPath { get; init; }

    /// <summary>
    /// Overall OCR quality.
    /// </summary>
    public OcrQuality Quality { get; init; }

    /// <summary>
    /// Whether downstream re-OCR is recommended.
    /// </summary>
    public bool RecommendReocr { get; init; }

    /// <summary>
    /// Pages that need re-OCR (1-indexed).
    /// </summary>
    public int[]? PagesNeedingReocr { get; init; }

    /// <summary>
    /// Original source path for reference.
    /// </summary>
    public required string SourcePath { get; init; }

    /// <summary>
    /// MIME type of original.
    /// </summary>
    public required string SourceMimeType { get; init; }
}
