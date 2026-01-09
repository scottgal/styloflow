using System.Text;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.DocumentLayoutAnalysis.PageSegmenter;
using UglyToad.PdfPig.DocumentLayoutAnalysis.ReadingOrderDetector;
using UglyToad.PdfPig.DocumentLayoutAnalysis.WordExtractor;
using PdfTextBlock = UglyToad.PdfPig.DocumentLayoutAnalysis.TextBlock;

namespace StyloFlow.Converters.PdfPig;

/// <summary>
/// Simple utility for PDF to Markdown conversion.
/// Standalone alternative to Docling - no external services required.
/// </summary>
public static class PdfToMarkdown
{
    /// <summary>
    /// Convert a PDF file to Markdown with embedded images.
    /// </summary>
    /// <param name="pdfPath">Path to the PDF file.</param>
    /// <param name="outputDir">Optional output directory for images. If null, uses same directory as PDF.</param>
    /// <param name="options">Conversion options.</param>
    /// <returns>Markdown content and list of extracted image paths.</returns>
    public static PdfMarkdownResult Convert(string pdfPath, string? outputDir = null, PdfToMarkdownOptions? options = null)
    {
        options ??= new PdfToMarkdownOptions();
        outputDir ??= Path.GetDirectoryName(pdfPath) ?? ".";

        var pdfName = Path.GetFileNameWithoutExtension(pdfPath);
        var imagesDir = Path.Combine(outputDir, $"{pdfName}_images");

        var markdown = new StringBuilder();
        var extractedImages = new List<string>();
        var pageMetadata = new List<PageInfo>();

        using var document = PdfDocument.Open(pdfPath);

        // Document metadata
        if (options.IncludeMetadata)
        {
            AppendMetadata(markdown, document);
        }

        // Bookmarks / Table of Contents
        if (options.IncludeBookmarks && document.TryGetBookmarks(out var bookmarks) && bookmarks.Roots.Any())
        {
            AppendBookmarks(markdown, bookmarks);
        }

        // Process pages
        for (var pageNum = 1; pageNum <= document.NumberOfPages; pageNum++)
        {
            var page = document.GetPage(pageNum);

            if (options.IncludePageMarkers)
            {
                markdown.AppendLine($"\n## Page {pageNum}\n");
            }

            // Extract text with layout analysis
            var pageText = ExtractPageText(page, options);
            markdown.AppendLine(pageText);

            // Extract images
            if (options.ExtractImages)
            {
                var images = page.GetImages().ToList();
                var imageIndex = 0;

                foreach (var image in images)
                {
                    try
                    {
                        var imagePath = ExtractImage(image, imagesDir, pdfName, pageNum, imageIndex++);
                        if (imagePath != null)
                        {
                            extractedImages.Add(imagePath);

                            if (options.EmbedImageReferences)
                            {
                                var relativePath = Path.GetRelativePath(outputDir, imagePath);
                                markdown.AppendLine($"\n![Image {pageNum}-{imageIndex}]({relativePath.Replace("\\", "/")})\n");
                            }
                        }
                    }
                    catch { /* Skip failed images */ }
                }
            }

            // Page separator
            if (pageNum < document.NumberOfPages)
            {
                markdown.AppendLine("\n---\n");
            }

            pageMetadata.Add(new PageInfo
            {
                PageNumber = pageNum,
                Width = page.Width,
                Height = page.Height,
                WordCount = page.GetWords().Count(),
                ImageCount = page.GetImages().Count()
            });
        }

        return new PdfMarkdownResult
        {
            Markdown = markdown.ToString(),
            ExtractedImages = extractedImages,
            Pages = pageMetadata,
            Title = document.Information?.Title,
            Author = document.Information?.Author,
            PageCount = document.NumberOfPages
        };
    }

    /// <summary>
    /// Convert and save to file.
    /// </summary>
    public static PdfMarkdownResult ConvertAndSave(string pdfPath, string? outputPath = null, PdfToMarkdownOptions? options = null)
    {
        var result = Convert(pdfPath, Path.GetDirectoryName(outputPath ?? pdfPath), options);

        outputPath ??= Path.ChangeExtension(pdfPath, ".md");
        File.WriteAllText(outputPath, result.Markdown);
        result.OutputPath = outputPath;

        return result;
    }

