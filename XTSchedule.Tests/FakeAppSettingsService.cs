using XTSchedule.Core.Interfaces;
using XTSchedule.Core.Models;

namespace XTSchedule.Tests;

/// <summary>Giữ state trong bộ nhớ — test không được đụng vào %AppData% thật
/// của máy chạy CI/dev.</summary>
public sealed class FakeAppSettingsService : IAppSettingsService
{
    private AppSettings _settings = new();

    public AppSettings Load() => _settings;

    public void Save(AppSettings settings) => _settings = settings;
}
