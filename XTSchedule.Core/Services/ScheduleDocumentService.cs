using XTSchedule.Core.Enums;
using XTSchedule.Core.Interfaces;
using XTSchedule.Core.Models;

namespace XTSchedule.Core.Services;

public sealed class ScheduleDocumentService : IScheduleDocumentService
{
    public ScheduleDocument CreateNew()
    {
        var today = DateTime.Today;
        var document = new ScheduleDocument
        {
            ProjectName = "Dự án mẫu",
            ProjectCode = "XT-SCH-001",
            InvestorName = "Chủ đầu tư",
            ConsultantName = "Đơn vị tư vấn"
        };

        document.Tasks.Add(new ScheduleTask
        {
            SortOrder = 1,
            Level = 0,
            TaskType = ScheduleTaskType.Group,
            Number = "1",
            Name = "Chuẩn bị hồ sơ",
            StartDate = today,
            FinishDate = today.AddDays(6)
        });

        document.Tasks.Add(new ScheduleTask
        {
            SortOrder = 2,
            Level = 1,
            Number = "1.1",
            Name = "Thu thập tài liệu đầu vào",
            StartDate = today,
            FinishDate = today.AddDays(2)
        });

        document.Tasks.Add(new ScheduleTask
        {
            SortOrder = 3,
            Level = 1,
            Number = "1.2",
            Name = "Lập đề cương và tiến độ",
            StartDate = today.AddDays(3),
            FinishDate = today.AddDays(6)
        });

        return document;
    }
}
