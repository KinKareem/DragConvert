using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using ImageMagick;

namespace DragConvert;

internal static class ImageConverter
{
    private static readonly HashSet<string> Supported = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".webp", ".bmp", ".gif", ".tif", ".tiff"
    };

    public static ConvertResult ConvertMany(IEnumerable<string> files, string target)
    {
        string ext = target.ToLowerInvariant();
        int ok = 0, skipped = 0, failed = 0;
        string? lastError = null;

        foreach (var file in files)
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
                ConvertOne(file, ext);
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

    private static void ConvertOne(string src, string ext)
    {
        using var image = new MagickImage(src);

        MagickFormat format;
        switch (ext)
        {
            case "jpg":
            case "jpeg":
                format = MagickFormat.Jpeg;
                image.Quality = 92;
                if (image.HasAlpha) // JPEG tidak punya transparansi -> latar putih
                {
                    image.BackgroundColor = MagickColors.White;
                    image.Alpha(AlphaOption.Remove);
                }
                break;
            case "webp":
                format = MagickFormat.WebP;
                image.Quality = 90;
                break;
            case "png":
                format = MagickFormat.Png;
                break;
            default:
                throw new NotSupportedException($"Format {ext} belum didukung.");
        }

        string outPath = UniquePath(src, ext);
        image.Write(outPath, format);
        NotifyShell(outPath);
    }

    // Beri tahu Explorer/Desktop ada file baru supaya langsung tampil tanpa refresh
    internal static void NotifyShell(string filePath)
    {
        const int SHCNE_CREATE = 0x00000002;
        const int SHCNE_UPDATEDIR = 0x00001000;
        const uint SHCNF_PATHW = 0x0005;
        const uint SHCNF_FLUSHNOWAIT = 0x2000;

        SHChangeNotify(SHCNE_CREATE, SHCNF_PATHW | SHCNF_FLUSHNOWAIT, filePath, IntPtr.Zero);
        string? dir = Path.GetDirectoryName(filePath);
        if (dir != null)
            SHChangeNotify(SHCNE_UPDATEDIR, SHCNF_PATHW | SHCNF_FLUSHNOWAIT, dir, IntPtr.Zero);
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern void SHChangeNotify(int wEventId, uint uFlags, string dwItem1, IntPtr dwItem2);

    internal static string UniquePath(string src, string ext)
    {
        string dir = Path.GetDirectoryName(src)!;
        string name = Path.GetFileNameWithoutExtension(src);
        string result = Path.Combine(dir, $"{name}.{ext}");
        int i = 1;
        while (File.Exists(result))
            result = Path.Combine(dir, $"{name} ({i++}).{ext}");
        return result;
    }
}