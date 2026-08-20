using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using XTSchedule.Core.Enums;
using XTSchedule.Core.Models;

namespace XTSchedule.UI.Printing;

/// <summary>Vẽ tay 1 trang in (bảng công việc + Gantt bar) bằng DrawingVisual —
/// dùng chung cho 3 chỗ: PrintDialog.PrintVisual (in thật), cửa sổ Print Preview
/// (hiện y hệt), và xuất PDF (rasterize từng trang thành ảnh rồi nhúng qua
/// PdfSharp — xem XTSchedule.Export). 1mm = 96/25.4 đơn vị WPF (96 DPI chuẩn).</summary>
public static class SchedulePageRenderer
{
    private const double MmToPx = 96.0 / 25.4;

    public sealed record PageResult(DrawingVisual Visual, double WidthPx, double HeightPx);

    /// <summary>Rasterize 1 trang — dùng cho Print Preview (hiển thị) và xuất PDF
    /// (nhúng làm ảnh full-page qua PdfSharp, xem XTSchedule.Export). Dpi cao hơn
    /// 96 màn hình để bản in/PDF không bị mờ.</summary>
    public static RenderTargetBitmap RenderToBitmap(PageResult page, double dpi = 150)
    {
        var scale = dpi / 96.0;
        var bitmap = new RenderTargetBitmap(
            (int)(page.WidthPx * scale), (int)(page.HeightPx * scale), dpi, dpi, PixelFormats.Pbgra32);
        bitmap.Render(page.Visual);
        return bitmap;
    }

    public static (double WidthMm, double HeightMm) GetPaperSizeMm(PaperSize size, PageOrientation orientation)
    {
        var (w, h) = size switch
        {
            PaperSize.A4 => (210.0, 297.0),
            PaperSize.A3 => (297.0, 420.0),
            PaperSize.A2 => (420.0, 594.0),
            PaperSize.A1 => (594.0, 841.0),
            _ => (297.0, 420.0)
        };

        return orientation == PageOrientation.Landscape ? (h, w) : (w, h);
    }

    /// <summary>Chia Tasks thành các trang theo chiều cao khả dụng, trả về 1
    /// DrawingVisual cho mỗi trang (đã vẽ đầy đủ header/table/timeline/footer).</summary>
    public static List<PageResult> RenderPages(ScheduleDocument document, SchedulePrintSettings settings)
    {
        var tasks = document.Tasks.Where(t => !IsHiddenByCollapsedAncestor(document.Tasks, t)).ToList();
        var (paperWmm, paperHmm) = GetPaperSizeMm(settings.PaperSize, settings.Orientation);
        var widthPx = paperWmm * MmToPx;
        var heightPx = paperHmm * MmToPx;
        var marginLeft = settings.MarginLeftMm * MmToPx;
        var marginRight = settings.MarginRightMm * MmToPx;
        var marginTop = settings.MarginTopMm * MmToPx;
        var marginBottom = settings.MarginBottomMm * MmToPx;

        var contentWidth = widthPx - marginLeft - marginRight;
        var contentHeight = heightPx - marginTop - marginBottom;

        const double titleHeight = 26;
        const double timelineHeaderHeight = 30;
        var rowHeight = Math.Max(14, settings.RowHeightMm * MmToPx);

        var tableAreaHeight = contentHeight - titleHeight - timelineHeaderHeight - 16;
        var rowsPerPage = Math.Max(1, (int)(tableAreaHeight / rowHeight));

        var dated = tasks.Where(t => t.StartDate.HasValue && t.FinishDate.HasValue).ToList();
        var rangeStart = dated.Count > 0 ? dated.Min(t => t.StartDate!.Value.Date) : DateTime.Today;
        var rangeFinish = dated.Count > 0 ? dated.Max(t => t.FinishDate!.Value.Date) : DateTime.Today;
        var totalDays = Math.Max(1, (rangeFinish - rangeStart).Days + 1);

        var taskColumnWidth = Math.Min(contentWidth * 0.6, settings.TaskColumnWidthMm * MmToPx);
        var timelineWidth = Math.Max(80, contentWidth - taskColumnWidth);
        var pixelsPerDay = settings.FitToPageWidth
            ? Math.Max(2, timelineWidth / totalDays)
            : Math.Max(6, timelineWidth / totalDays);

        var pages = new List<PageResult>();
        var pageCount = Math.Max(1, (int)Math.Ceiling(tasks.Count / (double)rowsPerPage));

        for (var pageIndex = 0; pageIndex < pageCount; pageIndex++)
        {
            var pageTasks = tasks.Skip(pageIndex * rowsPerPage).Take(rowsPerPage).ToList();
            var visual = RenderPage(document, settings, pageTasks, rangeStart, totalDays, pixelsPerDay,
                widthPx, heightPx, marginLeft, marginTop, contentWidth,
                titleHeight, timelineHeaderHeight, rowHeight, taskColumnWidth,
                pageIndex + 1, pageCount);
            pages.Add(new PageResult(visual, widthPx, heightPx));
        }

        return pages;
    }

