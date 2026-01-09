using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.DocumentLayoutAnalysis.PageSegmenter;
using UglyToad.PdfPig.DocumentLayoutAnalysis.WordExtractor;

namespace StyloFlow.Converters.PdfPig;

/// <summary>
/// Extracts tables from PDFs as structured data.
/// Outputs can be converted to Parquet, CSV, JSON for DataSummarizer integration.
/// </summary>
public class PdfTableExtractor
{
    private readonly ILogger<PdfTableExtractor>? _logger;

    public PdfTableExtractor(ILogger<PdfTableExtractor>? logger = null)
    {
        _logger = logger;
    }

    /// <summary>
    /// Extract all tables from a PDF.
    /// </summary>
    public TableExtractionResult ExtractTables(string pdfPath, TableExtractionOptions? options = null)
    {
        options ??= new TableExtractionOptions();

        using var document = PdfDocument.Open(pdfPath);
        return ExtractTablesFromDocument(document, pdfPath, options);
    }

    /// <summary>
    /// Extract all tables from a PDF document.
    /// </summary>
    public TableExtractionResult ExtractTablesFromDocument(PdfDocument document, string sourcePath, TableExtractionOptions? options = null)
    {
        options ??= new TableExtractionOptions();
        var tables = new List<ExtractedTable>();

        for (var pageNum = 1; pageNum <= document.NumberOfPages; pageNum++)
        {
            try
            {
                var page = document.GetPage(pageNum);
                var pageTables = ExtractTablesFromPage(page, pageNum, options);
                tables.AddRange(pageTables);
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Failed to extract tables from page {Page}", pageNum);
            }
        }

        return new TableExtractionResult
        {
            SourcePath = sourcePath,
            Tables = tables,
            TotalTables = tables.Count,
            PagesWithTables = tables.Select(t => t.PageNumber).Distinct().Count()
        };
    }

    /// <summary>
    /// Extract tables from a single page.
    /// </summary>
    public List<ExtractedTable> ExtractTablesFromPage(Page page, int pageNum, TableExtractionOptions? options = null)
    {
        options ??= new TableExtractionOptions();
        var tables = new List<ExtractedTable>();

        try
        {
            // Use PdfPig's table extractor
            var detectedTables = page.GetTables();
            var tableIndex = 0;

            foreach (var table in detectedTables)
            {
                var extractedTable = ConvertTable(table, pageNum, tableIndex++, options);
                if (extractedTable != null && extractedTable.Rows.Count >= options.MinRows)
                {
                    tables.Add(extractedTable);
                }
            }
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "Table detection failed on page {Page}, trying heuristic extraction", pageNum);

            // Fallback to heuristic table detection
            if (options.UseHeuristicFallback)
            {
                var heuristicTables = ExtractTablesHeuristically(page, pageNum, options);
                tables.AddRange(heuristicTables);
            }
        }

