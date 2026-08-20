using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using XTCADStyle.Controls;
using XTSchedule.Core.Enums;
using XTSchedule.Core.Models;
using XTSchedule.UI.ViewModels;

namespace XTSchedule.UI.Views;

public partial class MainWindow
{
    private readonly MainWindowViewModel _viewModel;
    private Point? _dragStartPoint;
    private ScrollViewer? _gridScrollViewer;
    private bool _syncingScroll;

    public MainWindow(MainWindowViewModel viewModel)
    {
        _viewModel = viewModel;
        DataContext = viewModel;
        InitializeComponent();
        Closing += OnClosing;
        Loaded += OnLoaded;
        timelineControl.BeginEditCallback = _viewModel.PushUndoSnapshot;
        timelineControl.LinkRequested = _viewModel.AddPredecessorLink;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _viewModel.CheckForRecovery();

        // DataGrid tự cuộn dọc nội bộ (ScrollViewer trong template, chỉ có sau khi
        // đã Loaded) — đồng bộ với ScrollViewer bọc timeline để 2 bên luôn khớp
        // hàng, không cần restructure lại layout thành 1 ScrollViewer chung.
        _gridScrollViewer = FindVisualChild<ScrollViewer>(taskGrid);
        if (_gridScrollViewer != null)
        {
            _gridScrollViewer.ScrollChanged += GridScrollViewer_ScrollChanged;
        }

        TryAutoFitTimelineZoom();
    }

    /// <summary>Timeline tự co giãn theo khung nhìn cho tới khi user tự zoom tay
    /// (xem MainWindowViewModel.AutoFitZoom/IsAutoFitZoom) — gọi lại mỗi khi
    /// ScrollViewer đổi kích thước (resize cửa sổ) hoặc nội dung đổi tổng số
    /// ngày (ExtentWidth đổi do sửa ngày công việc).</summary>
    private void TryAutoFitTimelineZoom()
    {
        var tasks = _viewModel.Tasks.Where(t => t.StartDate.HasValue && t.FinishDate.HasValue).ToList();
        if (tasks.Count == 0 || timelineScroll.ViewportWidth <= 0)
        {
            return;
        }

        var start = tasks.Min(t => t.StartDate!.Value.Date);
        var finish = tasks.Max(t => t.FinishDate!.Value.Date);
        var totalDays = Math.Max(1, (finish - start).Days + 1);
        _viewModel.AutoFitZoom(timelineScroll.ViewportWidth, totalDays);
    }

    private void GridScrollViewer_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (_syncingScroll)
        {
            return;
        }

