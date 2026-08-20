using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using XTSchedule.Core.Enums;
using XTSchedule.Core.Models;

namespace XTSchedule.UI.Views;

public partial class PageSetupWindow
{
    private readonly SchedulePrintSettings _settings;

    /// <summary>True nếu user bấm Đồng ý — MainWindowViewModel chỉ áp dụng thay
    /// đổi (và đánh dấu dirty) khi cửa sổ đóng với kết quả này.</summary>
    public bool Applied { get; private set; }

    public PageSetupWindow(SchedulePrintSettings settings)
    {
        _settings = settings;
        InitializeComponent();
        LoadFromSettings();
    }

    private void LoadFromSettings()
    {
        cmbPaperSize.SelectedIndex = _settings.PaperSize switch
        {
            PaperSize.A4 => 0,
            PaperSize.A3 => 1,
            PaperSize.A2 => 2,
            PaperSize.A1 => 3,
            _ => 1
        };
        cmbOrientation.SelectedIndex = _settings.Orientation == PageOrientation.Landscape ? 1 : 0;

        txtMarginLeft.Text = _settings.MarginLeftMm.ToString(CultureInfo.InvariantCulture);
        txtMarginRight.Text = _settings.MarginRightMm.ToString(CultureInfo.InvariantCulture);
        txtMarginTop.Text = _settings.MarginTopMm.ToString(CultureInfo.InvariantCulture);
        txtMarginBottom.Text = _settings.MarginBottomMm.ToString(CultureInfo.InvariantCulture);
        txtTaskColumnWidth.Text = _settings.TaskColumnWidthMm.ToString(CultureInfo.InvariantCulture);
        txtFontSize.Text = _settings.FontSize.ToString(CultureInfo.InvariantCulture);
        txtRowHeight.Text = _settings.RowHeightMm.ToString(CultureInfo.InvariantCulture);

        chkFitToWidth.IsChecked = _settings.FitToPageWidth;
        chkRepeatTaskColumns.IsChecked = _settings.RepeatTaskColumns;
        chkRepeatTimelineHeader.IsChecked = _settings.RepeatTimelineHeader;
        chkShowWeekend.IsChecked = _settings.ShowWeekend;
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        _settings.PaperSize = cmbPaperSize.SelectedIndex switch
        {
            0 => PaperSize.A4,
            1 => PaperSize.A3,
            2 => PaperSize.A2,
            3 => PaperSize.A1,
            _ => PaperSize.A3
        };
        _settings.Orientation = cmbOrientation.SelectedIndex == 1 ? PageOrientation.Landscape : PageOrientation.Portrait;

        _settings.MarginLeftMm = ParseOr(txtMarginLeft, _settings.MarginLeftMm);
        _settings.MarginRightMm = ParseOr(txtMarginRight, _settings.MarginRightMm);
        _settings.MarginTopMm = ParseOr(txtMarginTop, _settings.MarginTopMm);
        _settings.MarginBottomMm = ParseOr(txtMarginBottom, _settings.MarginBottomMm);
        _settings.TaskColumnWidthMm = ParseOr(txtTaskColumnWidth, _settings.TaskColumnWidthMm);
        _settings.FontSize = ParseOr(txtFontSize, _settings.FontSize);
        _settings.RowHeightMm = ParseOr(txtRowHeight, _settings.RowHeightMm);

        _settings.FitToPageWidth = chkFitToWidth.IsChecked == true;
        _settings.RepeatTaskColumns = chkRepeatTaskColumns.IsChecked == true;
        _settings.RepeatTimelineHeader = chkRepeatTimelineHeader.IsChecked == true;
        _settings.ShowWeekend = chkShowWeekend.IsChecked == true;

        Applied = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => Close();

    private static double ParseOr(TextBox box, double fallback) =>
        double.TryParse(box.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value : fallback;
}
