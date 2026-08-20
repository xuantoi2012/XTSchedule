using System.Text.Json;
using XTSchedule.Core.Interfaces;
using XTSchedule.Core.Models;

namespace XTSchedule.Core.Services;

/// <summary>Đọc/ghi %AppData%\XTSchedule\settings.json — hiện chỉ chứa danh sách
/// file mở gần đây. Lỗi đọc/ghi (file hỏng, không có quyền...) không được ném ra
/// ngoài — mất recent-files list không đáng để chặn app khởi động/thoát.</summary>
public sealed class AppSettingsService : IAppSettingsService
{
    private static readonly string SettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "XTSchedule", "settings.json");

    public AppSettings Load()
    {
        try
        {
            if (!File.Exists(SettingsPath))
            {
                return new AppSettings();
            }

            var json = File.ReadAllText(SettingsPath);
            return JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
        }
        catch
        {
            return new AppSettings();
        }
    }

    public void Save(AppSettings settings)
    {
        try
        {
            var dir = Path.GetDirectoryName(SettingsPath);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }

            var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(SettingsPath, json);
        }
        catch
        {
            // best-effort — không chặn Save/Open của document vì lưu settings lỗi
        }
    }
}
