using System.Net.Http.Json;
using System.Net.Mail;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using SourceFilterMail.WinForms.Models;

namespace SourceFilterMail.WinForms.Services;

public sealed class CandidateInfoExtractor
{
    private const string FailureTypeInvalidEssay = "invalid_essay";
    private const string FailureTypeApiError = "api_error";
    private const string GeminiModel = "gemini-2.0-flash";
    private const int MaxEssayWords = 1500;
    private static readonly TimeSpan GeminiMinInterval = TimeSpan.FromSeconds(5);
    private static readonly SemaphoreSlim GeminiRequestGate = new(1, 1);
    private static DateTime _lastGeminiRequestUtc = DateTime.MinValue;
    private static readonly Regex PhoneRegex = new(@"(\+84|0)\d{9,10}", RegexOptions.Compiled);

    private static readonly Regex YearRegex = new(@"\b(19\d{2}|20[0-2]\d)\b", RegexOptions.Compiled);

    private static readonly Regex ContestCodeRegex = new(@"(?i)(mã\s*bài\s*dự\s*thi|ma\s*bai\s*du\s*thi|mã\s*bài)\s*[:\-]?\s*(?<value>[A-Z0-9\-_/]{3,})", RegexOptions.Compiled);

    private static readonly Regex NameRegex = new(@"(?i)(họ\s*và\s*tên\s*tác\s*giả|họ\s*và\s*tên|họ\s*tên|tác\s*giả)\s*[:\-]?\s*(?<value>.+)", RegexOptions.Compiled);

    private static readonly Regex AddressRegex = new(@"(?i)(địa\s*chỉ|dia\s*chi)\s*[:\-]?\s*(?<value>.+)", RegexOptions.Compiled);
    private static readonly Regex WordTokenRegex = new(@"[\p{L}\p{N}]+", RegexOptions.Compiled);

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

    public async Task<(string ScoreText, string DetailText, string ApiLog)> ScoreEssayAsync(string source, string? geminiApiKey)
    {
        if (string.IsNullOrWhiteSpace(source) || string.IsNullOrWhiteSpace(geminiApiKey))
        {
            return (string.Empty, string.Empty, "Gemini skipped: missing source text or API key.");
        }

        var preValidationResult = PreValidateEssay(source);
        if (preValidationResult is not null)
        {
            return (ToScoreCellValue(preValidationResult), ToDetailCellValue(preValidationResult), BuildApiLog(preValidationResult));
        }

        var result = await ScoreByGeminiAsync(geminiApiKey!, source);
        return (ToScoreCellValue(result), ToDetailCellValue(result), BuildApiLog(result));
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

            var model = GeminiModel;
            var url = $"https://generativelanguage.googleapis.com/v1beta/models/{model}:generateContent?key={apiKey}";
            var requestJson = JsonSerializer.Serialize(request);
            await WaitForGeminiRequestSlotAsync();
            var response = await _httpClient.PostAsync(url, new StringContent(requestJson, Encoding.UTF8, "application/json"));
            if (!response.IsSuccessStatusCode)
            {
                return new ContestEntry();
            }

            var json = await response.Content.ReadFromJsonAsync<JsonElement>();
            var rawText = json.GetProperty("candidates")[0].GetProperty("content").GetProperty("parts")[0].GetProperty("text").GetString();
            var text = rawText is null ? null : Regex.Replace(rawText, @"[\u200B-\u200F\uFEFF]", "");
            if (string.IsNullOrWhiteSpace(text))
            {
                return new ContestEntry();
            }

            var parsed = JsonSerializer.Deserialize<ContestEntry>(text, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
            });

            if (parsed is not null)
            {
                return parsed;
            }

            return new ContestEntry();
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