        return tables;
    }

    private ExtractedTable? ConvertTable(
        UglyToad.PdfPig.DocumentLayoutAnalysis.TableExtractor.Table table,
        int pageNum,
        int tableIndex,
        TableExtractionOptions options)
    {
        var rows = new List<TableRow>();
        var columnCount = 0;

        foreach (var row in table.Rows)
        {
            var cells = new List<TableCell>();

            foreach (var cell in row.Cells)
            {
                var cellText = string.Join(" ", cell.Words.Select(w => w.Text)).Trim();

                cells.Add(new TableCell
                {
                    Text = cellText,
                    RowIndex = rows.Count,
                    ColumnIndex = cells.Count,
                    X = cell.BoundingBox.Left,
                    Y = cell.BoundingBox.Bottom,
                    Width = cell.BoundingBox.Width,
                    Height = cell.BoundingBox.Height
                });
            }

            if (cells.Count > columnCount)
                columnCount = cells.Count;

            rows.Add(new TableRow { Cells = cells });
        }

        if (rows.Count == 0)
            return null;

        // Detect if first row is header
        var hasHeader = options.AutoDetectHeader && DetectHeader(rows);

        // Extract column names
        var columnNames = hasHeader && rows.Count > 0
            ? rows[0].Cells.Select((c, i) => string.IsNullOrWhiteSpace(c.Text) ? $"Column{i + 1}" : SanitizeColumnName(c.Text)).ToList()
            : Enumerable.Range(1, columnCount).Select(i => $"Column{i}").ToList();

        return new ExtractedTable
        {
            TableId = $"page{pageNum}_table{tableIndex}",
            PageNumber = pageNum,
            TableIndex = tableIndex,
            Rows = rows,
            ColumnCount = columnCount,
            RowCount = rows.Count,
            ColumnNames = columnNames,
            HasHeader = hasHeader,
            BoundingBox = new TableBoundingBox
            {
                X = table.BoundingBox.Left,
                Y = table.BoundingBox.Bottom,
                Width = table.BoundingBox.Width,
                Height = table.BoundingBox.Height
            }
        };
    }

    private List<ExtractedTable> ExtractTablesHeuristically(Page page, int pageNum, TableExtractionOptions options)
    {
        // Heuristic: Look for grid-like arrangements of text
        var words = page.GetWords().ToList();
        if (words.Count < options.MinRows * 2) // Need enough words for a table
            return [];

        // Group words by approximate Y position (rows)
        var rowGroups = words
            .GroupBy(w => Math.Round(w.BoundingBox.Bottom / 10) * 10) // Round to nearest 10 points
            .Where(g => g.Count() >= 2) // At least 2 items per row
            .OrderByDescending(g => g.Key) // Top to bottom (PDF coordinates)
            .ToList();

        if (rowGroups.Count < options.MinRows)
            return [];

        // Check if rows have consistent column structure
        var columnCounts = rowGroups.Select(g => g.Count()).ToList();
        var mostCommonColumnCount = columnCounts
            .GroupBy(c => c)
            .OrderByDescending(g => g.Count())
            .First().Key;

        // Filter to rows with consistent column count
        var tableRows = rowGroups
            .Where(g => Math.Abs(g.Count() - mostCommonColumnCount) <= 1)
            .ToList();

        if (tableRows.Count < options.MinRows)
            return [];

        // Convert to table structure
        var rows = new List<TableRow>();
        foreach (var rowGroup in tableRows)
        {
            var cells = rowGroup
                .OrderBy(w => w.BoundingBox.Left)
                .Select((w, i) => new TableCell
                {
                    Text = w.Text,
                    RowIndex = rows.Count,
                    ColumnIndex = i,
                    X = w.BoundingBox.Left,
                    Y = w.BoundingBox.Bottom,
                    Width = w.BoundingBox.Width,
                    Height = w.BoundingBox.Height
                })
                .ToList();

            rows.Add(new TableRow { Cells = cells });
        }

        var hasHeader = options.AutoDetectHeader && DetectHeader(rows);
        var columnNames = hasHeader && rows.Count > 0
            ? rows[0].Cells.Select((c, i) => string.IsNullOrWhiteSpace(c.Text) ? $"Column{i + 1}" : SanitizeColumnName(c.Text)).ToList()
            : Enumerable.Range(1, mostCommonColumnCount).Select(i => $"Column{i}").ToList();

        return
        [
            new ExtractedTable
            {
                TableId = $"page{pageNum}_table0_heuristic",
                PageNumber = pageNum,
                TableIndex = 0,
                Rows = rows,
                ColumnCount = mostCommonColumnCount,
                RowCount = rows.Count,
                ColumnNames = columnNames,
                HasHeader = hasHeader,
                IsHeuristic = true
            }
        ];
    }

    private bool DetectHeader(List<TableRow> rows)
    {
        if (rows.Count < 2)
            return false;

        var firstRow = rows[0];
        var secondRow = rows[1];

        // Header detection heuristics:
        // 1. First row has different formatting (all caps, bold indicators)
        // 2. First row cells are shorter (labels vs data)
        // 3. Subsequent rows have numeric data but first doesn't

        var firstRowTexts = firstRow.Cells.Select(c => c.Text).ToList();
        var secondRowTexts = secondRow.Cells.Select(c => c.Text).ToList();

        // Check if first row is all text and second has numbers
        var firstRowNumeric = firstRowTexts.Count(t => double.TryParse(t, out _));
        var secondRowNumeric = secondRowTexts.Count(t => double.TryParse(t, out _));

        if (firstRowNumeric == 0 && secondRowNumeric > 0)
            return true;

        // Check if first row texts are shorter (typical of headers)
        var avgFirstRowLength = firstRowTexts.Average(t => t.Length);
        var avgSecondRowLength = secondRowTexts.Average(t => t.Length);

        if (avgFirstRowLength < avgSecondRowLength * 0.7)
            return true;

        return false;
    }

    private string SanitizeColumnName(string name)
    {
        // Remove special characters, trim, convert spaces to underscores
        var sanitized = new StringBuilder();
        foreach (var c in name.Trim())
        {
            if (char.IsLetterOrDigit(c))
                sanitized.Append(c);
            else if (c == ' ' || c == '_' || c == '-')
                sanitized.Append('_');
        }

        var result = sanitized.ToString().Trim('_');

        // Ensure it starts with a letter
        if (result.Length > 0 && char.IsDigit(result[0]))
            result = "Col_" + result;

        return string.IsNullOrEmpty(result) ? "Column" : result;
    }
}

