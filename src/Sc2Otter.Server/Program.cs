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
builder.Services.AddSingleton<SettingsService>();
builder.Services.AddSingleton<ReplayAnalysisService>();

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
    
    // Clean up local player from database
    var settings = scope.ServiceProvider.GetRequiredService<SettingsService>();
    if (!string.IsNullOrWhiteSpace(settings.Current.MySc2Name))
    {
        var me = await db.Opponents.FirstOrDefaultAsync(o => o.Name.ToLower() == settings.Current.MySc2Name.ToLower());
        if (me != null)
        {
            db.Opponents.Remove(me);
            await db.SaveChangesAsync();
        }
    }

    // Fix any bad race strings from older data
    var badTerr = await db.Opponents.Where(o => o.Race == "Terr" || o.Race == "terran").ToListAsync();
    foreach(var o in badTerr) o.Race = "Terran";
    
    var badProt = await db.Opponents.Where(o => o.Race == "Prot" || o.Race == "protoss").ToListAsync();
    foreach(var o in badProt) o.Race = "Protoss";
    
    var badRand = await db.Opponents.Where(o => o.Race == "Rand" || o.Race == "random").ToListAsync();
    foreach(var o in badRand) o.Race = "Random";
    
    var badZerg = await db.Opponents.Where(o => o.Race == "zerg").ToListAsync();
    foreach(var o in badZerg) o.Race = "Zerg";
    
    if (badTerr.Count > 0 || badProt.Count > 0 || badRand.Count > 0 || badZerg.Count > 0)
    {
        await db.SaveChangesAsync();
    }
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

app.MapPost("/api/admin/scan-replays", (ReplayAnalysisService analyzer, ILogger<Program> logger) =>
{
    _ = Task.Run(async () => 
    {
        try
        {
            var replayDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "StarCraft II", "Accounts");
            if (!Directory.Exists(replayDir)) return;

            var cutoff = new DateTime(2026, 6, 30, 0, 0, 0, DateTimeKind.Utc);
            
            var files = Directory.GetFiles(replayDir, "*.SC2Replay", SearchOption.AllDirectories)
                .Where(f => f.Contains("Multiplayer") && File.GetLastWriteTimeUtc(f) >= cutoff)
                .ToList();
                
            logger.LogInformation("Found {Count} replays to scan since June 30, 2026", files.Count);
            
            int count = 0;
            foreach (var file in files)
            {
                await analyzer.AnalyzeReplayAsync(file);
                count++;
                if (count % 10 == 0) logger.LogInformation("Scanned {Count}/{Total} replays...", count, files.Count);
            }
            logger.LogInformation("Finished scanning all {Total} replays", files.Count);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error during bulk replay scan");
        }
    });
    return Results.Accepted(value: "Background scan started. Check console logs for progress.");
});

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
