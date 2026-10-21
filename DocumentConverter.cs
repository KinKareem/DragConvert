using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace DragConvert;

internal sealed record ConvertResult(int Converted, int Skipped, int Failed, string? LastError);

/// <summary>
/// Konversi dokumen (DOCX, PDF, TXT, RTF).
/// Jika LibreOffice terpasang, dipakai untuk hasil yang mempertahankan format.
/// Jika tidak (atau sumbernya PDF), dipakai engine bawaan yang hanya membawa teks polos.
/// </summary>
internal static class DocumentConverter
{
    public static readonly string[] TargetFormats = { "DOCX", "PDF", "TXT", "RTF" };

    private static readonly HashSet<string> BuiltIn =
        new(StringComparer.OrdinalIgnoreCase) { ".docx", ".pdf", ".txt", ".rtf" };

    private static readonly HashSet<string> LibreOfficeOnly =
        new(StringComparer.OrdinalIgnoreCase) { ".doc", ".odt" };

    public static ConvertResult ConvertMany(IEnumerable<string> files, string target)
    {
        string ext = target.ToLowerInvariant();
        string? soffice = FindLibreOffice();
        int ok = 0, skipped = 0, failed = 0;
        string? lastError = null;

        foreach (var file in files)
        {
            string srcExt = Path.GetExtension(file).ToLowerInvariant();
            bool supported = BuiltIn.Contains(srcExt) || (soffice != null && LibreOfficeOnly.Contains(srcExt));

            if (!File.Exists(file) || !supported || srcExt == "." + ext)
            {
                skipped++;
                continue;
            }

            try
            {
                // PDF selalu lewat engine bawaan (hasil teks lebih bersih daripada impor PDF LibreOffice)
                if (soffice != null && srcExt != ".pdf")
                    ConvertWithLibreOffice(soffice, file, ext);
                else
                    ConvertBuiltIn(file, ext);
                ok++;
            }
            catch (Exception ex)
            {
                // Kalau LibreOffice gagal, coba engine bawaan
                if (soffice != null && BuiltIn.Contains(srcExt) && srcExt != ".pdf")
                {
                    try { ConvertBuiltIn(file, ext); ok++; continue; }
                    catch (Exception ex2) { ex = ex2; }
                }
                failed++;
                lastError = ex.Message;
            }
        }
        return new ConvertResult(ok, skipped, failed, lastError);
    }

    private static void ConvertBuiltIn(string src, string ext)
    {
        var lines = DocIO.ReadParagraphs(src);

        if (src.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase) && lines.All(string.IsNullOrWhiteSpace))
            throw new InvalidOperationException("PDF tidak berisi teks (mungkin hasil scan).");

        string outPath = ImageConverter.UniquePath(src, ext);
        switch (ext)
        {
            case "txt": DocIO.WriteTxt(outPath, lines); break;
            case "docx": DocIO.WriteDocx(outPath, lines); break;
            case "pdf": DocIO.WritePdf(outPath, lines); break;
            case "rtf": DocIO.WriteRtf(outPath, lines); break;
            default: throw new NotSupportedException($"Format {ext} belum didukung.");
        }
        ImageConverter.NotifyShell(outPath);
    }

    private static void ConvertWithLibreOffice(string soffice, string src, string ext)
    {
        string filter = ext switch
        {
            "pdf" => "pdf",
            "docx" => "docx:\"MS Word 2007 XML\"",
            "txt" => "txt:Text",
            "rtf" => "rtf:\"Rich Text Format\"",
            _ => throw new NotSupportedException($"Format {ext} belum didukung.")
        };

        string tmp = Path.Combine(Path.GetTempPath(), "DragConvert_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmp);
        try
        {
            string profileDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "DragConvert", "lo-profile");
            Directory.CreateDirectory(profileDir);

            var psi = new ProcessStartInfo(soffice)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            psi.ArgumentList.Add("-env:UserInstallation=" + new Uri(profileDir).AbsoluteUri);
            psi.ArgumentList.Add("--headless");
            psi.ArgumentList.Add("--convert-to");
            psi.ArgumentList.Add(filter);
            psi.ArgumentList.Add("--outdir");
            psi.ArgumentList.Add(tmp);
            psi.ArgumentList.Add(src);

            using var p = Process.Start(psi) ?? throw new InvalidOperationException("Gagal menjalankan LibreOffice.");
            p.BeginOutputReadLine();
            p.BeginErrorReadLine();
            if (!p.WaitForExit(120_000))
            {
                try { p.Kill(true); } catch { }
                throw new TimeoutException("LibreOffice terlalu lama merespons.");
            }

            string produced = Path.Combine(tmp, Path.GetFileNameWithoutExtension(src) + "." + ext);
            if (!File.Exists(produced))
                throw new InvalidOperationException("LibreOffice gagal mengonversi file.");

            string outPath = ImageConverter.UniquePath(src, ext);
            File.Move(produced, outPath);
            ImageConverter.NotifyShell(outPath);
        }
        finally
        {
            try { Directory.Delete(tmp, true); } catch { }
        }
    }

    private static string? FindLibreOffice()
    {
        string[] candidates =
        {
            @"C:\Program Files\LibreOffice\program\soffice.exe",
            @"C:\Program Files (x86)\LibreOffice\program\soffice.exe"
        };
        foreach (var c in candidates)
            if (File.Exists(c)) return c;

        foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(';'))
        {
            try
            {
                string f = Path.Combine(dir.Trim().Trim('"'), "soffice.exe");
                if (File.Exists(f)) return f;
            }
            catch { }
        }
        return null;
    }
}
