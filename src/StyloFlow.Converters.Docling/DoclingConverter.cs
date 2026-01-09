using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using UglyToad.PdfPig;

namespace StyloFlow.Converters.Docling;

/// <summary>
/// Docling-based document converter.
/// Converts PDFs and DOCX to Markdown using Docling API.
/// Also produces OCR artifacts for downstream processing.
/// </summary>
public class DoclingConverter : IContentConverter
{
    private readonly DoclingConfig _config;
    private readonly ISharedStorage _storage;
    private readonly HttpClient _http;
    private readonly ILogger<DoclingConverter>? _logger;
    private readonly TimeSpan _pollInterval = TimeSpan.FromSeconds(2);

    public DoclingConverter(
        DoclingConfig config,
        ISharedStorage storage,
        ILogger<DoclingConverter>? logger = null)
    {
        _config = config;
        _storage = storage;
        _logger = logger;
        _http = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(config.TimeoutSeconds + 60)
        };
    }

    public string ConverterId => "docling";
    public string DisplayName => "Docling Document Converter";
    public int Priority => 100; // High priority for PDFs

    public IReadOnlyList<string> SupportedInputTypes =>
    [
        "application/pdf",
        "application/vnd.openxmlformats-officedocument.wordprocessingml.document", // DOCX
        "application/msword", // DOC
        "image/png",
        "image/jpeg",
        "image/tiff"
    ];

    public IReadOnlyList<string> SupportedOutputTypes =>
    [
        "text/markdown",
        "text/plain"
    ];

    public bool CanConvert(string inputPath, string inputMimeType, string outputFormat)
    {
        return SupportedInputTypes.Contains(inputMimeType, StringComparer.OrdinalIgnoreCase) &&
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
            // Get local path for input
            await using var inputHandle = await _storage.GetLocalPathAsync(inputPath, ct);
            var localInputPath = inputHandle.Path;

            if (!File.Exists(localInputPath))
            {
                return ConversionResult.Failure($"Input file not found: {inputPath}", sw.Elapsed);
            }

            progress?.Report(new ConversionProgress
            {
                CurrentStep = "Starting Docling conversion...",
                Elapsed = sw.Elapsed
            });

            // For PDFs, check if split processing is needed
            string markdown;
            var ocrArtifacts = new List<OcrArtifact>();

            if (inputMimeType == "application/pdf" && _config.EnableSplitProcessing)
            {
                var result = await ConvertPdfWithSplitAsync(localInputPath, options, progress, ct);
                markdown = result.markdown;
                ocrArtifacts = result.ocrArtifacts;
            }
            else
            {
                markdown = await ConvertStandardAsync(localInputPath, _config.PdfBackend, ct);
            }

            // Assess overall quality
            var quality = AssessTextQuality(markdown);
            var needsReocr = quality == OcrQuality.Garbage || quality == OcrQuality.Low;

            // If garbage, try OCR backend
            if (needsReocr && _config.EnableOcrFallback && inputMimeType == "application/pdf")
            {
                _logger?.LogInformation("Docling output quality is {Quality}, trying OCR backend", quality);
                progress?.Report(new ConversionProgress
                {
                    CurrentStep = "Output quality low, trying OCR backend...",
                    Elapsed = sw.Elapsed
                });

                try
                {
                    var ocrMarkdown = await ConvertStandardAsync(localInputPath, _config.OcrPdfBackend, ct);
                    var ocrQuality = AssessTextQuality(ocrMarkdown);
                    if (ocrQuality > quality)
                    {
                        markdown = ocrMarkdown;
                        quality = ocrQuality;
                        needsReocr = quality == OcrQuality.Garbage || quality == OcrQuality.Low;
                    }
                }
                catch (Exception ex)
                {
                    _logger?.LogWarning(ex, "OCR fallback failed");
                }
            }

            // If still garbage and it's a PDF, try PdfPig as last resort
            if (needsReocr && inputMimeType == "application/pdf")
            {
                try
                {
                    var pdfPigResult = ExtractWithPdfPig(localInputPath);
                    var pdfPigQuality = AssessTextQuality(pdfPigResult);
                    if (pdfPigQuality > quality)
                    {
                        markdown = pdfPigResult;
                        quality = pdfPigQuality;
                        needsReocr = quality == OcrQuality.Garbage || quality == OcrQuality.Low;
                    }
                }
                catch (Exception ex)
                {
                    _logger?.LogWarning(ex, "PdfPig fallback failed");
                }
            }

            // Store markdown output
            var outputFileName = Path.GetFileNameWithoutExtension(inputPath) + ".md";
            var outputPath = $"converted/{Guid.NewGuid():N}/{outputFileName}";

            var storedContent = await _storage.StoreTextAsync(
                markdown,
                outputPath,
                "text/markdown",
                new Dictionary<string, string>
                {
                    ["source"] = inputPath,
                    ["converter"] = ConverterId,
                    ["quality"] = quality.ToString()
                },
                ct);

            // Store OCR artifacts if we have them and quality is questionable
            string? ocrArtifactsPath = null;
            if (_config.StoreOcrArtifacts && (ocrArtifacts.Count > 0 || needsReocr))
            {
                ocrArtifactsPath = $"converted/{Guid.NewGuid():N}/ocr";

                // Create OCR result document
                var ocrResult = new DocumentOcrResult
                {
                    SourcePath = inputPath,
                    TotalPages = ocrArtifacts.Count,
                    PagesNeedingReocr = ocrArtifacts.Count(a => a.NeedsReocr),
                    OverallQuality = quality,
                    AverageConfidence = ocrArtifacts.Any() ? ocrArtifacts.Average(a => a.Confidence ?? 0) : 0,
                    Pages = ocrArtifacts,
                    MarkdownPath = storedContent.Path,
                    OcrArtifactsPath = ocrArtifactsPath
                };

                // Store OCR result as JSON for downstream processors
                var ocrJson = JsonSerializer.Serialize(ocrResult, new JsonSerializerOptions { WriteIndented = true });
                await _storage.StoreTextAsync(ocrJson, $"{ocrArtifactsPath}/result.json", "application/json", ct: ct);
            }

            _logger?.LogInformation(
                "Converted {Input} to {Output} in {Duration}ms (quality: {Quality})",
                inputPath, storedContent.Path, sw.ElapsedMilliseconds, quality);

            return new ConversionResult
            {
                Success = true,
                OutputPath = storedContent.Path,
                OutputMimeType = "text/markdown",
                OutputSizeBytes = storedContent.SizeBytes,
                OutputHash = storedContent.ContentHash,
                Duration = sw.Elapsed,
                ProducerName = ConverterId,
                Metadata = new Dictionary<string, object>
                {
                    ["quality"] = quality.ToString(),
                    ["needsReocr"] = needsReocr,
                    ["ocrArtifactsPath"] = ocrArtifactsPath ?? "",
                    ["pagesNeedingReocr"] = ocrArtifacts.Where(a => a.NeedsReocr).Select(a => a.PageNumber).ToArray()
                }
            };
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Docling conversion failed for {Input}", inputPath);
            return ConversionResult.Failure(ex.Message, sw.Elapsed);
        }
    }

    private async Task<(string markdown, List<OcrArtifact> ocrArtifacts)> ConvertPdfWithSplitAsync(
        string filePath,
        ConversionOptions options,
        IProgress<ConversionProgress>? progress,
        CancellationToken ct)
    {
        int totalPages;
        try
        {
            using var doc = PdfDocument.Open(filePath);
            totalPages = doc.NumberOfPages;
        }
        catch
        {
            return (await ConvertStandardAsync(filePath, _config.PdfBackend, ct), []);
        }

        if (totalPages <= _config.MinPagesForSplit)
        {
            return (await ConvertStandardAsync(filePath, _config.PdfBackend, ct), []);
        }

        var chunks = new List<(int start, int end, string result, OcrArtifact? artifact)>();
        var pagesPerChunk = _config.PagesPerChunk;
        var numChunks = (int)Math.Ceiling((double)totalPages / pagesPerChunk);

        for (var i = 0; i < numChunks; i++)
        {
            ct.ThrowIfCancellationRequested();

            var startPage = i * pagesPerChunk + 1;
            var endPage = Math.Min(startPage + pagesPerChunk - 1, totalPages);

            progress?.Report(new ConversionProgress
            {
                PercentComplete = (double)i / numChunks * 100,
                CurrentStep = $"Processing pages {startPage}-{endPage}",
                CurrentPage = startPage,
                TotalPages = totalPages
            });

            try
            {
                var taskId = await StartConversionAsync(filePath, startPage, endPage, _config.PdfBackend, ct);
                var result = await WaitForCompletionAsync(taskId, ct);

                var quality = AssessTextQuality(result);
                var artifact = new OcrArtifact
                {
                    SourcePath = filePath,
                    PageNumber = startPage,
                    Text = result,
                    Quality = quality,
                    NeedsReocr = quality == OcrQuality.Garbage || quality == OcrQuality.Low,
                    ReocrReason = quality == OcrQuality.Garbage ? "Garbled text detected" : null,
                    Producer = "docling"
                };

                chunks.Add((startPage, endPage, result, artifact));
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Failed to convert pages {Start}-{End}", startPage, endPage);
                chunks.Add((startPage, endPage, "", new OcrArtifact
                {
                    SourcePath = filePath,
                    PageNumber = startPage,
                    Quality = OcrQuality.Empty,
                    NeedsReocr = true,
                    ReocrReason = $"Conversion failed: {ex.Message}",
                    Producer = "docling"
                }));
            }
        }

        // Combine results with page markers
        var sb = new StringBuilder();
        foreach (var chunk in chunks.OrderBy(c => c.start))
        {
            if (!string.IsNullOrEmpty(chunk.result))
            {
                sb.AppendLine($"<!-- PAGE:{chunk.start}-{chunk.end} -->");
                sb.AppendLine(chunk.result);
                sb.AppendLine();
                sb.AppendLine("---");
                sb.AppendLine();
            }
        }

        var ocrArtifacts = chunks
            .Where(c => c.artifact != null)
            .Select(c => c.artifact!)
            .ToList();

        return (sb.ToString(), ocrArtifacts);
    }

    private async Task<string> ConvertStandardAsync(string filePath, string? backend, CancellationToken ct)
    {
        var taskId = await StartConversionAsync(filePath, null, null, backend, ct);
        return await WaitForCompletionAsync(taskId, ct);
    }

    private async Task<string> StartConversionAsync(
        string filePath,
        int? startPage,
        int? endPage,
        string? backend,
        CancellationToken ct)
    {
        using var content = new MultipartFormDataContent();
        await using var stream = File.OpenRead(filePath);
        var streamContent = new StreamContent(stream);
        content.Add(streamContent, "files", Path.GetFileName(filePath));

        if (!string.IsNullOrEmpty(backend) && filePath.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
        {
            content.Add(new StringContent(backend), "pdf_backend");
        }

        if (startPage.HasValue && endPage.HasValue)
        {
            content.Add(new StringContent(startPage.Value.ToString()), "page_range");
            content.Add(new StringContent(endPage.Value.ToString()), "page_range");
        }

        var response = await _http.PostAsync($"{_config.BaseUrl}/v1/convert/file/async", content, ct);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.GetProperty("task_id").GetString()
               ?? throw new Exception("No task ID returned from Docling");
    }

    private async Task<string> WaitForCompletionAsync(string taskId, CancellationToken ct)
    {
        var timeout = TimeSpan.FromSeconds(_config.TimeoutSeconds);
        var startTime = DateTime.UtcNow;

        while (DateTime.UtcNow - startTime < timeout)
        {
            ct.ThrowIfCancellationRequested();

            await Task.Delay(_pollInterval, ct);

            var statusResponse = await _http.GetAsync($"{_config.BaseUrl}/v1/status/poll/{taskId}", ct);
            if (!statusResponse.IsSuccessStatusCode) continue;

            var statusJson = await statusResponse.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(statusJson);
            var status = doc.RootElement.GetProperty("task_status").GetString()?.ToUpperInvariant();

            if (status == "SUCCESS")
            {
                return await GetResultAsync(taskId, ct);
            }

            if (status == "FAILURE" || status == "REVOKED")
            {
                throw new Exception($"Docling conversion failed: {status}");
            }
        }

        throw new TimeoutException($"Conversion timed out after {timeout.TotalMinutes:F0} minutes");
    }

    private async Task<string> GetResultAsync(string taskId, CancellationToken ct)
    {
        var response = await _http.GetAsync($"{_config.BaseUrl}/v1/result/{taskId}", ct);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.GetProperty("document").GetProperty("md_content").GetString()
               ?? throw new Exception("No markdown content returned from Docling");
    }

    /// <summary>
    /// Assess the quality of extracted text.
    /// Returns a quality rating that downstream processors can use
    /// to decide if re-OCR is needed.
    /// </summary>
    private static OcrQuality AssessTextQuality(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return OcrQuality.Empty;

        var sample = text.Length > 2000 ? text[..2000] : text;
        var alphaCount = sample.Count(char.IsLetter);

        if (alphaCount < 10)
            return OcrQuality.Empty;

        var upperCount = sample.Count(char.IsUpper);
        var upperRatio = (double)upperCount / alphaCount;

        // High ratio of uppercase is suspicious
        if (upperRatio > 0.4 && alphaCount > 50)
            return OcrQuality.Garbage;

        // Check for unusual letter distribution
        var letterFreq = sample
            .Where(char.IsLetter)
            .GroupBy(char.ToLower)
            .ToDictionary(g => g.Key, g => g.Count());

        if (letterFreq.Count > 0)
        {
            var avgFreq = (double)alphaCount / letterFreq.Count;
            var variance = letterFreq.Values.Average(v => Math.Pow(v - avgFreq, 2));
            var stdDev = Math.Sqrt(variance);
            if (avgFreq > 5 && stdDev / avgFreq > 2.5)
                return OcrQuality.Garbage;
        }

        // Check vowel ratio (very low = likely garbage)
        var vowelCount = sample.Count(c => "aeiouAEIOU".Contains(c));
        var vowelRatio = (double)vowelCount / alphaCount;
        if (vowelRatio < 0.15 && alphaCount > 50)
            return OcrQuality.Garbage;

        if (vowelRatio < 0.25)
            return OcrQuality.Low;

        if (upperRatio > 0.2)
            return OcrQuality.Medium;

        return OcrQuality.High;
    }

    private static string ExtractWithPdfPig(string filePath)
    {
        var sb = new StringBuilder();
        using var document = PdfDocument.Open(filePath);

        foreach (var page in document.GetPages())
        {
            var text = page.Text;
            if (!string.IsNullOrWhiteSpace(text))
            {
                if (sb.Length > 0)
                {
                    sb.AppendLine();
                    sb.AppendLine("---");
                    sb.AppendLine();
                }
                sb.AppendLine(text);
            }
        }

        return sb.ToString();
    }

    public async Task<bool> IsAvailableAsync(CancellationToken ct = default)
    {
        try
        {
            var response = await _http.GetAsync($"{_config.BaseUrl}/health", ct);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }
}
