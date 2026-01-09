namespace StyloFlow.Converters.Docling;

/// <summary>
/// Configuration for Docling document converter.
/// </summary>
public class DoclingConfig
{
    /// <summary>
    /// Base URL for Docling API (e.g., "http://localhost:5001").
    /// </summary>
    public string BaseUrl { get; set; } = "http://localhost:5001";

    /// <summary>
    /// Timeout for conversion in seconds.
    /// </summary>
    public int TimeoutSeconds { get; set; } = 1200;

    /// <summary>
    /// PDF backend: "pypdfium2" (fast) or "docling" (OCR-capable).
    /// </summary>
    public string PdfBackend { get; set; } = "pypdfium2";

    /// <summary>
    /// OCR backend for scanned PDFs: "tesseract" or "easyocr".
    /// </summary>
    public string OcrPdfBackend { get; set; } = "docling";

    /// <summary>
    /// Enable automatic OCR fallback when text extraction fails.
    /// </summary>
    public bool EnableOcrFallback { get; set; } = true;

    /// <summary>
    /// Enable split processing for large PDFs.
    /// </summary>
    public bool EnableSplitProcessing { get; set; } = true;

    /// <summary>
    /// Pages per chunk for split processing.
    /// </summary>
    public int PagesPerChunk { get; set; } = 50;

    /// <summary>
    /// Maximum concurrent chunks.
    /// </summary>
    public int MaxConcurrentChunks { get; set; } = 2;

    /// <summary>
    /// Minimum pages before splitting.
    /// </summary>
    public int MinPagesForSplit { get; set; } = 50;

    /// <summary>
    /// Auto-detect GPU and optimize settings.
    /// </summary>
    public bool AutoDetectGpu { get; set; } = true;

    /// <summary>
    /// Whether to extract images from documents.
    /// </summary>
    public bool ExtractImages { get; set; } = true;

    /// <summary>
    /// Whether to perform OCR on extracted images.
    /// </summary>
    public bool OcrImages { get; set; } = true;

    /// <summary>
    /// Store OCR results as separate artifacts.
    /// </summary>
    public bool StoreOcrArtifacts { get; set; } = true;
}
