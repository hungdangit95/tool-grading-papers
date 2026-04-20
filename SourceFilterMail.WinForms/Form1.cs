using SourceFilterMail.WinForms.Models;
using SourceFilterMail.WinForms.Services;

namespace SourceFilterMail.WinForms;

public partial class Form1 : Form
{
    private readonly GmailReaderService _gmailReaderService = new();
    private readonly AttachmentTextService _attachmentTextService = new();
    private readonly AttachmentMediaAnalyzer _attachmentMediaAnalyzer = new();
    private readonly CandidateInfoExtractor _candidateInfoExtractor = new();
    private readonly ExcelExporter _excelExporter = new();

    public Form1()
    {
        InitializeComponent();
        txtOutput.Text = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "ContestDownloads");
        dtpDate.Value = DateTime.Today;
    }

    private void btnCredential_Click(object? sender, EventArgs e)
    {
        using var dialog = new OpenFileDialog
        {
            Filter = "JSON files (*.json)|*.json",
            Title = "Ch?n credential.json",
        };

        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            txtCredential.Text = dialog.FileName;
        }
    }

    private void btnOutput_Click(object? sender, EventArgs e)
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = "Ch?n thu m?c luu t?p t?i v?",
        };

        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            txtOutput.Text = dialog.SelectedPath;
        }
    }

    private async void btnRun_Click(object? sender, EventArgs e)
    {
        if (!File.Exists(txtCredential.Text))
        {
            MessageBox.Show(this, "Kh?ng t?m th?y file credential json.", "Thi?u d? li?u", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        if (string.IsNullOrWhiteSpace(txtOutput.Text))
        {
            MessageBox.Show(this, "B?n c?n ch?n thu m?c luu file.", "Thi?u d? li?u", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        ToggleRunning(true);

        try
        {
            txtLog.Clear();
            AppendLog("B?t d?u d?c Gmail...");

            var rootFolder = Path.Combine(txtOutput.Text, dtpDate.Value.ToString("yyyy-MM-dd"));
            var attachmentFolder = Path.Combine(rootFolder, "attachments");
            Directory.CreateDirectory(attachmentFolder);

            var mails = await _gmailReaderService.ReadDailyMailsAsync(
                txtCredential.Text,
                dtpDate.Value.Date,
                attachmentFolder,
                AppendLog,
                CancellationToken.None);

            AppendLog($"?? d?c {mails.Count} mail.");

            var geminiApiKey = ResolveGeminiApiKey();
            if (string.IsNullOrWhiteSpace(geminiApiKey))
            {
                AppendLog("Khong tim thay Gemini API key. He thong se bo qua cham diem.");
            }
            var entries = new List<ContestEntry>();
            var scoredOneEligibleEssay = false;
            for (var i = 0; i < mails.Count; i++)
            {
                var mail = mails[i];
                AppendLog($"?ang ph?n t?ch mail {i + 1}/{mails.Count}: {mail.Subject}");

                var attachmentText = await _attachmentTextService.BuildCombinedTextAsync(mail.SavedAttachmentPaths, AppendLog);
                var entry = await _candidateInfoExtractor.ExtractAsync(mail, attachmentText, geminiApiKey, i + 1);
                entry.SavedAttachmentPaths = string.Join(";", mail.SavedAttachmentPaths);
                entry.AttachmentFolderPath = mail.AttachmentFolderPath;

                var media = _attachmentMediaAnalyzer.Analyze(mail.SavedAttachmentPaths);
                entry.AnhVideo = media.DisplayText;
                entry.AnhVideoHighlightZipRed = media.HighlightZipRed;
                entry.AnhVideoHighlightGreen = media.HighlightMediaGreen;
                entry.SoLuongVaTenFile = AttachmentMediaAnalyzer.BuildInventorySummary(mail.SavedAttachmentPaths);
                var eligibility = _attachmentMediaAnalyzer.AnalyzeScoringEligibility(mail.SavedAttachmentPaths);
                if (!eligibility.IsEligible)
                {
                    entry.KhongHopLe = "1";
                    entry.Diem = string.Empty;
                    entry.LyDoChiTiet = eligibility.Reason;
                }
                else
                {
                    entry.KhongHopLe = string.Empty;
                    if (string.IsNullOrWhiteSpace(geminiApiKey))
                    {
                        entry.Diem = string.Empty;
                        entry.LyDoChiTiet = "Du dieu kien cham nhung thieu Gemini API key nen chua cham duoc.";
                        entries.Add(entry);
                        continue;
                    }

                    if (scoredOneEligibleEssay)
                    {
                        entry.Diem = string.Empty;
                        entry.LyDoChiTiet = "Tam thoi bo qua cham bai hop le nay de tranh rate limit 429 (che do test: chi cham 1 bai hop le).";
                        entries.Add(entry);
                        continue;
                    }

                    var scoring = await _candidateInfoExtractor.ScoreEssayAsync(attachmentText, geminiApiKey);
                    AppendLog($"Gemini API log ({entry.ContestCode}): {scoring.ApiLog}");
                    entry.Diem = scoring.ScoreText;
                    entry.LyDoChiTiet = string.IsNullOrWhiteSpace(scoring.DetailText)
                        ? eligibility.Reason
                        : $"{scoring.DetailText}{Environment.NewLine}{Environment.NewLine}--- Gemini API ---{Environment.NewLine}{scoring.ApiLog}";
                    if (entry.Diem.StartsWith("Khong hop le", StringComparison.OrdinalIgnoreCase))
                    {
                        entry.KhongHopLe = "1";
                    }
                    else if (!entry.Diem.StartsWith("Chua cham duoc", StringComparison.OrdinalIgnoreCase))
                    {
                        scoredOneEligibleEssay = true;
                    }
                }

                entries.Add(entry);
            }

            dgvResult.DataSource = entries;

            var excelPath = Path.Combine(rootFolder, $"ThongKe_{dtpDate.Value:yyyyMMdd}.xlsx");
            await _excelExporter.ExportAsync(entries, excelPath);

            AppendLog($"Xu?t Excel xong: {excelPath}");
            MessageBox.Show(this, "Ho?n th?nh x? l? mail v? xu?t Excel.", "Th?nh c?ng", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            AppendLog($"L?i: {ex.Message}");
            MessageBox.Show(this, ex.ToString(), "L?i x? l?", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            ToggleRunning(false);
        }
    }

    private void ToggleRunning(bool isRunning)
    {
        btnRun.Enabled = !isRunning;
        btnCredential.Enabled = !isRunning;
        btnOutput.Enabled = !isRunning;
        Cursor = isRunning ? Cursors.WaitCursor : Cursors.Default;
    }

    private void AppendLog(string message)
    {
        if (InvokeRequired)
        {
            BeginInvoke(() => AppendLog(message));
            return;
        }

        txtLog.AppendText($"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}");
    }

    private string ResolveGeminiApiKey()
    {
        var key = txtGemini.Text.Trim();
        if (!string.IsNullOrWhiteSpace(key))
        {
            return key;
        }

        var candidatePaths = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "key.txt"),
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "key.txt"),
            Path.Combine(AppContext.BaseDirectory, "key.txt"),
            Path.Combine(Directory.GetCurrentDirectory(), "key.txt"),
        };

        foreach (var candidatePath in candidatePaths)
        {
            var fullPath = Path.GetFullPath(candidatePath);
            if (!File.Exists(fullPath))
            {
                continue;
            }

            var fileKey = File.ReadAllText(fullPath).Trim();
            if (!string.IsNullOrWhiteSpace(fileKey))
            {
                return fileKey;
            }
        }

        return string.Empty;
    }
}
