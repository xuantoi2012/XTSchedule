using System.Collections;
using System.Globalization;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using XTSchedule.Core.Enums;
using XTSchedule.Core.Models;

namespace XTSchedule.UI.Controls;

/// <summary>Vẽ tay bằng DrawingContext (không dùng UIElement con cho từng thanh/ô) —
/// đơn giản hơn nhiều so với virtualizing panel thật, và với vài trăm dòng vẫn đủ
/// nhanh vì WPF chỉ gọi lại OnRender khi InvalidateVisual(), không phải mỗi frame.
/// Đổi lại: không có "true virtualization" kiểu chỉ vẽ phần đang cuộn tới — coi là
/// đủ dùng cho quy mô 1 bảng tiến độ, chưa cần thiết phải làm phức tạp hơn.
///
/// Cũng tự xử lý chuột để kéo-sửa ngày trực tiếp trên bar (kéo giữa = dời cả
/// StartDate/FinishDate, kéo mép trái/phải = đổi 1 đầu) — xem HitTestBar/
/// OnMouseLeftButtonDown/OnMouseMove/OnMouseLeftButtonUp.</summary>
public sealed class ScheduleTimelineControl : FrameworkElement
{
    private const double HeaderHeight = 40.0;
    private const double RowHeight = 32.0;
    private const double EdgeGrabPx = 6.0;

    /// <summary>Lề trái/phải cố định — không có lề thì nhãn ngày bắt đầu của
    /// công việc sớm nhất (bar chạm mép trái, x=0) và tay cầm liên kết của công
    /// việc muộn nhất (chạm mép phải) sẽ bị cắt mất, không có chỗ vẽ.</summary>
    private const double LeftMargin = 55.0;
    private const double RightMargin = 55.0;

