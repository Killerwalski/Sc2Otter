namespace Sc2Otter.Server.Services;

using System.Runtime.InteropServices;
using Microsoft.AspNetCore.SignalR;
using Sc2Otter.Server.Hubs;

public class HotkeyService(IHubContext<ScoutHub> hubContext, ILogger<HotkeyService> logger) : BackgroundService
{
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

    private void RunMessagePump(CancellationToken ct)
    {
        var registered = RegisterHotKey(IntPtr.Zero, HOTKEY_ADD_NOTE, MOD_CONTROL | MOD_SHIFT, VK_N);
        if (registered)
        {
            logger.LogInformation("Global hotkey registered: Ctrl+Shift+N (Add Note)");
        }
        else
        {
            logger.LogWarning("Failed to register global hotkey Ctrl+Shift+N. It may be in use by another application.");
            return;
        }

        try
        {
            while (!ct.IsCancellationRequested)
            {
                if (PeekMessage(out var msg, IntPtr.Zero, 0, 0, 1)) // PM_REMOVE = 1
                {
                    if (msg.message == WM_HOTKEY)
                    {
                        var hotkeyId = (int)msg.wParam;
                        if (hotkeyId == HOTKEY_ADD_NOTE)
                        {
                            logger.LogInformation("Hotkey pressed: Ctrl+Shift+N");
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
