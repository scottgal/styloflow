namespace StyloFlow.Converters.PdfPig;

/// <summary>
/// Configuration for PdfPig PDF converter.
/// </summary>
public class PdfPigConfig
{
    /// <summary>
    /// Word extraction method.
    /// </summary>
    public WordExtractorType WordExtractor { get; set; } = WordExtractorType.NearestNeighbour;

    /// <summary>
    /// Page segmentation method for detecting text blocks.
    /// </summary>
    public PageSegmenterType PageSegmenter { get; set; } = PageSegmenterType.DocstrumBoundingBoxes;

    /// <summary>
    /// Reading order detection method.
    /// </summary>
    public ReadingOrderType ReadingOrderDetector { get; set; } = ReadingOrderType.Unsupervised;

    /// <summary>
    /// Enable layout analysis (block detection, reading order).
    /// </summary>
    public bool UseLayoutAnalysis { get; set; } = true;

    /// <summary>
    /// Extract images from PDFs.
    /// </summary>
    public bool ExtractImages { get; set; } = true;

    /// <summary>
    /// Extract document metadata (title, author, etc.).
    /// </summary>
    public bool ExtractMetadata { get; set; } = true;

    /// <summary>
    /// Extract bookmarks/table of contents.
    /// </summary>
    public bool ExtractBookmarks { get; set; } = true;

    /// <summary>
    /// Include page markers in output (<!-- PAGE:N -->).
    /// </summary>
    public bool IncludePageMarkers { get; set; } = true;

    /// <summary>
    /// Try to detect headers by font size.
    /// </summary>
    public bool DetectHeaders { get; set; } = true;

    /// <summary>
    /// Extract word-level bounding boxes for downstream OCR comparison.
    /// </summary>
    public bool ExtractWordBoxes { get; set; } = false;

    /// <summary>
    /// Detect and filter decorations (headers, footers, page numbers).
    /// </summary>
    public bool FilterDecorations { get; set; } = true;

    /// <summary>
    /// Export format for structured output.
    /// </summary>
    public ExportFormat ExportFormat { get; set; } = ExportFormat.Markdown;

    /// <summary>
    /// Minimum word confidence threshold (0-1).
    /// Words below this are flagged for re-OCR.
    /// </summary>
    public double MinWordConfidence { get; set; } = 0.0;
}

/// <summary>
/// Word extraction algorithms.
/// </summary>
public enum WordExtractorType
{
    /// <summary>
    /// Default PdfPig word extraction.
    /// </summary>
    Default,

    /// <summary>
    /// Nearest neighbour - handles rotated and curved text.
    /// </summary>
    NearestNeighbour
}

/// <summary>
/// Page segmentation algorithms for detecting text blocks.
/// </summary>
public enum PageSegmenterType
{
    /// <summary>
    /// No segmentation - treat page as single block.
    /// </summary>
    None,

    /// <summary>
    /// Recursive XY Cut - good for multi-column layouts.
    /// </summary>
    RecursiveXYCut,

    /// <summary>
    /// Docstrum - bottom-up nearest-neighbor clustering.
    /// Handles L-shaped text and rotated paragraphs.
    /// </summary>
    DocstrumBoundingBoxes
}

/// <summary>
/// Reading order detection algorithms.
/// </summary>
public enum ReadingOrderType
{
    /// <summary>
    /// No reordering - use blocks as-is.
    /// </summary>
    None,

    /// <summary>
    /// Based on letter rendering sequence.
    /// </summary>
    RenderingBased,

    /// <summary>
    /// Unsupervised using Allen's interval algebra.
    /// Best for complex layouts.
    /// </summary>
    Unsupervised
}

/// <summary>
/// Output export formats.
/// </summary>
public enum ExportFormat
{
    /// <summary>
    /// Markdown with headers and formatting.
    /// </summary>
    Markdown,

    /// <summary>
    /// Plain text.
    /// </summary>
    PlainText,

    /// <summary>
    /// PAGE XML format (includes reading order).
    /// </summary>
    PageXml,

    /// <summary>
    /// ALTO XML format.
    /// </summary>
    AltoXml,

    /// <summary>
    /// hOCR HTML format.
    /// </summary>
    HOcr
}
