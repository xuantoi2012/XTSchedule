# XTSchedule Development Notes

## Current Scope

XTSchedule is a standalone WPF desktop app scaffolded from the architecture roadmap. It uses .NET 10, MVVM-style separation, dependency injection, JSON project files, and XTCADStyle for the UI foundation.

## Implemented

- Created solution structure:
  - `XTSchedule.App`
  - `XTSchedule.Core`
  - `XTSchedule.UI`
  - `XTSchedule.Export`
- Integrated `XTCADStyle` from the sibling repository.
- Added app bootstrap with `Microsoft.Extensions.DependencyInjection`.
- Added global exception handling.
- Added XTCADStyle light theme resources.
- Added main shell using:
  - `XTCadWindow`
  - `XTButton`
  - `XTDataGrid`
  - `XTDatePicker`
- Added core schedule models:
  - `ScheduleDocument`
  - `ScheduleTask`
  - `ScheduleDisplaySettings`
  - `SchedulePrintSettings`
  - `DocumentHeaderSettings`
  - `ScheduleFileEnvelope`
- Added `.xtschedule` JSON save/open envelope.
- Added sample Vietnamese schedule data.
- Added editable task grid:
  - task number
  - task name
  - start date
  - finish date
  - duration
- Added `INotifyPropertyChanged` support for `ScheduleTask`.
- Editing start/finish date updates duration.
- Editing duration updates finish date.
- Added basic Gantt timeline renderer with `DrawingContext`.
- Timeline redraws when task dates/duration change.
- Added toolbar commands:
  - New
  - Open
  - Save
  - Add group
  - Add task
  - Delete
  - Move up
  - Move down
  - Indent
  - Outdent
  - Paste rows
  - Shift -1 day
  - Shift +1 day
  - Sequence tasks
- Added clipboard paste for tab-delimited or pipe-delimited rows.

## Verified

- `dotnet build XTSchedule.slnx -p:DeployToBundle=false`
- XAML resource audit against XTCADStyle.
- Last verification result:
  - build succeeded
  - 0 warnings
  - 0 errors
  - all checked `StaticResource` keys resolved

## Not Yet Implemented

- Save As as a separate toolbar button.
- Recent files.
- Dirty state and unsaved-change prompts.
- Auto-save and recovery.
- Undo/Redo.
- Duplicate row.
- True multi-select batch editing.
- Group collapse/expand.
- Full keyboard shortcuts.
- Drag/drop row reorder.
- Timeline zoom modes for day/week/month.
- Month/week/day timeline headers.
- Full grid/timeline scroll synchronization.
- Timeline virtualization for large schedules.
- Page setup.
- Print layout model.
- Print preview.
- PDF export.
- Word export.
- Template library and template manager.
- Automated tests.

## Test Command

```powershell
cd C:\Users\condu\source\repos\xuantoi2012\XTSchedule
dotnet run --project .\XTSchedule.App\XTSchedule.App.csproj
```
