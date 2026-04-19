using System.Net.Mail;
using System.Text;
using Google;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Gmail.v1;
using Google.Apis.Gmail.v1.Data;
using Google.Apis.Services;
using Google.Apis.Util.Store;
using SourceFilterMail.WinForms.Models;
using GmailMessage = Google.Apis.Gmail.v1.Data.Message;

namespace SourceFilterMail.WinForms.Services;

public sealed class GmailReaderService
{
    private static readonly string[] Scopes = [GmailService.Scope.GmailReadonly];

    public async Task<List<GmailMailItem>> ReadDailyMailsAsync(
        string credentialFilePath,
        DateTime day,
        string downloadFolder,
        Action<string> log,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(downloadFolder);
        var service = await BuildServiceAsync(credentialFilePath, cancellationToken);

        var dayStart = day.Date;
        var dayEnd = dayStart.AddDays(1);
        var result = new List<GmailMailItem>();
        var seenMessageIds = new HashSet<string>(StringComparer.Ordinal);

        // 2. Epoch
        var startEpoch = new DateTimeOffset(dayStart).ToUnixTimeSeconds();
        var endEpoch = new DateTimeOffset(dayEnd).ToUnixTimeSeconds();

        // 3. Query chỉ lấy INBOX; loại mail hệ thống Google (no-reply@accounts.google.com)
        var query =
            $"in:inbox after:{startEpoch} before:{endEpoch} -from:no-reply@accounts.google.com";
        var logText =
            $"in:inbox after:{dayStart} before:{dayEnd}, exclude from:no-reply@accounts.google.com";
        log($"Gmail query (inbox only): {logText}");

        await ReadByQueryAsync(
            service,
            query,
            downloadFolder,
            result,
            seenMessageIds,
            log,
            cancellationToken
        );

        // 4. Filter + sort
        var filtered = result
            .Where(m =>
            {
                if (IsGoogleAccountsNoReplySender(m.SenderAddress))
                {
                    return false;
                }

                if (!m.SentAt.HasValue)
                    return false;

                var t = m.SentAt.Value.Kind == DateTimeKind.Utc
                    ? m.SentAt.Value.ToLocalTime()
                    : m.SentAt.Value;

                return t >= dayStart && t < dayEnd;
            })
            .OrderBy(m =>
            {
                if (!m.SentAt.HasValue)
                    return DateTime.MinValue;

                return m.SentAt.Value.Kind == DateTimeKind.Utc
                    ? m.SentAt.Value.ToLocalTime()
                    : m.SentAt.Value;
            })
            .ToList();
        return filtered;
    }

    private static bool IsGoogleAccountsNoReplySender(string? senderAddress)
    {
        if (string.IsNullOrWhiteSpace(senderAddress))
        {
            return false;
        }

        return string.Equals(
            senderAddress.Trim(),
            "no-reply@accounts.google.com",
            StringComparison.OrdinalIgnoreCase);
    }

    private static async Task ReadByQueryAsync(
        GmailService service,
        string query,
        string downloadFolder,
        List<GmailMailItem> result,
        HashSet<string> seenMessageIds,
        Action<string> log,
        CancellationToken cancellationToken)
    {
        var listRequest = service.Users.Messages.List("me");
        listRequest.Q = query;
        listRequest.MaxResults = 500;

        string? nextPageToken = null;
        do
        {
            listRequest.PageToken = nextPageToken;
            var listResponse = await listRequest.ExecuteAsync(cancellationToken);
            if (listResponse.Messages is null)
            {
                break;
            }

            foreach (var messageRef in listResponse.Messages)
            {
                if (string.IsNullOrWhiteSpace(messageRef.Id) || !seenMessageIds.Add(messageRef.Id))
                {
                    continue;
                }

                cancellationToken.ThrowIfCancellationRequested();
                var message = await service.Users.Messages.Get("me", messageRef.Id).ExecuteAsync(cancellationToken);
                var item = await BuildMailItemAsync(service, message, downloadFolder, log, cancellationToken);
                result.Add(item);
            }

            nextPageToken = listResponse.NextPageToken;
        }
        while (!string.IsNullOrWhiteSpace(nextPageToken));
    }

    private static async Task<GmailService> BuildServiceAsync(string credentialFilePath, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(credentialFilePath, FileMode.Open, FileAccess.Read);
        var secrets = GoogleClientSecrets.FromStream(stream).Secrets;

        var tokenPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "SourceFilterMail",
            "gmail-token");

        var credential = await GoogleWebAuthorizationBroker.AuthorizeAsync(
            secrets,
            Scopes,
            "contest-admin",
            cancellationToken,
            new FileDataStore(tokenPath, true));

