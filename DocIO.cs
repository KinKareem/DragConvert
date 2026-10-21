using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using DocumentFormat.OpenXml;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;
using Pkg = DocumentFormat.OpenXml.Packaging;
using W = DocumentFormat.OpenXml.Wordprocessing;

namespace DragConvert;

/// <summary>Engine bawaan: ekstrak teks polos dari dokumen lalu tulis ke format tujuan.</summary>
internal static class DocIO
{
    // ---------- BACA ----------
    public static List<string> ReadParagraphs(string path)
    {
        string ext = System.IO.Path.GetExtension(path).ToLowerInvariant();
        string text = ext switch
        {
            ".txt" => File.ReadAllText(path),
            ".docx" => ReadDocx(path),
            ".rtf" => RunSta(() => ReadRtf(path)),
            ".pdf" => ReadPdf(path),
            _ => throw new NotSupportedException($"Format {ext} butuh LibreOffice terpasang.")
        };
        return text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n').ToList();
    }

    private static string ReadDocx(string path)
    {
        using var doc = Pkg.WordprocessingDocument.Open(path, false);
        var body = doc.MainDocumentPart?.Document?.Body;
        if (body == null) return "";
        return string.Join("\n", body.Descendants<W.Paragraph>().Select(p => p.InnerText));
    }

    private static string ReadPdf(string path)
    {
        using var doc = PdfDocument.Open(path);
        var sb = new StringBuilder();
        foreach (var page in doc.GetPages())
        {
            sb.AppendLine(ContentOrderTextExtractor.GetText(page));
            sb.AppendLine();
        }
        return sb.ToString();
    }

    private static string ReadRtf(string path)
    {
        var rtb = new System.Windows.Controls.RichTextBox();
        var range = new System.Windows.Documents.TextRange(rtb.Document.ContentStart, rtb.Document.ContentEnd);
        using var fs = File.OpenRead(path);
        range.Load(fs, System.Windows.DataFormats.Rtf);
        return range.Text;
    }

    private static T RunSta<T>(Func<T> func)
    {
        T result = default!;
        Exception? error = null;
        var t = new Thread(() =>
        {
            try { result = func(); }
            catch (Exception ex) { error = ex; }
        });
        t.SetApartmentState(ApartmentState.STA);
        t.Start();
        t.Join();
        if (error != null) throw new InvalidOperationException(error.Message, error);
        return result;
    }

    // ---------- TULIS ----------
    public static void WriteTxt(string path, List<string> lines) =>
        File.WriteAllLines(path, lines, new UTF8Encoding(true));

    public static void WriteDocx(string path, List<string> lines)
    {
        using var doc = Pkg.WordprocessingDocument.Create(path, WordprocessingDocumentType.Document);
        var main = doc.AddMainDocumentPart();
        var body = new W.Body();
        foreach (var line in lines)
        {
            body.Append(new W.Paragraph(
                new W.Run(new W.Text(line) { Space = SpaceProcessingModeValues.Preserve })));
        }
        main.Document = new W.Document(body);
        main.Document.Save();
    }

    public static void WritePdf(string path, List<string> lines)
    {
        QuestPDF.Settings.License = LicenseType.Community;

        QuestPDF.Fluent.Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(2, Unit.Centimetre);
                page.DefaultTextStyle(x => x.FontSize(11).FontFamily("Segoe UI"));
                page.Content().Column(col =>
                {
                    foreach (var line in lines)
                        col.Item().Text(string.IsNullOrEmpty(line) ? " " : line);
                });
            });
        }).GeneratePdf(path);
    }

    public static void WriteImagesPdf(string path, List<(byte[] Data, bool Landscape)> pages)
    {
        QuestPDF.Settings.License = LicenseType.Community;

        QuestPDF.Fluent.Document.Create(container =>
        {
            foreach (var (data, landscape) in pages)
            {
                container.Page(page =>
                {
                    page.Size(landscape ? PageSizes.A4.Landscape() : PageSizes.A4);
                    page.Margin(1, Unit.Centimetre);
                    page.Content().Image(data).FitArea();
                });
            }
        }).GeneratePdf(path);
    }

    public static void WriteRtf(string path, List<string> lines)
    {
        var sb = new StringBuilder();
        sb.Append(@"{\rtf1\ansi\ansicpg1252\deff0{\fonttbl{\f0\fnil Calibri;}}\f0\fs22 ");
        foreach (var line in lines)
        {
            sb.Append(EscapeRtf(line));
            sb.Append("\\par\r\n");
        }
        sb.Append('}');
        File.WriteAllText(path, sb.ToString(), Encoding.ASCII);
    }

    private static string EscapeRtf(string s)
    {
        var sb = new StringBuilder();
        foreach (char c in s)
        {
            if (c == '\\' || c == '{' || c == '}') sb.Append('\\').Append(c);
            else if (c == '\t') sb.Append("\\tab ");
            else if (c < 0x20) continue;
            else if (c < 0x80) sb.Append(c);
            else sb.Append("\\u").Append((short)c).Append('?');
        }
        return sb.ToString();
    }
}