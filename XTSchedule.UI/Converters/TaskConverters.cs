using System.Globalization;
using System.Windows;
using System.Windows.Data;
using XTSchedule.Core.Enums;

namespace XTSchedule.UI.Converters;

/// <summary>Chỉ hiện nút thu gọn/mở rộng trên dòng Group — dòng Task thường
/// không có gì để thu gọn.</summary>
public sealed class TaskTypeToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is ScheduleTaskType.Group ? Visibility.Visible : Visibility.Hidden;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Xoay icon Mat.ChevronDown -90° khi thu gọn (trỏ phải) — dùng 1 icon
/// vector duy nhất thay vì ký tự Unicode ▸/▾ (font-dependent, hay bị lệch dòng
/// và trông "béo" so với các icon Material khác trong app).</summary>
public sealed class CollapseAngleConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is true ? -90.0 : 0.0;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
