using System.Diagnostics;
using System.Text;
using Microsoft.Extensions.Logging;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.DocumentLayoutAnalysis.PageSegmenter;
using UglyToad.PdfPig.DocumentLayoutAnalysis.ReadingOrderDetector;
using UglyToad.PdfPig.DocumentLayoutAnalysis.WordExtractor;
using PdfTextBlock = UglyToad.PdfPig.DocumentLayoutAnalysis.TextBlock;

namespace StyloFlow.Converters.PdfPig;

/// <summary>
/// PDF converter using PdfPig library.
/// Extracts text with layout analysis, images, and document structure.
/// Works without external services - pure .NET implementation.
/// </summary>
public class PdfPigConverter : IContentConverter
{
    private readonly PdfPigConfig _config;
    private readonly ISharedStorage _storage;
    private readonly ILogger<PdfPigConverter>? _logger;

    public PdfPigConverter(
        PdfPigConfig config,
        ISharedStorage storage,
        ILogger<PdfPigConverter>? logger = null)
    {
        _config = config;
        _storage = storage;
        _logger = logger;
    }

    public string ConverterId => "pdfpig";
    public string DisplayName => "PdfPig PDF Converter";
    public int Priority => 50; // Lower than Docling, but available offline

    public IReadOnlyList<string> SupportedInputTypes => ["application/pdf"];
    public IReadOnlyList<string> SupportedOutputTypes => ["text/markdown", "text/plain"];

    public bool CanConvert(string inputPath, string inputMimeType, string outputFormat)
    {
        return inputMimeType.Equals("application/pdf", StringComparison.OrdinalIgnoreCase) &&
               (outputFormat.Equals("markdown", StringComparison.OrdinalIgnoreCase) ||
                outputFormat.Equals("text", StringComparison.OrdinalIgnoreCase));
    }

