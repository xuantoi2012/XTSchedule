using XTSchedule.Core.Enums;
using XTSchedule.Core.Models;
using XTSchedule.Core.Services;
using XTSchedule.UI.ViewModels;
using Xunit;

namespace XTSchedule.Tests;

/// <summary>Chỉ test các thao tác KHÔNG mở dialog (OpenFileDialog/SaveFileDialog/
/// ModernMessageBox) — New/Open/Save thật sẽ treo test runner vì chờ user click.
/// Sửa/Undo/Redo/Zoom/Collapse/multi-select đều không đụng dialog nên test được
/// trực tiếp qua ICommand.Execute như code thật gọi.</summary>
public class MainWindowViewModelTests
{
    private static MainWindowViewModel CreateViewModel() =>
        new(new ScheduleDocumentService(), new FakeAppSettingsService());

    [Fact]
    public void FreshDocument_IsNotDirty()
    {
        var vm = CreateViewModel();
        Assert.False(vm.IsDirty);
        Assert.Equal(3, vm.Tasks.Count); // seed data: 1 group + 2 con
    }

    [Fact]
    public void AddTask_IncreasesCount_AndMarksDirty()
    {
        var vm = CreateViewModel();
        var before = vm.Tasks.Count;

        vm.AddTaskCommand.Execute(null);

        Assert.Equal(before + 1, vm.Tasks.Count);
        Assert.True(vm.IsDirty);
    }

    [Fact]
    public void DeleteSelected_RemovesTask()
    {
        var vm = CreateViewModel();
        vm.SelectedTask = vm.Tasks[1];
        var before = vm.Tasks.Count;

        vm.DeleteCommand.Execute(null);

        Assert.Equal(before - 1, vm.Tasks.Count);
    }

    [Fact]
    public void DeleteSelected_WithMultiSelection_RemovesAllSelected()
    {
        var vm = CreateViewModel();
        var toDelete = new[] { vm.Tasks[1], vm.Tasks[2] };
        vm.SetMultiSelection(toDelete);
        vm.SelectedTask = toDelete[0];

        vm.DeleteCommand.Execute(null);

        Assert.Single(vm.Tasks);
    }

    [Fact]
    public void Duplicate_ClonesSelectedTask_RightAfterIt()
    {
        var vm = CreateViewModel();
        var source = vm.Tasks[1];
        vm.SelectedTask = source;

        vm.DuplicateCommand.Execute(null);

        var copy = vm.Tasks[2];
        Assert.Equal(source.Name + " (sao chép)", copy.Name);
        Assert.Equal(source.StartDate, copy.StartDate);
        Assert.NotEqual(source.Id, copy.Id);
    }

    [Fact]
    public void Indent_IncreasesLevel_AndOutdent_Reverts()
    {
        var vm = CreateViewModel();
        var task = vm.Tasks[1];
        vm.SelectedTask = task;
        var originalLevel = task.Level;

        vm.IndentCommand.Execute(null);
        Assert.Equal(originalLevel + 1, task.Level);

        vm.OutdentCommand.Execute(null);
        Assert.Equal(originalLevel, task.Level);
    }

    [Fact]
    public void Undo_RevertsAddTask_Redo_ReappliesIt()
    {
        var vm = CreateViewModel();
        var before = vm.Tasks.Count;

        vm.AddTaskCommand.Execute(null);
        Assert.Equal(before + 1, vm.Tasks.Count);

        vm.UndoCommand.Execute(null);
        Assert.Equal(before, vm.Tasks.Count);

        vm.RedoCommand.Execute(null);
        Assert.Equal(before + 1, vm.Tasks.Count);
    }

    [Fact]
    public void Undo_WhenNothingToUndo_DoesNothing()
    {
        var vm = CreateViewModel();
        var before = vm.Tasks.Count;

        vm.UndoCommand.Execute(null);

        Assert.Equal(before, vm.Tasks.Count);
    }

    [Fact]
    public void SetZoomCommand_ChangesPixelsPerDay()
    {
        var vm = CreateViewModel();
        Assert.Equal(32.0, vm.PixelsPerDay);

        vm.SetZoomCommand.Execute(3.0);

        Assert.Equal(3.0, vm.PixelsPerDay);
    }

    [Fact]
    public void AdjustZoom_MultipliesCurrentValue_ClampedToRange()
    {
        var vm = CreateViewModel();

        vm.AdjustZoom(1.5);
        Assert.Equal(48.0, vm.PixelsPerDay);

        vm.AdjustZoom(1000); // phải bị clamp, không vượt MaxPixelsPerDay
        Assert.Equal(MainWindowViewModel.MaxPixelsPerDay, vm.PixelsPerDay);

        vm.PixelsPerDay = MainWindowViewModel.MinPixelsPerDay;
        vm.AdjustZoom(0.01); // phải bị clamp, không dưới MinPixelsPerDay
        Assert.Equal(MainWindowViewModel.MinPixelsPerDay, vm.PixelsPerDay);
    }

    [Fact]
    public void ToggleCollapse_OnGroup_HidesChildrenInTasksView()
    {
        var vm = CreateViewModel();
        var group = vm.Tasks.First(t => t.TaskType == ScheduleTaskType.Group);
        var fullCount = vm.TasksView.Cast<ScheduleTask>().Count();

        vm.ToggleCollapseCommand.Execute(group);

        var collapsedCount = vm.TasksView.Cast<ScheduleTask>().Count();
        Assert.True(collapsedCount < fullCount);
        Assert.Contains(group, vm.TasksView.Cast<ScheduleTask>()); // group tự nó vẫn hiện
    }

