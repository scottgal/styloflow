using System.Text;
using Microsoft.Extensions.Logging;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.DocumentLayoutAnalysis.PageSegmenter;
using UglyToad.PdfPig.DocumentLayoutAnalysis.ReadingOrderDetector;
using PdfTextBlock = UglyToad.PdfPig.DocumentLayoutAnalysis.TextBlock;

namespace StyloFlow.Converters.PdfPig;

/// <summary>
/// Layout analyzer for OCR output.
/// Applies the same techniques used for PDF extraction to improve OCR results.
/// </summary>
public class OcrLayoutAnalyzer
{
    private readonly PdfPigConfig _config;
    private readonly ILogger<OcrLayoutAnalyzer>? _logger;

    public OcrLayoutAnalyzer(PdfPigConfig? config = null, ILogger<OcrLayoutAnalyzer>? logger = null)
    {
        _config = config ?? new PdfPigConfig();
        _logger = logger;
    }

    /// <summary>
    /// Process OCR word boxes through layout analysis.
    /// Applies segmentation, reading order, and decoration filtering.
    /// </summary>
    /// <param name="ocrWords">Word boxes from OCR engine (Tesseract, etc.)</param>
    /// <param name="pageWidth">Page width in pixels/points.</param>
    /// <param name="pageHeight">Page height in pixels/points.</param>
    /// <returns>Processed text with proper reading order.</returns>
    public OcrLayoutResult Analyze(IReadOnlyList<OcrWordInput> ocrWords, double pageWidth, double pageHeight)
    {
        if (ocrWords.Count == 0)
        {
            return new OcrLayoutResult
            {
                Text = "",
                Blocks = [],
                Quality = ExtractionQuality.Empty
            };
        }

        try
        {
            // Convert OCR words to PdfPig-compatible format
            var words = ConvertToWords(ocrWords);

            // Apply page segmentation
            var blocks = GetTextBlocks(words);

            // Apply reading order detection
            var orderedBlocks = GetOrderedBlocks(blocks);

            // Filter decorations
            if (_config.FilterDecorations)
            {
                orderedBlocks = FilterDecorations(orderedBlocks, pageWidth, pageHeight);
            }

            // Build result
            var resultBlocks = new List<AnalyzedBlock>();
            var sb = new StringBuilder();
            var readingOrder = 0;

            foreach (var block in orderedBlocks)
            {
                var blockText = string.Join(" ", block.TextLines.SelectMany(l => l.Words).Select(w => w.Text));
                if (string.IsNullOrWhiteSpace(blockText)) continue;

                resultBlocks.Add(new AnalyzedBlock
                {
                    ReadingOrder = readingOrder++,
                    Text = blockText,
                    BoundingBox = new BoundingBox(
                        block.BoundingBox.Left,
                        block.BoundingBox.Bottom,
                        block.BoundingBox.Width,
                        block.BoundingBox.Height),
                    WordCount = block.TextLines.SelectMany(l => l.Words).Count(),
                    LineCount = block.TextLines.Count,
                    BlockType = ClassifyBlock(block, pageWidth, pageHeight)
                });

                sb.AppendLine(blockText);
                sb.AppendLine();
            }

            var text = sb.ToString();
            var quality = AssessQuality(text, ocrWords);

            return new OcrLayoutResult
            {
                Text = text,
                Blocks = resultBlocks,
                Quality = quality,
                TotalWords = ocrWords.Count,
                ProcessedWords = resultBlocks.Sum(b => b.WordCount)
            };
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Layout analysis failed, returning raw text");

            // Fallback to simple concatenation
            var text = string.Join(" ", ocrWords.Select(w => w.Text));
            return new OcrLayoutResult
            {
                Text = text,
                Blocks = [],
                Quality = ExtractionQuality.Unknown,
                TotalWords = ocrWords.Count,
                ProcessedWords = ocrWords.Count
            };
        }
    }

