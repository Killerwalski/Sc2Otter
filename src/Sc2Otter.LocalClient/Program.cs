using Sc2Otter.Core.Interfaces;
using Sc2Otter.LocalClient.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddSingleton<SettingsService>();
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
    client.BaseAddress = new Uri("http://localhost:5177");
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

var host = builder.Build();
host.Run();

class HubStarterService(ScoutHubClient client) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await client.StartAsync(stoppingToken);
    }
}
