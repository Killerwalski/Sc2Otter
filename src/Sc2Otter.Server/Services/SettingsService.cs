namespace Sc2Otter.Server.Services;

using System.Text.Json;
using System.IO;

public class UserSettings
{
    public string MySc2Name { get; set; } = string.Empty;
    public string HotkeyModifiers { get; set; } = "Ctrl+Shift";
    public string HotkeyChar { get; set; } = "N";
    public int PollingIntervalMs { get; set; } = 2000;
    public string ReplayDirectory { get; set; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "StarCraft II", "Accounts");
    
    public UserSettings Clone() => (UserSettings)MemberwiseClone();
}

public class SettingsService
{
    private readonly string _settingsFilePath;
    
    public UserSettings Current { get; private set; } = new();
    public event Action? OnSettingsChanged;

    public SettingsService()
    {
        var appData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Sc2Otter");
        Directory.CreateDirectory(appData);
        _settingsFilePath = Path.Combine(appData, "user_settings.json");
        Load();
    }

    public void Update(UserSettings newSettings)
    {
        Current = newSettings.Clone();
        Save();
        OnSettingsChanged?.Invoke();
    }

    private void Load()
    {
        if (File.Exists(_settingsFilePath))
        {
            try
            {
                var json = File.ReadAllText(_settingsFilePath);
                Current = JsonSerializer.Deserialize<UserSettings>(json) ?? new UserSettings();
            }
            catch
            {
                Current = new UserSettings();
            }
        }
    }

    private void Save()
    {
        var json = JsonSerializer.Serialize(Current, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(_settingsFilePath, json);
    }
}