    /// <summary>
    /// Group OCR characters into words using nearest neighbor algorithm.
    /// Use this when OCR returns character-level results.
    /// </summary>
    public IReadOnlyList<OcrWordInput> GroupCharactersIntoWords(
        IReadOnlyList<OcrCharInput> characters,
        double maxCharSpacing = 10.0)
    {
        if (characters.Count == 0)
            return [];

        var words = new List<OcrWordInput>();
        var currentWord = new List<OcrCharInput> { characters[0] };

        for (var i = 1; i < characters.Count; i++)
        {
            var prev = characters[i - 1];
            var curr = characters[i];

            // Check if on same line (similar Y coordinate)
            var sameLine = Math.Abs(prev.Y - curr.Y) < prev.Height * 0.5;

            // Check horizontal distance
            var horizontalGap = curr.X - (prev.X + prev.Width);
            var isNeighbor = sameLine && horizontalGap >= 0 && horizontalGap < maxCharSpacing;

            if (isNeighbor)
            {
                currentWord.Add(curr);
            }
            else
            {
                // Emit current word
                if (currentWord.Count > 0)
                {
                    words.Add(CreateWordFromChars(currentWord));
                }
                currentWord = [curr];
            }
        }

        // Emit final word
        if (currentWord.Count > 0)
        {
            words.Add(CreateWordFromChars(currentWord));
        }

        return words;
    }

    private OcrWordInput CreateWordFromChars(List<OcrCharInput> chars)
    {
        var text = string.Concat(chars.Select(c => c.Text));
        var minX = chars.Min(c => c.X);
        var minY = chars.Min(c => c.Y);
        var maxX = chars.Max(c => c.X + c.Width);
        var maxY = chars.Max(c => c.Y + c.Height);
        var avgConfidence = chars.Average(c => c.Confidence);

        return new OcrWordInput
        {
            Text = text,
            X = minX,
            Y = minY,
            Width = maxX - minX,
            Height = maxY - minY,
            Confidence = avgConfidence
        };
    }

    private IReadOnlyList<Word> ConvertToWords(IReadOnlyList<OcrWordInput> ocrWords)
    {
        // Create synthetic Word objects from OCR output
        return ocrWords.Select(OcrWordAdapter.CreateWord).ToList();
    }

    private IReadOnlyList<PdfTextBlock> GetTextBlocks(IReadOnlyList<Word> words)
    {
        if (words.Count == 0)
            return [];

        return _config.PageSegmenter switch
        {
            PageSegmenterType.RecursiveXYCut => RecursiveXYCut.Instance.GetBlocks(words),
            PageSegmenterType.DocstrumBoundingBoxes => DocstrumBoundingBoxes.Instance.GetBlocks(words),
            _ => DefaultPageSegmenter.Instance.GetBlocks(words)
        };
    }

    private IEnumerable<PdfTextBlock> GetOrderedBlocks(IReadOnlyList<PdfTextBlock> blocks)
    {
        if (blocks.Count == 0)
            return [];

        return _config.ReadingOrderDetector switch
        {
            ReadingOrderType.Unsupervised => UnsupervisedReadingOrderDetector.Instance.Get(blocks),
            ReadingOrderType.RenderingBased => RenderingReadingOrderDetector.Instance.Get(blocks),
            _ => blocks
        };
    }