/// <summary>
/// Options for table extraction.
/// </summary>
public class TableExtractionOptions
{
    /// <summary>Minimum rows for a valid table.</summary>
    public int MinRows { get; set; } = 2;

    /// <summary>Minimum columns for a valid table.</summary>
    public int MinColumns { get; set; } = 2;

    /// <summary>Auto-detect if first row is header.</summary>
    public bool AutoDetectHeader { get; set; } = true;

    /// <summary>Use heuristic extraction if standard fails.</summary>
    public bool UseHeuristicFallback { get; set; } = true;

    /// <summary>Include table position in output.</summary>
    public bool IncludePosition { get; set; } = true;
}

/// <summary>
/// Result of table extraction.
/// </summary>
public class TableExtractionResult
{
    public required string SourcePath { get; init; }
    public required IReadOnlyList<ExtractedTable> Tables { get; init; }
    public int TotalTables { get; init; }
    public int PagesWithTables { get; init; }
}

/// <summary>
/// A table extracted from a PDF.
/// </summary>
public class ExtractedTable
{
    public required string TableId { get; init; }
    public int PageNumber { get; init; }
    public int TableIndex { get; init; }
    public required IReadOnlyList<TableRow> Rows { get; init; }
    public int ColumnCount { get; init; }
    public int RowCount { get; init; }
    public required IReadOnlyList<string> ColumnNames { get; init; }
    public bool HasHeader { get; init; }
    public bool IsHeuristic { get; init; }
    public TableBoundingBox? BoundingBox { get; init; }

    /// <summary>
    /// Get data rows (excluding header if present).
    /// </summary>
    public IEnumerable<TableRow> DataRows => HasHeader ? Rows.Skip(1) : Rows;

    /// <summary>
    /// Convert to CSV format.
    /// </summary>
    public string ToCsv(bool includeHeader = true)
    {
        var sb = new StringBuilder();

        if (includeHeader)
        {
            sb.AppendLine(string.Join(",", ColumnNames.Select(EscapeCsvField)));
        }

        foreach (var row in DataRows)
        {
            var values = new List<string>();
            for (var i = 0; i < ColumnCount; i++)
            {
                var cell = row.Cells.FirstOrDefault(c => c.ColumnIndex == i);
                values.Add(EscapeCsvField(cell?.Text ?? ""));
            }
            sb.AppendLine(string.Join(",", values));
        }

        return sb.ToString();
    }

    /// <summary>
    /// Convert to JSON format.
    /// </summary>
    public string ToJson(bool indented = true)
    {
        var records = new List<Dictionary<string, object>>();

        foreach (var row in DataRows)
        {
            var record = new Dictionary<string, object>();
            for (var i = 0; i < ColumnCount; i++)
            {
                var columnName = i < ColumnNames.Count ? ColumnNames[i] : $"Column{i + 1}";
                var cell = row.Cells.FirstOrDefault(c => c.ColumnIndex == i);
                var value = cell?.Text ?? "";

                // Try to parse as number
                if (double.TryParse(value, out var numValue))
                    record[columnName] = numValue;
                else
                    record[columnName] = value;
            }
            records.Add(record);
        }

        return JsonSerializer.Serialize(records, new JsonSerializerOptions
        {
            WriteIndented = indented
        });
    }

