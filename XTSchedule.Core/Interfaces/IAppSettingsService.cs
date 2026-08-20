using XTSchedule.Core.Models;

namespace XTSchedule.Core.Interfaces;

public interface IAppSettingsService
{
    AppSettings Load();

    void Save(AppSettings settings);
}
