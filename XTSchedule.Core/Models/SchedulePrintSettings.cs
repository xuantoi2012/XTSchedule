using XTSchedule.Core.Enums;

namespace XTSchedule.Core.Models;

public sealed class SchedulePrintSettings
{
    public PaperSize PaperSize { get; set; } = PaperSize.A3;

    public PageOrientation Orientation { get; set; } = PageOrientation.Landscape;

    public double MarginLeftMm { get; set; } = 15;

    public double MarginRightMm { get; set; } = 10;

    public double MarginTopMm { get; set; } = 10;

    public double MarginBottomMm { get; set; } = 10;

    public double TaskColumnWidthMm { get; set; } = 90;

    public double FontSize { get; set; } = 10;

    public double RowHeightMm { get; set; } = 7;

    public bool FitToPageWidth { get; set; } = true;

    public bool RepeatTaskColumns { get; set; } = true;

    public bool RepeatTimelineHeader { get; set; } = true;

    public bool ShowWeekend { get; set; } = true;
}
