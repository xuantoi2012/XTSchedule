using System.ComponentModel;
using System.Runtime.CompilerServices;
using XTSchedule.Core.Enums;

namespace XTSchedule.Core.Models;

public sealed class ScheduleTask : INotifyPropertyChanged
{
    private Guid _id = Guid.NewGuid();
    private Guid? _parentId;
    private int _sortOrder;
    private int _level;
    private ScheduleTaskType _taskType = ScheduleTaskType.Task;
    private string _number = string.Empty;
    private string _name = string.Empty;
    private DateTime? _startDate;
    private DateTime? _finishDate;
    private double _progress;
    private string _predecessors = string.Empty;
    private string _note = string.Empty;
    private string _barStyleId = "Default";
    private bool _isCollapsed;
    private bool _isMilestone;
    private bool _isHidden;

    public event PropertyChangedEventHandler? PropertyChanged;

    public Guid Id
    {
        get => _id;
        set => SetField(ref _id, value);
    }

    public Guid? ParentId
    {
        get => _parentId;
        set => SetField(ref _parentId, value);
    }

    public int SortOrder
    {
        get => _sortOrder;
        set => SetField(ref _sortOrder, value);
    }

    public int Level
    {
        get => _level;
        set => SetField(ref _level, value);
    }

    public ScheduleTaskType TaskType
    {
        get => _taskType;
        set => SetField(ref _taskType, value);
    }

    public string Number
    {
        get => _number;
        set => SetField(ref _number, value);
    }

    public string Name
    {
        get => _name;
        set => SetField(ref _name, value);
    }

    public DateTime? StartDate
    {
        get => _startDate;
        set
        {
            if (!SetField(ref _startDate, value?.Date))
            {
                return;
            }

            if (_finishDate.HasValue && _startDate.HasValue && _finishDate.Value.Date < _startDate.Value.Date)
            {
                FinishDate = _startDate;
            }
            else
            {
                OnPropertyChanged(nameof(Duration));
            }
        }
    }

    public DateTime? FinishDate
    {
        get => _finishDate;
        set
        {
            if (!SetField(ref _finishDate, value?.Date))
            {
                return;
            }

            if (_startDate.HasValue && _finishDate.HasValue && _finishDate.Value.Date < _startDate.Value.Date)
            {
                StartDate = _finishDate;
            }
            else
            {
                OnPropertyChanged(nameof(Duration));
            }
        }
    }

    public int Duration
    {
        get =>
            StartDate.HasValue && FinishDate.HasValue
                ? Math.Max(1, (FinishDate.Value.Date - StartDate.Value.Date).Days + 1)
                : 0;
        set
        {
            if (value < 1)
            {
                value = 1;
            }

            StartDate ??= DateTime.Today;
            FinishDate = StartDate.Value.Date.AddDays(value - 1);
            OnPropertyChanged();
        }
    }

    public double Progress
    {
        get => _progress;
        set => SetField(ref _progress, value);
    }

    /// <summary>STT (Number) của (các) công việc phải xong trước — nhập tay,
    /// cách nhau bằng dấu phẩy (vd "1.1,1.2"), kiểu MS Project. Đối chiếu theo
    /// Number tại thời điểm tính (MainWindowViewModel.ApplyDependencyCascade),
    /// không lưu ID nội bộ — đơn giản/đủ dùng cho lịch nhanh, không phải CPM đầy
    /// đủ. Đánh đổi: nếu sắp xếp/xóa/thêm dòng làm đổi Number của công việc
    /// trước, liên kết cũ trỏ theo STT cũ sẽ không còn khớp, phải gõ lại. STT
    /// không tồn tại thì bị bỏ qua lặng lẽ.</summary>
    public string Predecessors
    {
        get => _predecessors;
        set => SetField(ref _predecessors, value);
    }

    public string Note
    {
        get => _note;
        set => SetField(ref _note, value);
    }

    public string BarStyleId
    {
        get => _barStyleId;
        set => SetField(ref _barStyleId, value);
    }

    public bool IsCollapsed
    {
        get => _isCollapsed;
        set => SetField(ref _isCollapsed, value);
    }

    public bool IsMilestone
    {
        get => _isMilestone;
        set => SetField(ref _isMilestone, value);
    }

    public bool IsHidden
    {
        get => _isHidden;
        set => SetField(ref _isHidden, value);
    }

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
