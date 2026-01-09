namespace StyloFlow.Converters.OpenXml;

/// <summary>
/// Configuration for OpenXml document converter.
/// </summary>
public class OpenXmlConfig
{
    /// <summary>
    /// Extract images from documents.
    /// </summary>
    public bool ExtractImages { get; set; } = true;

    /// <summary>
    /// Extract document metadata (title, author, etc.).
    /// </summary>
    public bool ExtractMetadata { get; set; } = true;

    /// <summary>
    /// Convert tables to markdown format.
    /// </summary>
    public bool ConvertTables { get; set; } = true;

    /// <summary>
    /// Preserve heading hierarchy (H1-H6).
    /// </summary>
    public bool PreserveHeadings { get; set; } = true;

    /// <summary>
    /// Convert numbered lists to markdown.
    /// </summary>
    public bool ConvertLists { get; set; } = true;

    /// <summary>
    /// Include comments as annotations.
    /// </summary>
    public bool IncludeComments { get; set; } = false;

    /// <summary>
    /// Include headers and footers.
    /// </summary>
    public bool IncludeHeadersFooters { get; set; } = false;

    /// <summary>
    /// Include document properties in output.
    /// </summary>
    public bool IncludeProperties { get; set; } = true;

    /// <summary>
    /// Maximum image size to extract (0 = unlimited).
    /// </summary>
    public int MaxImageSizeBytes { get; set; } = 0;
}
