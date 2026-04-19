using System.Net.Http.Json;
using System.Net.Mail;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using SourceFilterMail.WinForms.Models;

namespace SourceFilterMail.WinForms.Services;

public sealed class CandidateInfoExtractor
{
    private static readonly Regex PhoneRegex = new(@"(\+84|0)\d{9,10}", RegexOptions.Compiled);

    private static readonly Regex YearRegex = new(@"\b(19\d{2}|20[0-2]\d)\b", RegexOptions.Compiled);

    private static readonly Regex ContestCodeRegex = new(@"(?i)(mã\s*bài\s*dự\s*thi|ma\s*bai\s*du\s*thi|mã\s*bài)\s*[:\-]?\s*(?<value>[A-Z0-9\-_/]{3,})", RegexOptions.Compiled);

    private static readonly Regex NameRegex = new(@"(?i)(họ\s*và\s*tên\s*tác\s*giả|họ\s*và\s*tên|họ\s*tên|tác\s*giả)\s*[:\-]?\s*(?<value>.+)", RegexOptions.Compiled);

    private static readonly Regex AddressRegex = new(@"(?i)(địa\s*chỉ|dia\s*chi)\s*[:\-]?\s*(?<value>.+)", RegexOptions.Compiled);

    private static readonly string[] ProvinceHints =
    [
        "Hà Nội", "Hồ Chí Minh", "Đà Nẵng", "Cần Thơ", "Hải Phòng", "An Giang", "Bà Rịa", "Bạc Liêu", "Bắc Giang", "Bắc Kạn", "Bắc Ninh", "Bến Tre", "Bình Dương", "Bình Định", "Bình Phước", "Bình Thuận", "Cà Mau", "Cao Bằng", "Đắk Lắk", "Đắk Nông", "Điện Biên", "Đồng Nai", "Đồng Tháp", "Gia Lai", "Hà Giang", "Hà Nam", "Hà Tĩnh", "Hải Dương", "Hậu Giang", "Hòa Bình", "Hưng Yên", "Khánh Hòa", "Kiên Giang", "Kon Tum", "Lai Châu", "Lâm Đồng", "Lạng Sơn", "Lào Cai", "Long An", "Nam Định", "Nghệ An", "Ninh Bình", "Ninh Thuận", "Phú Thọ", "Phú Yên", "Quảng Bình", "Quảng Nam", "Quảng Ngãi", "Quảng Ninh", "Quảng Trị", "Sóc Trăng", "Sơn La", "Tây Ninh", "Thái Bình", "Thái Nguyên", "Thanh Hóa", "Thừa Thiên Huế", "Tiền Giang", "Trà Vinh", "Tuyên Quang", "Vĩnh Long", "Vĩnh Phúc", "Yên Bái"
    ];

    private readonly HttpClient _httpClient;

    public CandidateInfoExtractor(HttpClient? httpClient = null)
    {
        _httpClient = httpClient ?? new HttpClient();
    }

    public async Task<ContestEntry> ExtractAsync(GmailMailItem mail, string attachmentText, string? geminiApiKey, int index)
    {
        var sender = ParseSender(mail.SenderRaw);
        var source = BuildSource(mail, attachmentText, sender.Email);

        var entry = new ContestEntry
        {
            ContestCode = $"BDT-{index:0000}",
            AuthorName = sender.Name,
            Email = sender.Email,
            SentAt = mail.SentAt,
            SenderDisplayName = sender.Name,
            SenderAddress = sender.Email,
            Subject = mail.Subject,
        };

        ApplyRegexExtraction(entry, source);

        if (!string.IsNullOrWhiteSpace(geminiApiKey))
        {
            var geminiResult = await ExtractByGeminiAsync(geminiApiKey!, source);
            Merge(entry, geminiResult);
        }

        entry.AuthorName = CleanAuthorName(entry.AuthorName);

        if (string.IsNullOrWhiteSpace(entry.Email))
        {
            entry.Email = sender.Email;
        }

        return entry;
    }

    private static void ApplyRegexExtraction(ContestEntry entry, string source)
    {
        entry.Phone = PhoneRegex.Match(source).Value;
        entry.BirthYear = YearRegex.Match(source).Value;

        var contestCode = ContestCodeRegex.Match(source).Groups["value"].Value.Trim();
        if (!string.IsNullOrWhiteSpace(contestCode))
        {
            entry.ContestCode = contestCode;
        }

        var name = NameRegex.Match(source).Groups["value"].Value.Trim();
        if (!string.IsNullOrWhiteSpace(name))
        {
            entry.AuthorName = name;
        }

        var address = AddressRegex.Match(source).Groups["value"].Value.Trim();
        if (!string.IsNullOrWhiteSpace(address))
        {
            entry.Address = address;
        }

        if (string.IsNullOrWhiteSpace(entry.Province))
        {
            entry.Province = InferProvince(source, entry.Address);
        }
    }

