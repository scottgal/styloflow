using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Rendering;
using Microsoft.Extensions.Logging;

namespace StyloFlow.Converters.PdfPig;

/// <summary>
/// Detects scanned PDFs and extracts pages as images for OCR processing.
/// Enables the flow: DocSummarizer → ScannedDetected → ImageSummarizer → OCR → DocSummarizer
/// </summary>
public class ScannedPdfDetector
{
    private readonly ILogger<ScannedPdfDetector>? _logger;

    public ScannedPdfDetector(ILogger<ScannedPdfDetector>? logger = null)
    {
        _logger = logger;
    }

    /// <summary>
    /// Analyze a PDF to determine if it's scanned (image-based) or has searchable text.
    /// </summary>
    public PdfAnalysisResult Analyze(string pdfPath)
    {
        using var document = PdfDocument.Open(pdfPath);
        return AnalyzeDocument(document, pdfPath);
    }

    /// <summary>
    /// Analyze a PDF to determine if it's scanned (image-based) or has searchable text.
    /// </summary>
    public PdfAnalysisResult Analyze(PdfDocument document, string sourcePath = "")
    {
        return AnalyzeDocument(document, sourcePath);
    }

    private PdfAnalysisResult AnalyzeDocument(PdfDocument document, string sourcePath)
    {
        var pageAnalyses = new List<PageAnalysis>();
        var totalWords = 0;
        var totalImages = 0;
        var scannedPageCount = 0;
        var textPageCount = 0;

        for (var pageNum = 1; pageNum <= document.NumberOfPages; pageNum++)
        {
            var page = document.GetPage(pageNum);
            var analysis = AnalyzePage(page, pageNum);
            pageAnalyses.Add(analysis);

            totalWords += analysis.WordCount;
            totalImages += analysis.ImageCount;

            if (analysis.IsLikelyScanned)
                scannedPageCount++;
            else
                textPageCount++;
        }

        // Determine document type
        var documentType = DetermineDocumentType(scannedPageCount, textPageCount, totalWords, totalImages);

        return new PdfAnalysisResult
        {
            SourcePath = sourcePath,
            PageCount = document.NumberOfPages,
            TotalWords = totalWords,
            TotalImages = totalImages,
            ScannedPageCount = scannedPageCount,
            TextPageCount = textPageCount,
            DocumentType = documentType,
            NeedsOcr = documentType == PdfDocumentType.Scanned || documentType == PdfDocumentType.Mixed,
            Pages = pageAnalyses,
            Confidence = CalculateConfidence(pageAnalyses)
        };
    }

    private PageAnalysis AnalyzePage(Page page, int pageNum)
    {
        var words = page.GetWords().ToList();
        var images = page.GetImages().ToList();
        var letters = page.Letters.Count;

        // Calculate text density (letters per page area)
        var pageArea = page.Width * page.Height;
        var textDensity = pageArea > 0 ? letters / pageArea : 0;

        // Check if there's a full-page image (common in scanned docs)
        var hasFullPageImage = false;
        double largestImageCoverage = 0;

        foreach (var image in images)
        {
            try
            {
                var imageBounds = image.Bounds;
                var imageArea = imageBounds.Width * imageBounds.Height;
                var coverage = imageArea / pageArea;

                if (coverage > largestImageCoverage)
                    largestImageCoverage = coverage;

                if (coverage > 0.8) // Image covers >80% of page
                    hasFullPageImage = true;
            }
            catch { /* Some images don't have bounds */ }
        }

        // Assess text quality
        var textQuality = AssessTextQuality(page.Text, words.Count);

        // Determine if page is likely scanned
        var isLikelyScanned = DetermineIfScanned(
            wordCount: words.Count,
            imageCount: images.Count,
            hasFullPageImage: hasFullPageImage,
            largestImageCoverage: largestImageCoverage,
            textDensity: textDensity,
            textQuality: textQuality
        );

        return new PageAnalysis
        {
            PageNumber = pageNum,
            WordCount = words.Count,
            LetterCount = letters,
            ImageCount = images.Count,
            Width = page.Width,
            Height = page.Height,
            TextDensity = textDensity,
            HasFullPageImage = hasFullPageImage,
            LargestImageCoverage = largestImageCoverage,
            TextQuality = textQuality,
            IsLikelyScanned = isLikelyScanned,
            ScanConfidence = CalculatePageScanConfidence(isLikelyScanned, words.Count, hasFullPageImage, largestImageCoverage)
        };
    }

