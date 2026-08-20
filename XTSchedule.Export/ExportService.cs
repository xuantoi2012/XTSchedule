using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using XTSchedule.Core.Models;

namespace XTSchedule.Export;

/// <summary>Không tham chiếu WPF — nhận ảnh trang (PNG bytes) đã render sẵn từ
/// UI layer (xem XTSchedule.UI.Printing.SchedulePageRenderer) thay vì tự vẽ lại
/// Gantt bar bằng PdfSharp, để không phải duy trì 2 bộ logic vẽ giống hệt nhau
/// cho Print/Preview và cho PDF.</summary>
public static class ExportService
{
    public static void ExportPagesToPdf(IEnumerable<(byte[] PngBytes, double WidthMm, double HeightMm)> pages, string outputPath)
    {
        using var document = new PdfDocument();

        foreach (var (pngBytes, widthMm, heightMm) in pages)
        {
            var page = document.AddPage();
            page.Width = XUnit.FromMillimeter(widthMm);
            page.Height = XUnit.FromMillimeter(heightMm);

            using var gfx = XGraphics.FromPdfPage(page);
            using var stream = new MemoryStream(pngBytes);
            using var image = XImage.FromStream(stream);
            gfx.DrawImage(image, 0, 0, page.Width.Point, page.Height.Point);
        }

        document.Save(outputPath);
    }

    /// <summary>Xuất Word dạng bảng dữ liệu (Số thứ tự/Tên/Bắt đầu/Kết thúc/Số
    /// ngày) — KHÔNG có sơ đồ Gantt (Word không hợp để nhúng 1 hình ảnh dài cho
    /// tiến độ nhiều trang như PDF); ai cần xem timeline thì dùng bản PDF.</summary>
    public static void ExportToWord(ScheduleDocument document, string outputPath)
    {
        using var wordDocument = WordprocessingDocument.Create(outputPath, WordprocessingDocumentType.Document);
        var mainPart = wordDocument.AddMainDocumentPart();
        mainPart.Document = new Document();
        var body = mainPart.Document.AppendChild(new Body());

        body.AppendChild(new Paragraph(new Run(
            new RunProperties(new Bold(), new FontSize { Val = "32" }),
            new Text(document.HeaderSettings.Title))));

        body.AppendChild(new Paragraph(new Run(new Text(document.ProjectName))));
        if (!string.IsNullOrWhiteSpace(document.ProjectCode))
        {
            body.AppendChild(new Paragraph(new Run(new Text($"Mã dự án: {document.ProjectCode}"))));
        }

        body.AppendChild(new Paragraph());

        var table = new Table();
        table.AppendChild(new TableProperties(
            new TableBorders(
                new TopBorder { Val = BorderValues.Single, Size = 4 },
                new BottomBorder { Val = BorderValues.Single, Size = 4 },
                new LeftBorder { Val = BorderValues.Single, Size = 4 },
                new RightBorder { Val = BorderValues.Single, Size = 4 },
                new InsideHorizontalBorder { Val = BorderValues.Single, Size = 4 },
                new InsideVerticalBorder { Val = BorderValues.Single, Size = 4 })));

        table.AppendChild(BuildHeaderRow("STT", "Công việc", "Bắt đầu", "Kết thúc", "Số ngày"));

        foreach (var task in document.Tasks)
        {
            var indent = new string(' ', task.Level * 4);
            table.AppendChild(BuildRow(
                task.Number,
                indent + task.Name,
                task.StartDate?.ToString("dd/MM/yyyy") ?? "",
                task.FinishDate?.ToString("dd/MM/yyyy") ?? "",
                task.Duration.ToString()));
        }

        body.AppendChild(table);
        mainPart.Document.Save();
    }

    private static TableRow BuildHeaderRow(params string[] cells)
    {
        var row = new TableRow();
        foreach (var cell in cells)
        {
            row.Append(new TableCell(new Paragraph(new Run(
                new RunProperties(new Bold()), new Text(cell)))));
        }

        return row;
    }

    private static TableRow BuildRow(params string[] cells)
    {
        var row = new TableRow();
        foreach (var cell in cells)
        {
            row.Append(new TableCell(new Paragraph(new Run(new Text(cell)))));
        }

        return row;
    }
}
