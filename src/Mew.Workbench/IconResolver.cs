using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using Aprillz.MewUI;

namespace Mew.Workbench;

/// <summary>
/// 图标解析:自定义图标路径优先,否则提取命令对应可执行文件的图标;结果按路径缓存。
/// 是否视为 URL(不提取图标)由调用方按各自领域规则判定,本类不持有该判定。
/// </summary>
public sealed class IconResolver
{
    private readonly Dictionary<string, ImageSource?> _cache = new(StringComparer.OrdinalIgnoreCase);

    public ImageSource? Resolve(string? iconPath, string command, bool isUrlCommand)
    {
        if (!string.IsNullOrWhiteSpace(iconPath))
        {
            return FromPath(iconPath);
        }

        command = command.Trim();
        if (command.Length == 0 || isUrlCommand)
        {
            return null;
        }

        var exePath = ResolveExePath(command);
        return exePath is null ? null : FromPath(exePath);
    }

    private ImageSource? FromPath(string path)
    {
        if (_cache.TryGetValue(path, out var cached))
        {
            return cached;
        }

        var result = ExtractIcon(path);
        _cache[path] = result;
        return result;
    }

    /// <summary>从文件提取图标:图片文件直接加载,否则取关联可执行文件图标(无缓存)。</summary>
    public static ImageSource? ExtractIcon(string path)
    {
        try
        {
            if (IsImageFile(path))
            {
                return ImageSource.FromFile(path);
            }

            using var icon = Icon.ExtractAssociatedIcon(path);
            if (icon is null)
            {
                return null;
            }

            using var bitmap = icon.ToBitmap();
            return ImageSource.FromBgraPixels(bitmap.Width, bitmap.Height, BitmapToBgra(bitmap), hasAlpha: true);
        }
        catch (Exception ex) when (ex is ArgumentException or FileNotFoundException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static bool IsImageFile(string path) =>
        Path.GetExtension(path).ToLowerInvariant() is ".png" or ".jpg" or ".jpeg" or ".bmp" or ".gif" or ".ico" or ".webp";

    private static string? ResolveExePath(string command)
    {
        if (File.Exists(command))
        {
            return command;
        }

        var extensions = new[] { ".exe", ".bat", ".cmd", ".com", ".lnk" };
        foreach (var directory in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
        {
            var dir = directory.Trim();
            if (dir.Length == 0)
            {
                continue;
            }

            foreach (var extension in extensions)
            {
                var candidate = Path.Combine(dir, command + extension);
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
        }

        return null;
    }

    private static byte[] BitmapToBgra(Bitmap bitmap)
    {
        var width = bitmap.Width;
        var height = bitmap.Height;
        var bgra = new byte[width * height * 4];
        var data = bitmap.LockBits(new System.Drawing.Rectangle(0, 0, width, height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            var stride = data.Stride;
            var rowBytes = width * 4;
            for (var y = 0; y < height; y++)
            {
                Marshal.Copy(data.Scan0 + y * stride, bgra, y * rowBytes, rowBytes);
            }
        }
        finally
        {
            bitmap.UnlockBits(data);
        }

        return bgra;
    }
}