    public async Task<ConversionResult> ConvertAsync(
        string inputPath,
        string inputMimeType,
        ConversionOptions options,
        IProgress<ConversionProgress>? progress = null)
    {
        var sw = Stopwatch.StartNew();
        var ct = options.CancellationToken;

        try
        {
            await using var inputHandle = await _storage.GetLocalPathAsync(inputPath, ct);
            var localInputPath = inputHandle.Path;

            if (!File.Exists(localInputPath))
            {
                return ConversionResult.Failure($"Input file not found: {inputPath}", sw.Elapsed);
            }

            progress?.Report(new ConversionProgress
            {
                CurrentStep = "Opening PDF...",
                Elapsed = sw.Elapsed
            });

            using var document = PdfDocument.Open(localInputPath);
            var totalPages = document.NumberOfPages;

            _logger?.LogInformation("Processing PDF with {Pages} pages: {Path}", totalPages, inputPath);

            var markdown = new StringBuilder();
            var extractedImages = new List<ConvertedAsset>();
            var pageQualities = new List<ExtractionQuality>();
            var pageMetadataList = new List<PageMetadata>();
            var allFonts = new Dictionary<string, FontInfo>();
            var totalWords = 0;
            var totalAnnotations = 0;

            // Extract document metadata
            PdfDocumentMetadata? docMetadata = null;
            if (_config.ExtractMetadata)
            {
                docMetadata = AppendMetadata(markdown, document);
            }
            else
            {
                docMetadata = new PdfDocumentMetadata
                {
                    PageCount = totalPages,
                    Version = document.Version.ToString()
                };
            }

            // Extract bookmarks/outline
            var hasBookmarks = false;
            var bookmarkCount = 0;
            if (_config.ExtractBookmarks && document.TryGetBookmarks(out var bookmarks))
            {
                hasBookmarks = bookmarks.Roots.Any();
                bookmarkCount = CountBookmarks(bookmarks);
                AppendBookmarks(markdown, bookmarks);
            }

            docMetadata.HasBookmarks = hasBookmarks;
            docMetadata.BookmarkCount = bookmarkCount;

            // Process each page
            for (var pageNum = 1; pageNum <= totalPages; pageNum++)
            {
                ct.ThrowIfCancellationRequested();

                progress?.Report(new ConversionProgress
                {
                    PercentComplete = (double)(pageNum - 1) / totalPages * 100,
                    CurrentStep = $"Processing page {pageNum}/{totalPages}",
                    CurrentPage = pageNum,
                    TotalPages = totalPages,
                    Elapsed = sw.Elapsed
                });

                var page = document.GetPage(pageNum);
                var (pageMarkdown, quality, wordCount) = ExtractPageWithMetadata(page, pageNum);
                pageQualities.Add(quality);
                totalWords += wordCount;

                // Extract page-level metadata
                var pageImages = page.GetImages().ToList();
                var pageFonts = ExtractPageFonts(page, allFonts);
                var pageAnnotations = 0;

                // Try to get annotations count
                try
                {
                    pageAnnotations = page.GetAnnotations().Count();
                    totalAnnotations += pageAnnotations;
                }
                catch { /* Annotations may not be available */ }

                pageMetadataList.Add(new PageMetadata
                {
                    PageNumber = pageNum,
                    Width = page.Width,
                    Height = page.Height,
                    Rotation = (int)page.Rotation.Value,
                    WordCount = wordCount,
                    ImageCount = pageImages.Count,
                    AnnotationCount = pageAnnotations,
                    Quality = quality,
                    FontNames = pageFonts.Count > 0 ? pageFonts : null
                });

                // Add page content to markdown
                if (_config.IncludePageMarkers)
                {
                    markdown.AppendLine($"\n<!-- PAGE:{pageNum} -->\n");
                }

                markdown.AppendLine(pageMarkdown);
                markdown.AppendLine("\n---\n");

                // Extract images if requested
                if (_config.ExtractImages && options.ExtractAssets)
                {
                    foreach (var image in pageImages)
                    {
                        try
                        {
                            var imageAsset = await ExtractImageAsync(image, pageNum, inputPath, ct);
                            if (imageAsset != null)
                            {
                                extractedImages.Add(imageAsset);
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger?.LogWarning(ex, "Failed to extract image from page {Page}", pageNum);
                        }
                    }
                }
            }

            // Finalize document metadata
            docMetadata.TotalWords = totalWords;
            docMetadata.TotalImages = extractedImages.Count;
            docMetadata.TotalAnnotations = totalAnnotations;
            docMetadata.Fonts = allFonts.Values.ToList();
            docMetadata.Pages = pageMetadataList;

            // Store markdown output
            var outputFileName = Path.GetFileNameWithoutExtension(inputPath) + ".md";
            var outputPath = $"converted/{Guid.NewGuid():N}/{outputFileName}";

            var overallQuality = AssessOverallQuality(pageQualities);
            var needsReocr = overallQuality == ExtractionQuality.Garbage || overallQuality == ExtractionQuality.Low;

            var storedContent = await _storage.StoreTextAsync(
                markdown.ToString(),
                outputPath,
                "text/markdown",
                new Dictionary<string, string>
                {
                    ["source"] = inputPath,
                    ["converter"] = ConverterId,
                    ["pages"] = totalPages.ToString(),
                    ["quality"] = overallQuality.ToString(),
                    ["imagesExtracted"] = extractedImages.Count.ToString()
                },
                ct);

            _logger?.LogInformation(
                "Converted PDF to {Output} in {Duration}ms ({Pages} pages, {Images} images, quality: {Quality})",
                storedContent.Path, sw.ElapsedMilliseconds, totalPages, extractedImages.Count, overallQuality);

            return new ConversionResult
            {
                Success = true,
                OutputPath = storedContent.Path,
                OutputMimeType = "text/markdown",
                OutputSizeBytes = storedContent.SizeBytes,
                OutputHash = storedContent.ContentHash,
                Duration = sw.Elapsed,
                ProducerName = ConverterId,
                Assets = extractedImages.Count > 0 ? extractedImages : null,
                Metadata = new Dictionary<string, object>
                {
                    ["pages"] = totalPages,
                    ["quality"] = overallQuality.ToString(),
                    ["needsReocr"] = needsReocr,
                    ["hasImages"] = extractedImages.Count > 0,
                    ["totalWords"] = totalWords,
                    ["totalAnnotations"] = totalAnnotations,
                    ["hasBookmarks"] = hasBookmarks,
                    ["bookmarkCount"] = bookmarkCount,
                    ["fontCount"] = allFonts.Count,
                    ["pdfVersion"] = docMetadata.Version ?? "unknown",
                    ["documentMetadata"] = docMetadata
                }
            };
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "PdfPig conversion failed for {Input}", inputPath);
            return ConversionResult.Failure(ex.Message, sw.Elapsed);
        }
    }

    private (string markdown, ExtractionQuality quality) ExtractPage(Page page, int pageNum)
    {
        var (markdown, quality, _) = ExtractPageWithMetadata(page, pageNum);
        return (markdown, quality);
    }

