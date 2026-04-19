namespace SourceFilterMail.WinForms.Models;

public sealed class GmailMailItem
{
    public string MessageId { get; set; } = string.Empty;
    public string Subject { get; set; } = string.Empty;
    public string SenderRaw { get; set; } = string.Empty;
    public string SenderAddress { get; set; } = string.Empty;
    public DateTime? SentAt { get; set; }
    public string Snippet { get; set; } = string.Empty;
    public string PlainTextBody { get; set; } = string.Empty;
    public string AttachmentFolderPath { get; set; } = string.Empty;
    public List<string> SavedAttachmentPaths { get; } = [];
}
