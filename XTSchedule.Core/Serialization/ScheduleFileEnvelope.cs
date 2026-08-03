using XTSchedule.Core.Models;

namespace XTSchedule.Core.Serialization;

public sealed class ScheduleFileEnvelope
{
    public string FileFormat { get; set; } = "XTSchedule";

    public string Version { get; set; } = "1.0";

    public ScheduleDocument Document { get; set; } = new();
}
