using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;
using XTCADStyle.Themes;
using XTSchedule.Core.Enums;
using XTSchedule.Core.Interfaces;
using XTSchedule.Core.Models;
using XTSchedule.Core.Serialization;
using XTSchedule.UI.Printing;
using XTSchedule.UI.Views;

namespace XTSchedule.UI.ViewModels;

public sealed class MainWindowViewModel : INotifyPropertyChanged
{
    private const int MaxRecentFiles = 8;
    private const int MaxUndoDepth = 50;

    private static readonly string RecoveryFilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "XTSchedule", "recovery.xtschedule");

    public static readonly string TemplatesFolder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "XTSchedule", "Templates");

    private readonly IScheduleDocumentService _documentService;
    private readonly IAppSettingsService _settingsService;
    private readonly List<string> _undoStack = [];
    private readonly List<string> _redoStack = [];
    private readonly DispatcherTimer _autoSaveTimer;
    private ScheduleDocument _document;
    private string? _filePath;
    private ScheduleTask? _selectedTask;
    private bool _isDirty;
    private bool _suppressDirty;
    private bool _suppressUndoCapture;
    private bool _applyingCascade;
    private bool _applyingRollup;

    public MainWindowViewModel(IScheduleDocumentService documentService, IAppSettingsService settingsService)
    {
        _documentService = documentService;
        _settingsService = settingsService;

        _document = documentService.CreateNew();
        Tasks = _document.Tasks;
        AttachDirtyTracking(Tasks);

        TasksView = CollectionViewSource.GetDefaultView(Tasks);
        TasksView.Filter = FilterCollapsedTasks;

        RecentFiles = new ObservableCollection<string>(
            _settingsService.Load().RecentFiles.Where(File.Exists).Take(MaxRecentFiles));

        NewCommand = new RelayCommand(_ => NewDocument());
        OpenCommand = new RelayCommand(_ => OpenDocument());
        SaveCommand = new RelayCommand(_ => SaveDocument());
        SaveAsCommand = new RelayCommand(_ => SaveDocumentAs());
        AddGroupCommand = new RelayCommand(_ => AddTask(ScheduleTaskType.Group));
        AddTaskCommand = new RelayCommand(_ => AddTask(ScheduleTaskType.Task));
        DeleteCommand = new RelayCommand(_ => DeleteSelected(), _ => SelectedTask is not null);
        DuplicateCommand = new RelayCommand(_ => DuplicateSelected(), _ => SelectedTask is not null);
        MoveUpCommand = new RelayCommand(_ => MoveSelected(-1), _ => SelectedTask is not null);
        MoveDownCommand = new RelayCommand(_ => MoveSelected(1), _ => SelectedTask is not null);
        IndentCommand = new RelayCommand(_ => ChangeIndent(1), _ => SelectedTask is not null);
        OutdentCommand = new RelayCommand(_ => ChangeIndent(-1), _ => SelectedTask is not null);
        PasteRowsCommand = new RelayCommand(_ => PasteRows());
        ShiftBackCommand = new RelayCommand(_ => ShiftSelected(-1), _ => SelectedTask is not null);
        ShiftForwardCommand = new RelayCommand(_ => ShiftSelected(1), _ => SelectedTask is not null);
        SequenceCommand = new RelayCommand(_ => SequenceFromSelected(), _ => Tasks.Count > 1);
        OpenRecentCommand = new RelayCommand(p => OpenRecentFile(p as string));
        ToggleCollapseCommand = new RelayCommand(p => ToggleCollapse(p as ScheduleTask));
        UndoCommand = new RelayCommand(_ => Undo(), _ => _undoStack.Count > 0);
        RedoCommand = new RelayCommand(_ => Redo(), _ => _redoStack.Count > 0);
        SetZoomCommand = new RelayCommand(p =>
        {
            if (p is double pixelsPerDay)
            {
                PixelsPerDay = pixelsPerDay;
            }
        });
        ZoomInCommand = new RelayCommand(_ => AdjustZoom(1.25));
        ZoomOutCommand = new RelayCommand(_ => AdjustZoom(1 / 1.25));
        PageSetupCommand = new RelayCommand(_ => ShowPageSetup());
        PrintPreviewCommand = new RelayCommand(_ => ShowPrintPreview());
        PrintCommand = new RelayCommand(_ => Print());
        ExportPdfCommand = new RelayCommand(_ => ExportPdf());
        ExportWordCommand = new RelayCommand(_ => ExportWord());
        SaveAsTemplateCommand = new RelayCommand(_ => SaveAsTemplate());
        NewFromTemplateCommand = new RelayCommand(p => NewFromTemplate(p as string));

        _autoSaveTimer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(2) };
        _autoSaveTimer.Tick += (_, _) => TryWriteRecoveryFile();
        _autoSaveTimer.Start();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ScheduleDocument Document
    {
        get => _document;
        private set
        {
            DetachDirtyTracking(Tasks);
            _document = value;
            Tasks = value.Tasks;
            AttachDirtyTracking(Tasks);
            TasksView = CollectionViewSource.GetDefaultView(Tasks);
            TasksView.Filter = FilterCollapsedTasks;
            OnPropertyChanged(nameof(TasksView));
            SelectedTask = Tasks.FirstOrDefault();
            OnPropertyChanged();
            OnPropertyChanged(nameof(WindowTitle));
        }
    }

    public string WindowTitle
    {
        get
        {
            var baseTitle = string.IsNullOrWhiteSpace(_filePath)
                ? $"XTSchedule - {Document.ProjectName}"
                : $"XTSchedule - {Document.ProjectName} ({Path.GetFileName(_filePath)})";
            return IsDirty ? baseTitle + " *" : baseTitle;
        }
    }

    /// <summary>True nếu có thay đổi chưa lưu — theo dõi qua CollectionChanged của
    /// Tasks và PropertyChanged của từng ScheduleTask (xem AttachDirtyTracking).
    /// _suppressDirty chặn New/Open/Save/Undo-Redo tự đánh dấu dirty khi chúng gán
    /// lại dữ liệu hàng loạt (không phải do user gõ tay).</summary>
    public bool IsDirty
    {
        get => _isDirty;
        private set
        {
            if (_isDirty == value)
            {
                return;
            }

            _isDirty = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(WindowTitle));
        }
    }

    public ObservableCollection<ScheduleTask> Tasks { get; private set; }

    /// <summary>View lọc theo IsCollapsed của group cha — grid bind vào đây thay vì
    /// Tasks trực tiếp để ẩn/hiện được các dòng con khi thu gọn nhóm.</summary>
    public ICollectionView TasksView { get; private set; }

    public ObservableCollection<string> RecentFiles { get; }

    private const double BaselinePixelsPerDay = 32.0; // = 100% — mốc zoom "Ngày" mặc định

    public const double MinPixelsPerDay = 0.75;
    public const double MaxPixelsPerDay = 320.0;

    private double _pixelsPerDay = BaselinePixelsPerDay;

    /// <summary>Mật độ px/ngày của timeline — zoom liên tục (Ctrl+lăn chuột,
    /// xem MainWindow.xaml.cs) thay vì 3 mức cố định. Nút "Ngày/Tuần/Tháng"
    /// trong menu Xem chỉ set nhanh về 1 giá trị preset (32/10/3).</summary>
    public double PixelsPerDay
    {
        get => _pixelsPerDay;
        set
        {
            SetPixelsPerDayCore(value);
            IsAutoFitZoom = false;
        }
    }

    private void SetPixelsPerDayCore(double value)
    {
        var clamped = Math.Clamp(value, MinPixelsPerDay, MaxPixelsPerDay);
        if (Math.Abs(_pixelsPerDay - clamped) < 0.001)
        {
            return;
        }

        _pixelsPerDay = clamped;
        OnPropertyChanged(nameof(PixelsPerDay));
        OnPropertyChanged(nameof(ZoomPercentText));
        OnPropertyChanged(nameof(ZoomSliderValue));
    }

    /// <summary>True nếu user CHƯA tự zoom tay lần nào cho tài liệu đang mở —
    /// timeline tự co giãn theo khung nhìn (xem AutoFitZoom, gọi từ MainWindow
    /// khi ScrollViewer đổi kích thước). Tắt hẳn ngay khi user zoom tay (nút
    /// +/-, slider, Ctrl+lăn chuột) và chỉ bật lại khi New/Open/mở mẫu khác.</summary>
    public bool IsAutoFitZoom { get; private set; } = true;

    /// <summary>Gọi từ MainWindow mỗi khi khung nhìn timeline đổi kích thước
    /// hoặc tổng số ngày của tài liệu đổi — không làm gì nếu user đã tự zoom.</summary>
    public void AutoFitZoom(double viewportWidthPx, int totalDays)
    {
        if (!IsAutoFitZoom || viewportWidthPx <= 0 || totalDays <= 0)
        {
            return;
        }

        SetPixelsPerDayCore(viewportWidthPx / totalDays);
    }

    /// <summary>"100%" ứng với mốc zoom "Ngày" (32px/ngày) — hiện ở thanh trạng
    /// thái dưới cùng, giống chỉ số % zoom của Word/Excel.</summary>
    public string ZoomPercentText => $"{PixelsPerDay / BaselinePixelsPerDay * 100:0}%";

    /// <summary>0-100 cho <see cref="Slider"/> ở status bar — map log-scale sang
    /// PixelsPerDay vì biên độ 0.75..320 quá rộng để dùng thang tuyến tính (kéo
    /// slider sẽ dồn hết cảm giác zoom vào 1 đoạn nhỏ đầu thanh).</summary>
    public double ZoomSliderValue
    {
        get => Math.Log(PixelsPerDay / MinPixelsPerDay) / Math.Log(MaxPixelsPerDay / MinPixelsPerDay) * 100;
        set
        {
            var ratio = Math.Clamp(value, 0, 100) / 100;
            PixelsPerDay = MinPixelsPerDay * Math.Pow(MaxPixelsPerDay / MinPixelsPerDay, ratio);
        }
    }

    /// <summary>Gọi từ Ctrl+lăn chuột — factor >1 zoom vào (lăn lên), <1 zoom ra
    /// (lăn xuống), nhân dồn theo giá trị hiện tại nên cảm giác zoom đều ở mọi
    /// mức, không tuyến tính cộng dồn (giống Figma/Miro).</summary>
    public void AdjustZoom(double factor) => PixelsPerDay *= factor;

    public ScheduleTask? SelectedTask
    {
        get => _selectedTask;
        set
        {
            _selectedTask = value;
            OnPropertyChanged();
            RaiseCommandStates();
        }
    }

    private IReadOnlyList<ScheduleTask> _multiSelection = [];

    /// <summary>Đẩy từ MainWindow.xaml.cs khi DataGrid.SelectionChanged (Extended
    /// selection không có cách bind thẳng SelectedItems trong WPF). Delete/Indent/
    /// Outdent/Shift ±ngày áp dụng cho toàn bộ danh sách này khi chọn nhiều dòng.</summary>
    public void SetMultiSelection(IEnumerable<ScheduleTask> tasks) => _multiSelection = tasks.ToList();

    private IReadOnlyList<ScheduleTask> EffectiveSelection =>
        _multiSelection.Count > 1 ? _multiSelection : SelectedTask is null ? [] : [SelectedTask];

    public ICommand NewCommand { get; }

    public ICommand OpenCommand { get; }

    public ICommand SaveCommand { get; }

    public ICommand SaveAsCommand { get; }

    public ICommand AddGroupCommand { get; }

    public ICommand AddTaskCommand { get; }

    public ICommand DeleteCommand { get; }

    public ICommand DuplicateCommand { get; }

    public ICommand MoveUpCommand { get; }

    public ICommand MoveDownCommand { get; }

    public ICommand IndentCommand { get; }

    public ICommand OutdentCommand { get; }

    public ICommand PasteRowsCommand { get; }

    public ICommand ShiftBackCommand { get; }

    public ICommand ShiftForwardCommand { get; }

    public ICommand SequenceCommand { get; }

    public ICommand OpenRecentCommand { get; }

    public ICommand ToggleCollapseCommand { get; }

    public ICommand UndoCommand { get; }

    public ICommand RedoCommand { get; }

    public ICommand SetZoomCommand { get; }

    public ICommand ZoomInCommand { get; }

    public ICommand ZoomOutCommand { get; }

    public ICommand PageSetupCommand { get; }

    public ICommand PrintPreviewCommand { get; }

    public ICommand PrintCommand { get; }

    public ICommand ExportPdfCommand { get; }

    public ICommand ExportWordCommand { get; }

    public ICommand SaveAsTemplateCommand { get; }

    public ICommand NewFromTemplateCommand { get; }

    /// <summary>Gọi từ MainWindow.Closing — trả về false để hủy đóng cửa sổ
    /// (user chọn Cancel ở hộp thoại xác nhận, hoặc Save As bị hủy).</summary>
    public bool CanClose()
    {
        var ok = ConfirmDiscardChanges("Đóng chương trình");
        if (ok)
        {
            _autoSaveTimer.Stop();
        }

        return ok;
    }

    /// <summary>Gọi 1 lần lúc khởi động (từ MainWindow, sau InitializeComponent) —
    /// nếu có file recovery từ phiên trước bị đóng đột ngột (crash/mất điện, không
    /// qua Closing bình thường vì Closing luôn dọn file này), hỏi khôi phục.</summary>
    public void CheckForRecovery()
    {
        if (!File.Exists(RecoveryFilePath))
        {
            return;
        }

        var result = ModernMessageBox.Show(
            "Phát hiện có thay đổi chưa lưu từ phiên làm việc trước (có thể do chương trình bị đóng đột ngột).\n" +
            "Bạn có muốn khôi phục không?",
            "Khôi phục dữ liệu", MessageBoxButton.YesNo, MessageBoxImage.Question);

        if (result != MessageBoxResult.Yes)
        {
            ClearRecoveryFile();
            return;
        }

        try
        {
            var json = File.ReadAllText(RecoveryFilePath);
            var envelope = JsonSerializer.Deserialize<ScheduleFileEnvelope>(json, JsonOptions());
            if (envelope?.Document is null)
            {
                ClearRecoveryFile();
                return;
            }

            _suppressDirty = true;
            try
            {
                Document = envelope.Document;
                _filePath = null;
            }
            finally
            {
                _suppressDirty = false;
            }

            IsDirty = true;
            OnPropertyChanged(nameof(WindowTitle));
        }
        catch
        {
            // file recovery hỏng — bỏ qua, không chặn app khởi động
        }
        finally
        {
            ClearRecoveryFile();
        }
    }

    // ── New / Open / Save ────────────────────────────────────────────────────

    private void NewDocument()
    {
        if (!ConfirmDiscardChanges("Tạo mới"))
        {
            return;
        }

        _suppressDirty = true;
        try
        {
            Document = _documentService.CreateNew();
            _filePath = null;
        }
        finally
        {
            _suppressDirty = false;
        }

        IsDirty = false;
        ResetUndoHistory();
        OnPropertyChanged(nameof(WindowTitle));
    }

    private void OpenDocument()
    {
        if (!ConfirmDiscardChanges("Mở tiến độ khác"))
        {
            return;
        }

        var dialog = new OpenFileDialog
        {
            Title = "Mở tiến độ",
            Filter = "XTSchedule (*.xtschedule)|*.xtschedule|JSON (*.json)|*.json|All files (*.*)|*.*"
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        LoadFromPath(dialog.FileName);
    }

    private void OpenRecentFile(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        if (!File.Exists(path))
        {
            ModernMessageBox.Show($"Không tìm thấy file:\n{path}", "Mở gần đây",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            RemoveRecentFile(path);
            return;
        }

        if (!ConfirmDiscardChanges("Mở tiến độ khác"))
        {
            return;
        }

        LoadFromPath(path);
    }

    private void LoadFromPath(string path)
    {
        ScheduleFileEnvelope? envelope;
        try
        {
            var json = File.ReadAllText(path);
            envelope = JsonSerializer.Deserialize<ScheduleFileEnvelope>(json, JsonOptions());
        }
        catch (Exception ex)
        {
            ModernMessageBox.Show($"Không đọc được file:\n{ex.Message}", "Mở tiến độ",
                MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        if (envelope?.Document is null || !string.Equals(envelope.FileFormat, "XTSchedule", StringComparison.OrdinalIgnoreCase))
        {
            ModernMessageBox.Show("File không đúng định dạng XTSchedule.", "Mở tiến độ",
                MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        _suppressDirty = true;
        try
        {
            Document = envelope.Document;
            _filePath = path;
        }
        finally
        {
            _suppressDirty = false;
        }

        IsDirty = false;
        ResetUndoHistory();
        OnPropertyChanged(nameof(WindowTitle));
        AddRecentFile(path);
    }

    private void SaveDocument()
    {
        if (string.IsNullOrWhiteSpace(_filePath))
        {
            SaveDocumentAs();
            return;
        }

        WriteDocument(_filePath);
        IsDirty = false;
        AddRecentFile(_filePath);
        ClearRecoveryFile();
    }

    private void SaveDocumentAs()
    {
        var dialog = new SaveFileDialog
        {
            Title = "Lưu tiến độ",
            Filter = "XTSchedule (*.xtschedule)|*.xtschedule|JSON (*.json)|*.json",
            DefaultExt = ".xtschedule",
            FileName = $"{SanitizeFileName(Document.ProjectName)}.xtschedule"
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        _filePath = dialog.FileName;
        WriteDocument(_filePath);
        IsDirty = false;
        OnPropertyChanged(nameof(WindowTitle));
        AddRecentFile(_filePath);
        ClearRecoveryFile();
    }

    private void WriteDocument(string path)
    {
        var envelope = new ScheduleFileEnvelope { Document = Document };
        var json = JsonSerializer.Serialize(envelope, JsonOptions());
        File.WriteAllText(path, json);
    }

    // ── Page setup / Print / Export ──────────────────────────────────────────

    private void ShowPageSetup()
    {
        var dialog = new PageSetupWindow(Document.PrintSettings) { Owner = System.Windows.Application.Current.MainWindow };
        dialog.ShowDialog();
        if (dialog.Applied)
        {
            IsDirty = true;
        }
    }

    private void ShowPrintPreview()
    {
        var window = new PrintPreviewWindow(Document, Document.PrintSettings) { Owner = System.Windows.Application.Current.MainWindow };
        window.ShowDialog();
    }

    private void Print()
    {
        var dialog = new PrintDialog();
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        var pages = SchedulePageRenderer.RenderPages(Document, Document.PrintSettings);
        var fixedDocument = new FixedDocument();
        foreach (var page in pages)
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

    private void ExportPdf()
    {
        var dialog = new SaveFileDialog
        {
            Title = "Xuất PDF",
            Filter = "PDF (*.pdf)|*.pdf",
            DefaultExt = ".pdf",
            FileName = $"{SanitizeFileName(Document.ProjectName)}.pdf"
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            var pages = SchedulePageRenderer.RenderPages(Document, Document.PrintSettings);
            var (paperWidthMm, paperHeightMm) = SchedulePageRenderer.GetPaperSizeMm(
                Document.PrintSettings.PaperSize, Document.PrintSettings.Orientation);

            var pngPages = pages.Select(page => (
                PngBytes: ToPngBytes(SchedulePageRenderer.RenderToBitmap(page, 150)),
                WidthMm: paperWidthMm,
                HeightMm: paperHeightMm));

            global::XTSchedule.Export.ExportService.ExportPagesToPdf(pngPages, dialog.FileName);
            ModernMessageBox.Show("Đã xuất PDF thành công.", "Xuất PDF", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            ModernMessageBox.Show($"Xuất PDF thất bại:\n{ex.Message}", "Xuất PDF", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ExportWord()
    {
        var dialog = new SaveFileDialog
        {
            Title = "Xuất Word",
            Filter = "Word (*.docx)|*.docx",
            DefaultExt = ".docx",
            FileName = $"{SanitizeFileName(Document.ProjectName)}.docx"
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            global::XTSchedule.Export.ExportService.ExportToWord(Document, dialog.FileName);
            ModernMessageBox.Show("Đã xuất Word thành công.", "Xuất Word", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            ModernMessageBox.Show($"Xuất Word thất bại:\n{ex.Message}", "Xuất Word", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private static byte[] ToPngBytes(BitmapSource bitmap)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var ms = new MemoryStream();
        encoder.Save(ms);
        return ms.ToArray();
    }

    // ── Template — "đơn giản": lưu/mở như file thường, chỉ khác thư mục mặc
    // định (%AppData%\XTSchedule\Templates). Không có window quản lý riêng —
    // nút "Mẫu" liệt kê file trong thư mục đó y hệt cơ chế Recent Files. ───────

    private void SaveAsTemplate()
    {
        Directory.CreateDirectory(TemplatesFolder);
        var dialog = new SaveFileDialog
        {
            Title = "Lưu làm mẫu",
            Filter = "XTSchedule (*.xtschedule)|*.xtschedule",
            DefaultExt = ".xtschedule",
            InitialDirectory = TemplatesFolder,
            FileName = $"{SanitizeFileName(Document.ProjectName)}.xtschedule"
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        // Lưu snapshot hiện tại làm mẫu — KHÔNG đổi _filePath của tài liệu đang
        // mở (mẫu là 1 bản sao độc lập, không phải "file đang làm việc" mới).
        var envelope = new ScheduleFileEnvelope { Document = Document };
        File.WriteAllText(dialog.FileName, JsonSerializer.Serialize(envelope, JsonOptions()));
    }

    private void NewFromTemplate(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return;
        }

        if (!ConfirmDiscardChanges("Tạo tiến độ từ mẫu"))
        {
            return;
        }

        ScheduleFileEnvelope? envelope;
        try
        {
            envelope = JsonSerializer.Deserialize<ScheduleFileEnvelope>(File.ReadAllText(path), JsonOptions());
        }
        catch (Exception ex)
        {
            ModernMessageBox.Show($"Không đọc được mẫu:\n{ex.Message}", "Mẫu", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        if (envelope?.Document is null)
        {
            ModernMessageBox.Show("File mẫu không đúng định dạng.", "Mẫu", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        // Giống New — coi như tài liệu mới tinh chưa có đường dẫn lưu riêng, chỉ
        // khác là seed dữ liệu từ mẫu thay vì dữ liệu mẫu cứng trong code.
        _suppressDirty = true;
        try
        {
            Document = envelope.Document;
            _filePath = null;
        }
        finally
        {
            _suppressDirty = false;
        }

        IsDirty = false;
        ResetUndoHistory();
        OnPropertyChanged(nameof(WindowTitle));
    }

    // ── Auto-save / recovery ─────────────────────────────────────────────────

    private void TryWriteRecoveryFile()
    {
        if (!IsDirty)
        {
            return;
        }

        try
        {
            var dir = Path.GetDirectoryName(RecoveryFilePath);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }

            WriteDocument(RecoveryFilePath);
        }
        catch
        {
            // best-effort — auto-save lỗi không được làm gián đoạn công việc đang làm
        }
    }

    private static void ClearRecoveryFile()
    {
        try
        {
            if (File.Exists(RecoveryFilePath))
            {
                File.Delete(RecoveryFilePath);
            }
        }
        catch
        {
        }
    }

    /// <summary>True nếu được phép tiếp tục thao tác (không có gì chưa lưu, hoặc
    /// user đồng ý lưu/bỏ qua). False nếu user bấm Hủy — caller phải dừng lại.</summary>
    private bool ConfirmDiscardChanges(string actionTitle)
    {
        if (!IsDirty)
        {
            return true;
        }

        var result = ModernMessageBox.Show(
            "Tiến độ hiện tại có thay đổi chưa lưu. Lưu lại trước khi tiếp tục?",
            actionTitle, MessageBoxButton.YesNoCancel, MessageBoxImage.Question);

        switch (result)
        {
            case MessageBoxResult.Cancel:
                return false;
            case MessageBoxResult.No:
                ClearRecoveryFile();
                return true;
            case MessageBoxResult.Yes:
                SaveDocument();
                return !IsDirty; // vẫn dirty nghĩa là Save As bị hủy — không tiếp tục
            default:
                return false;
        }
    }

    // ── Recent files ─────────────────────────────────────────────────────────

    private void AddRecentFile(string path)
    {
        var existing = RecentFiles.FirstOrDefault(x => string.Equals(x, path, StringComparison.OrdinalIgnoreCase));
        if (existing != null)
        {
            RecentFiles.Remove(existing);
        }

        RecentFiles.Insert(0, path);
        while (RecentFiles.Count > MaxRecentFiles)
        {
            RecentFiles.RemoveAt(RecentFiles.Count - 1);
        }

        PersistRecentFiles();
    }

    private void RemoveRecentFile(string path)
    {
        var existing = RecentFiles.FirstOrDefault(x => string.Equals(x, path, StringComparison.OrdinalIgnoreCase));
        if (existing == null)
        {
            return;
        }

        RecentFiles.Remove(existing);
        PersistRecentFiles();
    }

    private void PersistRecentFiles()
    {
        _settingsService.Save(new AppSettings { RecentFiles = RecentFiles.ToList() });
    }

    private static JsonSerializerOptions JsonOptions()
    {
        return new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true
        };
    }

    private static string SanitizeFileName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var name = string.IsNullOrWhiteSpace(value) ? "XTSchedule" : value;
        return new string(name.Select(c => invalid.Contains(c) ? '_' : c).ToArray());
    }

    // ── Task editing ─────────────────────────────────────────────────────────

    private void AddTask(ScheduleTaskType taskType)
    {
        PushUndoSnapshot();
        var insertIndex = SelectedTask is null ? Tasks.Count : Tasks.IndexOf(SelectedTask) + 1;
        var reference = SelectedTask ?? Tasks.LastOrDefault();
        var start = reference?.FinishDate?.AddDays(1) ?? DateTime.Today;
        var task = new ScheduleTask
        {
            TaskType = taskType,
            Level = taskType == ScheduleTaskType.Group ? 0 : Math.Max(0, reference?.Level ?? 0),
            Name = taskType == ScheduleTaskType.Group ? "Nhóm công việc mới" : "Công việc mới",
            StartDate = start,
            FinishDate = start.AddDays(taskType == ScheduleTaskType.Group ? 4 : 2)
        };

        Tasks.Insert(insertIndex, task);
        Renumber();
        SelectedTask = task;
    }

    private void DuplicateSelected()
    {
        if (SelectedTask is null)
        {
            return;
        }

        PushUndoSnapshot();
        var source = SelectedTask;
        var copy = new ScheduleTask
        {
            TaskType = source.TaskType,
            Level = source.Level,
            Name = source.Name + " (sao chép)",
            StartDate = source.StartDate,
            FinishDate = source.FinishDate,
            Progress = source.Progress,
            Note = source.Note,
            BarStyleId = source.BarStyleId,
            IsMilestone = source.IsMilestone
        };

        var insertIndex = Tasks.IndexOf(source) + 1;
        Tasks.Insert(insertIndex, copy);
        Renumber();
        SelectedTask = copy;
    }

    private void PasteRows()
    {
        if (!System.Windows.Clipboard.ContainsText())
        {
            return;
        }

        var rows = ParseClipboardRows(System.Windows.Clipboard.GetText()).ToList();
        if (rows.Count == 0)
        {
            return;
        }

        PushUndoSnapshot();
        var insertIndex = SelectedTask is null ? Tasks.Count : Tasks.IndexOf(SelectedTask) + 1;
        foreach (var row in rows)
        {
            Tasks.Insert(insertIndex++, row);
        }

        Renumber();
        SelectedTask = rows[0];
    }

    private static IEnumerable<ScheduleTask> ParseClipboardRows(string text)
    {
        var lines = text
            .Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Trim())
            .Where(line => !string.IsNullOrWhiteSpace(line));

        foreach (var line in lines)
        {
            var cells = line.Contains('\t')
                ? line.Split('\t')
                : line.Split('|');
            cells = cells.Select(cell => cell.Trim()).Where(cell => cell.Length > 0).ToArray();

            if (cells.Length == 0)
            {
                continue;
            }

            var nameIndex = cells.Length >= 4 && LooksLikeNumber(cells[0]) ? 1 : 0;
            var startIndex = nameIndex + 1;
            var finishIndex = nameIndex + 2;
            var start = startIndex < cells.Length && TryParseDate(cells[startIndex], out var parsedStart)
                ? parsedStart
                : DateTime.Today;
            var finish = finishIndex < cells.Length && TryParseDate(cells[finishIndex], out var parsedFinish)
                ? parsedFinish
                : start;

            yield return new ScheduleTask
            {
                Name = cells[nameIndex],
                StartDate = start,
                FinishDate = finish < start ? start : finish
            };
        }
    }

    private static bool LooksLikeNumber(string value)
    {
        return value.All(c => char.IsDigit(c) || c == '.');
    }

    private static bool TryParseDate(string value, out DateTime date)
    {
        var formats = new[] { "d/M/yyyy", "dd/MM/yyyy", "d-M-yyyy", "dd-MM-yyyy", "yyyy-MM-dd" };
        return DateTime.TryParseExact(value, formats, CultureInfo.GetCultureInfo("vi-VN"), DateTimeStyles.None, out date)
               || DateTime.TryParse(value, CultureInfo.GetCultureInfo("vi-VN"), DateTimeStyles.None, out date);
    }

    private void ShiftSelected(int days)
    {
        var targets = EffectiveSelection;
        if (targets.Count == 0)
        {
            return;
        }

        PushUndoSnapshot();
        foreach (var task in targets)
        {
            task.StartDate = task.StartDate?.AddDays(days);
            task.FinishDate = task.FinishDate?.AddDays(days);
        }
    }

    private void SequenceFromSelected()
    {
        var startIndex = SelectedTask is null ? 0 : Tasks.IndexOf(SelectedTask);
        if (startIndex < 0 || startIndex >= Tasks.Count)
        {
            return;
        }

        PushUndoSnapshot();
        var cursor = Tasks[startIndex].StartDate ?? DateTime.Today;
        for (var i = startIndex; i < Tasks.Count; i++)
        {
            var task = Tasks[i];
            var duration = Math.Max(1, task.Duration);
            task.StartDate = cursor;
            task.FinishDate = cursor.AddDays(duration - 1);
            cursor = task.FinishDate.Value.AddDays(1);
        }
    }

    private void DeleteSelected()
    {
        var targets = EffectiveSelection;
        if (targets.Count == 0)
        {
            return;
        }

        PushUndoSnapshot();
        var index = SelectedTask is null ? -1 : Tasks.IndexOf(SelectedTask);
        foreach (var task in targets)
        {
            Tasks.Remove(task);
        }

        Renumber();
        SelectedTask = Tasks.Count == 0 ? null : Tasks[Math.Clamp(index, 0, Tasks.Count - 1)];
    }

    private void MoveSelected(int offset)
    {
        if (SelectedTask is null)
        {
            return;
        }

        var oldIndex = Tasks.IndexOf(SelectedTask);
        var newIndex = oldIndex + offset;
        if (oldIndex < 0 || newIndex < 0 || newIndex >= Tasks.Count)
        {
            return;
        }

        PushUndoSnapshot();
        Tasks.Move(oldIndex, newIndex);
        Renumber();
    }

    /// <summary>Dùng cho kéo-thả sắp xếp lại hàng trong grid (xem MainWindow.xaml.cs).
    /// newLevel khác null nghĩa là thả vào/ra khỏi 1 nhóm — đổi luôn Level của
    /// dòng kéo theo dòng đích (thả lên group → thành con của group đó; thả
    /// ngang 1 dòng top-level → tự "thoát" khỏi nhóm cũ).</summary>
    public void MoveTask(ScheduleTask task, int newIndex, int? newLevel = null)
    {
        var oldIndex = Tasks.IndexOf(task);
        if (oldIndex < 0)
        {
            return;
        }

        newIndex = Math.Clamp(newIndex, 0, Tasks.Count - 1);
        if (oldIndex == newIndex && (newLevel is null || newLevel == task.Level))
        {
            return;
        }

        PushUndoSnapshot();
        if (newLevel.HasValue)
        {
            task.Level = Math.Clamp(newLevel.Value, 0, 5);
        }

        if (oldIndex != newIndex)
        {
            Tasks.Move(oldIndex, newIndex);
        }

        Renumber();
        SelectedTask = task;
    }

    /// <summary>Kéo tay cầm "liên kết" từ bar predecessor sang bar dependent trên
    /// timeline (xem ScheduleTimelineControl.LinkRequested) — thêm Number của
    /// predecessor vào Predecessors của dependent nếu chưa có.</summary>
    public void AddPredecessorLink(ScheduleTask predecessor, ScheduleTask dependent)
    {
        if (ReferenceEquals(predecessor, dependent))
        {
            return;
        }

        var existing = dependent.Predecessors
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();
        if (existing.Contains(predecessor.Number))
        {
            return;
        }

        PushUndoSnapshot();
        existing.Add(predecessor.Number);
        dependent.Predecessors = string.Join(",", existing);
    }

    private void ChangeIndent(int offset)
    {
        var targets = EffectiveSelection;
        if (targets.Count == 0)
        {
            return;
        }

        PushUndoSnapshot();
        foreach (var task in targets)
        {
            task.Level = Math.Clamp(task.Level + offset, 0, 5);
        }

        Renumber();
    }

    private void ToggleCollapse(ScheduleTask? task)
    {
        if (task is null || task.TaskType != ScheduleTaskType.Group)
        {
            return;
        }

        task.IsCollapsed = !task.IsCollapsed;
        TasksView.Refresh();
    }

    /// <summary>Ẩn 1 dòng nếu có group cha (Level nhỏ hơn, đứng trước nó trong danh
    /// sách phẳng) đang IsCollapsed — phân cấp trong app này suy ra từ Level +
    /// thứ tự, không phải qua ParentId.</summary>
    private bool FilterCollapsedTasks(object obj)
    {
        if (obj is not ScheduleTask task)
        {
            return true;
        }

        var index = Tasks.IndexOf(task);
        if (index < 0)
        {
            return true;
        }

        for (var i = index - 1; i >= 0; i--)
        {
            var candidate = Tasks[i];
            if (candidate.Level >= task.Level)
            {
                continue;
            }

            if (candidate.IsCollapsed)
            {
                return false;
            }

            if (candidate.Level == 0)
            {
                break;
            }
        }

        return true;
    }

    private void Renumber()
    {
        var topLevel = 0;
        var childCounters = new int[8];

        for (var i = 0; i < Tasks.Count; i++)
        {
            var task = Tasks[i];
            task.SortOrder = i + 1;

            if (task.Level <= 0)
            {
                topLevel++;
                Array.Clear(childCounters);
                task.Number = topLevel.ToString();
                continue;
            }

            childCounters[task.Level]++;
            for (var level = task.Level + 1; level < childCounters.Length; level++)
            {
                childCounters[level] = 0;
            }

            var parts = new List<int> { Math.Max(1, topLevel) };
            for (var level = 1; level <= task.Level; level++)
            {
                parts.Add(Math.Max(1, childCounters[level]));
            }

            task.Number = string.Join(".", parts);
        }

        ApplyDependencyCascade();
        RollupGroupDates();
        TasksView.Refresh();
    }

    /// <summary>"Việc trước" (ScheduleTask.Predecessors) — đơn giản hoá của
    /// dependency: chỉ đẩy XUÔI (không kéo lùi, không phải CPM đầy đủ). Nếu
    /// công việc bắt đầu trước khi (các) việc trước xong, tự dời StartDate về
    /// ngay sau ngày kết thúc muộn nhất, giữ nguyên Duration. Chạy nhiều lượt
    /// (tối đa 10) để dây chuyền A→B→C tự lan truyền hết trong 1 lần gọi.</summary>
    private void ApplyDependencyCascade()
    {
        if (_applyingCascade)
        {
            return;
        }

        _applyingCascade = true;
        try
        {
            for (var pass = 0; pass < 10; pass++)
            {
                var changed = false;
                foreach (var task in Tasks)
                {
                    if (string.IsNullOrWhiteSpace(task.Predecessors) || !task.StartDate.HasValue)
                    {
                        continue;
                    }

                    DateTime? maxFinish = null;
                    foreach (var number in task.Predecessors.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                    {
                        var predecessor = Tasks.FirstOrDefault(t => t.Number == number);
                        if (predecessor?.FinishDate is { } finish && (maxFinish is null || finish > maxFinish))
                        {
                            maxFinish = finish;
                        }
                    }

                    if (maxFinish is null)
                    {
                        continue;
                    }

                    var earliestStart = maxFinish.Value.AddDays(1);
                    if (task.StartDate.Value < earliestStart)
                    {
                        var duration = Math.Max(1, task.Duration);
                        task.StartDate = earliestStart;
                        task.FinishDate = earliestStart.AddDays(duration - 1);
                        changed = true;
                    }
                }

                if (!changed)
                {
                    break;
                }
            }
        }
        finally
        {
            _applyingCascade = false;
        }
    }

    /// <summary>Group luôn = min(StartDate)..max(FinishDate) của TOÀN BỘ con
    /// cháu (mọi cấp, không chỉ con trực tiếp) — quét theo Level>group.Level nên
    /// không phụ thuộc thứ tự xử lý nhóm lồng nhau: nhóm con cháu sâu nhất luôn
    /// nằm trong phạm vi quét của nhóm cha, dù nhóm con đã tính lại hay chưa.
    /// Vì vậy Group không cho kéo/resize/liên kết tay trên timeline (xem
    /// ScheduleTimelineControl.HitTestBar) — mọi sửa tay sẽ bị ghi đè ngay.</summary>
    private void RollupGroupDates()
    {
        if (_applyingRollup)
        {
            return;
        }

        _applyingRollup = true;
        try
        {
            for (var i = 0; i < Tasks.Count; i++)
            {
                var group = Tasks[i];
                if (group.TaskType != ScheduleTaskType.Group)
                {
                    continue;
                }

                DateTime? minStart = null;
                DateTime? maxFinish = null;
                for (var j = i + 1; j < Tasks.Count && Tasks[j].Level > group.Level; j++)
                {
                    var child = Tasks[j];
                    if (child.StartDate is { } s && (minStart is null || s < minStart))
                    {
                        minStart = s;
                    }

                    if (child.FinishDate is { } f && (maxFinish is null || f > maxFinish))
                    {
                        maxFinish = f;
                    }
                }

                if (minStart.HasValue && maxFinish.HasValue)
                {
                    group.StartDate = minStart;
                    group.FinishDate = maxFinish;
                }
            }
        }
        finally
        {
            _applyingRollup = false;
        }
    }

    private void RaiseCommandStates()
    {
        foreach (var command in new[]
                 {
                     DeleteCommand, DuplicateCommand, MoveUpCommand, MoveDownCommand,
                     IndentCommand, OutdentCommand, ShiftBackCommand, ShiftForwardCommand
                 }.OfType<RelayCommand>())
        {
            command.RaiseCanExecuteChanged();
        }
    }

    // ── Undo / Redo ──────────────────────────────────────────────────────────

    /// <summary>Gọi TRƯỚC mỗi thao tác sửa dữ liệu (từ command handler, hoặc từ
    /// MainWindow.xaml.cs khi user bắt đầu sửa 1 ô trong grid) để lưu lại trạng
    /// thái ngay trước đó vào undo stack. Không dùng command pattern chi tiết cho
    /// từng loại sửa — snapshot toàn bộ Tasks (JSON) đơn giản và đúng cho quy mô
    /// 1 bảng tiến độ (vài trăm dòng), đổi lại 1 chút bộ nhớ cho việc không phải
    /// tự bảo trì "undo action" riêng cho từng thao tác mới thêm sau này.</summary>
    public void PushUndoSnapshot()
    {
        if (_suppressUndoCapture)
        {
            return;
        }

        _undoStack.Add(SerializeTasks());
        if (_undoStack.Count > MaxUndoDepth)
        {
            _undoStack.RemoveAt(0);
        }

        _redoStack.Clear();
        RaiseUndoRedoStates();
    }

    private void Undo()
    {
        if (_undoStack.Count == 0)
        {
            return;
        }

        _redoStack.Add(SerializeTasks());
        var snapshot = _undoStack[^1];
        _undoStack.RemoveAt(_undoStack.Count - 1);
        RestoreSnapshot(snapshot);
        RaiseUndoRedoStates();
    }

    private void Redo()
    {
        if (_redoStack.Count == 0)
        {
            return;
        }

        _undoStack.Add(SerializeTasks());
        var snapshot = _redoStack[^1];
        _redoStack.RemoveAt(_redoStack.Count - 1);
        RestoreSnapshot(snapshot);
        RaiseUndoRedoStates();
    }

    private void ResetUndoHistory()
    {
        _undoStack.Clear();
        _redoStack.Clear();
        RaiseUndoRedoStates();

        // Tài liệu mới/khác — timeline nên tự fit lại theo nội dung của NÓ, kể
        // cả khi tài liệu trước đó user đã tự zoom tay.
        IsAutoFitZoom = true;
    }

    private string SerializeTasks() => JsonSerializer.Serialize(Tasks.ToList(), JsonOptions());

    private void RestoreSnapshot(string json)
    {
        var restored = JsonSerializer.Deserialize<List<ScheduleTask>>(json, JsonOptions()) ?? [];

        _suppressDirty = true;
        _suppressUndoCapture = true;
        try
        {
            Tasks.Clear();
            foreach (var task in restored)
            {
                Tasks.Add(task);
            }
        }
        finally
        {
            _suppressDirty = false;
            _suppressUndoCapture = false;
        }

        SelectedTask = Tasks.FirstOrDefault();
        TasksView.Refresh();
        IsDirty = true;
    }

    private void RaiseUndoRedoStates()
    {
        (UndoCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (RedoCommand as RelayCommand)?.RaiseCanExecuteChanged();
    }

    // ── Dirty tracking ───────────────────────────────────────────────────────

    private void AttachDirtyTracking(ObservableCollection<ScheduleTask> tasks)
    {
        tasks.CollectionChanged += TasksOnCollectionChanged;
        foreach (var task in tasks)
        {
            task.PropertyChanged += TaskOnPropertyChanged;
        }
    }

    private void DetachDirtyTracking(ObservableCollection<ScheduleTask> tasks)
    {
        tasks.CollectionChanged -= TasksOnCollectionChanged;
        foreach (var task in tasks)
        {
            task.PropertyChanged -= TaskOnPropertyChanged;
        }
    }

    private void TasksOnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems != null)
        {
            foreach (ScheduleTask task in e.OldItems)
            {
                task.PropertyChanged -= TaskOnPropertyChanged;
            }
        }

        if (e.NewItems != null)
        {
            foreach (ScheduleTask task in e.NewItems)
            {
                task.PropertyChanged += TaskOnPropertyChanged;
            }
        }

        MarkDirty();
    }

    private void TaskOnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        MarkDirty();

        // Sửa ngày trực tiếp trên grid/kéo bar không đi qua Renumber() — cần
        // tự chạy cascade + rollup ở đây để việc trước/sau và Group luôn ăn
        // khớp ngay lập tức.
        if (e.PropertyName is nameof(ScheduleTask.StartDate) or nameof(ScheduleTask.FinishDate) or nameof(ScheduleTask.Predecessors))
        {
            ApplyDependencyCascade();
            RollupGroupDates();
        }
    }

    private void MarkDirty()
    {
        if (_suppressDirty)
        {
            return;
        }

        IsDirty = true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