        return new GmailService(new BaseClientService.Initializer
        {
            HttpClientInitializer = credential,
            ApplicationName = "SourceFilterMail",
        });
    }

    private static async Task<GmailMailItem> BuildMailItemAsync(
        GmailService service,
        GmailMessage message,
        string downloadFolder,
        Action<string> log,
        CancellationToken cancellationToken)
    {
        var senderRaw = GetHeader(message, "From");
        var senderAddress = ParseSenderAddress(senderRaw);
        var item = new GmailMailItem
        {
            MessageId = message.Id ?? Guid.NewGuid().ToString("N"),
            Subject = GetHeader(message, "Subject"),
            SenderRaw = senderRaw,
            SenderAddress = senderAddress,
            SentAt = ResolveSentAt(message),
            Snippet = message.Snippet ?? string.Empty,
            PlainTextBody = ReadBodyText(message.Payload),
        };

        if (message.Payload is null)
        {
            return item;
        }

        var senderFolderName = string.IsNullOrWhiteSpace(senderAddress) ? "unknown-sender" : SanitizeFileName(senderAddress);
        var saveFolder = Path.Combine(downloadFolder, senderFolderName, SanitizeFileName(item.MessageId));
        Directory.CreateDirectory(saveFolder);
        item.AttachmentFolderPath = saveFolder;

        await DownloadAttachmentsRecursiveAsync(service, message.Id!, message.Payload, saveFolder, item.SavedAttachmentPaths, log, cancellationToken);

        return item;
    }

    private static async Task DownloadAttachmentsRecursiveAsync(
        GmailService service,
        string messageId,
        MessagePart part,
        string saveFolder,
        List<string> savedPaths,
        Action<string> log,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(part.Filename) && !string.IsNullOrWhiteSpace(part.Body?.AttachmentId))
        {
            var getRequest = service.Users.Messages.Attachments.Get("me", messageId, part.Body.AttachmentId);
            var attachment = await getRequest.ExecuteAsync(cancellationToken);
            if (!string.IsNullOrWhiteSpace(attachment.Data))
            {
                var bytes = Convert.FromBase64String(NormalizeBase64(attachment.Data));
                var fileName = EnsureUniquePath(Path.Combine(saveFolder, SanitizeFileName(part.Filename)));
                await File.WriteAllBytesAsync(fileName, bytes, cancellationToken);
                savedPaths.Add(fileName);
                log($"Downloaded: {Path.GetFileName(fileName)}");
            }
        }

        if (part.Parts is null)
        {
            return;
        }

        foreach (var child in part.Parts)
        {
            await DownloadAttachmentsRecursiveAsync(service, messageId, child, saveFolder, savedPaths, log, cancellationToken);
        }
    }

    private static string ReadBodyText(MessagePart? payload)
    {
        if (payload is null)
        {
            return string.Empty;
        }

        if (string.Equals(payload.MimeType, "text/plain", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(payload.Body?.Data))
        {
            try
            {
                var bytes = Convert.FromBase64String(NormalizeBase64(payload.Body.Data));
                return Encoding.UTF8.GetString(bytes);
            }
            catch
            {
                return string.Empty;
            }
        }

        if (payload.Parts is null)
        {
            return string.Empty;
        }

        foreach (var child in payload.Parts)
        {
            var text = ReadBodyText(child);
            if (!string.IsNullOrWhiteSpace(text))
            {
                return text;
            }
        }

        return string.Empty;
    }

    private static string GetHeader(GmailMessage message, string key)
    {
        return message.Payload?.Headers?.FirstOrDefault(h => string.Equals(h.Name, key, StringComparison.OrdinalIgnoreCase))?.Value ?? string.Empty;
    }

    private static DateTime? ParseGmailDate(string rawDate)
    {
        return DateTimeOffset.TryParse(rawDate, out var dt) ? dt.LocalDateTime : null;
    }

    private static DateTime? ResolveSentAt(GmailMessage message)
    {
        var fromHeader = ParseGmailDate(GetHeader(message, "Date"));
        if (fromHeader.HasValue)
        {
            return fromHeader.Value;
        }

        if (message.InternalDate.HasValue)
        {
            return DateTimeOffset.FromUnixTimeMilliseconds(message.InternalDate.Value).LocalDateTime;
        }

        return null;
    }

    private static string ParseSenderAddress(string senderRaw)
    {
        if (string.IsNullOrWhiteSpace(senderRaw))
        {
            return string.Empty;
        }

        try
        {
            return new MailAddress(senderRaw).Address;
        }
        catch
        {
            return senderRaw;
        }
    }

    private static string NormalizeBase64(string value)
    {
        var normalized = value.Replace('-', '+').Replace('_', '/');
        return normalized.PadRight(normalized.Length + (4 - normalized.Length % 4) % 4, '=');
    }

    private static string SanitizeFileName(string input)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return string.Join("_", input.Split(invalid, StringSplitOptions.RemoveEmptyEntries)).Trim();
    }

    private static string EnsureUniquePath(string path)
    {
        if (!File.Exists(path))
        {
            return path;
        }

        var folder = Path.GetDirectoryName(path) ?? string.Empty;
        var fileName = Path.GetFileNameWithoutExtension(path);
        var extension = Path.GetExtension(path);
        var index = 1;

        while (true)
        {
            var candidate = Path.Combine(folder, $"{fileName}_{index}{extension}");
            if (!File.Exists(candidate))
            {
                return candidate;
            }

            index++;
        }
    }
}
