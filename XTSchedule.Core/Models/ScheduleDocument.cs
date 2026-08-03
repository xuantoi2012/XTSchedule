using System.Collections.ObjectModel;

namespace XTSchedule.Core.Models;

public sealed class ScheduleDocument
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string ProjectName { get; set; } = string.Empty;

    public string ProjectCode { get; set; } = string.Empty;

    public string InvestorName { get; set; } = string.Empty;

    public string ConsultantName { get; set; } = string.Empty;

    public DateTime CreatedDate { get; set; } = DateTime.Today;

    public ObservableCollection<ScheduleTask> Tasks { get; set; } = [];

    public ScheduleDisplaySettings DisplaySettings { get; set; } = new();

    public SchedulePrintSettings PrintSettings { get; set; } = new();

    public DocumentHeaderSettings HeaderSettings { get; set; } = new();
}
