using System.Diagnostics;
using System.Text;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using Microsoft.Extensions.Logging;

namespace StyloFlow.Converters.OpenXml;

/// <summary>
/// DOCX converter using DocumentFormat.OpenXml.
/// Extracts text with structure, headings, sections, tables, and images.
/// Works without external services - pure .NET implementation.
/// </summary>
public class OpenXmlConverter : IContentConverter
{
    private readonly OpenXmlConfig _config;
    private readonly ISharedStorage _storage;
    private readonly ILogger<OpenXmlConverter>? _logger;

    public OpenXmlConverter(
        OpenXmlConfig config,
        ISharedStorage storage,
        ILogger<OpenXmlConverter>? logger = null)
    {
        _config = config;
        _storage = storage;
        _logger = logger;
    }

    public string ConverterId => "openxml";
    public string DisplayName => "OpenXml Document Converter";
    public int Priority => 50; // Same level as PdfPig - offline capable

    public IReadOnlyList<string> SupportedInputTypes =>
    [
        "application/vnd.openxmlformats-officedocument.wordprocessingml.document", // .docx
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",       // .xlsx
        "application/vnd.openxmlformats-officedocument.presentationml.presentation" // .pptx
    ];

    public IReadOnlyList<string> SupportedOutputTypes => ["text/markdown", "text/plain"];

