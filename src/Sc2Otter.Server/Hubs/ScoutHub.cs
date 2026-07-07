namespace Sc2Otter.Server.Hubs;

using Microsoft.AspNetCore.SignalR;
using Sc2Otter.Core.Events;
using Sc2Otter.Core.Interfaces;

public class ScoutHub(IServiceScopeFactory scopeFactory, ILogger<ScoutHub> logger) : Hub
{
    // -------------------------------------------------------------------------
    // User resolution
    // -------------------------------------------------------------------------

    /// <summary>
    /// Resolves the current user ID from either the cookie claim or the X-Sync-Key / syncKey
    /// query-string header. The result is cached in Context.Items for the lifetime of the
    /// connection so that subsequent hub method calls don't repeat the DB lookup.
    /// </summary>
    private async Task<int?> GetUserIdAsync()
    {
        // Return cached result for this connection if already resolved.
        if (Context.Items.TryGetValue("ResolvedUserId", out var cached))
            return cached as int?;

        var httpContext = Context.GetHttpContext();
        if (httpContext == null) return null;

        // 1. Cookie / OAuth claim
        var claim = httpContext.User?.FindFirst("Sc2OtterUserId");
        if (claim != null && int.TryParse(claim.Value, out var claimId))
        {
            Context.Items["ResolvedUserId"] = (int?)claimId;
            return claimId;
        }

        // 2. X-Sync-Key header or syncKey query-string (used by LocalOtter)
        string? syncKey = null;
        if (httpContext.Request.Headers.TryGetValue("X-Sync-Key", out var syncKeyValues))
            syncKey = syncKeyValues.FirstOrDefault();

        if (string.IsNullOrEmpty(syncKey) && httpContext.Request.Query.TryGetValue("syncKey", out var syncKeyQuery))
            syncKey = syncKeyQuery.FirstOrDefault();

        if (!string.IsNullOrEmpty(syncKey))
        {
            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<Sc2Otter.Data.ScoutDbContext>();
            var user = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions
                .FirstOrDefaultAsync(db.Users, u => u.SyncKey == syncKey);
            if (user != null)
            {
                Context.Items["ResolvedUserId"] = (int?)user.Id;
                return user.Id;
            }
        }

        // Cache a negative result too, so we don't re-hit the DB on unauthenticated connections.
        Context.Items["ResolvedUserId"] = (int?)null;
        return null;
    }

    // -------------------------------------------------------------------------
    // Note methods (Web UI ↔ Server)
    // -------------------------------------------------------------------------

    public async Task SaveNote(int opponentId, string content, string source = "keyboard")
    {
        var userId = await GetUserIdAsync();
        if (!userId.HasValue) return;

        using var scope = scopeFactory.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IOpponentRepository>();

        var note = await repo.AddNoteAsync(opponentId, content, source);
        var noteDto = new OpponentNoteDto(note.Id, note.Content, note.CreatedAt, note.Source);

        logger.LogInformation("Note saved for opponent {OpponentId}: {Content}", opponentId, content);
        await Clients.Group($"User_{userId.Value}").SendAsync("NoteAdded", opponentId, noteDto);
    }

    public async Task UpdateNote(int noteId, string content)
    {
        var userId = await GetUserIdAsync();
        if (!userId.HasValue) return;

        using var scope = scopeFactory.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IOpponentRepository>();

        await repo.UpdateNoteAsync(noteId, content);
        await Clients.Group($"User_{userId.Value}").SendAsync("NoteUpdated", noteId, content);
    }

    public async Task DeleteNote(int noteId)
    {
        var userId = await GetUserIdAsync();
        if (!userId.HasValue) return;

        using var scope = scopeFactory.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IOpponentRepository>();

        await repo.DeleteNoteAsync(noteId);
        await Clients.Group($"User_{userId.Value}").SendAsync("NoteDeleted", noteId);
    }

    // -------------------------------------------------------------------------
    // Tag methods (Web UI ↔ Server)
    // -------------------------------------------------------------------------

    public async Task AddTag(int opponentId, string tagName)
    {
        var userId = await GetUserIdAsync();
        if (!userId.HasValue) return;

        using var scope = scopeFactory.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IOpponentRepository>();

        await repo.AddTagAsync(opponentId, tagName);
        await Clients.Group($"User_{userId.Value}").SendAsync("TagAdded", opponentId, tagName);
    }

    public async Task RemoveTag(int opponentId, string tagName)
    {
        var userId = await GetUserIdAsync();
        if (!userId.HasValue) return;

        using var scope = scopeFactory.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IOpponentRepository>();

        await repo.RemoveTagAsync(opponentId, tagName);
        await Clients.Group($"User_{userId.Value}").SendAsync("TagRemoved", opponentId, tagName);
    }

    // -------------------------------------------------------------------------
    // Bulk detail fetch — used by the overlay to pre-load multiple opponents
    // -------------------------------------------------------------------------