    private async Task<EssayScoringResult> ScoreByGeminiAsync(string apiKey, string source)
    {
        try
        {
            var prompt = BuildScoringPrompt(source);
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

            var attempts = new List<string>();
            var model = GeminiModel;
            var requestJson = JsonSerializer.Serialize(request);
            const int maxRetries = 1;
            for (var attempt = 1; attempt <= maxRetries; attempt++)
            {
                var url = $"https://generativelanguage.googleapis.com/v1beta/models/{model}:generateContent?key={apiKey}";
                await WaitForGeminiRequestSlotAsync();
                var response = await _httpClient.PostAsync(url, new StringContent(requestJson, Encoding.UTF8, "application/json"));
                var responseText = await response.Content.ReadAsStringAsync();
                var status = $"{(int)response.StatusCode} {response.StatusCode}";
                attempts.Add($"[{model}] attempt {attempt}/{maxRetries} {status}");

                if (response.IsSuccessStatusCode)
                {
                    using var jsonDoc = JsonDocument.Parse(responseText);
                    var rawText = jsonDoc.RootElement.GetProperty("candidates")[0].GetProperty("content").GetProperty("parts")[0].GetProperty("text").GetString();
                    var text = rawText is null ? null : Regex.Replace(rawText, @"[\u200B-\u200F\uFEFF]", "");
                    if (string.IsNullOrWhiteSpace(text))
                    {
                        return EssayScoringResult.Invalid(
                            "Khong nhan duoc ket qua cham",
                            status,
                            $"Model={model}{Environment.NewLine}{responseText}",
                            FailureTypeApiError);
                    }

                    var parsed = ParseScoringResult(text, status, $"Model={model}{Environment.NewLine}{responseText}");
                    parsed.UsedModel = model;
                    return parsed;
                }

                if ((int)response.StatusCode == 429 && attempt < maxRetries)
                {
                    var delayMs = 1200 * (int)Math.Pow(2, attempt - 1);
                    await Task.Delay(delayMs);
                    continue;
                }

                break;
            }

            return EssayScoringResult.Invalid(
                "Khong goi duoc Gemini",
                "Gemini model failed",
                string.Join(Environment.NewLine, attempts),
                FailureTypeApiError);
        }
        catch (Exception ex)
        {
            return EssayScoringResult.Invalid("Khong doc duoc ket qua cham", "Exception", ex.ToString(), FailureTypeApiError);
        }
    }

    private static string BuildScoringPrompt(string source)
    {
        var clipped = source.Length <= 25000 ? source : source[..25000];
        return $$"""
Bạn là giám khảo cuộc thi viết văn tại Việt Nam.
NHIỆM VỤ: Đánh giá bài dự thi theo rubric và trả về JSON duy nhất.

BƯỚC 1: KIỂM TRA HỢP LỆ
- Không viết bằng tiếng Việt
- Không phải văn xuôi (ví dụ: thơ, kịch)
- Không đúng chủ đề: "Bữa cơm gia đình ấm áp yêu thương"
- Nội dung không phù hợp thuần phong mỹ tục Việt Nam
- Vượt quá 1500 từ

Nếu không hợp lệ:
{"isValid":false,"reasons":["..."]}

BƯỚC 2: CHẤM ĐIỂM (nếu hợp lệ)
1.1 (0-3): Nội dung tập trung đúng 1 trong các nội dung về gia đình
1.2 (0-2): Thể hiện tình cảm gia đình chân thực, có chiều sâu
2.1 (0-1): Thông điệp ý nghĩa, phù hợp giá trị gia đình Việt Nam
2.2 (0-1): Có dẫn chứng hoặc câu chuyện cụ thể, thuyết phục
2.3 (0-1): Truyền cảm hứng, tác động tích cực
3 (0-1): Sáng tạo, góc nhìn mới
4 (0-1): Diễn đạt rõ ràng, ít lỗi chính tả, trình bày tốt

Nếu hợp lệ:
{
  "isValid": true,
  "scores": {
    "1.1": number,
    "1.2": number,
    "2.1": number,
    "2.2": number,
    "2.3": number,
    "3": number,
    "4": number
  },
  "total": number,
  "comment": "Nhận xét ngắn gọn (<=100 từ)",
  "details": {
    "1.1": "Giải thích vì sao cho điểm mục 1.1",
    "1.2": "Giải thích vì sao cho điểm mục 1.2",
    "2.1": "Giải thích vì sao cho điểm mục 2.1",
    "2.2": "Giải thích vì sao cho điểm mục 2.2",
    "2.3": "Giải thích vì sao cho điểm mục 2.3",
    "3": "Giải thích vì sao cho điểm mục 3",
    "4": "Giải thích vì sao cho điểm mục 4"
  }
}

RÀNG BUỘC:
- Chỉ trả JSON, không thêm text ngoài JSON.
- Luôn tự tính lại tổng điểm trước khi trả kết quả.

NỘI DUNG BÀI DỰ THI:
{{clipped}}
""";
    }