    private static bool IsHiddenByCollapsedAncestor(ObservableCollection<ScheduleTask> all, ScheduleTask task)
    {
        var index = all.IndexOf(task);
        if (index < 0)
        {
            return false;
        }

        for (var i = index - 1; i >= 0; i--)
        {
            var candidate = all[i];
            if (candidate.Level >= task.Level)
            {
                continue;
            }

            if (candidate.IsCollapsed)
            {
                return true;
            }

            if (candidate.Level == 0)
            {
                break;
            }
        }

        return false;
    }

    private static DrawingVisual RenderPage(
        ScheduleDocument document, SchedulePrintSettings settings, List<ScheduleTask> pageTasks,
        DateTime rangeStart, int totalDays, double pixelsPerDay,
        double widthPx, double heightPx, double marginLeft, double marginTop, double contentWidth,
        double titleHeight, double timelineHeaderHeight, double rowHeight, double taskColumnWidth,
        int pageNumber, int pageCount)
    {
        var visual = new DrawingVisual();
        var typeface = new Typeface("Segoe UI");
        var dpi = 1.0; // FormattedText dpi param — 1.0 là đủ cho render offscreen/in, không phụ thuộc màn hình thật

        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, widthPx, heightPx));
            dc.PushTransform(new TranslateTransform(marginLeft, marginTop));

            // ── Tiêu đề ──
            var titleText = FormatText(document.HeaderSettings.Title, typeface, 14, Brushes.Black, dpi, bold: true);
            dc.DrawText(titleText, new Point(0, 0));
            var subtitle = FormatText(
                $"{document.ProjectName}   •   Trang {pageNumber}/{pageCount}",
                typeface, 10, Brushes.DimGray, dpi);
            dc.DrawText(subtitle, new Point(0, 16));

            var tableTop = titleHeight;

            // ── Bảng công việc (trái) ──
            var colNumber = 34.0;
            var colDuration = 46.0;
            var colDate = 58.0;
            var colName = Math.Max(60, taskColumnWidth - colNumber - colDuration - colDate * 2);

            void DrawHeaderCell(string text, double x, double w)
            {
                var ft = FormatText(text, typeface, 9, Brushes.Black, dpi, bold: true);
                dc.DrawText(ft, new Point(x + 2, tableTop + 2));
            }

            var xNumber = 0.0;
            var xName = xNumber + colNumber;
            var xStart = xName + colName;
            var xFinish = xStart + colDate;
            var xDuration = xFinish + colDate;

            DrawHeaderCell("STT", xNumber, colNumber);
            DrawHeaderCell("Công việc", xName, colName);
            DrawHeaderCell("Bắt đầu", xStart, colDate);
            DrawHeaderCell("Kết thúc", xFinish, colDate);
            DrawHeaderCell("Số ngày", xDuration, colDuration);

            var headerRowH = 18.0;
            dc.DrawLine(new Pen(Brushes.Black, 0.6), new Point(0, tableTop + headerRowH), new Point(taskColumnWidth, tableTop + headerRowH));

            for (var i = 0; i < pageTasks.Count; i++)
            {
                var task = pageTasks[i];
                var y = tableTop + headerRowH + i * rowHeight;
                var indent = task.Level * 8.0;

                dc.DrawText(FormatText(task.Number, typeface, 9, Brushes.Black, dpi), new Point(xNumber + 2, y + 3));
                dc.DrawText(
                    FormatText(task.Name, typeface, 9, Brushes.Black, dpi, bold: task.TaskType == ScheduleTaskType.Group),
                    new Point(xName + 2 + indent, y + 3));
                dc.DrawText(FormatText(task.StartDate?.ToString("dd/MM/yy") ?? "", typeface, 9, Brushes.Black, dpi), new Point(xStart + 2, y + 3));
                dc.DrawText(FormatText(task.FinishDate?.ToString("dd/MM/yy") ?? "", typeface, 9, Brushes.Black, dpi), new Point(xFinish + 2, y + 3));
                dc.DrawText(FormatText(task.Duration.ToString(CultureInfo.InvariantCulture), typeface, 9, Brushes.Black, dpi), new Point(xDuration + 2, y + 3));

                dc.DrawLine(new Pen(Brushes.LightGray, 0.4), new Point(0, y + rowHeight), new Point(taskColumnWidth, y + rowHeight));
            }

            // ── Timeline (phải) ──
            dc.PushTransform(new TranslateTransform(taskColumnWidth + 6, 0));

            if (settings.ShowWeekend)
            {
                for (var day = 0; day < totalDays; day++)
                {
                    var isWeekend = rangeStart.AddDays(day).DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;
                    if (!isWeekend)
                    {
                        continue;
                    }

                    var x = day * pixelsPerDay;
                    dc.DrawRectangle(
                        new SolidColorBrush(Color.FromArgb(28, 15, 108, 189)), null,
                        new Rect(x, tableTop, pixelsPerDay, headerRowH + pageTasks.Count * rowHeight));
                }
            }

            var monthCursor = rangeStart;
            while (monthCursor <= rangeStart.AddDays(totalDays - 1))
            {
                var x = (monthCursor - rangeStart).TotalDays * pixelsPerDay;
                dc.DrawLine(new Pen(Brushes.Gray, 0.6), new Point(x, tableTop), new Point(x, tableTop + headerRowH));
                var label = FormatText($"{monthCursor:MM/yyyy}", typeface, 8, Brushes.Black, dpi);
                dc.DrawText(label, new Point(x + 2, tableTop + 2));
                monthCursor = new DateTime(monthCursor.Year, monthCursor.Month, 1).AddMonths(1);
            }

            dc.DrawLine(new Pen(Brushes.Black, 0.6), new Point(0, tableTop + headerRowH), new Point(contentWidth - taskColumnWidth - 6, tableTop + headerRowH));

            for (var i = 0; i < pageTasks.Count; i++)
            {
                var task = pageTasks[i];
                var y = tableTop + headerRowH + i * rowHeight;

                if (task.StartDate.HasValue && task.FinishDate.HasValue)
                {
                    var x = (task.StartDate.Value.Date - rangeStart).TotalDays * pixelsPerDay;
                    var w = ((task.FinishDate.Value.Date - task.StartDate.Value.Date).TotalDays + 1) * pixelsPerDay;
                    var fill = task.TaskType == ScheduleTaskType.Group
                        ? new SolidColorBrush(Color.FromRgb(0, 121, 107))
                        : new SolidColorBrush(Color.FromRgb(21, 108, 189));
                    dc.DrawRoundedRectangle(fill, null, new Rect(x + 1, y + rowHeight * 0.22, Math.Max(2, w - 2), rowHeight * 0.56), 2, 2);
                }

                dc.DrawLine(new Pen(Brushes.LightGray, 0.4), new Point(0, y + rowHeight), new Point(contentWidth - taskColumnWidth - 6, y + rowHeight));
            }

            dc.Pop(); // timeline transform

            // ── Footer ──
            if (!string.IsNullOrWhiteSpace(document.HeaderSettings.Footer))
            {
                dc.DrawText(
                    FormatText(document.HeaderSettings.Footer, typeface, 8, Brushes.DimGray, dpi),
                    new Point(0, contentHeightSafe(contentWidth) - 12));
            }

            dc.Pop(); // margin transform
        }

        return visual;

        double contentHeightSafe(double _) => heightPx - marginTop - 4;
    }

    private static FormattedText FormatText(string value, Typeface typeface, double size, Brush brush, double dpi, bool bold = false)
    {
        var tf = bold ? new Typeface(typeface.FontFamily, FontStyles.Normal, FontWeights.Bold, FontStretches.Normal) : typeface;
        return new FormattedText(value ?? "", CultureInfo.CurrentCulture, FlowDirection.LeftToRight, tf, size, brush, dpi);
    }
}