    public async Task<List<OpponentDetectedEvent>> GetOpponentDetails(List<int> opponentIds)
    {
        using var scope = scopeFactory.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IOpponentRepository>();

        // Fetch all opponents in parallel — safe because each call uses its own scope.
        var tasks = opponentIds.Select(async id =>
        {
            var opponent = await repo.GetWithDetailsAsync(id);
            if (opponent is null) return null;

            var stats = await repo.GetStatsAsync(id);
            return new OpponentDetectedEvent(
                OpponentId: opponent.Id,
                Name: opponent.Name,
                Race: opponent.Race,
                GameMode: null,
                Notes: opponent.Notes.Select(n => new OpponentNoteDto(n.Id, n.Content, n.CreatedAt, n.Source)).ToList(),
                Tags: opponent.Tags.Select(t => t.Name).ToList(),
                TotalGames: stats.TotalGames,
                Wins: stats.Wins,
                Losses: stats.Losses,
                Mmr: opponent.Mmr,
                League: opponent.League);
        });

        var results = await Task.WhenAll(tasks);
        return results.Where(r => r is not null).ToList()!;
    }

    // -------------------------------------------------------------------------
    // Push methods (LocalOtter → Web)
    // -------------------------------------------------------------------------

    public async Task PushGameState(GameStateChangedEvent e)
    {
        var userId = await GetUserIdAsync();
        if (userId.HasValue) await Clients.OthersInGroup($"User_{userId.Value}").SendAsync("GameStateChanged", e);
    }

    public async Task PushOpponentsDetected(List<OpponentDetectedEvent> opponents)
    {
        var userId = await GetUserIdAsync();
        if (userId.HasValue) await Clients.OthersInGroup($"User_{userId.Value}").SendAsync("OpponentsDetected", opponents);
    }

    public async Task PushPostGameResults(object players)
    {
        var userId = await GetUserIdAsync();
        if (userId.HasValue) await Clients.OthersInGroup($"User_{userId.Value}").SendAsync("PostGameResults", players);
    }

    public async Task TriggerNoteInput()
    {
        var userId = await GetUserIdAsync();
        if (userId.HasValue) await Clients.OthersInGroup($"User_{userId.Value}").SendAsync("ActivateNoteInput");
    }

    public async Task TriggerBulkImport()
    {
        var userId = await GetUserIdAsync();
        // Group (not OthersInGroup) so the signal hits LocalClients too
        if (userId.HasValue) await Clients.Group($"User_{userId.Value}").SendAsync("StartBulkImport");
    }

    public async Task SendHeartbeat()
    {
        var userId = await GetUserIdAsync();
        if (userId.HasValue) await Clients.OthersInGroup($"User_{userId.Value}").SendAsync("LocalClientHeartbeat");
    }

    public async Task RequestConfigSync()
    {
        var userId = await GetUserIdAsync();
        if (userId.HasValue) await Clients.Group($"User_{userId.Value}").SendAsync("RequestConfigSync");
    }

    public async Task PushConfig(object config)
    {
        var userId = await GetUserIdAsync();
        if (userId.HasValue) await Clients.OthersInGroup($"User_{userId.Value}").SendAsync("ConfigReceived", config);
    }

    public async Task PushBulkScanProgress(int current, int total)
    {
        var userId = await GetUserIdAsync();
        if (!userId.HasValue) return;

        if (current == total)
        {
            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<Sc2Otter.Data.ScoutDbContext>();
            var user = await db.Users.FindAsync(userId.Value);
            if (user != null)
            {
                user.LastBulkScanAt = DateTime.UtcNow;
                await db.SaveChangesAsync();
            }
        }
        await Clients.OthersInGroup($"User_{userId.Value}").SendAsync("BulkScanProgress", current, total);
    }

    public async Task RequestGameStateRefresh()
    {
        var userId = await GetUserIdAsync();
        if (userId.HasValue) await Clients.OthersInGroup($"User_{userId.Value}").SendAsync("GameStateRefreshRequested");
    }

    // -------------------------------------------------------------------------
    // Hub lifecycle
    // -------------------------------------------------------------------------

    public override async Task OnConnectedAsync()
    {
        var userId = await GetUserIdAsync();
        if (userId.HasValue)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, $"User_{userId.Value}");
            logger.LogInformation("Client connected: {ConnectionId} for User {UserId}", Context.ConnectionId, userId.Value);
        }
        else
        {
            logger.LogWarning("Unauthenticated client connected: {ConnectionId}", Context.ConnectionId);
        }
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        var userId = await GetUserIdAsync();
        if (userId.HasValue)
        {
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"User_{userId.Value}");
            logger.LogInformation("Client disconnected: {ConnectionId} for User {UserId}", Context.ConnectionId, userId.Value);
        }
        await base.OnDisconnectedAsync(exception);
    }
}
