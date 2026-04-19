using System.IO.Compression;
using SharpCompress.Archives;

namespace SourceFilterMail.WinForms.Services;

/// <summary>
/// Detects image (.jpg/.jpeg/.png), video (.mp4), và file ghi âm (.m4a/.mp3/…) trong attachments,
/// kể cả trong zip/rar.
/// ZIP không đọc được → Excel tô đỏ ô cột; phát hiện được ảnh, video hoặc ghi âm → tô xanh lá.
/// </summary>
public sealed class AttachmentMediaAnalyzer
{
    private static readonly HashSet<string> ImageExtensions =
        new(StringComparer.OrdinalIgnoreCase) { ".jpg", ".jpeg", ".png", ".jng" };

    private const string VideoExtension = ".mp4";

    private static readonly HashSet<string> AudioExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".m4a",
        ".mp3",
        ".wav",
        ".aac",
        ".ogg",
        ".opus",
        ".flac",
        ".wma",
        ".amr",
        ".aiff",
        ".aif",
        ".m4b",
        ".weba",
        ".3gp",
        ".3g2",
        ".caf",
    };

    public MediaAnalysis Analyze(IEnumerable<string> attachmentPaths)
    {
        var paths = attachmentPaths.Where(p => !string.IsNullOrWhiteSpace(p) && File.Exists(p)).ToList();

        var hasImage = false;
        var hasVideo = false;
        var hasAudio = false;
        var hasUnreadableZip = false;
        var hasUnreadableRar = false;

        foreach (var path in paths)
        {
            var ext = Path.GetExtension(path);
            if (ImageExtensions.Contains(ext))
            {
                hasImage = true;
                continue;
            }

            if (string.Equals(ext, VideoExtension, StringComparison.OrdinalIgnoreCase))
            {
                hasVideo = true;
                continue;
            }

            if (AudioExtensions.Contains(ext))
            {
                hasAudio = true;
                continue;
            }

            if (string.Equals(ext, ".zip", StringComparison.OrdinalIgnoreCase))
            {
                var inner = ScanZipEntries(path);
                hasImage |= inner.HasImage;
                if (inner.HasMp4)
                {
                    hasVideo = true;
                }

                if (inner.HasAudio)
                {
                    hasAudio = true;
                }

                if (!inner.WasReadable)
                {
                    hasUnreadableZip = true;
                }

                continue;
            }

            if (string.Equals(ext, ".rar", StringComparison.OrdinalIgnoreCase))
            {
                var inner = ScanArchiveEntries(path);
                hasImage |= inner.HasImage;
                if (inner.HasMp4)
                {
                    hasVideo = true;
                }

                if (inner.HasAudio)
                {
                    hasAudio = true;
                }

                if (!inner.WasReadable)
                {
                    hasUnreadableRar = true;
                }
            }
        }

        var highlightMediaGreen = hasImage || hasVideo || hasAudio;
        var highlightZipRed = hasUnreadableZip;

        var parts = new List<string>();
        if (hasImage)
        {
            parts.Add("ảnh");
        }

        if (hasVideo)
        {
            parts.Add("video");
        }

        if (hasAudio)
        {
            parts.Add("ghi âm");
        }

        var display = parts.Count > 0 ? string.Join(", ", parts) : string.Empty;
        if (string.IsNullOrEmpty(display))
        {
            if (hasUnreadableZip)
            {
                display = "ZIP";
            }
            else if (hasUnreadableRar)
            {
                display = "RAR";
            }
        }

        return new MediaAnalysis(display, highlightZipRed, highlightMediaGreen);
    }

    /// <summary>
    /// Excel “Số lượng file và tên file”: mỗi file đính kèm; zip/rar thêm <c>tên.rar(a.mp4, b.png)</c>.
    /// </summary>
    public static string BuildInventorySummary(IEnumerable<string> attachmentPaths)
    {
        var paths = attachmentPaths
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(p => p.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(p => Path.GetFileName(p), StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (paths.Count == 0)
        {
            return "0 file";
        }

        var parts = new List<string>(paths.Count);
        foreach (var path in paths)
        {
            parts.Add(FormatInventoryFragment(path));
        }

        return $"{paths.Count} file: {string.Join("; ", parts)}";
    }

    private static string FormatInventoryFragment(string path)
    {
        var fileName = Path.GetFileName(path);
        if (string.IsNullOrEmpty(fileName))
        {
            return path;
        }

        if (!File.Exists(path))
        {
            return fileName;
        }

        var ext = Path.GetExtension(path);
        if (string.Equals(ext, ".zip", StringComparison.OrdinalIgnoreCase))
        {
            var inner = TryListZipInnerFileNames(path);
            if (inner is null)
            {
                return $"{fileName}(không đọc được)";
            }

            if (inner.Count == 0)
            {
                return $"{fileName}(trống)";
            }

            return $"{fileName}({string.Join(", ", inner)})";
        }

        if (string.Equals(ext, ".rar", StringComparison.OrdinalIgnoreCase))
        {
            var inner = TryListRarInnerFileNames(path);
            if (inner is null)
            {
                return $"{fileName}(không đọc được)";
            }

            if (inner.Count == 0)
            {
                return $"{fileName}(trống)";
            }

            return $"{fileName}({string.Join(", ", inner)})";
        }

        return fileName;
    }

    private static List<string>? TryListZipInnerFileNames(string path)
    {
        try
        {
            using var zip = ZipFile.OpenRead(path);
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in zip.Entries)
            {
                if (string.IsNullOrEmpty(entry.Name))
                {
                    continue;
                }

                var normalized = entry.FullName.Replace('\\', '/').TrimEnd('/');
                var baseName = Path.GetFileName(normalized);
                if (!string.IsNullOrEmpty(baseName))
                {
                    names.Add(baseName);
                }
            }

            return names.OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();
        }
        catch
        {
            return null;
        }
    }

    private static List<string>? TryListRarInnerFileNames(string path)
    {
        try
        {
            using var archive = ArchiveFactory.OpenArchive(path);
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in archive.Entries.Where(e => !e.IsDirectory))
            {
                var key = entry.Key ?? string.Empty;
                var normalized = key.Replace('\\', '/').TrimEnd('/');
                var baseName = Path.GetFileName(normalized);
                if (!string.IsNullOrEmpty(baseName))
                {
                    names.Add(baseName);
                }
            }

            return names.OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();
        }
        catch
        {
            return null;
        }
    }

    private static (bool WasReadable, bool HasImage, bool HasMp4, bool HasAudio) ScanZipEntries(string path)
    {
        try
        {
            using var zip = ZipFile.OpenRead(path);
            var hasImage = false;
            var hasMp4 = false;
            var hasAudio = false;
            foreach (var entry in zip.Entries)
            {
                if (string.IsNullOrEmpty(entry.Name))
                {
                    continue;
                }

                var innerExt = Path.GetExtension(entry.FullName);
                if (ImageExtensions.Contains(innerExt))
                {
                    hasImage = true;
                }

                if (string.Equals(innerExt, VideoExtension, StringComparison.OrdinalIgnoreCase))
                {
                    hasMp4 = true;
                }

                if (AudioExtensions.Contains(innerExt))
                {
                    hasAudio = true;
                }
            }

            return (true, hasImage, hasMp4, hasAudio);
        }
        catch
        {
            return (false, false, false, false);
        }
    }

    private static (bool WasReadable, bool HasImage, bool HasMp4, bool HasAudio) ScanArchiveEntries(string path)
    {
        try
        {
            using var archive = ArchiveFactory.OpenArchive(path);
            var hasImage = false;
            var hasMp4 = false;
            var hasAudio = false;
            foreach (var entry in archive.Entries.Where(e => !e.IsDirectory))
            {
                var innerExt = Path.GetExtension(entry.Key ?? string.Empty);
                if (ImageExtensions.Contains(innerExt))
                {
                    hasImage = true;
                }

                if (string.Equals(innerExt, VideoExtension, StringComparison.OrdinalIgnoreCase))
                {
                    hasMp4 = true;
                }

                if (AudioExtensions.Contains(innerExt))
                {
                    hasAudio = true;
                }
            }

            return (true, hasImage, hasMp4, hasAudio);
        }
        catch
        {
            return (false, false, false, false);
        }
    }

    public readonly record struct MediaAnalysis(string DisplayText, bool HighlightZipRed, bool HighlightMediaGreen);
}
