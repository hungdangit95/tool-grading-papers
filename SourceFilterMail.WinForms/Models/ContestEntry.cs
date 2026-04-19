namespace SourceFilterMail.WinForms.Models;

public sealed class ContestEntry
{
    public string ContestCode { get; set; } = string.Empty;
    public string AuthorName { get; set; } = string.Empty;
    public string BirthYear { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string Province { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;

    public DateTime? SentAt { get; set; }
    public string SenderDisplayName { get; set; } = string.Empty;
    public string SenderAddress { get; set; } = string.Empty;
    public string Subject { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public string SavedAttachmentPaths { get; set; } = string.Empty;
    public string AttachmentFolderPath { get; set; } = string.Empty;

    /// <summary>ảnh / video / ghi âm — text hiển thị khi xuất.</summary>
    public string AnhVideo { get; set; } = string.Empty;

    /// <summary>ZIP không đọc được → Excel tô đỏ ô cột media.</summary>
    public bool AnhVideoHighlightZipRed { get; set; }

    /// <summary>Có ảnh, video hoặc ghi âm → Excel tô xanh lá (ZIP lỗi vẫn ưu tiên đỏ).</summary>
    public bool AnhVideoHighlightGreen { get; set; }

    /// <summary>Số lượng file nộp và danh sách tên file (xuất Excel).</summary>
    public string SoLuongVaTenFile { get; set; } = string.Empty;

    /// <summary>Invalid row: no attachment files — export "1" and yellow highlight.</summary>
    public string KhongHopLe { get; set; } = string.Empty;
}

