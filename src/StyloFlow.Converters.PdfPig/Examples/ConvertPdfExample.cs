namespace StyloFlow.Converters.PdfPig.Examples;

/// <summary>
/// Example usage of PdfToMarkdown utility.
/// </summary>
public static class ConvertPdfExample
{
    /// <summary>
    /// Simple conversion - just call and get markdown with images.
    /// </summary>
    public static void SimpleConversion()
    {
        // Convert PDF to markdown - images extracted to {pdfname}_images folder
        var result = PdfToMarkdown.ConvertAndSave(
            pdfPath: @"C:\path\to\document.pdf",
            outputPath: @"C:\path\to\output.md");

        Console.WriteLine($"Converted {result.PageCount} pages");
        Console.WriteLine($"Extracted {result.ExtractedImages.Count} images");
        Console.WriteLine($"Output: {result.OutputPath}");
    }

    /// <summary>
    /// Conversion with custom options.
    /// </summary>
    public static void CustomConversion()
    {
        var options = new PdfToMarkdownOptions
        {
            // Layout analysis settings
            PageSegmenter = PageSegmenterType.RecursiveXYCut,  // Good for multi-column
            ReadingOrderDetector = ReadingOrderType.Unsupervised,
            UseNearestNeighbourWordExtractor = true,

            // Content options
            IncludeMetadata = true,      // YAML frontmatter
            IncludeBookmarks = true,     // Table of contents
            IncludePageMarkers = true,   // ## Page N headers
            DetectHeaders = true,        // Convert large text to ## headers
            FilterDecorations = true,    // Remove headers/footers/page numbers

            // Image handling
            ExtractImages = true,
            EmbedImageReferences = true  // ![Image](path/to/image.png)
        };

        var result = PdfToMarkdown.Convert(
            pdfPath: @"C:\path\to\document.pdf",
            outputDir: @"C:\output\folder",
            options: options);

        // Access the markdown content directly
        Console.WriteLine(result.Markdown);

        // Or iterate through pages
        foreach (var page in result.Pages)
        {
            Console.WriteLine($"Page {page.PageNumber}: {page.WordCount} words, {page.ImageCount} images");
        }
    }

    /// <summary>
    /// Minimal conversion - text only, no images.
    /// </summary>
    public static string TextOnly(string pdfPath)
    {
        var result = PdfToMarkdown.Convert(pdfPath, options: new PdfToMarkdownOptions
        {
            ExtractImages = false,
            EmbedImageReferences = false,
            IncludeMetadata = false,
            IncludePageMarkers = false
        });

        return result.Markdown;
    }
}
