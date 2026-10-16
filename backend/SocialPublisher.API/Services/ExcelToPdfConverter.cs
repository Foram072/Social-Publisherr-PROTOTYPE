using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using ExcelDataReader;

namespace SocialPublisher.API.Services
{
    /// <summary>
    /// Turns an Excel / CSV file into a simple PDF (one table per sheet) with NO PDF library,
    /// no Office and no LibreOffice. The PDF is written directly by this class.
    ///
    /// It keeps the cell TEXT only (no colours, charts, images or merged cells).
    /// Text uses the standard PDF font Helvetica, so characters outside Western European
    /// (Latin-1) are shown as "?".
    ///
    /// NuGet packages needed:
    ///   ExcelDataReader
    ///   ExcelDataReader.DataSet
    /// (PDFsharp / MigraDoc are NOT needed any more - you can uninstall them.)
    /// </summary>
    public static class ExcelToPdfConverter
    {
        private static readonly string[] Supported = { ".xls", ".xlsx", ".xlsm", ".csv" };

        private const int MaxRowsPerSheet = 2000;
        private const int MaxCellLength = 200;
        private const int MaxPages = 280;   // LinkedIn allows 300 pages per document

        // A4 landscape, in PDF points (1 pt = 1/72 inch)
        private const double PageW = 842;
        private const double PageH = 595;
        private const double Margin = 28;
        private const double Pad = 3;

        public static bool CanConvert(string extension) =>
            Supported.Contains((extension ?? string.Empty).ToLowerInvariant());

        /// <summary>Creates the PDF in a new temp folder and returns its path. The caller deletes the folder.</summary>
        public static string Convert(string sourcePath)
        {
            try
            {
                return ConvertCore(sourcePath);
            }
            catch (Exception ex)
            {
                var inner = ex.InnerException != null ? " -> " + ex.InnerException.Message : string.Empty;
                throw new Exception("Could not convert the spreadsheet to PDF: " + ex.Message + inner, ex);
            }
        }

