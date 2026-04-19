using System.Text;
using DocumentFormat.OpenXml.Packaging;
using UglyToad.PdfPig;
using System.IO.Compression;

namespace SourceFilterMail.WinForms.Services;

public sealed class AttachmentTextService
{
    private static readonly string[] SupportedDirectExtensions = [".pdf", ".docx", ".txt"];
    private static readonly string[] ArchiveExtensions = [".zip", ".rar"];

    public async Task<string> BuildCombinedTextAsync(IEnumerable<string> attachmentPaths, Action<string> log)
    {
        var builder = new StringBuilder();

        foreach (var attachmentPath in attachmentPaths)
        {
            if (!File.Exists(attachmentPath))
            {
                continue;
            }

            var extension = Path.GetExtension(attachmentPath).ToLowerInvariant();
            if (SupportedDirectExtensions.Contains(extension))
            {
                builder.AppendLine(await ExtractTextByExtensionAsync(attachmentPath, extension));
                continue;
            }

            if (ArchiveExtensions.Contains(extension))
            {
                builder.AppendLine(await ExtractTextFromArchiveAsync(attachmentPath, log));
            }
        }

        return builder.ToString();
    }

    private static async Task<string> ExtractTextByExtensionAsync(string path, string extension)
    {
        try
        {
            return extension switch
            {
                ".pdf" => ExtractPdfText(path),
                ".docx" => ExtractDocxText(path),
                ".txt" => await File.ReadAllTextAsync(path),
                _ => string.Empty,
            };
        }
        catch
        {
            return string.Empty;
        }
    }

    private static string ExtractPdfText(string path)
    {
        var builder = new StringBuilder();
        using var document = PdfDocument.Open(path);
        foreach (var page in document.GetPages())
        {
            builder.AppendLine(page.Text);
        }

        return builder.ToString();
    }

    private static string ExtractDocxText(string path)
    {
        using var document = WordprocessingDocument.Open(path, false);
        return document.MainDocumentPart?.Document?.Body?.InnerText ?? string.Empty;
    }

    private static async Task<string> ExtractTextFromArchiveAsync(string archivePath, Action<string> log)
    {
        var extractFolder = Path.Combine(Path.GetTempPath(), "SourceFilterMail", Path.GetFileNameWithoutExtension(archivePath), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(extractFolder);

        try
        {
            var archiveExtension = Path.GetExtension(archivePath).ToLowerInvariant();
            if (archiveExtension == ".zip")
            {
                ZipFile.ExtractToDirectory(archivePath, extractFolder, true);
            }
            else if (archiveExtension == ".rar")
            {
                log($"RAR parsing chưa được hỗ trợ trực tiếp: {Path.GetFileName(archivePath)}");
                return string.Empty;
            }

            var files = Directory.GetFiles(extractFolder, "*", SearchOption.AllDirectories)
                .Where(path => SupportedDirectExtensions.Contains(Path.GetExtension(path).ToLowerInvariant()))
                .ToList();

            var builder = new StringBuilder();
            foreach (var file in files)
            {
                var fileExtension = Path.GetExtension(file).ToLowerInvariant();
                builder.AppendLine(await ExtractTextByExtensionAsync(file, fileExtension));
            }

            return builder.ToString();
        }
        catch
        {
            log($"Can not extract archive: {Path.GetFileName(archivePath)}");
            return string.Empty;
        }
        finally
        {
            try
            {
                Directory.Delete(extractFolder, true);
            }
            catch
            {
                // Ignore cleanup errors.
            }
        }
    }
}
