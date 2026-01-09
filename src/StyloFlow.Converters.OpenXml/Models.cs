namespace StyloFlow.Converters.OpenXml;

/// <summary>
/// Block types in Word documents (matching PdfPig structure).
/// </summary>
public enum DocxBlockType
{
    Unknown,
    Paragraph,
    Heading,
    Title,
    Table,
    List,
    ListItem,
    Image,
    Header,
    Footer,
    Footnote,
    Caption
}

/// <summary>
/// A block of content with reading order (consistent with PdfPig output).
/// </summary>
public class DocumentBlock
{
    public int ReadingOrder { get; set; }
    public DocxBlockType BlockType { get; set; }
    public required string Text { get; set; }
    public int ParagraphIndex { get; set; }
    public int SectionIndex { get; set; }
    public int? HeadingLevel { get; set; }
    public int? ListLevel { get; set; }
    public bool IsNumberedList { get; set; }
    public int WordCount { get; set; }
    public string? StyleId { get; set; }

    /// <summary>
    /// Position within section (0-1 normalized).
    /// Useful for header/footer detection.
    /// </summary>
    public double? RelativePosition { get; set; }
}

/// <summary>
/// Extraction quality assessment (matching PdfPig).
/// </summary>
public enum ExtractionQuality
{
    Unknown,
    High,
    Medium,
    Low,
    Garbage,
    Empty
}

/// <summary>
/// Structured extraction result (matching PdfPig output format).
/// </summary>
public class DocxExtractionResult
{
    public required string Text { get; set; }
    public required string Markdown { get; set; }
    public required IReadOnlyList<DocumentBlock> Blocks { get; set; }
    public required DocumentStructure Structure { get; set; }
    public ExtractionQuality Quality { get; set; }
    public int TotalWords { get; set; }
    public int TotalBlocks { get; set; }
}

/// <summary>
/// Table cell information.
/// </summary>
public class TableCellInfo
{
    public int Row { get; set; }
    public int Column { get; set; }
    public required string Text { get; set; }
    public int? ColSpan { get; set; }
    public int? RowSpan { get; set; }
}

/// <summary>
/// Table structure information.
/// </summary>
public class TableInfo
{
    public int ReadingOrder { get; set; }
    public int Rows { get; set; }
    public int Columns { get; set; }
    public required string Markdown { get; set; }
    public required IReadOnlyList<TableCellInfo> Cells { get; set; }
}

/// <summary>
/// Image information with position context.
/// </summary>
public class ImageInfo
{
    public int ReadingOrder { get; set; }
    public int ParagraphIndex { get; set; }
    public required string RelationshipId { get; set; }
    public required string ContentType { get; set; }
    public long? SizeBytes { get; set; }
    public string? AltText { get; set; }

    /// <summary>
    /// Width in EMUs (English Metric Units). Divide by 914400 for inches.
    /// </summary>
    public long? WidthEmu { get; set; }

    /// <summary>
    /// Height in EMUs. Divide by 914400 for inches.
    /// </summary>
    public long? HeightEmu { get; set; }

    /// <summary>
    /// Width in pixels (approximate).
    /// </summary>
    public int? WidthPixels => WidthEmu.HasValue ? (int)(WidthEmu.Value / 9525.0) : null;

    /// <summary>
    /// Height in pixels (approximate).
    /// </summary>
    public int? HeightPixels => HeightEmu.HasValue ? (int)(HeightEmu.Value / 9525.0) : null;
}