    private static EssayScoringResult ParseScoringResult(string rawText, string? httpStatus = null, string? rawApiResponse = null)
    {
        try
        {
            var result = JsonSerializer.Deserialize<EssayScoringResult>(rawText, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
            });
            if (result is null)
            {
                return EssayScoringResult.Invalid("Khong parse duoc JSON cham diem", httpStatus, rawApiResponse);
            }

            result.HttpStatus = httpStatus ?? string.Empty;
            result.RawApiResponse = rawApiResponse ?? string.Empty;
            result.ModelOutput = rawText;
            return result;
        }
        catch
        {
            return EssayScoringResult.Invalid("Khong parse duoc JSON cham diem", httpStatus, rawApiResponse);
        }
    }

    private static string ToScoreCellValue(EssayScoringResult result)
    {
        if (string.Equals(result.FailureType, FailureTypeApiError, StringComparison.Ordinal))
        {
            var reasons = result.Reasons is { Count: > 0 } ? string.Join("; ", result.Reasons) : "Loi he thong cham";
            return $"Chua cham duoc: {reasons}";
        }

        if (!result.IsValid)
        {
            var reasons = result.Reasons is { Count: > 0 } ? string.Join("; ", result.Reasons) : "Bai khong hop le";
            return $"Khong hop le: {reasons}";
        }

        var scores = result.Scores;
        if (scores is null)
        {
            return "Khong co diem";
        }

        return
            $"1.1:{scores.Score11:0.##}; 1.2:{scores.Score12:0.##}; 2.1:{scores.Score21:0.##}; 2.2:{scores.Score22:0.##}; " +
            $"2.3:{scores.Score23:0.##}; 3:{scores.Score3:0.##}; 4:{scores.Score4:0.##}; Tong:{result.Total:0.##}";
    }

    private static string ToDetailCellValue(EssayScoringResult result)
    {
        if (string.Equals(result.FailureType, FailureTypeApiError, StringComparison.Ordinal))
        {
            var reasons = result.Reasons is { Count: > 0 } ? string.Join("; ", result.Reasons) : "Loi he thong cham";
            return $"Chua cham duoc do loi Gemini/API. Chi tiet: {reasons}";
        }

        if (!result.IsValid)
        {
            var reasons = result.Reasons is { Count: > 0 } ? string.Join("; ", result.Reasons) : "Bai khong hop le";
            return $"Khong hop le. Ly do: {reasons}";
        }

        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(result.Comment))
        {
            parts.Add($"Nhan xet: {result.Comment.Trim()}");
        }

        if (result.Scores is not null)
        {
            parts.Add($"Tong diem: {result.Total:0.##}/10");
        }

        if (result.Details is { Count: > 0 })
        {
            var orderedKeys = new[] { "1.1", "1.2", "2.1", "2.2", "2.3", "3", "4" };
            foreach (var key in orderedKeys)
            {
                if (result.Details.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value))
                {
                    parts.Add($"{key}: {value.Trim()}");
                }
            }
        }

        if (parts.Count == 0)
        {
            return "Khong co ly do chi tiet.";
        }

        return string.Join(Environment.NewLine, parts);
    }

    private static string BuildApiLog(EssayScoringResult result)
    {
        var model = string.IsNullOrWhiteSpace(result.UsedModel) ? "N/A" : result.UsedModel;
        var status = string.IsNullOrWhiteSpace(result.HttpStatus) ? "N/A" : result.HttpStatus;
        var body = ClipForLog(result.RawApiResponse, 2000);
        var modelOutput = ClipForLog(result.ModelOutput, 1200);
        return $"Model: {model}{Environment.NewLine}HTTP: {status}{Environment.NewLine}Gemini response body:{Environment.NewLine}{body}{Environment.NewLine}Model output:{Environment.NewLine}{modelOutput}";
    }

    private static string ClipForLog(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "(empty)";
        }

        var trimmed = value.Trim();
        if (trimmed.Length <= maxLength)
        {
            return trimmed;
        }

        return $"{trimmed[..maxLength]} ...[truncated]";
    }

    private static EssayScoringResult? PreValidateEssay(string source)
    {
        var wordCount = CountEligibleWords(source);
        if (wordCount > MaxEssayWords)
        {
            return EssayScoringResult.Invalid(
                $"Bai viet vuot qua {MaxEssayWords} tu sau khi loai tru tieu de, thong tin ca nhan va phieu dang ky ({wordCount} tu).",
                "PreCheck",
                "PreCheck failed: word count limit exceeded.",
                FailureTypeInvalidEssay);
        }

        return null;
    }

    private static int CountEligibleWords(string source)
    {
        var lines = source.Replace("\r\n", "\n").Split('\n');
        var contentBuilder = new StringBuilder();
        var skippedTitle = false;

        foreach (var rawLine in lines)
        {
            var line = rawLine.Trim();
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            if (!skippedTitle)
            {
                skippedTitle = true;
                continue;
            }

            if (IsRegistrationLine(line) || IsPersonalInfoLine(line))
            {
                continue;
            }

            contentBuilder.Append(' ').Append(line);
        }

        return WordTokenRegex.Matches(contentBuilder.ToString()).Count;
    }

    private static bool IsRegistrationLine(string line)
    {
        var normalized = NormalizeForCompare(line);
        return normalized.Contains("phieu dang ky du thi", StringComparison.Ordinal) ||
               normalized.Contains("phieu dang ky", StringComparison.Ordinal);
    }

    private static bool IsPersonalInfoLine(string line)
    {
        var normalized = NormalizeForCompare(line);
        return normalized.Contains("ho va ten", StringComparison.Ordinal) ||
               normalized.Contains("nam sinh", StringComparison.Ordinal) ||
               normalized.Contains("sinh ngay", StringComparison.Ordinal) ||
               normalized.Contains("tuoi", StringComparison.Ordinal) ||
               normalized.Contains("chuc vu", StringComparison.Ordinal) ||
               normalized.Contains("lop", StringComparison.Ordinal) ||
               normalized.Contains("dia chi", StringComparison.Ordinal) ||
               normalized.Contains("don vi", StringComparison.Ordinal) ||
               normalized.Contains("lien he", StringComparison.Ordinal) ||
               normalized.Contains("truong", StringComparison.Ordinal) ||
               normalized.Contains("so dien thoai", StringComparison.Ordinal) ||
               normalized.Contains("dien thoai", StringComparison.Ordinal) ||
               normalized.Contains("email", StringComparison.Ordinal) ||
               normalized.Contains("thoi gian", StringComparison.Ordinal) ||
               normalized.Contains("dia diem", StringComparison.Ordinal);
    }

    private static string NormalizeForCompare(string value)
    {
        var formD = value.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(formD.Length);
        foreach (var c in formD)
        {
            var category = CharUnicodeInfo.GetUnicodeCategory(c);
            if (category != UnicodeCategory.NonSpacingMark)
            {
                builder.Append(char.ToLowerInvariant(c));
            }
        }

        return builder.ToString().Normalize(NormalizationForm.FormC);
    }

    private static async Task WaitForGeminiRequestSlotAsync()
    {
        await GeminiRequestGate.WaitAsync();
        try
        {
            var now = DateTime.UtcNow;
            var elapsed = now - _lastGeminiRequestUtc;
            if (_lastGeminiRequestUtc != DateTime.MinValue && elapsed < GeminiMinInterval)
            {
                await Task.Delay(GeminiMinInterval - elapsed);
            }

            _lastGeminiRequestUtc = DateTime.UtcNow;
        }
        finally
        {
            GeminiRequestGate.Release();
        }
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

    private sealed class EssayScoringResult
    {
        public bool IsValid { get; set; }
        public List<string> Reasons { get; set; } = [];
        public ScoreBreakdown? Scores { get; set; }
        public double Total { get; set; }
        public string Comment { get; set; } = string.Empty;
        public Dictionary<string, string> Details { get; set; } = [];
        public string HttpStatus { get; set; } = string.Empty;
        public string RawApiResponse { get; set; } = string.Empty;
        public string ModelOutput { get; set; } = string.Empty;
        public string UsedModel { get; set; } = string.Empty;
        public string FailureType { get; set; } = string.Empty;

        public static EssayScoringResult Invalid(
            string reason,
            string? httpStatus = null,
            string? rawApiResponse = null,
            string? failureType = null)
        {
            return new EssayScoringResult
            {
                IsValid = false,
                Reasons = [reason],
                HttpStatus = httpStatus ?? string.Empty,
                RawApiResponse = rawApiResponse ?? string.Empty,
                FailureType = failureType ?? FailureTypeInvalidEssay,
            };
        }
    }

    private sealed class ScoreBreakdown
    {
        [JsonPropertyName("1.1")]
        public double Score11 { get; set; }

        [JsonPropertyName("1.2")]
        public double Score12 { get; set; }

        [JsonPropertyName("2.1")]
        public double Score21 { get; set; }

        [JsonPropertyName("2.2")]
        public double Score22 { get; set; }

        [JsonPropertyName("2.3")]
        public double Score23 { get; set; }

        [JsonPropertyName("3")]
        public double Score3 { get; set; }

        [JsonPropertyName("4")]
        public double Score4 { get; set; }
    }
}
