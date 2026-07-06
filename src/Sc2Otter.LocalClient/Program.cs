using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Spectre.Console;
using Sc2Otter.Core.Interfaces;
using Sc2Otter.LocalClient.Services;

var settingsService = new SettingsService();

if (args.Length == 0)
{
    while (true)
    {
        Console.Clear();
        AnsiConsole.Write(new FigletText("Sc2Otter").Color(Color.Blue));
        
        var choice = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title("What would you like to do?")
                .PageSize(10)
                .AddChoices(new[] {
                    "Run Local Client",
                    "Configure Settings",
                    "Exit"
                }));

        if (choice == "Exit")
        {
            return;
        }

        if (choice == "Configure Settings")
        {
            var settings = settingsService.Current;
            
            settings.SyncKey = AnsiConsole.Ask<string>("Enter your [green]Server Sync Key[/]:", settings.SyncKey ?? "");
            settings.MySc2Name = AnsiConsole.Ask<string>("Enter your [green]SC2 Username[/] (comma separated for multiple):", settings.MySc2Name ?? "");
            settings.ReplayDirectory = AnsiConsole.Ask<string>("Enter your [green]Replay Directory[/]:", settings.ReplayDirectory ?? "");
            
            var dateStr = AnsiConsole.Ask<string>("Enter your [green]Bulk Scan Cutoff Date[/] (YYYY-MM-DD), or leave blank for none:", settings.BulkScanCutoffDate?.ToString("yyyy-MM-dd") ?? "");
            if (DateTime.TryParse(dateStr, out var parsedDate))
            {
                settings.BulkScanCutoffDate = DateTime.SpecifyKind(parsedDate, DateTimeKind.Utc);
            }
            else if (string.IsNullOrWhiteSpace(dateStr))
            {
                settings.BulkScanCutoffDate = null;
            }

            settingsService.Update(settings);
            
            AnsiConsole.MarkupLine("[green]Settings saved![/] Press any key to continue...");
            Console.ReadKey();
            continue;
        }

        if (choice == "Run Local Client")
        {
            break;
        }
    }
}

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddSingleton(settingsService);
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
