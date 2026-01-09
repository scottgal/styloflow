namespace StyloFlow.Converters.PdfPig.Signals;

/// <summary>
/// Signals emitted during PDF conversion for wave pipeline routing.
/// These enable intelligent routing based on content analysis.
/// </summary>
public static class PdfSignalTypes
{
    /// <summary>PDF successfully converted to markdown</summary>
    public const string PdfConverted = "pdf:converted";

    /// <summary>Scanned page detected - needs OCR</summary>
    public const string ScannedPageDetected = "pdf:scanned_page";

    /// <summary>Image extracted from PDF</summary>
    public const string ImageExtracted = "pdf:image_extracted";

    /// <summary>Text extraction quality is low - may need OCR</summary>
    public const string LowQualityText = "pdf:low_quality";

    /// <summary>Document structure extracted (bookmarks, sections)</summary>
    public const string StructureExtracted = "pdf:structure";

    /// <summary>Embedded font information</summary>
    public const string FontsDetected = "pdf:fonts";
}

/// <summary>
/// Signal emitted when a PDF is converted.
/// Downstream waves can act on conversion quality and content type.
/// </summary>
public record PdfConvertedSignal
{
    /// <summary>Signal type for routing.</summary>
    public string SignalType => PdfSignalTypes.PdfConverted;

    /// <summary>Path to source PDF.</summary>
    public required string SourcePath { get; init; }

    /// <summary>Path to converted markdown in shared storage.</summary>
    public required string OutputPath { get; init; }

    /// <summary>Overall extraction quality.</summary>
    public required ExtractionQuality Quality { get; init; }

    /// <summary>Document type (searchable, scanned, mixed).</summary>
    public required PdfDocumentType DocumentType { get; init; }

    /// <summary>Number of pages.</summary>
    public int PageCount { get; init; }

    /// <summary>Total words extracted.</summary>
    public int TotalWords { get; init; }

    /// <summary>Total images in document.</summary>
    public int TotalImages { get; init; }

    /// <summary>Number of pages that need OCR.</summary>
    public int PagesNeedingOcr { get; init; }

    /// <summary>Whether the document needs OCR processing.</summary>
    public bool NeedsOcr => PagesNeedingOcr > 0 || DocumentType is PdfDocumentType.Scanned or PdfDocumentType.Mixed;

    /// <summary>Document metadata.</summary>
    public PdfDocumentMetadata? Metadata { get; init; }
}

/// <summary>
/// Signal emitted for each scanned page that needs OCR.
/// ImageSummarizer or OCR waves can pick this up.
/// </summary>
public record ScannedPageSignal
{
    /// <summary>Signal type for routing.</summary>
    public string SignalType => PdfSignalTypes.ScannedPageDetected;

    /// <summary>Source PDF path.</summary>
    public required string SourcePdf { get; init; }

    /// <summary>Page number (1-based).</summary>
    public int PageNumber { get; init; }

    /// <summary>Path to extracted page image (if available).</summary>
    public string? PageImagePath { get; init; }

    /// <summary>Page width in points.</summary>
    public double Width { get; init; }

    /// <summary>Page height in points.</summary>
    public double Height { get; init; }

    /// <summary>Confidence that this page is scanned (0-1).</summary>
    public double Confidence { get; init; }

    /// <summary>Any text that was extracted (may be garbage).</summary>
    public string? ExtractedText { get; init; }

    /// <summary>Quality of any extracted text.</summary>
    public ExtractionQuality TextQuality { get; init; }
}

/// <summary>
/// Signal emitted when an image is extracted from PDF.
/// ImageSummarizer can analyze these for text content or visual description.
/// </summary>
public record ImageExtractedSignal
{
    /// <summary>Signal type for routing.</summary>
    public string SignalType => PdfSignalTypes.ImageExtracted;

    /// <summary>Source PDF path.</summary>
    public required string SourcePdf { get; init; }

    /// <summary>Page number where image was found.</summary>
    public int PageNumber { get; init; }

    /// <summary>Path to extracted image in shared storage.</summary>
    public required string ImagePath { get; init; }

