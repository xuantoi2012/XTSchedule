using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Markup;
using XTSchedule.Core.Models;
using XTSchedule.UI.Printing;

namespace XTSchedule.UI.Views;

public partial class PrintPreviewWindow
{
    private readonly List<SchedulePageRenderer.PageResult> _pages;

    public PrintPreviewWindow(ScheduleDocument document, SchedulePrintSettings settings)
    {
        InitializeComponent();
        _pages = SchedulePageRenderer.RenderPages(document, settings);
        txtPageCount.Text = $"{_pages.Count} trang";
        pagesList.ItemsSource = _pages.Select(p => SchedulePageRenderer.RenderToBitmap(p, dpi: 96)).ToList();
    }

    /// <summary>Gộp tất cả trang vào 1 FixedDocument rồi in 1 lần — gọi
    /// PrintDialog.PrintVisual() lặp lại cho từng trang sẽ tạo NHIỀU print job
    /// riêng biệt thay vì 1 tài liệu nhiều trang.</summary>
    private void Print_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new PrintDialog();
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        var fixedDocument = new FixedDocument();
        foreach (var page in _pages)
        {
            var bitmap = SchedulePageRenderer.RenderToBitmap(page, dpi: 150);
            var image = new Image { Source = bitmap, Width = page.WidthPx, Height = page.HeightPx };

            var fixedPage = new FixedPage { Width = page.WidthPx, Height = page.HeightPx };
            fixedPage.Children.Add(image);

            var pageContent = new PageContent();
            ((IAddChild)pageContent).AddChild(fixedPage);
            fixedDocument.Pages.Add(pageContent);
        }

        dialog.PrintDocument(fixedDocument.DocumentPaginator, "XTSchedule");
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
