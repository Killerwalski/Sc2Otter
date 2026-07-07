using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Sc2Otter.Core.Interfaces;
using Sc2Otter.LocalOtter.Services;

var settingsService = new SettingsService();

if (!args.Contains("--run"))
{
    while (true)
    {
        var version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "Unknown";
        Console.Clear();
        Console.WriteLine("========================================");
        Console.WriteLine($"           Sc2Otter v{version}          ");
        Console.WriteLine("========================================");
        Console.WriteLine();
        Console.WriteLine("What would you like to do?");
        Console.WriteLine("  1. Run Local Client");
        Console.WriteLine("  2. Configure Settings");
        Console.WriteLine("  3. Exit");
        Console.WriteLine();
        Console.Write("Enter your choice (1-3): ");
        
        var choice = Console.ReadLine()?.Trim();

        if (choice == "3")
        {
            return;
        }

        if (choice == "2")
        {
            var settings = settingsService.Current;
            
            Console.WriteLine();
            Console.Write($"Enter your Server Sync Key [{settings.SyncKey}]: ");
            var syncKey = Console.ReadLine();
            if (!string.IsNullOrWhiteSpace(syncKey)) settings.SyncKey = syncKey;
            
            Console.Write($"Enter your SC2 Username (comma separated for multiple) [{settings.MySc2Name}]: ");
            var sc2Name = Console.ReadLine();
            if (!string.IsNullOrWhiteSpace(sc2Name)) settings.MySc2Name = sc2Name;
            
            Console.Write($"Enter your Replay Directory [{settings.ReplayDirectory}]: ");
            var replayDir = Console.ReadLine();
            if (!string.IsNullOrWhiteSpace(replayDir)) settings.ReplayDirectory = replayDir;
            
            Console.Write($"Enter your Bulk Scan Cutoff Date (YYYY-MM-DD) [{settings.BulkScanCutoffDate?.ToString("yyyy-MM-dd")}]: ");
            var dateStr = Console.ReadLine();
            if (!string.IsNullOrWhiteSpace(dateStr))
            {
                if (DateTime.TryParse(dateStr, out var parsedDate))
                {
                    settings.BulkScanCutoffDate = DateTime.SpecifyKind(parsedDate, DateTimeKind.Utc);
                }
                else if (dateStr.Trim().ToLower() == "none" || dateStr.Trim().ToLower() == "null")
                {
                    settings.BulkScanCutoffDate = null;
                }
            }

            Console.WriteLine("\n--- AI Playstyle Analysis ---");
            Console.Write($"Enable AI Analysis? (true/false) [{settings.AiEnabled}]: ");
            var aiEnabled = Console.ReadLine();
            if (!string.IsNullOrWhiteSpace(aiEnabled) && bool.TryParse(aiEnabled, out var parsedAiEnabled)) settings.AiEnabled = parsedAiEnabled;

            if (settings.AiEnabled)
            {
                Console.Write($"Enter AI Provider (OpenAI/Gemini/Claude) [{settings.AiProvider}]: ");
                var aiProvider = Console.ReadLine();
                if (!string.IsNullOrWhiteSpace(aiProvider)) settings.AiProvider = aiProvider;

                string modelExample = "e.g. gpt-4o-mini, gemini-2.0-flash";
                if (settings.AiProvider?.Equals("Gemini", StringComparison.OrdinalIgnoreCase) == true)
                    modelExample = "e.g. gemini-2.0-flash, gemini-2.5-pro";
                else if (settings.AiProvider?.Equals("OpenAI", StringComparison.OrdinalIgnoreCase) == true)
                    modelExample = "e.g. gpt-4o-mini, gpt-4o";
                else if (settings.AiProvider?.Equals("Claude", StringComparison.OrdinalIgnoreCase) == true)
                    modelExample = "e.g. claude-3-haiku-20240307, claude-3-5-sonnet-20240620";

                Console.Write($"Enter AI Model ({modelExample}) [{settings.AiModel}]: ");
                var aiModel = Console.ReadLine();
                if (!string.IsNullOrWhiteSpace(aiModel)) settings.AiModel = aiModel;

                Console.Write($"Enter API Key [{(!string.IsNullOrEmpty(settings.AiApiKey) ? "********" : "None")}]: ");
                var aiKey = Console.ReadLine();
                if (!string.IsNullOrWhiteSpace(aiKey)) settings.AiApiKey = aiKey;
            }

            settingsService.Update(settings);
            
            Console.WriteLine();
            Console.WriteLine("Settings saved! Press Enter to continue...");
            Console.ReadLine();
            continue;
        }

        if (choice == "1")
        {
            break;
        }
    }
}
else
{
    Console.WriteLine("Starting in auto-run mode...");
}

var builder = Host.CreateApplicationBuilder(args);

builder.Logging.AddFilter("System.Net.Http.HttpClient", LogLevel.Warning);
builder.Logging.AddFilter("Microsoft.Extensions.Http", LogLevel.Warning);

builder.Services.AddSingleton(settingsService);
builder.Services.AddSingleton<LlmAnalysisService>();
builder.Services.AddSingleton<ReplayAnalysisService>();

builder.Services.AddHttpClient<ISc2GameClient, Sc2GameClient>(client =>
{
    client.BaseAddress = new Uri("http://localhost:6119");
    client.Timeout = TimeSpan.FromSeconds(3);
});

builder.Services.AddHttpClient<Sc2PulseClient>();

builder.Services.AddHttpClient<IOpponentRepository, HttpOpponentRepository>((sp, client) =>
{
    var settings = sp.GetRequiredService<SettingsService>();
    var serverUrl = settings.Current.ServerUrl.TrimEnd('/');
    client.BaseAddress = new Uri(serverUrl);
    
    if (!string.IsNullOrEmpty(settings.Current.SyncKey))
    {
        client.DefaultRequestHeaders.Add("X-Sync-Key", settings.Current.SyncKey);
    }
});

builder.Services.AddSingleton<ScoutHubClient>();

// Start the hub client as a hosted service
builder.Services.AddHostedService(sp => 
{
    var client = sp.GetRequiredService<ScoutHubClient>();
    return new HubStarterService(client);
});

builder.Services.AddHostedService<GameStateMonitor>();
builder.Services.AddHostedService<HotkeyService>();
builder.Services.AddHostedService<BulkReplayScannerService>();

var host = builder.Build();
host.Run();

class HubStarterService(ScoutHubClient client) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await client.StartAsync(stoppingToken);
    }
}
