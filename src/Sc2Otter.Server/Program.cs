using Microsoft.EntityFrameworkCore;
using Sc2Otter.Core.Interfaces;
using Sc2Otter.Data;
using Sc2Otter.Data.Repositories;
using Sc2Otter.Server.Components;
using Sc2Otter.Server.Hubs;
using Sc2Otter.Server.Services;
using System.Diagnostics;

var builder = WebApplication.CreateBuilder(args);

// --- Database ---
var dbFolder = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
    "Sc2Otter");
Directory.CreateDirectory(dbFolder);
var dbPath = Path.Combine(dbFolder, "scout.db");

builder.Services.AddDbContext<ScoutDbContext>(options =>
    options.UseSqlite($"Data Source={dbPath}"));

// --- Services ---
builder.Services.AddScoped<IOpponentRepository, OpponentRepository>();

builder.Services.AddHttpClient<ISc2GameClient, Sc2GameClient>(client =>
{
    client.BaseAddress = new Uri("http://localhost:6119");
    client.Timeout = TimeSpan.FromSeconds(3);
});

// Background services
builder.Services.AddHostedService<GameStateMonitor>();
builder.Services.AddHostedService<HotkeyService>();

// SignalR
builder.Services.AddSignalR();

// Blazor
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

var app = builder.Build();

// --- Create database on startup ---
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ScoutDbContext>();
    await db.Database.EnsureCreatedAsync();
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
}

app.UseStaticFiles();
app.UseAntiforgery();
app.MapStaticAssets();

// SignalR hub
app.MapHub<ScoutHub>("/scouthub");

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

// Auto-launch browser on startup
var url = "http://localhost:5177";
_ = Task.Run(async () =>
{
    await Task.Delay(1500);
    try
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = url,
            UseShellExecute = true
        });
    }
    catch
    {
        // Silently fail if browser can't be opened
    }
});

app.Run();
