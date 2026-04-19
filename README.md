# SourceFilterMail WinForms

Ph?n m?m WinForms d?c email d? thi t? Gmail theo ngày (00:00-24:00), t?i t?p dính kèm và xu?t Excel th?ng kê.

## Ch?c nang chính

- Import file `credential.json` d? dang nh?p Gmail API.
- L?y toàn b? email trong ngày dã ch?n (l?c `has:attachment`).
- T?i t?p dính kèm v? máy cá nhân.
- H? tr? d?c thông tin t? các d?nh d?ng: `zip`, `rar`, `docx`, `pdf`, `txt`.
- Trích xu?t tru?ng d? thi b?ng regex và có th? tang d? chính xác b?ng Gemini API.
- Xu?t Excel các c?t:
  - Mã bài d? thi
  - H? và tên tác gi?
  - Nam sinh
  - S? di?n tho?i
  - T?nh
  - Email

## Cách ch?y

1. Build và ch?y app:
   - `dotnet build SourceFilterMail.sln`
   - `dotnet run --project SourceFilterMail.WinForms`
2. Trên giao di?n:
   - Ch?n file `credential.json` (OAuth client c?a Gmail API).
   - Ch?n thu m?c luu.
   - Ch?n ngày c?n l?y mail.
   - (Tùy ch?n) nh?p Gemini API key.
   - B?m **Ch?y**.

## K?t qu? xu?t

- Attachments: `<thu_muc_ban_chon>/<yyyy-MM-dd>/attachments/...`
- Excel: `<thu_muc_ban_chon>/<yyyy-MM-dd>/ThongKe_<yyyyMMdd>.xlsx`

## Luu ý

- L?n d?u ch?y s? m? trình duy?t d? xác th?c Gmail OAuth, token luu t?i AppData c?a user.
- D? li?u t? do t? ngu?i d? thi có th? không chu?n; Gemini giúp chu?n hóa t?t hon.