    private (string markdown, ExtractionQuality quality, int wordCount) ExtractPageWithMetadata(Page page, int pageNum)
    {
        try
        {
            // Get words using configured extraction method
            var words = GetWords(page);

            string text;
            if (_config.UseLayoutAnalysis && words.Count > 0)
            {
                text = ExtractWithLayoutAnalysis(words, page);
            }
            else
            {
                text = page.Text;
            }

            var markdown = ConvertToMarkdown(text, page, words);
            var quality = AssessPageQuality(text, words.Count);

            return (markdown, quality, words.Count);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Error extracting page {Page}", pageNum);
            return ($"<!-- Error extracting page {pageNum}: {ex.Message} -->\n", ExtractionQuality.Empty, 0);
        }
    }

    private List<string> ExtractPageFonts(Page page, Dictionary<string, FontInfo> allFonts)
    {
        var pageFontNames = new List<string>();

        try
        {
            // Extract fonts from letters on the page
            foreach (var letter in page.Letters)
            {
                var fontName = letter.FontName;
                if (string.IsNullOrEmpty(fontName))
                    continue;

                pageFontNames.Add(fontName);

                if (allFonts.TryGetValue(fontName, out var existingFont))
                {
                    existingFont.UsageCount++;
                }
                else
                {
                    allFonts[fontName] = new FontInfo
                    {
                        Name = fontName,
                        IsSubset = fontName.Length > 7 && fontName[6] == '+', // Subset fonts start with 6-char prefix + '+'
                        UsageCount = 1
                    };
                }
            }
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "Failed to extract fonts from page");
        }