    /// <summary>Image MIME type.</summary>
    public required string MimeType { get; init; }

    /// <summary>Image size in bytes.</summary>
    public long SizeBytes { get; init; }

    /// <summary>Whether this is a full-page image (likely scanned content).</summary>
    public bool IsFullPage { get; init; }

    /// <summary>Percentage of page covered by this image.</summary>
    public double PageCoverage { get; init; }
}

/// <summary>
/// Signal emitted when text quality is low.
/// Triggers OCR fallback or quality improvement waves.
/// </summary>
public record LowQualityTextSignal
{
    /// <summary>Signal type for routing.</summary>
    public string SignalType => PdfSignalTypes.LowQualityText;

    /// <summary>Source PDF path.</summary>
    public required string SourcePdf { get; init; }

    /// <summary>Pages with low quality text.</summary>
    public required IReadOnlyList<int> AffectedPages { get; init; }

    /// <summary>Overall quality assessment.</summary>
    public ExtractionQuality Quality { get; init; }

    /// <summary>Suggested action.</summary>
    public required string SuggestedAction { get; init; }  // "ocr", "rescan", "review"
}

/// <summary>
/// Signal emitted with document structure information.
/// Enables structure-aware processing downstream.
/// </summary>
public record DocumentStructureSignal
{
    /// <summary>Signal type for routing.</summary>
    public string SignalType => PdfSignalTypes.StructureExtracted;

    /// <summary>Source PDF path.</summary>
    public required string SourcePdf { get; init; }

    /// <summary>Document title.</summary>
    public string? Title { get; init; }

    /// <summary>Document author.</summary>
    public string? Author { get; init; }

    /// <summary>Number of sections/chapters.</summary>
    public int SectionCount { get; init; }

    /// <summary>Bookmark/outline entries.</summary>
    public int BookmarkCount { get; init; }

    /// <summary>Table of contents available.</summary>
    public bool HasTableOfContents { get; init; }

    /// <summary>Bookmark titles for navigation.</summary>
    public IReadOnlyList<string>? BookmarkTitles { get; init; }
}

/// <summary>
/// Signal emitted when a table is detected.
/// DataSummarizer molecule can process structured data.
/// </summary>
public record TableDetectedSignal
{
    /// <summary>Signal type for routing.</summary>
    public const string SignalType = "pdf:table_detected";

    /// <summary>Source PDF path.</summary>
    public required string SourcePdf { get; init; }

    /// <summary>Table identifier.</summary>
    public required string TableId { get; init; }

    /// <summary>Page number where table was found.</summary>
    public int PageNumber { get; init; }

    /// <summary>Number of rows.</summary>
    public int RowCount { get; init; }

    /// <summary>Number of columns.</summary>
    public int ColumnCount { get; init; }

    /// <summary>Column names (if detected).</summary>
    public IReadOnlyList<string>? ColumnNames { get; init; }

    /// <summary>Whether first row is a header.</summary>
    public bool HasHeader { get; init; }

    /// <summary>Path to CSV export in shared storage.</summary>
    public string? CsvPath { get; init; }

    /// <summary>Path to JSON export in shared storage.</summary>
    public string? JsonPath { get; init; }

    /// <summary>Schema for Parquet generation.</summary>
    public TableSchema? Schema { get; init; }
}

/// <summary>
/// Signal emitted with all tables from a document.
/// Enables batch processing by DataSummarizer.
/// </summary>
public record TablesExtractedSignal
{
    /// <summary>Signal type for routing.</summary>
    public const string SignalType = "pdf:tables_extracted";

    /// <summary>Source PDF path.</summary>
    public required string SourcePdf { get; init; }

    /// <summary>Total number of tables found.</summary>
    public int TableCount { get; init; }

    /// <summary>Pages containing tables.</summary>
    public IReadOnlyList<int>? PagesWithTables { get; init; }

    /// <summary>Individual table signals.</summary>
    public IReadOnlyList<TableDetectedSignal>? Tables { get; init; }

    /// <summary>Combined output path (all tables).</summary>
    public string? CombinedOutputPath { get; init; }
}