    [Fact]
    public void CanClose_WhenNotDirty_ReturnsTrueWithoutPrompting()
    {
        var vm = CreateViewModel();
        Assert.True(vm.CanClose());
    }

    [Fact]
    public void Predecessor_PushesStartDate_WhenPredecessorFinishesLater()
    {
        var vm = CreateViewModel();
        var predecessor = vm.Tasks[1]; // "1.1"
        var dependent = vm.Tasks[2];   // "1.2"

        dependent.Predecessors = predecessor.Number;
        var originalDuration = dependent.Duration;

        predecessor.FinishDate = predecessor.FinishDate!.Value.AddDays(10);

        Assert.Equal(predecessor.FinishDate!.Value.AddDays(1), dependent.StartDate);
        Assert.Equal(originalDuration, dependent.Duration); // dời ngày, giữ nguyên số ngày
    }

    [Fact]
    public void Predecessor_CascadesAcrossChain()
    {
        // Dùng 3 task thường (không phải Group) — Group giờ tự tính ngày theo
        // con (RollupGroupDates), set tay lên Group sẽ bị ghi đè ngay.
        var vm = CreateViewModel();
        var a = vm.Tasks[1]; // "1.1"
        var b = vm.Tasks[2]; // "1.2"
        vm.SelectedTask = b;
        vm.AddTaskCommand.Execute(null);
        var c = vm.Tasks[3];

        b.Predecessors = a.Number;
        c.Predecessors = b.Number;

        a.FinishDate = a.FinishDate!.Value.AddDays(20);

        Assert.Equal(a.FinishDate!.Value.AddDays(1), b.StartDate);
        Assert.Equal(b.FinishDate!.Value.AddDays(1), c.StartDate);
    }

    [Fact]
    public void GroupDates_RollUpFromChildren_WhenChildDatesChange()
    {
        var vm = CreateViewModel();
        var group = vm.Tasks[0]; // "1" — group cha
        var child1 = vm.Tasks[1]; // "1.1"
        var child2 = vm.Tasks[2]; // "1.2"

        child2.FinishDate = child2.FinishDate!.Value.AddDays(15);

        Assert.Equal(child1.StartDate, group.StartDate); // vẫn là con sớm nhất
        Assert.Equal(child2.FinishDate, group.FinishDate); // con2 giờ muộn nhất
    }

    [Fact]
    public void GroupDates_CannotBeSetManually_RollupOverridesImmediately()
    {
        var vm = CreateViewModel();
        var group = vm.Tasks[0];
        var originalFinish = group.FinishDate;

        group.FinishDate = group.FinishDate!.Value.AddDays(100); // sửa tay

        Assert.Equal(originalFinish, group.FinishDate); // rollup ghi đè lại ngay
    }

    [Fact]
    public void Predecessor_UnknownNumber_IsIgnoredSilently()
    {
        var vm = CreateViewModel();
        var task = vm.Tasks[2];
        var originalStart = task.StartDate;

        task.Predecessors = "9.9";

        Assert.Equal(originalStart, task.StartDate);
    }

    [Fact]
    public void AddPredecessorLink_SetsPredecessorsAndCascades()
    {
        var vm = CreateViewModel();
        var predecessor = vm.Tasks[1]; // "1.1"
        var dependent = vm.Tasks[2];   // "1.2"
        predecessor.FinishDate = predecessor.FinishDate!.Value.AddDays(10);

        vm.AddPredecessorLink(predecessor, dependent);

        Assert.Equal(predecessor.Number, dependent.Predecessors);
        Assert.Equal(predecessor.FinishDate!.Value.AddDays(1), dependent.StartDate);
    }

    [Fact]
    public void AddPredecessorLink_DoesNotDuplicate_WhenCalledTwice()
    {
        var vm = CreateViewModel();
        var predecessor = vm.Tasks[1];
        var dependent = vm.Tasks[2];

        vm.AddPredecessorLink(predecessor, dependent);
        vm.AddPredecessorLink(predecessor, dependent);

        Assert.Equal(predecessor.Number, dependent.Predecessors);
    }

    [Fact]
    public void IsAutoFitZoom_TurnsOff_AfterManualZoom_AndResetsOnNew()
    {
        var vm = CreateViewModel();
        Assert.True(vm.IsAutoFitZoom);

        vm.SetZoomCommand.Execute(10.0);
        Assert.False(vm.IsAutoFitZoom);

        // AutoFitZoom không còn hiệu lực nữa vì user đã tự zoom tay
        vm.AutoFitZoom(1000, 5);
        Assert.Equal(10.0, vm.PixelsPerDay);
    }

    [Fact]
    public void AutoFitZoom_AppliesViewportRatio_WhileStillAutoFit()
    {
        var vm = CreateViewModel();
        vm.AutoFitZoom(500, 10); // 500px / 10 ngày = 50px/ngày

        Assert.Equal(50.0, vm.PixelsPerDay);
    }

    [Fact]
    public void ShiftSelected_MovesStartAndFinishDates()
    {
        var vm = CreateViewModel();
        var task = vm.Tasks[1];
        vm.SelectedTask = task;
        var originalStart = task.StartDate;

        vm.ShiftForwardCommand.Execute(null);

        Assert.Equal(originalStart!.Value.AddDays(1), task.StartDate);
    }
}