    public bool CanConvert(string inputPath, string inputMimeType, string outputFormat)
    {
        var isSupported = SupportedInputTypes.Any(t =>
            t.Equals(inputMimeType, StringComparison.OrdinalIgnoreCase));

        var canOutput = outputFormat.Equals("markdown", StringComparison.OrdinalIgnoreCase) ||
                        outputFormat.Equals("text", StringComparison.OrdinalIgnoreCase);

        return isSupported && canOutput;
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
                CurrentStep = "Opening document...",
                Elapsed = sw.Elapsed
            });

            // Route based on document type
            if (inputMimeType.Contains("wordprocessingml"))
            {
                return await ConvertWordDocumentAsync(localInputPath, inputPath, options, progress, sw, ct);
            }
            else if (inputMimeType.Contains("spreadsheetml"))
            {
                return await ConvertSpreadsheetAsync(localInputPath, inputPath, options, progress, sw, ct);
            }
            else if (inputMimeType.Contains("presentationml"))
            {
                return await ConvertPresentationAsync(localInputPath, inputPath, options, progress, sw, ct);
            }

            return ConversionResult.Failure($"Unsupported document type: {inputMimeType}", sw.Elapsed);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "OpenXml conversion failed for {Input}", inputPath);
            return ConversionResult.Failure(ex.Message, sw.Elapsed);
        }
    }

    private async Task<ConversionResult> ConvertWordDocumentAsync(
        string localInputPath,
        string inputPath,
        ConversionOptions options,
        IProgress<ConversionProgress>? progress,
        Stopwatch sw,
        CancellationToken ct)
    {
        using var document = WordprocessingDocument.Open(localInputPath, false);

        var mainPart = document.MainDocumentPart;
        if (mainPart?.Document?.Body == null)
        {
            return ConversionResult.Failure("Invalid or empty Word document", sw.Elapsed);
        }

        var markdown = new StringBuilder();
        var extractedImages = new List<ConvertedAsset>();
        var structure = new DocumentStructure();

        // Extract metadata
        if (_config.ExtractMetadata && document.PackageProperties != null)
        {
            AppendMetadata(markdown, document.PackageProperties, structure);
        }

        progress?.Report(new ConversionProgress
        {
            CurrentStep = "Extracting content...",
            Elapsed = sw.Elapsed
        });

        // Process body content
        var body = mainPart.Document.Body;
        var sectionIndex = 0;
        var currentSection = new DocumentSection { Index = sectionIndex, StartParagraph = 0 };
        var paragraphIndex = 0;

        foreach (var element in body.ChildElements)
        {
            ct.ThrowIfCancellationRequested();

            switch (element)
            {
                case Paragraph para:
                    var (paraMarkdown, heading) = ProcessParagraph(para, mainPart);
                    markdown.AppendLine(paraMarkdown);

                    if (heading != null)
                    {
                        structure.Headings.Add(new HeadingInfo
                        {
                            Text = heading.Text,
                            Level = heading.Level,
                            ParagraphIndex = paragraphIndex
                        });
                    }

                    // Extract inline images
                    if (_config.ExtractImages && options.ExtractAssets)
                    {
                        var images = await ExtractParagraphImagesAsync(para, mainPart, paragraphIndex, inputPath, ct);
                        extractedImages.AddRange(images);
                    }

                    paragraphIndex++;
                    break;

                case Table table:
                    if (_config.ConvertTables)
                    {
                        var tableMarkdown = ProcessTable(table, mainPart);
                        markdown.AppendLine(tableMarkdown);
                    }
                    break;

                case SectionProperties:
                    // End of current section
                    currentSection.EndParagraph = paragraphIndex;
                    structure.Sections.Add(currentSection);
                    sectionIndex++;
                    currentSection = new DocumentSection { Index = sectionIndex, StartParagraph = paragraphIndex };
                    break;
            }
        }

        // Close final section
        currentSection.EndParagraph = paragraphIndex;
        if (currentSection.StartParagraph < currentSection.EndParagraph)
        {
            structure.Sections.Add(currentSection);
        }

        structure.TotalParagraphs = paragraphIndex;
        structure.TotalImages = extractedImages.Count;

        // Store markdown output
        var outputFileName = Path.GetFileNameWithoutExtension(inputPath) + ".md";
        var outputPath = $"converted/{Guid.NewGuid():N}/{outputFileName}";

        var storedContent = await _storage.StoreTextAsync(
            markdown.ToString(),
            outputPath,
            "text/markdown",
            new Dictionary<string, string>
            {
                ["source"] = inputPath,
                ["converter"] = ConverterId,
                ["sections"] = structure.Sections.Count.ToString(),
                ["headings"] = structure.Headings.Count.ToString(),
                ["imagesExtracted"] = extractedImages.Count.ToString()
            },
            ct);

        _logger?.LogInformation(
            "Converted DOCX to {Output} in {Duration}ms ({Sections} sections, {Headings} headings, {Images} images)",
            storedContent.Path, sw.ElapsedMilliseconds,
            structure.Sections.Count, structure.Headings.Count, extractedImages.Count);

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
                ["sections"] = structure.Sections.Count,
                ["headings"] = structure.Headings.Count,
                ["paragraphs"] = structure.TotalParagraphs,
                ["hasImages"] = extractedImages.Count > 0,
                ["structure"] = structure
            }
        };
    }

    private (string markdown, HeadingInfo? heading) ProcessParagraph(Paragraph para, MainDocumentPart mainPart)
    {
        var text = GetParagraphText(para);
        if (string.IsNullOrWhiteSpace(text))
            return ("\n", null);

        var styleId = para.ParagraphProperties?.ParagraphStyleId?.Val?.Value;
        HeadingInfo? heading = null;

        // Check for heading styles
        if (_config.PreserveHeadings && !string.IsNullOrEmpty(styleId))
        {
            var headingLevel = GetHeadingLevel(styleId, mainPart);
            if (headingLevel > 0)
            {
                heading = new HeadingInfo { Text = text, Level = headingLevel };
                var prefix = new string('#', Math.Min(headingLevel, 6));
                return ($"{prefix} {text}\n", heading);
            }
        }

        // Check for list items
        if (_config.ConvertLists)
        {
            var numProp = para.ParagraphProperties?.NumberingProperties;
            if (numProp != null)
            {
                var ilvl = numProp.NumberingLevelReference?.Val?.Value ?? 0;
                var indent = new string(' ', ilvl * 2);

                // Check if numbered or bulleted
                var numId = numProp.NumberingId?.Val?.Value;
                var isNumbered = IsNumberedList(numId, mainPart);

                if (isNumbered)
                    return ($"{indent}1. {text}\n", null);
                else
                    return ($"{indent}- {text}\n", null);
            }
        }

        return ($"{text}\n", null);
    }

    private string GetParagraphText(Paragraph para)
    {
        var sb = new StringBuilder();

        foreach (var run in para.Descendants<Run>())
        {
            foreach (var child in run.ChildElements)
            {
                switch (child)
                {
                    case Text text:
                        sb.Append(text.Text);
                        break;

                    case Break br when br.Type?.Value == BreakValues.Page:
                        sb.Append("\n\n---\n\n");
                        break;

                    case Break:
                        sb.AppendLine();
                        break;

                    case TabChar:
                        sb.Append('\t');
                        break;
                }
            }

            // Handle bold/italic formatting
            var runProps = run.RunProperties;
            if (runProps != null)
            {
                var runText = string.Join("", run.Descendants<Text>().Select(t => t.Text));
                if (!string.IsNullOrEmpty(runText))
                {
                    if (runProps.Bold != null && runProps.Italic != null)
                    {
                        // Bold and italic - wrap in ***
                        // Already added text above, this is a simplified approach
                    }
                }
            }
        }

        return sb.ToString().Trim();
    }

    private int GetHeadingLevel(string styleId, MainDocumentPart mainPart)
    {
        // Standard heading styles
        if (styleId.StartsWith("Heading", StringComparison.OrdinalIgnoreCase) ||
            styleId.StartsWith("heading", StringComparison.OrdinalIgnoreCase))
        {
            if (int.TryParse(styleId.AsSpan(7), out var level))
                return level;
        }

        // Check style definitions for outline level
        var stylesPart = mainPart.StyleDefinitionsPart;
        if (stylesPart?.Styles != null)
        {
            var style = stylesPart.Styles.Descendants<Style>()
                .FirstOrDefault(s => s.StyleId?.Value == styleId);

            var outlineLevel = style?.StyleParagraphProperties?.OutlineLevel?.Val?.Value;
            if (outlineLevel.HasValue)
                return outlineLevel.Value + 1; // Outline levels are 0-based
        }

        return 0;
    }

    private bool IsNumberedList(int? numId, MainDocumentPart mainPart)
    {
        if (!numId.HasValue || mainPart.NumberingDefinitionsPart == null)
            return false;

        var numbering = mainPart.NumberingDefinitionsPart.Numbering;
        var numInstance = numbering?.Descendants<NumberingInstance>()
            .FirstOrDefault(n => n.NumberID?.Value == numId);

        if (numInstance?.AbstractNumId?.Val?.Value is int abstractNumId)
        {
            var abstractNum = numbering?.Descendants<AbstractNum>()
                .FirstOrDefault(a => a.AbstractNumberId?.Value == abstractNumId);

            var level = abstractNum?.Descendants<Level>().FirstOrDefault();
            var numFmt = level?.NumberingFormat?.Val?.Value;

            return numFmt != NumberFormatValues.Bullet;
        }

        return false;
    }

    private string ProcessTable(Table table, MainDocumentPart mainPart)
    {
        var sb = new StringBuilder();
        var rows = table.Descendants<TableRow>().ToList();

        if (rows.Count == 0)
            return "";

        var isFirstRow = true;
        foreach (var row in rows)
        {
            var cells = row.Descendants<TableCell>().ToList();
            sb.Append("| ");

            foreach (var cell in cells)
            {
                var cellText = string.Join(" ", cell.Descendants<Paragraph>()
                    .Select(p => GetParagraphText(p)))
                    .Replace("|", "\\|")  // Escape pipe characters
                    .Replace("\n", " ");  // Remove line breaks

                sb.Append(cellText);
                sb.Append(" | ");
            }

            sb.AppendLine();

            // Add header separator after first row
            if (isFirstRow)
            {
                sb.Append("| ");
                foreach (var _ in cells)
                {
                    sb.Append("--- | ");
                }
                sb.AppendLine();
                isFirstRow = false;
            }
        }

        sb.AppendLine();
        return sb.ToString();
    }

    private async Task<List<ConvertedAsset>> ExtractParagraphImagesAsync(
        Paragraph para,
        MainDocumentPart mainPart,
        int paragraphIndex,
        string sourcePath,
        CancellationToken ct)
    {
        var images = new List<ConvertedAsset>();

        // Find inline drawings
        var drawings = para.Descendants<DocumentFormat.OpenXml.Wordprocessing.Drawing>();

        foreach (var drawing in drawings)
        {
            // Get the blip (embedded image reference)
            var blip = drawing.Descendants<DocumentFormat.OpenXml.Drawing.Blip>().FirstOrDefault();
            if (blip?.Embed?.Value == null)
                continue;

            var relationshipId = blip.Embed.Value;
            var imagePart = mainPart.GetPartById(relationshipId) as ImagePart;

            if (imagePart == null)
                continue;

            try
            {
                using var stream = imagePart.GetStream();
                using var ms = new MemoryStream();
                await stream.CopyToAsync(ms, ct);

                if (_config.MaxImageSizeBytes > 0 && ms.Length > _config.MaxImageSizeBytes)
                    continue;

                ms.Position = 0;

                var extension = GetImageExtension(imagePart.ContentType);
                var imageName = $"para{paragraphIndex}_img{Guid.NewGuid():N}.{extension}";
                var imagePath = $"converted/{Path.GetFileNameWithoutExtension(sourcePath)}/images/{imageName}";

                var stored = await _storage.StoreAsync(ms, imagePath, imagePart.ContentType, ct: ct);

                images.Add(new ConvertedAsset
                {
                    Path = stored.Path,
                    Name = imageName,
                    MimeType = imagePart.ContentType,
                    SizeBytes = stored.SizeBytes,
                    SourcePage = paragraphIndex // Use paragraph index since DOCX doesn't have pages
                });
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Failed to extract image from paragraph {Index}", paragraphIndex);
            }
        }

        return images;
    }

    private static string GetImageExtension(string contentType)
    {
        return contentType.ToLowerInvariant() switch
        {
            "image/png" => "png",
            "image/jpeg" or "image/jpg" => "jpg",
            "image/gif" => "gif",
            "image/bmp" => "bmp",
            "image/tiff" => "tiff",
            "image/svg+xml" => "svg",
            _ => "bin"
        };
    }

    private void AppendMetadata(StringBuilder markdown, IPackageProperties props, DocumentStructure structure)
    {
        markdown.AppendLine("---");

        if (!string.IsNullOrEmpty(props.Title))
        {
            markdown.AppendLine($"title: \"{props.Title}\"");
            structure.Title = props.Title;
        }

        if (!string.IsNullOrEmpty(props.Creator))
        {
            markdown.AppendLine($"author: \"{props.Creator}\"");
            structure.Author = props.Creator;
        }

        if (!string.IsNullOrEmpty(props.Subject))
        {
            markdown.AppendLine($"subject: \"{props.Subject}\"");
        }

        if (!string.IsNullOrEmpty(props.Description))
        {
            markdown.AppendLine($"description: \"{props.Description}\"");
        }

        if (props.Created.HasValue)
        {
            markdown.AppendLine($"created: \"{props.Created.Value:O}\"");
            structure.CreatedDate = props.Created.Value;
        }

        if (props.Modified.HasValue)
        {
            markdown.AppendLine($"modified: \"{props.Modified.Value:O}\"");
            structure.ModifiedDate = props.Modified.Value;
        }

        markdown.AppendLine("---\n");
    }

    private Task<ConversionResult> ConvertSpreadsheetAsync(
        string localInputPath,
        string inputPath,
        ConversionOptions options,
        IProgress<ConversionProgress>? progress,
        Stopwatch sw,
        CancellationToken ct)
    {
        // TODO: Implement Excel conversion
        return Task.FromResult(ConversionResult.Failure("Excel conversion not yet implemented", sw.Elapsed));
    }

    private Task<ConversionResult> ConvertPresentationAsync(
        string localInputPath,
        string inputPath,
        ConversionOptions options,
        IProgress<ConversionProgress>? progress,
        Stopwatch sw,
        CancellationToken ct)
    {
        // TODO: Implement PowerPoint conversion
        return Task.FromResult(ConversionResult.Failure("PowerPoint conversion not yet implemented", sw.Elapsed));
    }

    public Task<bool> IsAvailableAsync(CancellationToken ct = default)
    {
        // OpenXml is always available - it's a pure .NET library
        return Task.FromResult(true);
    }
}

/// <summary>
/// Document structure information extracted from DOCX.
/// </summary>
public class DocumentStructure
{
    public string? Title { get; set; }
    public string? Author { get; set; }
    public DateTimeOffset? CreatedDate { get; set; }
    public DateTimeOffset? ModifiedDate { get; set; }
    public int TotalParagraphs { get; set; }
    public int TotalImages { get; set; }
    public List<DocumentSection> Sections { get; set; } = [];
    public List<HeadingInfo> Headings { get; set; } = [];
}

/// <summary>
/// Section of a document.
/// </summary>
public class DocumentSection
{
    public int Index { get; set; }
    public int StartParagraph { get; set; }
    public int EndParagraph { get; set; }
}

/// <summary>
/// Heading information.
/// </summary>
public class HeadingInfo
{
    public required string Text { get; set; }
    public int Level { get; set; }
    public int ParagraphIndex { get; set; }
}
