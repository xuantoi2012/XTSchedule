using System.Globalization;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using System.Windows.Media;
using XTSchedule.Core.Enums;
using XTSchedule.Core.Models;

namespace XTSchedule.UI.Controls;

public sealed class ScheduleTimelineControl : FrameworkElement
{
    public static readonly DependencyProperty ItemsSourceProperty =
        DependencyProperty.Register(
            nameof(ItemsSource),
            typeof(IEnumerable<ScheduleTask>),
            typeof(ScheduleTimelineControl),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, OnItemsSourceChanged));

    private IEnumerable<ScheduleTask>? _subscribedItems;
    private INotifyCollectionChanged? _subscribedCollection;

    public IEnumerable<ScheduleTask>? ItemsSource
    {
        get => (IEnumerable<ScheduleTask>?)GetValue(ItemsSourceProperty);
        set => SetValue(ItemsSourceProperty, value);
    }

    private static void OnItemsSourceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var control = (ScheduleTimelineControl)d;
        control.UnsubscribeItems();
        control.SubscribeItems(e.NewValue as IEnumerable<ScheduleTask>);
        control.InvalidateVisual();
    }

    private void SubscribeItems(IEnumerable<ScheduleTask>? items)
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

        foreach (var item in items)
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
            foreach (var item in _subscribedItems)
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

        InvalidateVisual();
    }

    private void OnTaskPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ScheduleTask.StartDate) or nameof(ScheduleTask.FinishDate) or nameof(ScheduleTask.Duration) or nameof(ScheduleTask.TaskType))
        {
            InvalidateVisual();
        }
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);

        var tasks = ItemsSource?.Where(t => t.StartDate.HasValue && t.FinishDate.HasValue).ToList() ?? [];
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

        var start = tasks.Min(t => t.StartDate!.Value.Date);
        var finish = tasks.Max(t => t.FinishDate!.Value.Date);
        var totalDays = Math.Max(1, (finish - start).Days + 1);
        var headerHeight = 32.0;
        var rowHeight = 32.0;
        var pixelsPerDay = Math.Max(12, ActualWidth / totalDays);
        var typeface = new Typeface("Segoe UI");
        var dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;

        for (var day = 0; day < totalDays; day++)
        {
            var x = day * pixelsPerDay;
            var isWeekend = start.AddDays(day).DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;
            if (isWeekend)
            {
                drawingContext.DrawRectangle(
                    new SolidColorBrush(Color.FromArgb(32, 15, 108, 189)),
                    null,
                    new Rect(x, headerHeight, pixelsPerDay, Math.Max(0, ActualHeight - headerHeight)));
            }

            drawingContext.DrawLine(new Pen(border, 0.6), new Point(x, 0), new Point(x, ActualHeight));
        }

        var headerText = new FormattedText(
            $"{start:dd/MM/yyyy} - {finish:dd/MM/yyyy}",
            CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            typeface,
            12,
            text,
            dpi);
        drawingContext.DrawText(headerText, new Point(10, 8));
        drawingContext.DrawLine(new Pen(border, 1), new Point(0, headerHeight), new Point(ActualWidth, headerHeight));

        for (var i = 0; i < tasks.Count; i++)
        {
            var task = tasks[i];
            var y = headerHeight + i * rowHeight;
            var x = (task.StartDate!.Value.Date - start).TotalDays * pixelsPerDay;
            var width = ((task.FinishDate!.Value.Date - task.StartDate.Value.Date).TotalDays + 1) * pixelsPerDay;
            var rect = new Rect(x + 4, y + 9, Math.Max(8, width - 8), 14);
            var fill = task.TaskType == ScheduleTaskType.Group ? groupBar : bar;

            drawingContext.DrawRoundedRectangle(fill, null, rect, 3, 3);
            drawingContext.DrawLine(new Pen(border, 0.6), new Point(0, y), new Point(ActualWidth, y));
        }
    }
}
