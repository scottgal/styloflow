namespace StyloFlow.Converters.PdfPig;

/// <summary>
/// Result of extracting a single page.
/// </summary>
public class PageExtractionResult
{
    public int PageNumber { get; set; }
    public double Width { get; set; }
    public double Height { get; set; }
    public string? RawText { get; set; }
    public string? Markdown { get; set; }
    public int WordCount { get; set; }
    public ExtractionQuality Quality { get; set; }
    public bool HasImages { get; set; }
    public List<WordBox>? WordBoxes { get; set; }
    public List<TextBlock>? TextBlocks { get; set; }
}

/// <summary>
/// Word with bounding box for OCR comparison/reprocessing.
/// </summary>
public class WordBox
{
    public required string Text { get; set; }
    public double X { get; set; }
    public double Y { get; set; }
    public double Width { get; set; }
    public double Height { get; set; }
    public double FontSize { get; set; }
    public double? Confidence { get; set; }
    public bool NeedsReocr { get; set; }
}

/// <summary>
/// Detected text block with reading order.
/// </summary>
public class TextBlock
{
    public int BlockIndex { get; set; }
    public int ReadingOrder { get; set; }
    public double X { get; set; }
    public double Y { get; set; }
    public double Width { get; set; }
    public double Height { get; set; }
    public string? Text { get; set; }
    public BlockType Type { get; set; }
    public List<TextLine>? Lines { get; set; }
}

/// <summary>
/// Text line within a block.
/// </summary>
public class TextLine
{
    public string? Text { get; set; }
    public double X { get; set; }
    public double Y { get; set; }
    public double Width { get; set; }
    public double Height { get; set; }
    public List<WordBox>? Words { get; set; }
}

/// <summary>
/// Type of text block.
/// </summary>
public enum BlockType
{
    Unknown,
    Paragraph,
    Header,
    Footer,
    PageNumber,
    Caption,
    Table,
    List,
    Title
}

/// <summary>
/// Quality of text extraction.
/// </summary>
public enum ExtractionQuality
{
    /// <summary>
    /// Unknown quality.
    /// </summary>
    Unknown,

    /// <summary>
    /// High quality - clean text extraction.
    /// </summary>
    High,

    /// <summary>
    /// Medium quality - mostly readable.
    /// </summary>
    Medium,

    /// <summary>
    /// Low quality - significant issues.
    /// </summary>
    Low,

    /// <summary>
    /// Garbage - garbled text, needs OCR.
    /// </summary>
    Garbage,

    /// <summary>
    /// Empty - no text extracted.
    /// </summary>
    Empty
}

/// <summary>
/// Document structure extracted from PDF.
/// </summary>
public class DocumentStructure
{
    public string? Title { get; set; }
    public string? Author { get; set; }
    public string? Subject { get; set; }
    public DateTime? CreationDate { get; set; }
    public int PageCount { get; set; }
    public List<BookmarkEntry>? Bookmarks { get; set; }
    public List<PageStructure>? Pages { get; set; }
}

/// <summary>
/// Bookmark/outline entry.
/// </summary>
public class BookmarkEntry
{
    public required string Title { get; set; }
    public int? PageNumber { get; set; }
    public int Level { get; set; }
    public List<BookmarkEntry>? Children { get; set; }
}

/// <summary>
/// Structure of a single page.
/// </summary>
public class PageStructure
{
    public int PageNumber { get; set; }
    public double Width { get; set; }
    public double Height { get; set; }
    public int BlockCount { get; set; }
    public int ImageCount { get; set; }
    public ExtractionQuality Quality { get; set; }
    public int Rotation { get; set; }
    public int WordCount { get; set; }
    public List<string>? FontNames { get; set; }
    public bool HasAnnotations { get; set; }
}

/// <summary>
/// Complete PDF document metadata extracted by PdfPig.
/// </summary>
public class PdfDocumentMetadata
{
    // Document Information Dictionary fields
    public string? Title { get; set; }
    public string? Author { get; set; }
    public string? Subject { get; set; }
    public string? Keywords { get; set; }
    public string? Creator { get; set; }
    public string? Producer { get; set; }
    public string? CreationDate { get; set; }
    public string? ModifiedDate { get; set; }

    // Document structure
    public int PageCount { get; set; }
    public string? Version { get; set; }
    public bool IsEncrypted { get; set; }
    public bool IsLinearized { get; set; }

    // First page dimensions (reference)
    public double PageWidth { get; set; }
    public double PageHeight { get; set; }
    public int PageRotation { get; set; }

    // Aggregated stats
    public int TotalWords { get; set; }
    public int TotalImages { get; set; }
    public int TotalAnnotations { get; set; }
    public bool HasBookmarks { get; set; }
    public int BookmarkCount { get; set; }

    // Font information
    public List<FontInfo>? Fonts { get; set; }

    // Per-page metadata
    public List<PageMetadata>? Pages { get; set; }
}

/// <summary>
/// Font information extracted from PDF.
/// </summary>
public class FontInfo
{
    public required string Name { get; set; }
    public string? Family { get; set; }
    public bool IsEmbedded { get; set; }
    public bool IsSubset { get; set; }
    public string? Encoding { get; set; }
    public int UsageCount { get; set; }
}

/// <summary>
/// Per-page metadata.
/// </summary>
public class PageMetadata
{
    public int PageNumber { get; set; }
    public double Width { get; set; }
    public double Height { get; set; }
    public int Rotation { get; set; }
    public int WordCount { get; set; }
    public int ImageCount { get; set; }
    public int AnnotationCount { get; set; }
    public ExtractionQuality Quality { get; set; }
    public List<string>? FontNames { get; set; }
}
