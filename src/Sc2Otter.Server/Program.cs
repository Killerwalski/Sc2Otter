using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication;
using System.Security.Claims;
using Sc2Otter.Core.Interfaces;
using Sc2Otter.Data;
using Sc2Otter.Data.Repositories;
using Sc2Otter.Server;
using Sc2Otter.Server.Components;
using Sc2Otter.Server.Hubs;
using Sc2Otter.Server.Services;
using System.Diagnostics;

var builder = WebApplication.CreateBuilder(args);

// --- Database ---
// Parse Railway's DATABASE_URL if it exists
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
var databaseUrl = Environment.GetEnvironmentVariable("DATABASE_URL");
if (!string.IsNullOrEmpty(databaseUrl))
{
    // postgresql://user:pass@host:port/dbname
    var uri = new Uri(databaseUrl);
    var userInfo = uri.UserInfo.Split(':');
    connectionString = $"Host={uri.Host};Port={uri.Port};Database={uri.AbsolutePath.TrimStart('/')};Username={userInfo[0]};Password={userInfo[1]};Ssl Mode=Require;Trust Server Certificate=true;";
}

if (string.IsNullOrEmpty(connectionString))
{
    throw new InvalidOperationException("CRITICAL ERROR: No database connection string was found. Please ensure you added the DATABASE_URL Reference Variable in your Railway project settings!");
}

builder.Services.AddDbContext<ScoutDbContext>(options =>
{
    options.UseNpgsql(connectionString);
});

// --- Authentication ---
builder.Services.AddAuthentication(options =>
{
    options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = "Discord";
})
.AddCookie(options =>
{
    options.LoginPath = "/login";
    options.LogoutPath = "/logout";
})
.AddDiscord(options =>
{
    options.ClientId = builder.Configuration["Discord:ClientId"] ?? "MissingClientId";
    options.ClientSecret = builder.Configuration["Discord:ClientSecret"] ?? "MissingClientSecret";
    options.SaveTokens = true;
    options.Scope.Add("identify");

    options.Events.OnCreatingTicket = async context =>
    {
        var db = context.HttpContext.RequestServices.GetRequiredService<ScoutDbContext>();
        var discordId = context.Principal?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        var username = context.Principal?.FindFirst(ClaimTypes.Name)?.Value;
        var avatar = context.User.TryGetProperty("avatar", out var avatarElem) && avatarElem.ValueKind == System.Text.Json.JsonValueKind.String 
            ? avatarElem.GetString() : null;
        var avatarUrl = avatar != null ? $"https://cdn.discordapp.com/avatars/{discordId}/{avatar}.png" : null;

        if (discordId != null)
        {
            var user = await db.Users.FirstOrDefaultAsync(u => u.DiscordId == discordId);
            if (user == null)
            {
                user = new Sc2Otter.Core.Models.AppUser 
                { 
                    DiscordId = discordId, 
                    Username = username ?? "Unknown", 
                    AvatarUrl = avatarUrl,
                    SyncKey = Guid.NewGuid().ToString("N")
                };
                db.Users.Add(user);
            }
            else
            {
                user.Username = username ?? user.Username;
                user.AvatarUrl = avatarUrl;
            }
            await db.SaveChangesAsync();
            
            var identity = (ClaimsIdentity)context.Principal!.Identity!;
            identity.AddClaim(new Claim("Sc2OtterUserId", user.Id.ToString()));
            if (user.AvatarUrl != null)
            {
                identity.AddClaim(new Claim("avatarUrl", user.AvatarUrl));
            }
        }
    };
});

builder.Services.AddCascadingAuthenticationState();
builder.Services.AddAuthorizationCore();

// --- Services ---
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUserService, CurrentUserService>();
builder.Services.AddScoped<IOpponentRepository, OpponentRepository>();
builder.Services.AddSingleton<SettingsService>();
// Services that stay in server
// Local services have been moved to Sc2Otter.LocalClient

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
app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();
app.MapStaticAssets();

app.MapGet("/login", () => Results.Challenge(new AuthenticationProperties { RedirectUri = "/" }, new[] { "Discord" }));
app.MapGet("/logout", () => Results.SignOut(new AuthenticationProperties { RedirectUri = "/" }, new[] { CookieAuthenticationDefaults.AuthenticationScheme }));

// SignalR hub
app.MapHub<ScoutHub>("/scouthub");

// API
app.MapApiEndpoints();


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