    private static string ExtractPageText(Page page, PdfToMarkdownOptions options)
    {
        var words = options.UseNearestNeighbourWordExtractor
            ? page.GetWords(NearestNeighbourWordExtractor.Instance).ToList()
            : page.GetWords().ToList();

        if (words.Count == 0)
            return "";

        // Apply layout analysis
        IReadOnlyList<PdfTextBlock> blocks = options.PageSegmenter switch
        {
            PageSegmenterType.RecursiveXYCut => RecursiveXYCut.Instance.GetBlocks(words),
            PageSegmenterType.DocstrumBoundingBoxes => DocstrumBoundingBoxes.Instance.GetBlocks(words),
            _ => DefaultPageSegmenter.Instance.GetBlocks(words)
        };

        // Apply reading order detection
        IEnumerable<PdfTextBlock> orderedBlocks = options.ReadingOrderDetector switch
        {
            ReadingOrderType.Unsupervised => UnsupervisedReadingOrderDetector.Instance.Get(blocks),
            ReadingOrderType.RenderingBased => RenderingReadingOrderDetector.Instance.Get(blocks),
            _ => blocks
        };

        // Filter decorations
        if (options.FilterDecorations)
        {
            orderedBlocks = FilterDecorations(orderedBlocks.ToList(), page);
        }

        var sb = new StringBuilder();
        var avgFontSize = words.Count > 0
            ? words.Average(w => w.Letters.FirstOrDefault()?.PointSize ?? 12)
            : 12;

        foreach (var block in orderedBlocks)
        {
            var blockText = string.Join(" ", block.TextLines.SelectMany(l => l.Words).Select(w => w.Text));
            if (string.IsNullOrWhiteSpace(blockText))
                continue;

            // Detect headers by font size
            if (options.DetectHeaders)
            {
                var blockWords = block.TextLines.SelectMany(l => l.Words).ToList();
                var blockFontSize = blockWords.Count > 0
                    ? blockWords.Average(w => w.Letters.FirstOrDefault()?.PointSize ?? 12)
                    : avgFontSize;

                if (blockFontSize > avgFontSize * 1.5 && blockText.Length < 150)
                {
                    sb.AppendLine($"## {blockText.Trim()}");
                    sb.AppendLine();
                    continue;
                }
                else if (blockFontSize > avgFontSize * 1.25 && blockText.Length < 100)
                {
                    sb.AppendLine($"### {blockText.Trim()}");
                    sb.AppendLine();
                    continue;
                }
            }

            sb.AppendLine(blockText.Trim());
            sb.AppendLine();
        }

        return sb.ToString();
    }

    private static IEnumerable<PdfTextBlock> FilterDecorations(IReadOnlyList<PdfTextBlock> blocks, Page page)
    {
        var pageHeight = page.Height;
        var pageWidth = page.Width;

        return blocks.Where(block =>
        {
            var bbox = block.BoundingBox;

            // Skip very small blocks at top/bottom
            if (bbox.Height < pageHeight * 0.02)
            {
                if (bbox.Bottom > pageHeight * 0.95 || bbox.Top < pageHeight * 0.05)
                    return false;
            }

            // Skip narrow blocks at extreme top/bottom
            if (bbox.Width < pageWidth * 0.3)
            {
                if (bbox.Bottom > pageHeight * 0.9 || bbox.Top < pageHeight * 0.1)
                    return false;
            }

            return true;
        });
    }

    private static string? ExtractImage(IPdfImage image, string imagesDir, string pdfName, int pageNum, int imageIndex)
    {
        byte[]? imageBytes = null;
        string extension = "bin";

        if (image.TryGetPng(out var pngBytes) && pngBytes != null && pngBytes.Length > 0)
        {
            imageBytes = pngBytes;
            extension = "png";
        }
        else
        {
            try
            {
                var rawBytes = image.RawBytes.ToArray();
                if (rawBytes.Length > 0)
                {
                    imageBytes = rawBytes;
                    // Try to detect format
                    if (rawBytes.Length > 2)
                    {
                        if (rawBytes[0] == 0xFF && rawBytes[1] == 0xD8)
                            extension = "jpg";
                        else if (rawBytes[0] == 0x89 && rawBytes[1] == 0x50)
                            extension = "png";
                    }
                }
            }
            catch
            {
                return null;
            }
        }

        if (imageBytes == null || imageBytes.Length == 0)
            return null;

        // Ensure directory exists
        Directory.CreateDirectory(imagesDir);

        var imagePath = Path.Combine(imagesDir, $"{pdfName}_p{pageNum}_img{imageIndex}.{extension}");
        File.WriteAllBytes(imagePath, imageBytes);

        return imagePath;
    }

