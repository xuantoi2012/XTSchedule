namespace XTSchedule.Core.Models;

public sealed class ScheduleDisplaySettings
{
    public TimelineScale TimelineScale { get; set; } = TimelineScale.Week;

    public bool ShowWeekend { get; set; } = true;
}
