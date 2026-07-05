namespace Sc2Otter.Server.Services;

using System.Runtime.InteropServices;
using Microsoft.AspNetCore.SignalR;
using Sc2Otter.Server.Hubs;

public class HotkeyService(IHubContext<ScoutHub> hubContext, SettingsService settings, ILogger<HotkeyService> logger) : BackgroundService
{
    private const int MOD_ALT = 0x0001;
    private const int MOD_CONTROL = 0x0002;
    private const int MOD_SHIFT = 0x0004;
    private const int VK_N = 0x4E;
    private const int WM_HOTKEY = 0x0312;
    private const int HOTKEY_ADD_NOTE = 1;

    [DllImport("user32.dll")]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, int fsModifiers, int vk);

    [DllImport("user32.dll")]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    [DllImport("user32.dll")]
    private static extern bool PeekMessage(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax, uint wRemoveMsg);

    [DllImport("user32.dll")]
    private static extern bool TranslateMessage(ref MSG lpMsg);

    [DllImport("user32.dll")]
    private static extern IntPtr DispatchMessage(ref MSG lpMsg);

    [StructLayout(LayoutKind.Sequential)]
    private struct MSG
    {
        public IntPtr hwnd;
        public uint message;
        public IntPtr wParam;
        public IntPtr lParam;
        public uint time;
        public POINT pt;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int x;
        public int y;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            logger.LogWarning("Global hotkeys are only supported on Windows. HotkeyService will not run.");
            return;
        }

        // Run the message pump on a dedicated STA thread
        await Task.Factory.StartNew(() => RunMessagePump(stoppingToken),
            stoppingToken,
            TaskCreationOptions.LongRunning,
            TaskScheduler.Default);
    }

    private int ParseModifiers(string mods)
    {
        int result = 0;
        if (mods.Contains("Ctrl")) result |= MOD_CONTROL;
        if (mods.Contains("Shift")) result |= MOD_SHIFT;
        if (mods.Contains("Alt")) result |= MOD_ALT;
        return result;
    }

    private int ParseKey(string key)
    {
        if (string.IsNullOrEmpty(key)) return VK_N;
        char c = key.ToUpperInvariant()[0];
        if (c >= 'A' && c <= 'Z') return c;
        return VK_N;
    }

    private void RegisterCurrentHotkey(int mods, int key)
    {
        var registered = RegisterHotKey(IntPtr.Zero, HOTKEY_ADD_NOTE, mods, key);
        if (registered)
            logger.LogInformation("Global hotkey registered.");
        else
            logger.LogWarning("Failed to register global hotkey. It may be in use.");
    }

    private void RunMessagePump(CancellationToken ct)
    {
        int currentMods = ParseModifiers(settings.Current.HotkeyModifiers);
        int currentKey = ParseKey(settings.Current.HotkeyChar);
        
        RegisterCurrentHotkey(currentMods, currentKey);

        try
        {
            while (!ct.IsCancellationRequested)
            {
                int newMods = ParseModifiers(settings.Current.HotkeyModifiers);
                int newKey = ParseKey(settings.Current.HotkeyChar);
                if (newMods != currentMods || newKey != currentKey)
                {
                    UnregisterHotKey(IntPtr.Zero, HOTKEY_ADD_NOTE);
                    currentMods = newMods;
                    currentKey = newKey;
                    RegisterCurrentHotkey(currentMods, currentKey);
                }

                if (PeekMessage(out var msg, IntPtr.Zero, 0, 0, 1)) // PM_REMOVE = 1
                {
                    if (msg.message == WM_HOTKEY)
                    {
                        var hotkeyId = (int)msg.wParam;
                        if (hotkeyId == HOTKEY_ADD_NOTE)
                        {
                            logger.LogInformation("Hotkey pressed");
                            _ = hubContext.Clients.All.SendAsync("ActivateNoteInput", ct);
                        }
                    }

                    TranslateMessage(ref msg);
                    DispatchMessage(ref msg);
                }
                else
                {
                    Thread.Sleep(50);
                }
            }
        }
        finally
        {
            UnregisterHotKey(IntPtr.Zero, HOTKEY_ADD_NOTE);
            logger.LogInformation("Global hotkey unregistered.");
        }
    }
}