    private static void AppendMetadata(StringBuilder markdown, PdfDocument document)
    {
        var info = document.Information;

        markdown.AppendLine("---");

        if (!string.IsNullOrEmpty(info?.Title))
            markdown.AppendLine($"title: \"{EscapeYaml(info.Title)}\"");
        if (!string.IsNullOrEmpty(info?.Author))
            markdown.AppendLine($"author: \"{EscapeYaml(info.Author)}\"");
        if (!string.IsNullOrEmpty(info?.Subject))
            markdown.AppendLine($"subject: \"{EscapeYaml(info.Subject)}\"");
        if (!string.IsNullOrEmpty(info?.Keywords))
            markdown.AppendLine($"keywords: \"{EscapeYaml(info.Keywords)}\"");
        if (!string.IsNullOrEmpty(info?.Creator))
            markdown.AppendLine($"creator: \"{EscapeYaml(info.Creator)}\"");
        if (!string.IsNullOrEmpty(info?.Producer))
            markdown.AppendLine($"producer: \"{EscapeYaml(info.Producer)}\"");
        if (!string.IsNullOrEmpty(info?.CreationDate))
            markdown.AppendLine($"created: \"{info.CreationDate}\"");
        if (!string.IsNullOrEmpty(info?.ModifiedDate))
            markdown.AppendLine($"modified: \"{info.ModifiedDate}\"");

        markdown.AppendLine($"pages: {document.NumberOfPages}");
        markdown.AppendLine($"pdf_version: \"{document.Version}\"");
        markdown.AppendLine("---\n");
    }

    private static void AppendBookmarks(StringBuilder markdown, UglyToad.PdfPig.Outline.Bookmarks bookmarks)
    {
        markdown.AppendLine("## Table of Contents\n");

        foreach (var bookmark in bookmarks.Roots)
        {
            AppendBookmark(markdown, bookmark, 0);
        }

        markdown.AppendLine();
    }

    private static void AppendBookmark(StringBuilder markdown, UglyToad.PdfPig.Outline.BookmarkNode bookmark, int level)
    {
        var indent = new string(' ', level * 2);
        markdown.AppendLine($"{indent}- {bookmark.Title}");

        foreach (var child in bookmark.Children)
        {
            AppendBookmark(markdown, child, level + 1);
        }
    }

    private static string EscapeYaml(string value) =>
        value.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r");
}

/// <summary>
/// Options for PDF to Markdown conversion.
/// </summary>
public class PdfToMarkdownOptions
{
    /// <summary>
    /// Include document metadata in YAML frontmatter.
    /// </summary>
    public bool IncludeMetadata { get; set; } = true;

    /// <summary>
    /// Include bookmarks/table of contents.
    /// </summary>
    public bool IncludeBookmarks { get; set; } = true;

    /// <summary>
    /// Include page number markers.
    /// </summary>
    public bool IncludePageMarkers { get; set; } = true;

    /// <summary>
    /// Extract images to files.
    /// </summary>
    public bool ExtractImages { get; set; } = true;

    /// <summary>
    /// Embed image references in markdown.
    /// </summary>
    public bool EmbedImageReferences { get; set; } = true;

    /// <summary>
    /// Use nearest neighbour word extractor (better for complex layouts).
    /// </summary>
    public bool UseNearestNeighbourWordExtractor { get; set; } = true;

    /// <summary>
    /// Page segmentation algorithm.
    /// </summary>
    public PageSegmenterType PageSegmenter { get; set; } = PageSegmenterType.DocstrumBoundingBoxes;

    /// <summary>
    /// Reading order detection algorithm.
    /// </summary>
    public ReadingOrderType ReadingOrderDetector { get; set; } = ReadingOrderType.Unsupervised;

    /// <summary>
    /// Filter headers/footers/page numbers.
    /// </summary>
    public bool FilterDecorations { get; set; } = true;

    /// <summary>
    /// Detect headers by font size.
    /// </summary>
    public bool DetectHeaders { get; set; } = true;
}

/// <summary>
/// Result of PDF to Markdown conversion.
/// </summary>
public class PdfMarkdownResult
{
    /// <summary>
    /// The generated markdown content.
    /// </summary>
    public required string Markdown { get; init; }

    /// <summary>
    /// Paths to extracted images.
    /// </summary>
    public required IReadOnlyList<string> ExtractedImages { get; init; }

    /// <summary>
    /// Per-page information.
    /// </summary>
    public required IReadOnlyList<PageInfo> Pages { get; init; }

    /// <summary>
    /// Document title (if available).
    /// </summary>
    public string? Title { get; init; }

    /// <summary>
    /// Document author (if available).
    /// </summary>
    public string? Author { get; init; }

    /// <summary>
    /// Total page count.
    /// </summary>
    public int PageCount { get; init; }

    /// <summary>
    /// Output file path (if saved).
    /// </summary>
    public string? OutputPath { get; set; }
}

/// <summary>
/// Information about a single page.
/// </summary>
public class PageInfo
{
    public int PageNumber { get; init; }
    public double Width { get; init; }
    public double Height { get; init; }
    public int WordCount { get; init; }
    public int ImageCount { get; init; }
}