    private bool DetermineIfScanned(
        int wordCount,
        int imageCount,
        bool hasFullPageImage,
        double largestImageCoverage,
        double textDensity,
        ExtractionQuality textQuality)
    {
        // Clear indicators of a scanned page:
        // 1. Full-page image with very few/no words
        if (hasFullPageImage && wordCount < 20)
            return true;

        // 2. Large image coverage (>50%) with low text quality
        if (largestImageCoverage > 0.5 && (textQuality == ExtractionQuality.Garbage || textQuality == ExtractionQuality.Empty))
            return true;

        // 3. Has images but no extractable text
        if (imageCount > 0 && wordCount == 0)
            return true;

        // 4. Very low text density with images
        if (imageCount > 0 && textDensity < 0.001 && wordCount < 50)
            return true;

        return false;
    }

    private PdfDocumentType DetermineDocumentType(int scannedPages, int textPages, int totalWords, int totalImages)
    {
        var totalPages = scannedPages + textPages;
        if (totalPages == 0)
            return PdfDocumentType.Empty;

        var scannedRatio = (double)scannedPages / totalPages;

        if (scannedRatio > 0.9)
            return PdfDocumentType.Scanned;

        if (scannedRatio < 0.1)
            return PdfDocumentType.Searchable;

        return PdfDocumentType.Mixed;
    }

    private ExtractionQuality AssessTextQuality(string text, int wordCount)
    {
        if (string.IsNullOrWhiteSpace(text) || wordCount == 0)
            return ExtractionQuality.Empty;

        var alphaCount = text.Count(char.IsLetter);
        if (alphaCount < 10)
            return ExtractionQuality.Empty;

        // Check for garbage indicators
        var upperCount = text.Count(char.IsUpper);
        var upperRatio = (double)upperCount / alphaCount;

        var vowelCount = text.Count(c => "aeiouAEIOU".Contains(c));
        var vowelRatio = (double)vowelCount / alphaCount;

        if (upperRatio > 0.4 || vowelRatio < 0.15)
            return ExtractionQuality.Garbage;

        if (upperRatio > 0.25 || vowelRatio < 0.25)
            return ExtractionQuality.Low;

        if (wordCount < 20)
            return ExtractionQuality.Medium;

        return ExtractionQuality.High;
    }

    private double CalculatePageScanConfidence(bool isScanned, int wordCount, bool hasFullPageImage, double imageCoverage)
    {
        if (!isScanned)
            return 0;

        var confidence = 0.5;

        if (hasFullPageImage)
            confidence += 0.3;

        if (wordCount == 0)
            confidence += 0.2;
        else if (wordCount < 10)
            confidence += 0.1;

        if (imageCoverage > 0.9)
            confidence += 0.1;

        return Math.Min(confidence, 1.0);
    }

    private double CalculateConfidence(List<PageAnalysis> pages)
    {
        if (pages.Count == 0)
            return 0;

        var scannedPages = pages.Where(p => p.IsLikelyScanned).ToList();
        if (scannedPages.Count == 0)
            return 1.0; // High confidence it's NOT scanned

        return scannedPages.Average(p => p.ScanConfidence);
    }