    private static string BuildSource(GmailMailItem mail, string attachmentText, string senderEmail)
    {
        var builder = new StringBuilder();
        builder.AppendLine(mail.Subject);
        builder.AppendLine(mail.Snippet);
        builder.AppendLine(mail.PlainTextBody);
        builder.AppendLine(attachmentText);
        builder.AppendLine(senderEmail);
        return builder.ToString();
    }

    private async Task<ContestEntry> ExtractByGeminiAsync(string apiKey, string source)
    {
        try
        {
            var prompt = BuildGeminiPrompt(source);
            var url = $"https://generativelanguage.googleapis.com/v1beta/models/gemini-2.0-flash:generateContent?key={apiKey}";

            var request = new
            {
                contents = new[]
                {
                    new
                    {
                        parts = new[]
                        {
                            new
                            {
                                text = prompt,
                            },
                        },
                    },
                },
                generationConfig = new
                {
                    temperature = 0.1,
                    responseMimeType = "application/json",
                },
            };

            var response = await _httpClient.PostAsJsonAsync(url, request);
            if (!response.IsSuccessStatusCode)
            {
                return new ContestEntry();
            }

            var json = await response.Content.ReadFromJsonAsync<JsonElement>();
            var text = json.GetProperty("candidates")[0].GetProperty("content").GetProperty("parts")[0].GetProperty("text").GetString();
            if (string.IsNullOrWhiteSpace(text))
            {
                return new ContestEntry();
            }

            var parsed = JsonSerializer.Deserialize<ContestEntry>(text, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
            });

            return parsed ?? new ContestEntry();
        }
        catch
        {
            return new ContestEntry();
        }
    }

    private static string BuildGeminiPrompt(string source)
    {
        var clipped = source.Length <= 15000 ? source : source[..15000];

        return $"""
Bạn là hệ thống trích xuất thông tin bài dự thi.
Hãy đọc nội dung dưới đây và trả về duy nhất JSON object, không giải thích.
Các trường: contestCode, authorName, birthYear, phone, province, email, address.
Nếu không tìm thấy thì trả chuỗi rỗng.

Nội dung:
{clipped}
""";
    }

    private static void Merge(ContestEntry target, ContestEntry candidate)
    {
        if (!string.IsNullOrWhiteSpace(candidate.ContestCode)) target.ContestCode = candidate.ContestCode;
        if (!string.IsNullOrWhiteSpace(candidate.AuthorName)) target.AuthorName = candidate.AuthorName;
        if (!string.IsNullOrWhiteSpace(candidate.BirthYear)) target.BirthYear = candidate.BirthYear;
        if (!string.IsNullOrWhiteSpace(candidate.Phone)) target.Phone = candidate.Phone;
        if (!string.IsNullOrWhiteSpace(candidate.Province)) target.Province = candidate.Province;
        if (!string.IsNullOrWhiteSpace(candidate.Email)) target.Email = candidate.Email;
        if (!string.IsNullOrWhiteSpace(candidate.Address)) target.Address = candidate.Address;
    }

    private static string CleanAuthorName(string rawName)
    {
        if (string.IsNullOrWhiteSpace(rawName))
        {
            return string.Empty;
        }

        var compact = Regex.Replace(rawName, @"\s+", " ").Trim();
        var stopTokens = new[]
        {
            " dia chi", " dia chi", " nam sinh", " nam sinh", " so dien thoai", " so dien thoai",
            " email", " noi dung", " noi dung", " bai du thi", " bai du thi"
        };

        var normalized = compact.ToLowerInvariant();
        var cutIndex = compact.Length;
        foreach (var token in stopTokens)
        {
            var index = normalized.IndexOf(token, StringComparison.Ordinal);
            if (index >= 0 && index < cutIndex)
            {
                cutIndex = index;
            }
        }

        compact = compact[..cutIndex].Trim(' ', ',', ';', '-', ':', '.');

        var punctuationBreak = compact.IndexOfAny(['\n', '\r', ',', ';', '|', '/', '\\']);
        if (punctuationBreak > 0)
        {
            compact = compact[..punctuationBreak].Trim();
        }

        var words = compact.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length > 8)
        {
            compact = string.Join(' ', words.Take(8));
        }

        if (compact.Length > 80)
        {
            compact = compact[..80].Trim();
        }

        return compact;
    }

    private static (string Name, string Email) ParseSender(string senderRaw)
    {
        try
        {
            var address = new MailAddress(senderRaw);
            return (address.DisplayName, address.Address);
        }
        catch
        {
            return (string.Empty, senderRaw);
        }
    }

    private static string InferProvince(string source, string address)
    {
        foreach (var province in ProvinceHints)
        {
            if (source.Contains(province, StringComparison.OrdinalIgnoreCase) ||
                address.Contains(province, StringComparison.OrdinalIgnoreCase))
            {
                return province;
            }
        }

        return string.Empty;
    }
}
