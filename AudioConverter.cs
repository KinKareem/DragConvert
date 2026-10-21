using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace DragConvert;

/// <summary>Konversi audio lewat FFmpeg (harus terpasang di PC).</summary>
internal static class AudioConverter
{
    public static readonly string[] TargetFormats = { "MP3", "WAV", "M4A", "FLAC", "OGG" };

    // Audio + video (untuk mengambil audionya saja)
    private static readonly HashSet<string> Supported = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp3", ".wav", ".flac", ".m4a", ".aac", ".ogg", ".opus", ".wma", ".aiff", ".aif",
        ".mp4", ".mkv", ".mov", ".webm", ".avi"
    };

    private const string InstallHint = "FFmpeg tidak ditemukan. Install: winget install Gyan.FFmpeg, lalu jalankan ulang aplikasi.";

    public static ConvertResult ConvertMany(IEnumerable<string> files, string target)
    {
        var list = files.ToList();
        string ext = target.ToLowerInvariant();

        string? ffmpeg = FindFfmpeg();
        if (ffmpeg == null)
            return new ConvertResult(0, 0, list.Count, InstallHint);

        int ok = 0, skipped = 0, failed = 0;
        string? lastError = null;

        foreach (var file in list)
        {
            string srcExt = Path.GetExtension(file);
            if (!File.Exists(file) || !Supported.Contains(srcExt) ||
                srcExt.TrimStart('.').Equals(ext, StringComparison.OrdinalIgnoreCase))
            {
                skipped++;
                continue;
            }

            try
            {
                ConvertOne(ffmpeg, file, ext);
                ok++;
            }
            catch (Exception ex)
            {
                failed++;
                lastError = ex.Message;
            }
        }
        return new ConvertResult(ok, skipped, failed, lastError);
    }

    private static void ConvertOne(string ffmpeg, string src, string ext)
    {
        string[] codec = ext switch
        {
            "mp3" => new[] { "-codec:a", "libmp3lame", "-q:a", "2" },
            "wav" => new[] { "-codec:a", "pcm_s16le" },
            "flac" => new[] { "-codec:a", "flac" },
            "m4a" => new[] { "-codec:a", "aac", "-b:a", "192k" },
            "ogg" => new[] { "-codec:a", "libvorbis", "-q:a", "5" },
            _ => throw new NotSupportedException($"Format {ext} belum didukung.")
        };

        string outPath = ImageConverter.UniquePath(src, ext);

        var psi = new ProcessStartInfo(ffmpeg)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true
        };
        foreach (var a in new[] { "-y", "-hide_banner", "-loglevel", "error", "-i", src, "-vn" })
            psi.ArgumentList.Add(a);
        foreach (var a in codec) psi.ArgumentList.Add(a);
        psi.ArgumentList.Add(outPath);

        using var p = Process.Start(psi) ?? throw new InvalidOperationException("Gagal menjalankan FFmpeg.");
        var errTask = p.StandardError.ReadToEndAsync();
        if (!p.WaitForExit(600_000))
        {
            try { p.Kill(true); } catch { }
            TryDelete(outPath);
            throw new TimeoutException("FFmpeg terlalu lama merespons.");
        }

        if (p.ExitCode != 0 || !File.Exists(outPath))
        {
            string err = errTask.Result.Trim();
            string lastLine = err.Split('\n').LastOrDefault()?.Trim() ?? "";
            TryDelete(outPath);
            throw new InvalidOperationException(
                string.IsNullOrEmpty(lastLine) ? "FFmpeg gagal mengonversi file." : lastLine);
        }

        ImageConverter.NotifyShell(outPath);
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }

    private static string? FindFfmpeg()
    {
        string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string[] candidates =
        {
            Path.Combine(AppContext.BaseDirectory, "ffmpeg.exe"),
            Path.Combine(local, "DragConvert", "ffmpeg", "ffmpeg.exe"),
            Path.Combine(local, "Microsoft", "WinGet", "Links", "ffmpeg.exe"),
            @"C:\ffmpeg\bin\ffmpeg.exe",
            @"C:\Program Files\ffmpeg\bin\ffmpeg.exe"
        };
        foreach (var c in candidates)
            if (File.Exists(c)) return c;

        foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(';'))
        {
            try
            {
                string f = Path.Combine(dir.Trim().Trim('"'), "ffmpeg.exe");
                if (File.Exists(f)) return f;
            }
            catch { }
        }
        return null;
    }
}