    /// <summary>
    /// Extract pages that need OCR as images.
    /// Returns paths to extracted page images.
    /// </summary>
    public async Task<List<ExtractedPageImage>> ExtractPagesForOcrAsync(
        string pdfPath,
        string outputDir,
        PdfAnalysisResult? analysis = null,
        CancellationToken ct = default)
    {
        analysis ??= Analyze(pdfPath);

        if (!analysis.NeedsOcr)
            return [];

        var extractedPages = new List<ExtractedPageImage>();
        Directory.CreateDirectory(outputDir);

        using var document = PdfDocument.Open(pdfPath);
        var pdfName = Path.GetFileNameWithoutExtension(pdfPath);

        foreach (var page in analysis.Pages.Where(p => p.IsLikelyScanned))
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                var pdfPage = document.GetPage(page.PageNumber);
                var imagePath = await ExtractPageAsImageAsync(pdfPage, page.PageNumber, pdfName, outputDir, ct);

                if (imagePath != null)
                {
                    extractedPages.Add(new ExtractedPageImage
                    {
                        PageNumber = page.PageNumber,
                        ImagePath = imagePath,
                        Width = page.Width,
                        Height = page.Height,
                        SourcePdf = pdfPath
                    });
                }
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Failed to extract page {Page} as image", page.PageNumber);
            }
        }

        return extractedPages;
    }

    private async Task<string?> ExtractPageAsImageAsync(
        Page page,
        int pageNum,
        string pdfName,
        string outputDir,
        CancellationToken ct)
    {
        // Try to extract the full-page image directly
        var images = page.GetImages().ToList();

        foreach (var image in images)
        {
            try
            {
                // Check if this image covers most of the page
                var imageBounds = image.Bounds;
                var coverage = (imageBounds.Width * imageBounds.Height) / (page.Width * page.Height);

                if (coverage > 0.7) // Large enough to be the page content
                {
                    if (image.TryGetPng(out var pngBytes) && pngBytes != null && pngBytes.Length > 0)
                    {
                        var path = Path.Combine(outputDir, $"{pdfName}_page{pageNum}.png");
                        await File.WriteAllBytesAsync(path, pngBytes, ct);
                        return path;
                    }

                    var rawBytes = image.RawBytes.ToArray();
                    if (rawBytes.Length > 0)
                    {
                        var ext = DetectImageFormat(rawBytes);
                        var path = Path.Combine(outputDir, $"{pdfName}_page{pageNum}.{ext}");
                        await File.WriteAllBytesAsync(path, rawBytes, ct);
                        return path;
                    }
                }
            }
            catch { /* Try next image */ }
        }

        // If no suitable image found, we'd need a PDF renderer
        // For now, return null - the calling code can use a PDF renderer like SkiaSharp
        _logger?.LogDebug("No extractable page image for page {Page}, PDF rendering required", pageNum);
        return null;
    }

    private static string DetectImageFormat(byte[] bytes)
    {
        if (bytes.Length > 2)
        {
            if (bytes[0] == 0xFF && bytes[1] == 0xD8)
                return "jpg";
            if (bytes[0] == 0x89 && bytes[1] == 0x50)
                return "png";
            if (bytes[0] == 0x47 && bytes[1] == 0x49)
                return "gif";
        }
        return "bin";
    }
}

/// <summary>
/// Result of analyzing a PDF for scanned content.
/// </summary>
public class PdfAnalysisResult
{
    public string SourcePath { get; init; } = "";
    public int PageCount { get; init; }
    public int TotalWords { get; init; }
    public int TotalImages { get; init; }
    public int ScannedPageCount { get; init; }
    public int TextPageCount { get; init; }
    public PdfDocumentType DocumentType { get; init; }
    public bool NeedsOcr { get; init; }
    public double Confidence { get; init; }
    public required IReadOnlyList<PageAnalysis> Pages { get; init; }
}

/// <summary>
/// Analysis of a single page.
/// </summary>
public class PageAnalysis
{
    public int PageNumber { get; init; }
    public int WordCount { get; init; }
    public int LetterCount { get; init; }
    public int ImageCount { get; init; }
    public double Width { get; init; }
    public double Height { get; init; }
    public double TextDensity { get; init; }
    public bool HasFullPageImage { get; init; }
    public double LargestImageCoverage { get; init; }
    public ExtractionQuality TextQuality { get; init; }
    public bool IsLikelyScanned { get; init; }
    public double ScanConfidence { get; init; }
}

/// <summary>
/// Type of PDF document.
/// </summary>
public enum PdfDocumentType
{
    /// <summary>
    /// Empty or invalid PDF.
    /// </summary>
    Empty,

    /// <summary>
    /// Searchable PDF with extractable text.
    /// </summary>
    Searchable,

    /// <summary>
    /// Scanned PDF - images of pages, needs OCR.
    /// </summary>
    Scanned,

    /// <summary>
    /// Mixed - some pages searchable, some scanned.
    /// </summary>
    Mixed
}

/// <summary>
/// Extracted page image for OCR processing.
/// </summary>
public class ExtractedPageImage
{
    public int PageNumber { get; init; }
    public required string ImagePath { get; init; }
    public double Width { get; init; }
    public double Height { get; init; }
    public required string SourcePdf { get; init; }
}