    /// <summary>
    /// Convert to list of dictionaries (for Parquet conversion).
    /// </summary>
    public List<Dictionary<string, object?>> ToRecords()
    {
        var records = new List<Dictionary<string, object?>>();

        foreach (var row in DataRows)
        {
            var record = new Dictionary<string, object?>();
            for (var i = 0; i < ColumnCount; i++)
            {
                var columnName = i < ColumnNames.Count ? ColumnNames[i] : $"Column{i + 1}";
                var cell = row.Cells.FirstOrDefault(c => c.ColumnIndex == i);
                var value = cell?.Text ?? "";

                // Try to infer type
                if (string.IsNullOrEmpty(value))
                    record[columnName] = null;
                else if (double.TryParse(value, out var numValue))
                    record[columnName] = numValue;
                else if (DateTime.TryParse(value, out var dateValue))
                    record[columnName] = dateValue;
                else if (bool.TryParse(value, out var boolValue))
                    record[columnName] = boolValue;
                else
                    record[columnName] = value;
            }
            records.Add(record);
        }

        return records;
    }

    /// <summary>
    /// Get schema information for Parquet.
    /// </summary>
    public TableSchema GetSchema()
    {
        var columns = new List<ColumnSchema>();

        for (var i = 0; i < ColumnCount; i++)
        {
            var columnName = i < ColumnNames.Count ? ColumnNames[i] : $"Column{i + 1}";

            // Infer type from data
            var values = DataRows
                .Select(r => r.Cells.FirstOrDefault(c => c.ColumnIndex == i)?.Text ?? "")
                .Where(v => !string.IsNullOrEmpty(v))
                .ToList();

            var inferredType = InferColumnType(values);

            columns.Add(new ColumnSchema
            {
                Name = columnName,
                Index = i,
                InferredType = inferredType
            });
        }

        return new TableSchema
        {
            TableId = TableId,
            Columns = columns
        };
    }

    private ColumnDataType InferColumnType(List<string> values)
    {
        if (values.Count == 0)
            return ColumnDataType.String;

        var allNumeric = values.All(v => double.TryParse(v, out _));
        if (allNumeric)
        {
            var allIntegers = values.All(v => long.TryParse(v, out _));
            return allIntegers ? ColumnDataType.Integer : ColumnDataType.Double;
        }

        var allDates = values.All(v => DateTime.TryParse(v, out _));
        if (allDates)
            return ColumnDataType.DateTime;

        var allBool = values.All(v => bool.TryParse(v, out _));
        if (allBool)
            return ColumnDataType.Boolean;

        return ColumnDataType.String;
    }

    private static string EscapeCsvField(string field)
    {
        if (field.Contains(',') || field.Contains('"') || field.Contains('\n'))
        {
            return $"\"{field.Replace("\"", "\"\"")}\"";
        }
        return field;
    }
}

/// <summary>
/// A row in a table.
/// </summary>
public class TableRow
{
    public required IReadOnlyList<TableCell> Cells { get; init; }
}

/// <summary>
/// A cell in a table.
/// </summary>
public class TableCell
{
    public required string Text { get; init; }
    public int RowIndex { get; init; }
    public int ColumnIndex { get; init; }
    public double X { get; init; }
    public double Y { get; init; }
    public double Width { get; init; }
    public double Height { get; init; }
    public int? RowSpan { get; init; }
    public int? ColSpan { get; init; }
}

/// <summary>
/// Bounding box for a table.
/// </summary>
public class TableBoundingBox
{
    public double X { get; init; }
    public double Y { get; init; }
    public double Width { get; init; }
    public double Height { get; init; }
}

/// <summary>
/// Schema for a table (for Parquet generation).
/// </summary>
public class TableSchema
{
    public required string TableId { get; init; }
    public required IReadOnlyList<ColumnSchema> Columns { get; init; }
}

/// <summary>
/// Schema for a column.
/// </summary>
public class ColumnSchema
{
    public required string Name { get; init; }
    public int Index { get; init; }
    public ColumnDataType InferredType { get; init; }
}

/// <summary>
/// Data types for columns.
/// </summary>
public enum ColumnDataType
{
    String,
    Integer,
    Double,
    Boolean,
    DateTime
}