        private static string ConvertCore(string sourcePath)
        {
            // Needed so older .xls files with special characters can be read
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

            DataSet data;
            using (var stream = File.Open(sourcePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                var isCsv = Path.GetExtension(sourcePath).Equals(".csv", StringComparison.OrdinalIgnoreCase);
                using var reader = isCsv
                    ? ExcelReaderFactory.CreateCsvReader(stream)
                    : ExcelReaderFactory.CreateReader(stream);
                data = reader.AsDataSet();
            }

            var sheets = new List<(string Name, List<string[]> Rows)>();
            foreach (DataTable sheet in data.Tables)
            {
                var rows = ReadRows(sheet);
                if (rows.Count > 0)
                    sheets.Add((sheet.TableName, rows));
            }

            if (sheets.Count == 0)
                throw new Exception("The spreadsheet has no data to post.");

            var pdfBytes = BuildPdf(sheets);

            var outDir = Path.Combine(Path.GetTempPath(), "li_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(outDir);
            var pdfPath = Path.Combine(outDir, Path.GetFileNameWithoutExtension(sourcePath) + ".pdf");
            File.WriteAllBytes(pdfPath, pdfBytes);

            return pdfPath;
        }

        // ── Read the sheet as text, dropping empty rows and empty columns ─────────
        private static List<string[]> ReadRows(DataTable sheet)
        {
            var all = new List<string[]>();
            var colCount = sheet.Columns.Count;

            foreach (DataRow row in sheet.Rows)
            {
                if (all.Count >= MaxRowsPerSheet) break;

                var cells = new string[colCount];
                var hasValue = false;
                for (var c = 0; c < colCount; c++)
                {
                    var text = CellText(row[c]);
                    cells[c] = text;
                    if (text.Length > 0) hasValue = true;
                }
                if (hasValue) all.Add(cells);
            }

            if (all.Count == 0) return all;

            // keep only columns that contain something
            var keep = Enumerable.Range(0, colCount)
                                 .Where(c => all.Any(r => r[c].Length > 0))
                                 .ToArray();

            return all.Select(r => keep.Select(c => r[c]).ToArray()).ToList();
        }

        private static string CellText(object? value)
        {
            if (value == null || value is DBNull) return string.Empty;

            string text = value switch
            {
                DateTime dt => dt.TimeOfDay == TimeSpan.Zero ? dt.ToString("d", CultureInfo.CurrentCulture) : dt.ToString("g", CultureInfo.CurrentCulture),
                double d => d.ToString("0.##########", CultureInfo.CurrentCulture),
                bool b => b ? "TRUE" : "FALSE",
                _ => System.Convert.ToString(value, CultureInfo.CurrentCulture) ?? string.Empty
            };

            text = text.Replace("\r", " ").Replace("\n", " ").Trim();
            return text.Length > MaxCellLength ? text.Substring(0, MaxCellLength) + "..." : text;
        }

        // ══════════════════════════════════════════════════════════════════════════
        //  Minimal PDF writer: tables made of rectangles + Helvetica text
        // ══════════════════════════════════════════════════════════════════════════
        private static byte[] BuildPdf(List<(string Name, List<string[]> Rows)> sheets)
        {
            var inv = CultureInfo.InvariantCulture;
            var pages = new List<string>();
            var page = new StringBuilder();
            double top = Margin;          // distance from the TOP edge of the page
            var stop = false;

            string N(double v) => v.ToString("0.##", inv);

            void AddText(string font, double size, double x, double y, string text)
            {
                page.Append("BT /").Append(font).Append(' ').Append(N(size)).Append(" Tf ")
                    .Append(N(x)).Append(' ').Append(N(y)).Append(" Td (")
                    .Append(EscapePdf(text)).Append(") Tj ET\n");
            }

            void FinishPage()
            {
                if (page.Length > 0)
                {
                    pages.Add(page.ToString());
                    page.Clear();
                }
            }

            foreach (var (name, rows) in sheets)
            {
                if (stop || pages.Count >= MaxPages) break;

                FinishPage();
                top = Margin;

                // sheet name
                AddText("F2", 12, Margin, PageH - top - 12, name);
                top += 24;

                var colCount = rows[0].Length;

                // smaller text when there are many columns
                double fontSize = colCount <= 6 ? 10 : colCount <= 10 ? 8 : 6.5;
                double charW = fontSize * 0.55;     // average width of one character
                double lineH = fontSize * 1.25;
                double usable = PageW - 2 * Margin;

                // column widths follow the longest text, clamped so nothing becomes too small or too wide
                var weights = Enumerable.Range(0, colCount)
                    .Select(c => (double)Math.Clamp(rows.Take(100).Max(r => r[c].Length), 4, 40))
                    .ToArray();
                var totalWeight = weights.Sum();
                var widths = weights.Select(w => usable * w / totalWeight).ToArray();

                List<string>[] WrapRow(string[] cells)
                {
                    var result = new List<string>[colCount];
                    for (var c = 0; c < colCount; c++)
                    {
                        var maxChars = Math.Max(1, (int)((widths[c] - 2 * Pad) / charW));
                        result[c] = WrapText(cells[c], maxChars, 8);
                    }
                    return result;
                }

                double RowHeight(List<string>[] lines) =>
                    lines.Max(l => l.Count) * lineH + 2 * Pad;

                void DrawRow(List<string>[] lines, double rowH, bool header)
                {
                    var x = Margin;
                    var yBottom = PageH - top - rowH;

                    for (var c = 0; c < colCount; c++)
                    {
                        if (header)
                        {
                            page.Append("0.88 g ")
                                .Append(N(x)).Append(' ').Append(N(yBottom)).Append(' ')
                                .Append(N(widths[c])).Append(' ').Append(N(rowH)).Append(" re f 0 g\n");
                        }

                        page.Append("0.6 G 0.5 w ")
                            .Append(N(x)).Append(' ').Append(N(yBottom)).Append(' ')
                            .Append(N(widths[c])).Append(' ').Append(N(rowH)).Append(" re S 0 G\n");

                        for (var l = 0; l < lines[c].Count; l++)
                        {
                            var baseline = PageH - top - Pad - fontSize * 0.9 - l * lineH;
                            AddText(header ? "F2" : "F1", fontSize, x + Pad, baseline, lines[c][l]);
                        }

                        x += widths[c];
                    }

                    top += rowH;
                }

                var headerLines = WrapRow(rows[0]);
                var headerH = RowHeight(headerLines);

                for (var r = 0; r < rows.Count; r++)
                {
                    var lines = r == 0 ? headerLines : WrapRow(rows[r]);
                    var rowH = r == 0 ? headerH : RowHeight(lines);

                    if (top + rowH > PageH - Margin)
                    {
                        if (pages.Count + 1 >= MaxPages) { stop = true; break; }

                        FinishPage();
                        top = Margin;

                        // repeat the header row on every new page
                        if (r > 0)
                            DrawRow(headerLines, headerH, header: true);
                    }

                    DrawRow(lines, rowH, header: r == 0);
                }
            }

            FinishPage();

            if (pages.Count == 0)
                throw new Exception("The spreadsheet has no data to post.");

            return WritePdfFile(pages);
        }

        /// <summary>Splits text into lines of at most maxChars characters (breaking long words).</summary>
        private static List<string> WrapText(string text, int maxChars, int maxLines)
        {
            var lines = new List<string>();
            var current = new StringBuilder();

            void Push()
            {
                lines.Add(current.ToString());
                current.Clear();
            }

            foreach (var word in (text ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                var w = word;

                // break words that are longer than one line
                while (w.Length > maxChars)
                {
                    if (current.Length > 0) Push();
                    lines.Add(w.Substring(0, maxChars));
                    w = w.Substring(maxChars);
                }

                if (current.Length == 0)
                    current.Append(w);
                else if (current.Length + 1 + w.Length <= maxChars)
                    current.Append(' ').Append(w);
                else
                {
                    Push();
                    current.Append(w);
                }
            }

            if (current.Length > 0 || lines.Count == 0)
                Push();

            if (lines.Count > maxLines)
            {
                lines = lines.Take(maxLines).ToList();
                var last = lines[maxLines - 1];
                lines[maxLines - 1] = (last.Length > 3 ? last.Substring(0, last.Length - 3) : last) + "...";
            }

            return lines;
        }

        /// <summary>Escapes text for a PDF string. Characters outside Latin-1 become "?".</summary>
        private static string EscapePdf(string text)
        {
            var sb = new StringBuilder(text.Length + 8);
            foreach (var ch in text)
            {
                if (ch == '\\' || ch == '(' || ch == ')') { sb.Append('\\').Append(ch); }
                else if (ch < 32 || ch > 255) { sb.Append('?'); }
                else { sb.Append(ch); }
            }
            return sb.ToString();
        }

        /// <summary>Writes the PDF file structure (catalog, fonts, pages, cross-reference table).</summary>
        private static byte[] WritePdfFile(List<string> pages)
        {
            var latin1 = Encoding.Latin1;
            using var ms = new MemoryStream();

            void W(string s)
            {
                var b = latin1.GetBytes(s);
                ms.Write(b, 0, b.Length);
            }

            var n = pages.Count;
            var offsets = new long[5 + 2 * n];     // object ids 1 .. 4 + 2n

            void Begin(int id)
            {
                offsets[id] = ms.Position;
                W(id + " 0 obj\n");
            }

            W("%PDF-1.4\n");

            Begin(1);
            W("<< /Type /Catalog /Pages 2 0 R >>\nendobj\n");

            Begin(2);
            var kids = string.Join(" ", Enumerable.Range(0, n).Select(i => (5 + 2 * i) + " 0 R"));
            W("<< /Type /Pages /Kids [" + kids + "] /Count " + n + " >>\nendobj\n");

            Begin(3);
            W("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>\nendobj\n");

            Begin(4);
            W("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica-Bold /Encoding /WinAnsiEncoding >>\nendobj\n");

            for (var i = 0; i < n; i++)
            {
                var pageId = 5 + 2 * i;
                var contentId = 6 + 2 * i;

                Begin(pageId);
                W("<< /Type /Page /Parent 2 0 R /MediaBox [0 0 842 595] " +
                  "/Resources << /Font << /F1 3 0 R /F2 4 0 R >> >> /Contents " + contentId + " 0 R >>\nendobj\n");

                var content = latin1.GetBytes(pages[i]);
                Begin(contentId);
                W("<< /Length " + content.Length + " >>\nstream\n");
                ms.Write(content, 0, content.Length);
                W("\nendstream\nendobj\n");
            }

            var xrefPos = ms.Position;
            W("xref\n0 " + offsets.Length + "\n");
            W("0000000000 65535 f \n");
            for (var id = 1; id < offsets.Length; id++)
                W(offsets[id].ToString("D10", CultureInfo.InvariantCulture) + " 00000 n \n");

            W("trailer\n<< /Size " + offsets.Length + " /Root 1 0 R >>\nstartxref\n" + xrefPos + "\n%%EOF\n");

            return ms.ToArray();
        }
    }
}