    // IEnumerable (không generic) — ItemsSource thực tế bind vào ICollectionView
    // (MainWindowViewModel.TasksView, để lọc dòng bị thu gọn nhóm) chỉ implement
    // IEnumerable thường, không phải IEnumerable<ScheduleTask>. Khai generic ở
    // đây từng khiến binding fail âm thầm — WPF không báo lỗi, control chỉ nhận
    // null và vẽ ra khoảng trắng.
    public static readonly DependencyProperty ItemsSourceProperty =
        DependencyProperty.Register(
            nameof(ItemsSource),
            typeof(IEnumerable),
            typeof(ScheduleTimelineControl),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender | FrameworkPropertyMetadataOptions.AffectsMeasure, OnItemsSourceChanged));

    /// <summary>Px/ngày liên tục — thay cho enum 3 mức cố định để hỗ trợ
    /// Ctrl+lăn chuột zoom mượt (xem MainWindow.xaml.cs TaskGrid/Timeline
    /// PreviewMouseWheel). Nút "Ngày/Tuần/Tháng" trong menu Xem chỉ là preset
    /// nhảy nhanh tới 1 giá trị cụ thể, không còn là enum riêng.</summary>
    public static readonly DependencyProperty PixelsPerDayProperty =
        DependencyProperty.Register(
            nameof(PixelsPerDay),
            typeof(double),
            typeof(ScheduleTimelineControl),
            new FrameworkPropertyMetadata(32.0, FrameworkPropertyMetadataOptions.AffectsRender | FrameworkPropertyMetadataOptions.AffectsMeasure));

    private IEnumerable? _subscribedItems;
    private INotifyCollectionChanged? _subscribedCollection;

    private enum DragMode { None, Move, ResizeStart, ResizeEnd, Link }

    private DragMode _dragMode = DragMode.None;
    private ScheduleTask? _dragTask;
    private DateTime _dragOriginPointerDate;
    private DateTime _dragOriginStart;
    private DateTime _dragOriginFinish;
    private DateTime _dragRangeStart; // "start" mốc quy đổi X→ngày, chốt lúc bắt đầu kéo để tránh giật hình khi range đổi giữa chừng
    private Point _linkDragPoint;

    /// <summary>Gọi khi kéo-thả tay cầm liên kết từ 1 bar sang bar khác xong —
    /// MainWindow không tự sửa Predecessors ở đây (control này không nên biết
    /// cú pháp "1.1,1.2") mà giao lại cho ViewModel qua callback này.</summary>
    public Action<ScheduleTask, ScheduleTask>? LinkRequested { get; set; }

    public IEnumerable? ItemsSource
    {
        get => (IEnumerable?)GetValue(ItemsSourceProperty);
        set => SetValue(ItemsSourceProperty, value);
    }

    public double PixelsPerDay
    {
        get => (double)GetValue(PixelsPerDayProperty);
        set => SetValue(PixelsPerDayProperty, value);
    }

    /// <summary>Gọi 1 lần ngay khi bắt đầu kéo sửa ngày trên 1 task — để
    /// MainWindowViewModel chèn 1 undo snapshot trước khi thay đổi, giống
    /// TaskGrid_BeginningEdit cho việc sửa trực tiếp trên grid.</summary>
    public Action? BeginEditCallback { get; set; }

    public ScheduleTimelineControl()
    {
        Cursor = Cursors.Arrow;
    }

    private static void OnItemsSourceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var control = (ScheduleTimelineControl)d;
        control.UnsubscribeItems();
        control.SubscribeItems(e.NewValue as IEnumerable);
        control.InvalidateVisual();
    }

    private void SubscribeItems(IEnumerable? items)
    {
        _subscribedItems = items;
        _subscribedCollection = items as INotifyCollectionChanged;
        if (_subscribedCollection is not null)
        {
            _subscribedCollection.CollectionChanged += OnCollectionChanged;
        }

        if (items is null)
        {
            return;
        }

        foreach (var item in items.OfType<ScheduleTask>())
        {
            item.PropertyChanged += OnTaskPropertyChanged;
        }
    }

    private void UnsubscribeItems()
    {
        if (_subscribedCollection is not null)
        {
            _subscribedCollection.CollectionChanged -= OnCollectionChanged;
        }

        if (_subscribedItems is not null)
        {
            foreach (var item in _subscribedItems.OfType<ScheduleTask>())
            {
                item.PropertyChanged -= OnTaskPropertyChanged;
            }
        }

        _subscribedCollection = null;
        _subscribedItems = null;
    }

    private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null)
        {
            foreach (ScheduleTask item in e.OldItems)
            {
                item.PropertyChanged -= OnTaskPropertyChanged;
            }
        }

        if (e.NewItems is not null)
        {
            foreach (ScheduleTask item in e.NewItems)
            {
                item.PropertyChanged += OnTaskPropertyChanged;
            }
        }

        InvalidateMeasure();
        InvalidateVisual();
    }

    private void OnTaskPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ScheduleTask.StartDate) or nameof(ScheduleTask.FinishDate) or nameof(ScheduleTask.Duration)
            or nameof(ScheduleTask.TaskType) or nameof(ScheduleTask.IsCollapsed) or nameof(ScheduleTask.Progress))
        {
            InvalidateMeasure();
            InvalidateVisual();
        }
    }

    private List<ScheduleTask> GetVisibleTasks() =>
        ItemsSource?.OfType<ScheduleTask>().Where(t => t.StartDate.HasValue && t.FinishDate.HasValue).ToList() ?? [];

    private (List<ScheduleTask> Tasks, DateTime Start, int TotalDays) GetLayoutContext()
    {
        var tasks = GetVisibleTasks();
        if (tasks.Count == 0)
        {
            return (tasks, DateTime.Today, 1);
        }

        var start = tasks.Min(t => t.StartDate!.Value.Date);
        var finish = tasks.Max(t => t.FinishDate!.Value.Date);
        return (tasks, start, Math.Max(1, (finish - start).Days + 1));
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var (tasks, _, totalDays) = GetLayoutContext();
        var height = HeaderHeight + Math.Max(1, tasks.Count) * RowHeight;

        if (tasks.Count == 0)
        {
            return new Size(Math.Max(200, double.IsInfinity(availableSize.Width) ? 200 : availableSize.Width), height);
        }

        var width = Math.Max(200, totalDays * PixelsPerDay + LeftMargin + RightMargin);
        return new Size(width, height);
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);

        var (tasks, start, totalDays) = GetLayoutContext();
        var background = TryFindResource("CadPanelBackground") as Brush ?? Brushes.White;
        var border = TryFindResource("CadBorder") as Brush ?? Brushes.LightGray;
        var text = TryFindResource("CadTextSecondary") as Brush ?? Brushes.DimGray;
        var bar = TryFindResource("CadSelectionBlue") as Brush ?? Brushes.SteelBlue;
        var groupBar = TryFindResource("CadDraftCyan") as Brush ?? Brushes.Teal;

        drawingContext.DrawRectangle(background, null, new Rect(0, 0, ActualWidth, ActualHeight));
        drawingContext.DrawRectangle(
            null,
            new Pen(border, 1),
            new Rect(0.5, 0.5, Math.Max(0, ActualWidth - 1), Math.Max(0, ActualHeight - 1)));

        if (tasks.Count == 0 || ActualWidth <= 0 || ActualHeight <= 0)
        {
            return;
        }

        var pixelsPerDay = PixelsPerDay;
        var typeface = new Typeface("Segoe UI");
        var dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;

        DrawDayColumns(drawingContext, start, totalDays, pixelsPerDay, border);
        DrawHeader(drawingContext, start, totalDays, pixelsPerDay, typeface, dpi, text, border);
        DrawBars(drawingContext, tasks, start, pixelsPerDay, bar, groupBar, border, typeface, dpi, text);
    }

    private void DrawDayColumns(DrawingContext dc, DateTime start, int totalDays, double pixelsPerDay, Brush border)
    {
        var weekendBrush = new SolidColorBrush(Color.FromArgb(32, 15, 108, 189));
        for (var day = 0; day < totalDays; day++)
        {
            var x = LeftMargin + day * pixelsPerDay;
            var isWeekend = start.AddDays(day).DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;
            if (isWeekend && pixelsPerDay >= 5)
            {
                dc.DrawRectangle(weekendBrush, null, new Rect(x, HeaderHeight, pixelsPerDay, Math.Max(0, ActualHeight - HeaderHeight)));
            }
        }
    }

    /// <summary>Header 2 dòng: dòng trên nhóm theo tháng, dòng dưới chi tiết theo
    /// mật độ px/ngày hiện tại (zoom liên tục nên dùng ngưỡng thay vì enum cố
    /// định) — dày (≥16px/ngày): số ngày; vừa (≥5px/ngày): mốc đầu mỗi tuần;
    /// thưa: để trống (thông tin tháng đã đủ ở dòng trên).</summary>
    private void DrawHeader(DrawingContext dc, DateTime start, int totalDays, double pixelsPerDay,
        Typeface typeface, double dpi, Brush text, Brush border)
    {
        var monthRowHeight = HeaderHeight / 2;

        var cursor = start;
        while (cursor <= start.AddDays(totalDays - 1))
        {
            var monthStart = new DateTime(cursor.Year, cursor.Month, 1);
            var monthEnd = monthStart.AddMonths(1).AddDays(-1);
            var segmentStart = cursor == start ? start : monthStart;
            var segmentEnd = monthEnd > start.AddDays(totalDays - 1) ? start.AddDays(totalDays - 1) : monthEnd;

            var x = LeftMargin + (segmentStart - start).TotalDays * pixelsPerDay;
            var w = ((segmentEnd - segmentStart).TotalDays + 1) * pixelsPerDay;

            var label = FormatText($"Tháng {cursor.Month}/{cursor.Year}", typeface, 11, text, dpi);
            if (w > label.Width + 6)
            {
                dc.DrawText(label, new Point(x + 4, 4));
            }

            dc.DrawLine(new Pen(border, 1), new Point(x, 0), new Point(x, monthRowHeight));
            cursor = monthEnd.AddDays(1);
        }

        dc.DrawLine(new Pen(border, 1), new Point(0, monthRowHeight), new Point(ActualWidth, monthRowHeight));

        if (pixelsPerDay >= 16)
        {
            for (var day = 0; day < totalDays; day++)
            {
                var date = start.AddDays(day);
                var x = LeftMargin + day * pixelsPerDay;
                var label = FormatText(date.Day.ToString(CultureInfo.InvariantCulture), typeface, 10, text, dpi);
                dc.DrawText(label, new Point(x + pixelsPerDay / 2 - label.Width / 2, monthRowHeight + 4));
                dc.DrawLine(new Pen(border, 0.6), new Point(x, monthRowHeight), new Point(x, ActualHeight));
            }
        }
        else if (pixelsPerDay >= 5)
        {
            var weekCursor = start.AddDays(-(int)start.DayOfWeek + (int)DayOfWeek.Monday);
            if (weekCursor > start)
            {
                weekCursor = weekCursor.AddDays(-7);
            }

            while (weekCursor <= start.AddDays(totalDays - 1))
            {
                var x = LeftMargin + (weekCursor - start).TotalDays * pixelsPerDay;
                if (x >= LeftMargin)
                {
                    var label = FormatText(weekCursor.ToString("dd/MM"), typeface, 10, text, dpi);
                    dc.DrawText(label, new Point(x + 3, monthRowHeight + 4));
                    dc.DrawLine(new Pen(border, 0.6), new Point(x, monthRowHeight), new Point(x, ActualHeight));
                }

                weekCursor = weekCursor.AddDays(7);
            }
        }
        // pixelsPerDay < 5: ranh giới tháng đã vẽ ở dòng trên, dòng dưới để trống.
    }

    private void DrawBars(DrawingContext dc, List<ScheduleTask> tasks, DateTime start, double pixelsPerDay,
        Brush bar, Brush groupBar, Brush border, Typeface typeface, double dpi, Brush text)
    {
        var progressBrush = new SolidColorBrush(Color.FromArgb(110, 0, 0, 0));
        var linkBrush = TryFindResource("CadSelectionBlue") as Brush ?? Brushes.SteelBlue;
        var linkIcon = TryFindResource("Mat.Link") as Geometry;

        for (var i = 0; i < tasks.Count; i++)
        {
            var task = tasks[i];
            var y = HeaderHeight + i * RowHeight;
            var rect = BarRect(task, start, pixelsPerDay, i);
            var fill = task.TaskType == ScheduleTaskType.Group ? groupBar : bar;

            dc.DrawRoundedRectangle(fill, null, rect, 3, 3);

            var progress = Math.Clamp(task.Progress, 0, 100);
            if (progress > 0)
            {
                var progressWidth = rect.Width * progress / 100.0;
                var progressRect = new Rect(rect.X, rect.Y + rect.Height * 0.55, progressWidth, rect.Height * 0.35);
                dc.DrawRoundedRectangle(progressBrush, null, progressRect, 2, 2);
            }

            var isGroup = task.TaskType == ScheduleTaskType.Group;

            // Tay cầm kéo-liên kết ("hyperlink" kiểu MS Project/Word) — icon chain-link
            // sát mép phải bar, kéo từ đây sang bar khác để đặt Predecessors. Nhãn
            // ngày kết thúc vẽ SAU icon (không chồng lên nhau). Group tự tính ngày
            // theo việc con (RollupGroupDates trong VM) nên không cho kéo/liên kết tay.
            double finishLabelStartX;
            if (!isGroup)
            {
                var handleCenter = LinkHandleCenter(rect);
                var handleBrush = _dragMode == DragMode.Link && ReferenceEquals(_dragTask, task) ? linkBrush : text;
                DrawLinkIcon(dc, linkIcon, handleCenter, handleBrush);
                finishLabelStartX = handleCenter.X + LinkIconSize / 2 + 4;
            }
            else
            {
                finishLabelStartX = rect.Right + 5;
            }

            // Nhãn ngày 2 bên bar (kiểu MS Project) — bắt đầu bên trái, kết thúc
            // bên phải sau tay cầm liên kết — chỉ hiện khi đủ chỗ.
            if (pixelsPerDay >= 3)
            {
                var rowCenterY = y + RowHeight / 2 - 6;
                var startLabel = FormatText($"{task.StartDate:dd/MM}", typeface, 9, text, dpi);
                dc.DrawText(startLabel, new Point(rect.Left - 5 - startLabel.Width, rowCenterY));

                var finishLabel = FormatText($"{task.FinishDate:dd/MM}", typeface, 9, text, dpi);
                dc.DrawText(finishLabel, new Point(finishLabelStartX, rowCenterY));
            }

            dc.DrawLine(new Pen(border, 0.6), new Point(0, y), new Point(ActualWidth, y));
        }

        if (_dragMode == DragMode.Link && _dragTask is not null)
        {
            var sourceRow = tasks.IndexOf(_dragTask);
            if (sourceRow >= 0)
            {
                var sourceRect = BarRect(_dragTask, start, pixelsPerDay, sourceRow);
                var from = LinkHandleCenter(sourceRect);
                var dash = new Pen(linkBrush, 1.5) { DashStyle = new DashStyle([4, 2], 0) };
                dc.DrawLine(dash, from, _linkDragPoint);
                dc.DrawEllipse(linkBrush, null, _linkDragPoint, 3, 3);
            }
        }
    }

    private const double LinkHandleRadius = 7.0;
    private const double LinkIconSize = 13.0;

    private static Point LinkHandleCenter(Rect barRect) => new(barRect.Right + 12, barRect.Y + barRect.Height / 2);

    private Rect BarRect(ScheduleTask task, DateTime start, double pixelsPerDay, int row)
    {
        var y = HeaderHeight + row * RowHeight;
        var x = LeftMargin + (task.StartDate!.Value.Date - start).TotalDays * pixelsPerDay;
        var width = ((task.FinishDate!.Value.Date - task.StartDate.Value.Date).TotalDays + 1) * pixelsPerDay;
        return new Rect(x + 2, y + 9, Math.Max(4, width - 4), 14);
    }

    /// <summary>Vẽ icon Mat.Link (chain-link, giống nút "Link" của Word/khái
    /// niệm liên kết trong MS Project) căn giữa tại center — geometry gốc vẽ
    /// trong khung 24x24 nên scale về LinkIconSize rồi mới dịch vào vị trí.</summary>
    private static void DrawLinkIcon(DrawingContext dc, Geometry? icon, Point center, Brush brush)
    {
        if (icon is null)
        {
            dc.DrawEllipse(brush, null, center, LinkIconSize / 2, LinkIconSize / 2);
            return;
        }

        var transform = new TransformGroup();
        transform.Children.Add(new ScaleTransform(LinkIconSize / 24.0, LinkIconSize / 24.0));
        transform.Children.Add(new TranslateTransform(center.X - LinkIconSize / 2, center.Y - LinkIconSize / 2));

        dc.PushTransform(transform);
        dc.DrawGeometry(brush, null, icon);
        dc.Pop();
    }

    private static FormattedText FormatText(string value, Typeface typeface, double size, Brush brush, double dpi) =>
        new(value, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, typeface, size, brush, dpi);

    // ── Kéo sửa ngày trực tiếp trên bar ──────────────────────────────────────

    private (ScheduleTask? Task, DragMode Mode, DateTime Start, Rect BarRect) HitTestBar(Point point)
    {
        var (tasks, start, _) = GetLayoutContext();
        if (tasks.Count == 0)
        {
            return (null, DragMode.None, start, default);
        }

        var row = (int)((point.Y - HeaderHeight) / RowHeight);
        if (row < 0 || row >= tasks.Count)
        {
            return (null, DragMode.None, start, default);
        }

        var task = tasks[row];
        var pixelsPerDay = PixelsPerDay;
        var visualRect = BarRect(task, start, pixelsPerDay, row);
        var barRect = new Rect(visualRect.X, HeaderHeight + row * RowHeight, Math.Max(1, visualRect.Width), RowHeight);

        // Group tự tính ngày = min/max của các việc con (MainWindowViewModel.
        // RollupGroupDates) — không cho kéo dời/resize/liên kết tay trên bar
        // Group, vì mọi thay đổi tay sẽ bị ghi đè ngay ở lần rollup tiếp theo.
        if (task.TaskType == ScheduleTaskType.Group)
        {
            return (null, DragMode.None, start, barRect);
        }

        // Tay cầm liên kết nằm NGOÀI bar (rect.Right+10) — kiểm tra trước, không
        // bị early-return "ngoài barRect" ở dưới chặn mất.
        var handleCenter = LinkHandleCenter(visualRect);
        if ((point - handleCenter).Length <= LinkHandleRadius)
        {
            return (task, DragMode.Link, start, barRect);
        }

        if (point.X < barRect.Left || point.X > barRect.Right)
        {
            return (null, DragMode.None, start, barRect);
        }

        var edge = Math.Min(EdgeGrabPx, barRect.Width / 3);
        var mode = point.X <= barRect.Left + edge ? DragMode.ResizeStart
            : point.X >= barRect.Right - edge ? DragMode.ResizeEnd
            : DragMode.Move;

        return (task, mode, start, barRect);
    }

    private DateTime PointToDate(double x, DateTime rangeStart) =>
        rangeStart.AddDays(Math.Floor((x - LeftMargin) / PixelsPerDay));

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);

        var point = e.GetPosition(this);
        var (task, mode, rangeStart, _) = HitTestBar(point);
        if (task is null || mode == DragMode.None)
        {
            return;
        }

        _dragMode = mode;
        _dragTask = task;

        if (mode == DragMode.Link)
        {
            _linkDragPoint = point;
            CaptureMouse();
            e.Handled = true;
            InvalidateVisual();
            return;
        }

        BeginEditCallback?.Invoke();

        _dragRangeStart = rangeStart;
        _dragOriginPointerDate = PointToDate(point.X, rangeStart);
        _dragOriginStart = task.StartDate!.Value;
        _dragOriginFinish = task.FinishDate!.Value;

        CaptureMouse();
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);

        var point = e.GetPosition(this);

        if (_dragMode == DragMode.None || _dragTask is null)
        {
            var (_, hoverMode, _, _) = HitTestBar(point);
            Cursor = hoverMode switch
            {
                DragMode.ResizeStart or DragMode.ResizeEnd => Cursors.SizeWE,
                DragMode.Move => Cursors.SizeAll,
                DragMode.Link => Cursors.Cross,
                _ => Cursors.Arrow
            };
            return;
        }

        if (_dragMode == DragMode.Link)
        {
            _linkDragPoint = point;
            InvalidateVisual();
            return;
        }

        var pointerDate = PointToDate(point.X, _dragRangeStart);
        var deltaDays = (pointerDate - _dragOriginPointerDate).Days;

        switch (_dragMode)
        {
            case DragMode.Move:
                if (deltaDays >= 0)
                {
                    _dragTask.FinishDate = _dragOriginFinish.AddDays(deltaDays);
                    _dragTask.StartDate = _dragOriginStart.AddDays(deltaDays);
                }
                else
                {
                    _dragTask.StartDate = _dragOriginStart.AddDays(deltaDays);
                    _dragTask.FinishDate = _dragOriginFinish.AddDays(deltaDays);
                }

                break;

            case DragMode.ResizeStart:
                var newStart = _dragOriginStart.AddDays(deltaDays);
                _dragTask.StartDate = newStart > _dragTask.FinishDate ? _dragTask.FinishDate : newStart;
                break;

            case DragMode.ResizeEnd:
                var newFinish = _dragOriginFinish.AddDays(deltaDays);
                _dragTask.FinishDate = newFinish < _dragTask.StartDate ? _dragTask.StartDate : newFinish;
                break;
        }
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);

        if (_dragMode == DragMode.None)
        {
            return;
        }

        if (_dragMode == DragMode.Link && _dragTask is not null)
        {
            var point = e.GetPosition(this);
            var (targetTask, _, _, _) = HitTestBar(point);
            if (targetTask is not null && !ReferenceEquals(targetTask, _dragTask))
            {
                LinkRequested?.Invoke(_dragTask, targetTask);
            }
        }

        _dragMode = DragMode.None;
        _dragTask = null;
        ReleaseMouseCapture();
        e.Handled = true;
        InvalidateVisual();
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        if (_dragMode == DragMode.None)
        {
            Cursor = Cursors.Arrow;
        }
    }
}
