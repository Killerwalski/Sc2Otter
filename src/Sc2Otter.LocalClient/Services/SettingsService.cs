namespace Sc2Otter.LocalClient.Services;

using System.Text.Json;
using System.IO;

public class UserSettings
{
    public string MySc2Name { get; set; } = string.Empty;
    public string HotkeyModifiers { get; set; } = "Ctrl+Shift";
    public string HotkeyChar { get; set; } = "N";
    public int PollingIntervalMs { get; set; } = 2000;
    public string ReplayDirectory { get; set; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "StarCraft II", "Accounts");
    public DateTime? BulkScanCutoffDate { get; set; }
    
    // --- Cloud Sync ---
    public string ServerUrl { get; set; } = "https://sc2otter-production.up.railway.app";
    public string SyncKey { get; set; } = string.Empty;
    public string LastScanResult { get; set; } = string.Empty;
    
    public UserSettings Clone() => (UserSettings)MemberwiseClone();
}

public class SettingsService
{
    private readonly string _settingsFilePath;
    private readonly FileSystemWatcher _watcher;
    
    public UserSettings Current { get; private set; } = new();
    public event Action? OnSettingsChanged;

    public SettingsService()
    {
        var appData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Sc2Otter");
        Directory.CreateDirectory(appData);
        _settingsFilePath = Path.Combine(appData, "user_settings.json");
        Load();

        _watcher = new FileSystemWatcher(appData, "user_settings.json")
        {
            NotifyFilter = NotifyFilters.LastWrite
        };
        _watcher.Changed += OnFileChanged;
        _watcher.EnableRaisingEvents = true;
    }

    private void OnFileChanged(object sender, FileSystemEventArgs e)
    {
        // Add a small delay to avoid reading while the file is being written
        Task.Delay(100).ContinueWith(_ => 
        {
            Load();
            OnSettingsChanged?.Invoke();
        });
    }

    public void Update(UserSettings newSettings)
    {
        _watcher.EnableRaisingEvents = false;
        Current = newSettings.Clone();
        Save();
        OnSettingsChanged?.Invoke();
        _watcher.EnableRaisingEvents = true;
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
                // File might be locked, just keep current settings
            }
        }
    }

    private void Save()
    {
        var json = JsonSerializer.Serialize(Current, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(_settingsFilePath, json);
    }
}
