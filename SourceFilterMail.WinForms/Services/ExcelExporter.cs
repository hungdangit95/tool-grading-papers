using ClosedXML.Excel;
using SourceFilterMail.WinForms.Models;

namespace SourceFilterMail.WinForms.Services;

public sealed class ExcelExporter
{
    public Task ExportAsync(IEnumerable<ContestEntry> rows, string outputFilePath)
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add("TongHop");

        sheet.Cell(1, 1).Value = "Bai du thi";
        sheet.Cell(1, 2).Value = "Ho ten";
        sheet.Cell(1, 3).Value = "Nam sinh";
        sheet.Cell(1, 4).Value = "So dien thoai";
        sheet.Cell(1, 5).Value = "Dia chi";
        sheet.Cell(1, 6).Value = "Email";
        sheet.Cell(1, 7).Value = "Ngay gui bai";
        sheet.Cell(1, 8).Value = "Subject";
        sheet.Cell(1, 9).Value = "Duong dan luu attachment";
        sheet.Cell(1, 10).Value = "ảnh/video/ghi âm";
        sheet.Cell(1, 11).Value = "Số lượng file và tên file";
        sheet.Cell(1, 12).Value = "không hợp lệ";
        sheet.Cell(1, 13).Value = "Điểm";
        sheet.Cell(1, 14).Value = "Lý do chi tiết";

        var rowIndex = 2;
        foreach (var row in rows)
        {
            sheet.Cell(rowIndex, 1).Value = row.ContestCode;
            sheet.Cell(rowIndex, 2).Value = row.AuthorName;
            sheet.Cell(rowIndex, 3).Value = row.BirthYear;
            sheet.Cell(rowIndex, 4).Value = row.Phone;
            sheet.Cell(rowIndex, 5).Value = row.Province;
            sheet.Cell(rowIndex, 6).Value = row.Email;
            sheet.Cell(rowIndex, 7).Value = row.SentAt?.ToString("yyyy-MM-dd HH:mm:ss") ?? string.Empty;
            sheet.Cell(rowIndex, 8).Value = row.Subject;

            var attachmentFolderCell = sheet.Cell(rowIndex, 9);
            attachmentFolderCell.Value = row.AttachmentFolderPath;
            if (!string.IsNullOrWhiteSpace(row.AttachmentFolderPath) && Directory.Exists(row.AttachmentFolderPath))
            {
                attachmentFolderCell.SetHyperlink(new XLHyperlink(row.AttachmentFolderPath));
                attachmentFolderCell.Style.Font.Underline = XLFontUnderlineValues.Single;
                attachmentFolderCell.Style.Font.FontColor = XLColor.Blue;
            }

            var mediaCell = sheet.Cell(rowIndex, 10);
            mediaCell.Value = row.AnhVideo;
            if (row.AnhVideoHighlightZipRed)
            {
                mediaCell.Style.Fill.BackgroundColor = XLColor.LightCoral;
            }
            else if (row.AnhVideoHighlightGreen)
            {
                mediaCell.Style.Fill.BackgroundColor = XLColor.LightGreen;
            }

            sheet.Cell(rowIndex, 11).Value = row.SoLuongVaTenFile;

            var invalidCell = sheet.Cell(rowIndex, 12);
            invalidCell.Value = row.KhongHopLe;
            if (row.KhongHopLe == "1")
            {
                invalidCell.Style.Fill.BackgroundColor = XLColor.Yellow;
                sheet.Row(rowIndex).Style.Fill.BackgroundColor = XLColor.Yellow;
            }

            sheet.Cell(rowIndex, 13).Value = row.Diem;
            sheet.Cell(rowIndex, 14).Value = row.LyDoChiTiet;

            rowIndex++;
        }

        sheet.Columns().AdjustToContents();
        sheet.Column(11).Style.Alignment.WrapText = true;
        sheet.Column(11).Width = 56;
        sheet.Column(13).Style.Alignment.WrapText = true;
        sheet.Column(13).Width = 70;
        sheet.Column(14).Style.Alignment.WrapText = true;
        sheet.Column(14).Width = 90;
        workbook.SaveAs(outputFilePath);

        return Task.CompletedTask;
    }
}
