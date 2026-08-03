using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Windows.Input;
using Microsoft.Win32;
using XTSchedule.Core.Enums;
using XTSchedule.Core.Interfaces;
using XTSchedule.Core.Models;
using XTSchedule.Core.Serialization;

namespace XTSchedule.UI.ViewModels;

public sealed class MainWindowViewModel : INotifyPropertyChanged
{
    private readonly IScheduleDocumentService _documentService;
    private ScheduleDocument _document;
    private string? _filePath;
    private ScheduleTask? _selectedTask;

    public MainWindowViewModel(IScheduleDocumentService documentService)
    {
        _documentService = documentService;
        _document = documentService.CreateNew();
        Tasks = _document.Tasks;

        NewCommand = new RelayCommand(_ => NewDocument());
        OpenCommand = new RelayCommand(_ => OpenDocument());
        SaveCommand = new RelayCommand(_ => SaveDocument());
        SaveAsCommand = new RelayCommand(_ => SaveDocumentAs());
        AddGroupCommand = new RelayCommand(_ => AddTask(ScheduleTaskType.Group));
        AddTaskCommand = new RelayCommand(_ => AddTask(ScheduleTaskType.Task));
        DeleteCommand = new RelayCommand(_ => DeleteSelected(), _ => SelectedTask is not null);
        MoveUpCommand = new RelayCommand(_ => MoveSelected(-1), _ => SelectedTask is not null);
        MoveDownCommand = new RelayCommand(_ => MoveSelected(1), _ => SelectedTask is not null);
        IndentCommand = new RelayCommand(_ => ChangeIndent(1), _ => SelectedTask is not null);
        OutdentCommand = new RelayCommand(_ => ChangeIndent(-1), _ => SelectedTask is not null);
        PasteRowsCommand = new RelayCommand(_ => PasteRows());
        ShiftBackCommand = new RelayCommand(_ => ShiftSelected(-1), _ => SelectedTask is not null);
        ShiftForwardCommand = new RelayCommand(_ => ShiftSelected(1), _ => SelectedTask is not null);
        SequenceCommand = new RelayCommand(_ => SequenceFromSelected(), _ => Tasks.Count > 1);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ScheduleDocument Document
    {
        get => _document;
        private set
        {
            _document = value;
            Tasks = value.Tasks;
            SelectedTask = Tasks.FirstOrDefault();
            OnPropertyChanged();
            OnPropertyChanged(nameof(WindowTitle));
        }
    }

    public string WindowTitle => string.IsNullOrWhiteSpace(_filePath)
        ? $"XTSchedule - {Document.ProjectName}"
        : $"XTSchedule - {Document.ProjectName} ({Path.GetFileName(_filePath)})";

    public ObservableCollection<ScheduleTask> Tasks { get; private set; }

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

    public ICommand NewCommand { get; }

    public ICommand OpenCommand { get; }

    public ICommand SaveCommand { get; }

    public ICommand SaveAsCommand { get; }

    public ICommand AddGroupCommand { get; }

    public ICommand AddTaskCommand { get; }

    public ICommand DeleteCommand { get; }

    public ICommand MoveUpCommand { get; }

    public ICommand MoveDownCommand { get; }

    public ICommand IndentCommand { get; }

    public ICommand OutdentCommand { get; }

    public ICommand PasteRowsCommand { get; }

    public ICommand ShiftBackCommand { get; }

    public ICommand ShiftForwardCommand { get; }

    public ICommand SequenceCommand { get; }

    private void NewDocument()
    {
        Document = _documentService.CreateNew();
        _filePath = null;
        OnPropertyChanged(nameof(WindowTitle));
    }

    private void OpenDocument()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Mở tiến độ",
            Filter = "XTSchedule (*.xtschedule)|*.xtschedule|JSON (*.json)|*.json|All files (*.*)|*.*"
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        var json = File.ReadAllText(dialog.FileName);
        var envelope = JsonSerializer.Deserialize<ScheduleFileEnvelope>(json, JsonOptions());
        if (envelope?.Document is null || !string.Equals(envelope.FileFormat, "XTSchedule", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        Document = envelope.Document;
        _filePath = dialog.FileName;
        OnPropertyChanged(nameof(WindowTitle));
    }

    private void SaveDocument()
    {
        if (string.IsNullOrWhiteSpace(_filePath))
        {
            SaveDocumentAs();
            return;
        }

        WriteDocument(_filePath);
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
        OnPropertyChanged(nameof(WindowTitle));
    }

    private void WriteDocument(string path)
    {
        var envelope = new ScheduleFileEnvelope { Document = Document };
        var json = JsonSerializer.Serialize(envelope, JsonOptions());
        File.WriteAllText(path, json);
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

    private void AddTask(ScheduleTaskType taskType)
    {
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

    private void PasteRows()
    {
        if (!System.Windows.Clipboard.ContainsText())
        {
            return;
        }

        var insertIndex = SelectedTask is null ? Tasks.Count : Tasks.IndexOf(SelectedTask) + 1;
        var rows = ParseClipboardRows(System.Windows.Clipboard.GetText()).ToList();
        foreach (var row in rows)
        {
            Tasks.Insert(insertIndex++, row);
        }

        if (rows.Count > 0)
        {
            Renumber();
            SelectedTask = rows[0];
        }
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
        if (SelectedTask is null)
        {
            return;
        }

        SelectedTask.StartDate = SelectedTask.StartDate?.AddDays(days);
        SelectedTask.FinishDate = SelectedTask.FinishDate?.AddDays(days);
    }

    private void SequenceFromSelected()
    {
        var startIndex = SelectedTask is null ? 0 : Tasks.IndexOf(SelectedTask);
        if (startIndex < 0 || startIndex >= Tasks.Count)
        {
            return;
        }

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
        if (SelectedTask is null)
        {
            return;
        }

        var index = Tasks.IndexOf(SelectedTask);
        Tasks.Remove(SelectedTask);
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

        Tasks.Move(oldIndex, newIndex);
        Renumber();
    }

    private void ChangeIndent(int offset)
    {
        if (SelectedTask is null)
        {
            return;
        }

        SelectedTask.Level = Math.Clamp(SelectedTask.Level + offset, 0, 5);
        Renumber();
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
    }

    private void RaiseCommandStates()
    {
        foreach (var command in new[] { DeleteCommand, MoveUpCommand, MoveDownCommand, IndentCommand, OutdentCommand, ShiftBackCommand, ShiftForwardCommand }.OfType<RelayCommand>())
        {
            command.RaiseCanExecuteChanged();
        }
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
