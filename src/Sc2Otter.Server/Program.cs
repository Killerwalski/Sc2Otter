using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication;
using System.Security.Claims;
using Sc2Otter.Core.Interfaces;
using Sc2Otter.Core.Models;
using Sc2Otter.Data;
using Sc2Otter.Data.Repositories;
using Sc2Otter.Server;
using Sc2Otter.Server.Components;
using Sc2Otter.Server.Hubs;
using Sc2Otter.Server.Services;
using System.Diagnostics;
using Microsoft.AspNetCore.HttpOverrides;

var builder = WebApplication.CreateBuilder(args);

// --- Database ---
// Parse Railway's DATABASE_URL if it exists (format: postgresql://user:pass@host:port/dbname)
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
var databaseUrl = Environment.GetEnvironmentVariable("DATABASE_URL");
if (!string.IsNullOrEmpty(databaseUrl))
{
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
    options.Events.OnCreatingTicket = HandleDiscordTicketAsync;
});

builder.Services.AddCascadingAuthenticationState();
builder.Services.AddAuthorizationCore();

// --- Services ---
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUserService, CurrentUserService>();
builder.Services.AddScoped<IOpponentRepository, OpponentRepository>();
builder.Services.AddSingleton<SettingsService>();

// SignalR
builder.Services.AddSignalR();

// Blazor
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles;
});

var app = builder.Build();

// --- Migrate database on startup ---
await MigrateDatabaseAsync(app);

// --- Configure Forwarded Headers for Railway Proxy ---
var forwardedHeadersOptions = new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto
};
forwardedHeadersOptions.KnownNetworks.Clear();
forwardedHeadersOptions.KnownProxies.Clear();
app.UseForwardedHeaders(forwardedHeadersOptions);

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

// Auto-launch browser — only meaningful in development; skip on production servers.
if (app.Environment.IsDevelopment())
{
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
}

app.Run();

// -------------------------------------------------------------------------
// Local functions
// -------------------------------------------------------------------------

/// <summary>
/// Handles the Discord OAuth ticket creation event.
/// Upserts the AppUser record and adds the internal UserId + avatar claims to the principal.
/// Extracted from the inline lambda for readability and to keep Program.cs concise.
/// </summary>
static async Task HandleDiscordTicketAsync(Microsoft.AspNetCore.Authentication.OAuth.OAuthCreatingTicketContext context)
{
    var logger = context.HttpContext.RequestServices.GetRequiredService<ILogger<Program>>();
    logger.LogInformation("Discord OAuth: OnCreatingTicket started for user.");

    var db = context.HttpContext.RequestServices.GetRequiredService<ScoutDbContext>();
    var discordId = context.Principal?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
    var username = context.Principal?.FindFirst(ClaimTypes.Name)?.Value;

    logger.LogInformation("Discord OAuth: Found Discord ID: {DiscordId}, Username: {Username}", discordId, username);

    var avatar = context.User.TryGetProperty("avatar", out var avatarElem) && avatarElem.ValueKind == System.Text.Json.JsonValueKind.String
        ? avatarElem.GetString() : null;
    var avatarUrl = avatar != null ? $"https://cdn.discordapp.com/avatars/{discordId}/{avatar}.png" : null;

    if (discordId != null)
    {
        logger.LogInformation("Discord OAuth: Querying database for user.");
        var user = await db.Users.FirstOrDefaultAsync(u => u.DiscordId == discordId);
        if (user == null)
        {
            logger.LogInformation("Discord OAuth: Creating new user.");
            user = new AppUser
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
        logger.LogInformation("Discord OAuth: Saving changes to database.");
        await db.SaveChangesAsync();
        logger.LogInformation("Discord OAuth: Database save complete. UserId: {UserId}", user.Id);

        var identity = (ClaimsIdentity)context.Principal!.Identity!;
        identity.AddClaim(new Claim("Sc2OtterUserId", user.Id.ToString()));
        if (user.AvatarUrl != null)
        {
            identity.AddClaim(new Claim("avatarUrl", user.AvatarUrl));
        }
    }
    logger.LogInformation("Discord OAuth: OnCreatingTicket complete.");
}

/// <summary>
/// Runs EF Core migrations at startup with a backwards-compatibility bootstrap:
/// if the database was previously set up with EnsureCreated (no migration history table),
/// the initial migration record is faked so MigrateAsync doesn't try to re-apply it.
/// </summary>
static async Task MigrateDatabaseAsync(WebApplication app)
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<ScoutDbContext>();

    var connection = db.Database.GetDbConnection();
    await connection.OpenAsync();

    using (var command = connection.CreateCommand())
    {
        command.CommandText = "SELECT EXISTS (SELECT FROM pg_tables WHERE schemaname = 'public' AND tablename ILIKE 'users');";
        var usersExists = (bool)(await command.ExecuteScalarAsync() ?? false);

        command.CommandText = "SELECT EXISTS (SELECT FROM pg_tables WHERE schemaname = 'public' AND tablename = '__EFMigrationsHistory');";
        var historyTableExists = (bool)(await command.ExecuteScalarAsync() ?? false);

        bool initialMigrationExists = false;
        if (historyTableExists)
        {
            command.CommandText = "SELECT EXISTS (SELECT 1 FROM \"__EFMigrationsHistory\" WHERE \"MigrationId\" = '20260706012723_InitialPostgres');";
            initialMigrationExists = (bool)(await command.ExecuteScalarAsync() ?? false);
        }

        if (usersExists && !initialMigrationExists)
        {
            var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
            logger.LogInformation("Transitioning from EnsureCreated to Migrations. Faking InitialPostgres.");

            if (!historyTableExists)
            {
                command.CommandText = @"
                    CREATE TABLE ""__EFMigrationsHistory"" (
                        ""MigrationId"" character varying(150) NOT NULL,
                        ""ProductVersion"" character varying(32) NOT NULL,
                        CONSTRAINT ""PK___EFMigrationsHistory"" PRIMARY KEY (""MigrationId"")
                    );
                ";
                await command.ExecuteNonQueryAsync();
            }

            command.CommandText = @"
                INSERT INTO ""__EFMigrationsHistory"" (""MigrationId"", ""ProductVersion"")
                VALUES ('20260706012723_InitialPostgres', '10.0.9');
            ";
            await command.ExecuteNonQueryAsync();
        }
    }
    await connection.CloseAsync();

    await db.Database.MigrateAsync();
}
