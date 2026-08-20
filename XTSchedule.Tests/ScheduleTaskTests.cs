using XTSchedule.Core.Models;
using Xunit;

namespace XTSchedule.Tests;

public class ScheduleTaskTests
{
    [Fact]
    public void Duration_ComputedFromStartAndFinish()
    {
        var task = new ScheduleTask
        {
            StartDate = new DateTime(2026, 1, 1),
            FinishDate = new DateTime(2026, 1, 5)
        };

        Assert.Equal(5, task.Duration); // inclusive cả 2 đầu — 1..5 = 5 ngày
    }

    [Fact]
    public void SettingDuration_MovesFinishDate_KeepsStartDate()
    {
        var task = new ScheduleTask { StartDate = new DateTime(2026, 1, 1) };

        task.Duration = 3;

        Assert.Equal(new DateTime(2026, 1, 1), task.StartDate);
        Assert.Equal(new DateTime(2026, 1, 3), task.FinishDate);
    }

    [Fact]
    public void SettingStartDate_AfterFinishDate_PullsFinishDateAlong()
    {
        var task = new ScheduleTask
        {
            StartDate = new DateTime(2026, 1, 1),
            FinishDate = new DateTime(2026, 1, 5)
        };

        task.StartDate = new DateTime(2026, 1, 10);

        Assert.Equal(new DateTime(2026, 1, 10), task.FinishDate); // không cho Start > Finish
    }

    [Fact]
    public void SettingFinishDate_BeforeStartDate_PullsStartDateAlong()
    {
        var task = new ScheduleTask
        {
            StartDate = new DateTime(2026, 1, 5),
            FinishDate = new DateTime(2026, 1, 10)
        };

        task.FinishDate = new DateTime(2026, 1, 1);

        Assert.Equal(new DateTime(2026, 1, 1), task.StartDate);
    }

    [Fact]
    public void Duration_WithoutDates_IsZero()
    {
        var task = new ScheduleTask();
        Assert.Equal(0, task.Duration);
    }

    [Fact]
    public void PropertyChanged_FiresOnNameChange()
    {
        var task = new ScheduleTask();
        var raised = false;
        task.PropertyChanged += (_, e) => raised |= e.PropertyName == nameof(ScheduleTask.Name);

        task.Name = "Việc mới";

        Assert.True(raised);
    }
}