    private IEnumerable<PdfTextBlock> FilterDecorations(IEnumerable<PdfTextBlock> blocks, double pageWidth, double pageHeight)
    {
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

    private BlockType ClassifyBlock(PdfTextBlock block, double pageWidth, double pageHeight)
    {
        var bbox = block.BoundingBox;
        var relativeY = bbox.Centroid.Y / pageHeight;
        var relativeWidth = bbox.Width / pageWidth;

        // Header/footer detection
        if (relativeY > 0.9 || relativeY < 0.1)
        {
            if (relativeWidth < 0.5)
                return relativeY > 0.9 ? BlockType.Footer : BlockType.Header;
        }

        // Title detection (large, centered, near top)
        if (relativeY < 0.2 && relativeWidth > 0.3 && relativeWidth < 0.8)
        {
            var wordCount = block.TextLines.SelectMany(l => l.Words).Count();
            if (wordCount < 20)
                return BlockType.Title;
        }

        return BlockType.Paragraph;
    }

    private ExtractionQuality AssessQuality(string text, IReadOnlyList<OcrWordInput> words)
    {
        if (string.IsNullOrWhiteSpace(text))
            return ExtractionQuality.Empty;

        // Check average confidence
        var avgConfidence = words.Average(w => w.Confidence);
        if (avgConfidence < 0.5)
            return ExtractionQuality.Garbage;
        if (avgConfidence < 0.7)
            return ExtractionQuality.Low;
        if (avgConfidence < 0.85)
            return ExtractionQuality.Medium;

        // Check text quality
        var alphaCount = text.Count(char.IsLetter);
        if (alphaCount < 10)
            return ExtractionQuality.Empty;

        var upperCount = text.Count(char.IsUpper);
        var upperRatio = (double)upperCount / alphaCount;
        if (upperRatio > 0.4)
            return ExtractionQuality.Low;

        return ExtractionQuality.High;
    }
}

/// <summary>
/// Input word from OCR engine.
/// </summary>
public class OcrWordInput
{
    public required string Text { get; set; }
    public double X { get; set; }
    public double Y { get; set; }
    public double Width { get; set; }
    public double Height { get; set; }
    public double Confidence { get; set; } = 1.0;
}

/// <summary>
/// Input character from OCR engine (for character-level results).
/// </summary>
public class OcrCharInput
{
    public required string Text { get; set; }
    public double X { get; set; }
    public double Y { get; set; }
    public double Width { get; set; }
    public double Height { get; set; }
    public double Confidence { get; set; } = 1.0;
}

/// <summary>
/// Result of layout analysis on OCR output.
/// </summary>
public class OcrLayoutResult
{
    public required string Text { get; set; }
    public required IReadOnlyList<AnalyzedBlock> Blocks { get; set; }
    public ExtractionQuality Quality { get; set; }
    public int TotalWords { get; set; }
    public int ProcessedWords { get; set; }
}

/// <summary>
/// Analyzed text block with reading order.
/// </summary>
public class AnalyzedBlock
{
    public int ReadingOrder { get; set; }
    public required string Text { get; set; }
    public required BoundingBox BoundingBox { get; set; }
    public int WordCount { get; set; }
    public int LineCount { get; set; }
    public BlockType BlockType { get; set; }
}

/// <summary>
/// Bounding box for layout elements.
/// </summary>
public record BoundingBox(double X, double Y, double Width, double Height);

/// <summary>
/// Synthetic Word adapter for OCR output.
/// Creates PdfPig-compatible Word objects from OCR data.
/// </summary>
internal static class OcrWordAdapter
{
    /// <summary>
    /// Creates a Word by building synthetic letters.
    /// </summary>
    public static Word CreateWord(OcrWordInput ocr)
    {
        // Create synthetic letters to build the word
        var letters = new List<Letter>();
        var charWidth = ocr.Width / Math.Max(ocr.Text.Length, 1);

        for (var i = 0; i < ocr.Text.Length; i++)
        {
            var charX = ocr.X + i * charWidth;
            var letter = CreateSyntheticLetter(
                ocr.Text[i].ToString(),
                charX,
                ocr.Y,
                charWidth,
                ocr.Height);
            letters.Add(letter);
        }

        return new Word(letters);
    }

    /// <summary>
    /// Creates a synthetic Letter for OCR data using reflection.
    /// PdfPig's Letter class constructor signature varies by version.
    /// </summary>
    private static Letter CreateSyntheticLetter(string value, double x, double y, double width, double height)
    {
        var boundingBox = new PdfRectangle(x, y, x + width, y + height);
        var startBaseLine = new PdfPoint(x, y);
        var endBaseLine = new PdfPoint(x + width, y);

        // Use reflection to find and invoke the correct constructor
        var letterType = typeof(Letter);
        var constructors = letterType.GetConstructors()
            .OrderByDescending(c => c.GetParameters().Length)
            .ToArray();

        foreach (var ctor in constructors)
        {
            try
            {
                var parameters = ctor.GetParameters();
                var args = new object?[parameters.Length];

                for (int i = 0; i < parameters.Length; i++)
                {
                    var param = parameters[i];
                    var paramType = param.ParameterType;
                    var paramName = param.Name?.ToLowerInvariant() ?? "";

                    args[i] = paramType.Name switch
                    {
                        "String" => value,
                        "PdfRectangle" => boundingBox,
                        "PdfPoint" when paramName.Contains("start") => startBaseLine,
                        "PdfPoint" => endBaseLine,
                        "Double" when paramName.Contains("width") => width,
                        "Double" when paramName.Contains("font") || paramName.Contains("point") || paramName.Contains("size") => 12.0,
                        "Double" => height,
                        "Int32" or "Nullable`1" when paramType == typeof(int?) => 0,
                        "Int32" => 0,
                        _ when paramType.IsInterface => null,
                        _ when Nullable.GetUnderlyingType(paramType) != null => null,
                        _ when param.HasDefaultValue => param.DefaultValue,
                        _ when paramType.IsValueType => Activator.CreateInstance(paramType),
                        _ => null
                    };
                }

                return (Letter)ctor.Invoke(args);
            }
            catch
            {
                // Try next constructor
                continue;
            }
        }

        throw new InvalidOperationException($"Cannot create Letter instance - no compatible constructor found. Available: {string.Join(", ", constructors.Select(c => c.GetParameters().Length + " params"))}");
    }
}