        return pageFontNames.Distinct().ToList();
    }

    private static int CountBookmarks(UglyToad.PdfPig.Outline.Bookmarks bookmarks)
    {
        var count = 0;
        foreach (var root in bookmarks.Roots)
        {
            count += CountBookmarkNode(root);
        }
        return count;
    }

    private static int CountBookmarkNode(UglyToad.PdfPig.Outline.BookmarkNode node)
    {
        var count = 1;
        foreach (var child in node.Children)
        {
            count += CountBookmarkNode(child);
        }
        return count;
    }

    private IReadOnlyList<Word> GetWords(Page page)
    {
        return _config.WordExtractor switch
        {
            WordExtractorType.NearestNeighbour => page.GetWords(NearestNeighbourWordExtractor.Instance).ToList(),
            _ => page.GetWords().ToList()
        };
    }

    private string ExtractWithLayoutAnalysis(IReadOnlyList<Word> words, Page page)
    {
        try
        {
            // Get text blocks using configured segmenter
            IReadOnlyList<PdfTextBlock> blocks = _config.PageSegmenter switch
            {
                PageSegmenterType.RecursiveXYCut => RecursiveXYCut.Instance.GetBlocks(words),
                PageSegmenterType.DocstrumBoundingBoxes => DocstrumBoundingBoxes.Instance.GetBlocks(words),
                _ => DefaultPageSegmenter.Instance.GetBlocks(words)
            };

            // Apply reading order detection
            IEnumerable<PdfTextBlock> orderedBlocks = _config.ReadingOrderDetector switch
            {
                ReadingOrderType.Unsupervised => UnsupervisedReadingOrderDetector.Instance.Get(blocks),
                ReadingOrderType.RenderingBased => RenderingReadingOrderDetector.Instance.Get(blocks),
                _ => blocks
            };

            // Filter decorations (headers/footers) if enabled
            if (_config.FilterDecorations)
            {
                orderedBlocks = FilterDecorations(orderedBlocks, page);
            }

            var sb = new StringBuilder();
            foreach (var block in orderedBlocks)
            {
                var blockText = string.Join(" ", block.TextLines.SelectMany(l => l.Words).Select(w => w.Text));
                if (!string.IsNullOrWhiteSpace(blockText))
                {
                    sb.AppendLine(blockText);
                    sb.AppendLine();
                }
            }

            return sb.ToString();
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "Layout analysis failed, falling back to simple extraction");
            return page.Text;
        }
    }

    private IEnumerable<PdfTextBlock> FilterDecorations(IEnumerable<PdfTextBlock> blocks, Page page)
    {
        var pageHeight = page.Height;
        var pageWidth = page.Width;

        return blocks.Where(block =>
        {
            var bbox = block.BoundingBox;

            // Skip very small blocks at top/bottom (likely page numbers)
            if (bbox.Height < pageHeight * 0.02)
            {
                if (bbox.Bottom > pageHeight * 0.95 || bbox.Top < pageHeight * 0.05)
                    return false;
            }

            // Skip narrow blocks at extreme top/bottom (headers/footers)
            if (bbox.Width < pageWidth * 0.3)
            {
                if (bbox.Bottom > pageHeight * 0.9 || bbox.Top < pageHeight * 0.1)
                    return false;
            }

            return true;
        });
    }

    private string ConvertToMarkdown(string text, Page page, IReadOnlyList<Word> words)
    {
        if (string.IsNullOrWhiteSpace(text))
            return "";

        var sb = new StringBuilder();

        // Try to detect headers by font size
        if (_config.DetectHeaders && words.Count > 0)
        {
            var avgFontSize = words.Average(w => w.Letters.FirstOrDefault()?.PointSize ?? 12);
            var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries);

            foreach (var line in lines)
            {
                var trimmed = line.Trim();
                if (string.IsNullOrEmpty(trimmed)) continue;

                // Find words that match this line to get font size
                var lineWords = words.Where(w => trimmed.Contains(w.Text)).ToList();
                var lineFontSize = lineWords.Count > 0
                    ? lineWords.Average(w => w.Letters.FirstOrDefault()?.PointSize ?? 12)
                    : avgFontSize;

                // If significantly larger font, treat as header
                if (lineFontSize > avgFontSize * 1.3 && trimmed.Length < 100)
                {
                    if (lineFontSize > avgFontSize * 1.6)
                        sb.AppendLine($"## {trimmed}");
                    else
                        sb.AppendLine($"### {trimmed}");
                }
                else
                {
                    sb.AppendLine(trimmed);
                }
                sb.AppendLine();
            }
        }
        else
        {
            sb.AppendLine(text);
        }

        return sb.ToString();
    }

    private async Task<ConvertedAsset?> ExtractImageAsync(
        IPdfImage image,
        int pageNum,
        string sourcePath,
        CancellationToken ct)
    {
        try
        {
            if (!image.TryGetPng(out var pngBytes) || pngBytes == null || pngBytes.Length == 0)
            {
                // RawBytes might be a Span, IReadOnlyList, or similar - handle defensively
                try
                {
                    var rawBytes = image.RawBytes.ToArray();
                    if (rawBytes.Length == 0) return null;
                    pngBytes = rawBytes;
                }
                catch
                {
                    return null;
                }
            }

            var imageName = $"page{pageNum}_img{Guid.NewGuid():N}.png";
            var imagePath = $"converted/{Path.GetFileNameWithoutExtension(sourcePath)}/images/{imageName}";

            using var stream = new MemoryStream(pngBytes);
            var stored = await _storage.StoreAsync(stream, imagePath, "image/png", ct: ct);

            return new ConvertedAsset
            {
                Path = stored.Path,
                Name = imageName,
                MimeType = "image/png",
                SizeBytes = stored.SizeBytes,
                SourcePage = pageNum
            };
        }
        catch
        {
            return null;
        }
    }

    private PdfDocumentMetadata AppendMetadata(StringBuilder markdown, PdfDocument document)
    {
        var metadata = new PdfDocumentMetadata
        {
            PageCount = document.NumberOfPages,
            Version = document.Version.ToString()
        };

        var info = document.Information;

        markdown.AppendLine("---");

        // Core document information
        if (info != null)
        {
            if (!string.IsNullOrEmpty(info.Title))
            {
                markdown.AppendLine($"title: \"{EscapeYaml(info.Title)}\"");
                metadata.Title = info.Title;
            }
            if (!string.IsNullOrEmpty(info.Author))
            {
                markdown.AppendLine($"author: \"{EscapeYaml(info.Author)}\"");
                metadata.Author = info.Author;
            }
            if (!string.IsNullOrEmpty(info.Subject))
            {
                markdown.AppendLine($"subject: \"{EscapeYaml(info.Subject)}\"");
                metadata.Subject = info.Subject;
            }
            if (!string.IsNullOrEmpty(info.Keywords))
            {
                markdown.AppendLine($"keywords: \"{EscapeYaml(info.Keywords)}\"");
                metadata.Keywords = info.Keywords;
            }
            if (!string.IsNullOrEmpty(info.Creator))
            {
                markdown.AppendLine($"creator: \"{EscapeYaml(info.Creator)}\"");
                metadata.Creator = info.Creator;
            }
            if (!string.IsNullOrEmpty(info.Producer))
            {
                markdown.AppendLine($"producer: \"{EscapeYaml(info.Producer)}\"");
                metadata.Producer = info.Producer;
            }
            if (!string.IsNullOrEmpty(info.CreationDate))
            {
                markdown.AppendLine($"created: \"{info.CreationDate}\"");
                metadata.CreationDate = info.CreationDate;
            }
            if (!string.IsNullOrEmpty(info.ModifiedDate))
            {
                markdown.AppendLine($"modified: \"{info.ModifiedDate}\"");
                metadata.ModifiedDate = info.ModifiedDate;
            }
        }

        // Document structure info
        markdown.AppendLine($"pages: {document.NumberOfPages}");
        markdown.AppendLine($"pdf_version: \"{document.Version}\"");

        // Try to get encryption status
        try
        {
            metadata.IsEncrypted = document.IsEncrypted;
            if (document.IsEncrypted)
                markdown.AppendLine("encrypted: true");
        }
        catch { /* Some documents may not support this */ }

        // Extract page dimensions for first page as reference
        if (document.NumberOfPages > 0)
        {
            var firstPage = document.GetPage(1);
            metadata.PageWidth = firstPage.Width;
            metadata.PageHeight = firstPage.Height;
            metadata.PageRotation = (int)firstPage.Rotation.Value;

            markdown.AppendLine($"page_width: {firstPage.Width:F2}");
            markdown.AppendLine($"page_height: {firstPage.Height:F2}");

            if (firstPage.Rotation.Value != 0)
                markdown.AppendLine($"rotation: {firstPage.Rotation.Value}");
        }

        markdown.AppendLine("---\n");

        return metadata;
    }

    private static string EscapeYaml(string value)
    {
        return value
            .Replace("\\", "\\\\")
            .Replace("\"", "\\\"")
            .Replace("\n", "\\n")
            .Replace("\r", "\\r");
    }

    private void AppendBookmarks(StringBuilder markdown, UglyToad.PdfPig.Outline.Bookmarks bookmarks)
    {
        if (!bookmarks.Roots.Any()) return;

        markdown.AppendLine("## Table of Contents\n");
        foreach (var bookmark in bookmarks.Roots)
        {
            AppendBookmark(markdown, bookmark, 0);
        }
        markdown.AppendLine();
    }

    private void AppendBookmark(StringBuilder markdown, UglyToad.PdfPig.Outline.BookmarkNode bookmark, int level)
    {
        var indent = new string(' ', level * 2);
        markdown.AppendLine($"{indent}- {bookmark.Title}");

        foreach (var child in bookmark.Children)
        {
            AppendBookmark(markdown, child, level + 1);
        }
    }

    private ExtractionQuality AssessPageQuality(string text, int wordCount)
    {
        if (string.IsNullOrWhiteSpace(text) || wordCount == 0)
            return ExtractionQuality.Empty;

        var alphaCount = text.Count(char.IsLetter);
        if (alphaCount < 10)
            return ExtractionQuality.Empty;

        var upperCount = text.Count(char.IsUpper);
        var upperRatio = (double)upperCount / alphaCount;

        if (upperRatio > 0.4 && alphaCount > 50)
            return ExtractionQuality.Garbage;

        var vowelCount = text.Count(c => "aeiouAEIOU".Contains(c));
        var vowelRatio = (double)vowelCount / alphaCount;

        if (vowelRatio < 0.15 && alphaCount > 50)
            return ExtractionQuality.Garbage;

        if (vowelRatio < 0.25 || upperRatio > 0.25)
            return ExtractionQuality.Low;

        if (wordCount < 20)
            return ExtractionQuality.Medium;

        return ExtractionQuality.High;
    }

    private ExtractionQuality AssessOverallQuality(List<ExtractionQuality> qualities)
    {
        if (qualities.Count == 0) return ExtractionQuality.Empty;

        var garbageCount = qualities.Count(q => q == ExtractionQuality.Garbage);
        if (garbageCount > qualities.Count / 2)
            return ExtractionQuality.Garbage;

        var emptyCount = qualities.Count(q => q == ExtractionQuality.Empty);
        if (emptyCount > qualities.Count / 2)
            return ExtractionQuality.Empty;

        var lowCount = qualities.Count(q => q == ExtractionQuality.Low);
        if (lowCount + garbageCount > qualities.Count / 2)
            return ExtractionQuality.Low;

        var highCount = qualities.Count(q => q == ExtractionQuality.High);
        if (highCount > qualities.Count / 2)
            return ExtractionQuality.High;

        return ExtractionQuality.Medium;
    }

    public Task<bool> IsAvailableAsync(CancellationToken ct = default)
    {
        // PdfPig is always available - it's a pure .NET library
        return Task.FromResult(true);
    }
}