        _syncingScroll = true;
        try
        {
            timelineScroll.ScrollToVerticalOffset(e.VerticalOffset);
        }
        finally
        {
            _syncingScroll = false;
        }
    }

    /// <summary>Ctrl+lăn chuột = zoom liên tục (như Figma/Miro/hầu hết app CAD),
    /// lăn thường không giữ Ctrl vẫn cuộn dọc bình thường qua ScrollViewer.</summary>
    private void TimelineScroll_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if ((Keyboard.Modifiers & ModifierKeys.Control) == 0)
        {
            return;
        }

        e.Handled = true;
        var factor = e.Delta > 0 ? 1.15 : 1 / 1.15;
        _viewModel.AdjustZoom(factor);
    }

    private void TimelineScroll_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (e.ViewportWidthChange != 0 || e.ExtentWidthChange != 0)
        {
            TryAutoFitTimelineZoom();
        }

        if (_syncingScroll || _gridScrollViewer is null)
        {
            return;
        }

        _syncingScroll = true;
        try
        {
            _gridScrollViewer.ScrollToVerticalOffset(e.VerticalOffset);
        }
        finally
        {
            _syncingScroll = false;
        }
    }

    private static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T typed)
            {
                return typed;
            }

            var descendant = FindVisualChild<T>(child);
            if (descendant != null)
            {
                return descendant;
            }
        }

        return null;
    }

    private void TaskGrid_SelectionChanged(object sender, SelectionChangedEventArgs e) =>
        _viewModel.SetMultiSelection(taskGrid.SelectedItems.Cast<ScheduleTask>());

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (!_viewModel.CanClose())
        {
            e.Cancel = true;
        }
    }

    /// <summary>ContextMenu dựng bằng code thay vì bind ItemsSource trong XAML —
    /// ContextMenu là 1 cây visual riêng (Popup), DataContext không tự kế thừa từ
    /// nút bấm nên bind trực tiếp phức tạp hơn cần thiết cho 1 danh sách ngắn.</summary>
    private void RecentFiles_Click(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu { PlacementTarget = btnRecent, Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom };

        if (_viewModel.RecentFiles.Count == 0)
        {
            menu.Items.Add(new MenuItem { Header = "Chưa có file nào gần đây", IsEnabled = false });
        }
        else
        {
            foreach (var path in _viewModel.RecentFiles)
            {
                var item = new MenuItem { Header = Path.GetFileName(path), ToolTip = path };
                item.Click += (_, _) => _viewModel.OpenRecentCommand.Execute(path);
                menu.Items.Add(item);
            }
        }

        menu.IsOpen = true;
    }

    private void Templates_Click(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu { PlacementTarget = btnTemplates, Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom };
        var folder = MainWindowViewModel.TemplatesFolder;
        var files = Directory.Exists(folder)
            ? Directory.GetFiles(folder, "*.xtschedule").OrderBy(Path.GetFileName).ToList()
            : [];

        if (files.Count == 0)
        {
            menu.Items.Add(new MenuItem { Header = "Chưa có mẫu nào — dùng \"Lưu mẫu\" để tạo", IsEnabled = false });
        }
        else
        {
            foreach (var path in files)
            {
                menu.Items.Add(new MenuItem
                {
                    Header = Path.GetFileNameWithoutExtension(path),
                    Command = _viewModel.NewFromTemplateCommand,
                    CommandParameter = path
                });
            }
        }

        menu.IsOpen = true;
    }

    private void TaskGrid_DeleteRequested(object sender, RoutedEventArgs e) => _viewModel.DeleteCommand.Execute(null);

    // ── Toolbar rút gọn: mỗi nút nhóm mở 1 ContextMenu phẳng — style XTMenuItem
    // dùng chung trong app không có Popup cho submenu lồng nên không dựng được
    // menu bar phân cấp thật (File/Edit/View...), gom phẳng theo nhóm là lựa
    // chọn an toàn nhất với hạ tầng style hiện có. ────────────────────────────

    private void EditMenu_Click(object sender, RoutedEventArgs e) => ShowGroupMenu((Button)sender,
        ("Hoàn tác (Ctrl+Z)", _viewModel.UndoCommand, null),
        ("Làm lại (Ctrl+Y)", _viewModel.RedoCommand, null),
        ("Sao chép (Ctrl+D)", _viewModel.DuplicateCommand, null),
        ("Dán", _viewModel.PasteRowsCommand, null),
        ("Xóa (Delete)", _viewModel.DeleteCommand, null));

    private void TaskMenu_Click(object sender, RoutedEventArgs e) => ShowGroupMenu((Button)sender,
        ("Thêm nhóm", _viewModel.AddGroupCommand, null),
        ("Thêm việc", _viewModel.AddTaskCommand, null),
        ("Lên", _viewModel.MoveUpCommand, null),
        ("Xuống", _viewModel.MoveDownCommand, null),
        ("Indent", _viewModel.IndentCommand, null),
        ("Outdent", _viewModel.OutdentCommand, null),
        ("Nối tiếp", _viewModel.SequenceCommand, null),
        ("-1 ngày", _viewModel.ShiftBackCommand, null),
        ("+1 ngày", _viewModel.ShiftForwardCommand, null));

    private void ViewMenu_Click(object sender, RoutedEventArgs e) => ShowGroupMenu((Button)sender,
        ("Ngày", _viewModel.SetZoomCommand, 32.0),
        ("Tuần", _viewModel.SetZoomCommand, 10.0),
        ("Tháng", _viewModel.SetZoomCommand, 3.0));

    private void ExportMenu_Click(object sender, RoutedEventArgs e) => ShowGroupMenu((Button)sender,
        ("Thiết lập trang", _viewModel.PageSetupCommand, null),
        ("Xem trước khi in", _viewModel.PrintPreviewCommand, null),
        ("In", _viewModel.PrintCommand, null),
        ("Xuất PDF", _viewModel.ExportPdfCommand, null),
        ("Xuất Word", _viewModel.ExportWordCommand, null));

    private static void ShowGroupMenu(UIElement anchor, params (string Header, ICommand Command, object? Parameter)[] items) =>
        ShowGroupMenu(anchor, System.Windows.Controls.Primitives.PlacementMode.Bottom, items);

    private static void ShowGroupMenu(UIElement anchor, System.Windows.Controls.Primitives.PlacementMode placement,
        params (string Header, ICommand Command, object? Parameter)[] items)
    {
        var menu = new ContextMenu { PlacementTarget = anchor, Placement = placement };
        foreach (var (header, command, parameter) in items)
        {
            menu.Items.Add(new MenuItem { Header = header, Command = command, CommandParameter = parameter });
        }

        menu.IsOpen = true;
    }

    /// <summary>Chuột phải trên grid — chọn dòng dưới con trỏ trước (nếu chưa
    /// được chọn) rồi mở menu thao tác nhanh tại vị trí con trỏ.</summary>
    private void TaskGrid_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        var row = FindRowAt(e.GetPosition(taskGrid));
        if (row?.Item is ScheduleTask task && !taskGrid.SelectedItems.Contains(task))
        {
            taskGrid.SelectedItem = task;
        }

        var menu = new ContextMenu { PlacementTarget = taskGrid, Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint };
        menu.Items.Add(MakeMenuItem("Thêm nhóm", "Mat.FolderPlus", _viewModel.AddGroupCommand));
        menu.Items.Add(MakeMenuItem("Thêm việc", "Mat.FileDocumentPlus", _viewModel.AddTaskCommand));
        menu.Items.Add(MakeMenuItem("Sao chép (Ctrl+D)", "Mat.ContentCopy", _viewModel.DuplicateCommand));
        menu.Items.Add(MakeMenuItem("Xóa (Delete)", "Mat.Delete", _viewModel.DeleteCommand));
        menu.Items.Add(new Separator());
        menu.Items.Add(MakeMenuItem("-1 ngày", null, _viewModel.ShiftBackCommand));
        menu.Items.Add(MakeMenuItem("+1 ngày", null, _viewModel.ShiftForwardCommand));
        menu.Items.Add(MakeMenuItem("Indent", null, _viewModel.IndentCommand));
        menu.Items.Add(MakeMenuItem("Outdent", null, _viewModel.OutdentCommand));
        menu.Items.Add(new Separator());
        menu.Items.Add(MakeMenuItem("Hoàn tác (Ctrl+Z)", "Mat.Undo", _viewModel.UndoCommand));
        menu.Items.Add(MakeMenuItem("Làm lại (Ctrl+Y)", "Mat.Redo", _viewModel.RedoCommand));
        menu.IsOpen = true;
    }

    private static MenuItem MakeMenuItem(string header, string? iconResourceKey, ICommand command)
    {
        var item = new MenuItem { Header = header, Command = command };
        if (iconResourceKey != null && Application.Current.TryFindResource(iconResourceKey) is Geometry geometry)
        {
            item.Icon = new System.Windows.Shapes.Path
            {
                Data = geometry,
                Width = 13,
                Height = 13,
                Stretch = Stretch.Uniform,
                Fill = (Brush)Application.Current.FindResource("CadTextSecondary")
            };
        }

        return item;
    }

    /// <summary>Snapshot undo TRƯỚC khi 1 ô bắt đầu được sửa tay trên grid (gõ tên,
    /// đổi ngày...) — các thao tác qua nút bấm (Thêm việc, Xóa...) tự gọi
    /// PushUndoSnapshot() trong ViewModel, còn sửa trực tiếp trên ô thì chỉ có hook
    /// ở đây (WPF DataGrid không có sự kiện "trước khi 1 property đổi giá trị").</summary>
    private void TaskGrid_BeginningEdit(object sender, DataGridBeginningEditEventArgs e) => _viewModel.PushUndoSnapshot();

    // ── Kéo-thả sắp xếp lại hàng ─────────────────────────────────────────────

    private void TaskGrid_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _dragStartPoint = e.GetPosition(taskGrid);
    }

    private void TaskGrid_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (_dragStartPoint is null || e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        var current = e.GetPosition(taskGrid);
        if (Math.Abs(current.X - _dragStartPoint.Value.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(current.Y - _dragStartPoint.Value.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        var row = FindRowAt(_dragStartPoint.Value);
        _dragStartPoint = null;
        if (row?.Item is not ScheduleTask task)
        {
            return;
        }

        // XTDataGrid tự BeginEdit() khi bấm vào ô template (SingleClickEdit) —
        // bấm để bắt đầu kéo cũng kích hoạt edit mode luôn. Kéo/thả trong lúc
        // grid còn "AddNew or EditItem transaction" khiến TasksView.Refresh()
        // sau khi Move (trong ViewModel.Renumber) ném InvalidOperationException.
        // Commit ngay trước khi bắt đầu kéo để tránh việc này.
        taskGrid.CommitEdit(DataGridEditingUnit.Row, true);

        DragDrop.DoDragDrop(taskGrid, task, DragDropEffects.Move);
    }

    private void TaskGrid_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(typeof(ScheduleTask)) ? DragDropEffects.Move : DragDropEffects.None;
        e.Handled = true;
    }

    private void TaskGrid_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(typeof(ScheduleTask)) is not ScheduleTask dragged)
        {
            return;
        }

        var targetRow = FindRowAt(e.GetPosition(taskGrid));
        if (targetRow?.Item is not ScheduleTask target || ReferenceEquals(target, dragged))
        {
            return;
        }

        taskGrid.CommitEdit(DataGridEditingUnit.Row, true);
        var newIndex = _viewModel.Tasks.IndexOf(target);

        // Thả lên 1 Group → dòng kéo trở thành con của group đó. Thả lên 1
        // Task thường → dòng kéo "đứng cùng cấp" với dòng đó (cho phép vừa kéo
        // vào nhóm — thả lên 1 dòng con — vừa kéo ra khỏi nhóm — thả lên 1 dòng
        // top-level ngoài nhóm).
        var newLevel = target.TaskType == ScheduleTaskType.Group ? target.Level + 1 : target.Level;
        _viewModel.MoveTask(dragged, newIndex, newLevel);
    }

    private DataGridRow? FindRowAt(Point point)
    {
        var hit = VisualTreeHelper.HitTest(taskGrid, point)?.VisualHit;
        return hit is null ? null : XTDataGrid.FindParent<DataGridRow>(hit);
    }
}
