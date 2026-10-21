using System;
using System.Collections.Generic;
using System.IO;
using ImageMagick;

namespace DragConvert;

internal static class ImageConverter
{
    private static readonly HashSet<string> Supported = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".webp", ".bmp", ".gif", ".tif", ".tiff"
    };

    public sealed record Result(int Converted, int Skipped, int Failed, string? LastError);

    public static Result ConvertMany(IEnumerable<string> files, string target)
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
        return new Result(ok, skipped, failed, lastError);
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

        image.Write(UniquePath(src, ext), format);
    }

    private static string UniquePath(string src, string ext)
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
