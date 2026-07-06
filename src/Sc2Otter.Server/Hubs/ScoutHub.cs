namespace Sc2Otter.Server.Hubs;

using Microsoft.AspNetCore.SignalR;
using Sc2Otter.Core.Events;
using Sc2Otter.Core.Interfaces;

public class ScoutHub(IServiceScopeFactory scopeFactory, ILogger<ScoutHub> logger) : Hub
{
    private async Task<int?> GetUserIdAsync()
    {
        var httpContext = Context.GetHttpContext();
        if (httpContext == null) return null;

        var claim = httpContext.User?.FindFirst("Sc2OtterUserId");
        if (claim != null && int.TryParse(claim.Value, out var claimId))
        {
            return claimId;
        }

        if (httpContext.Request.Headers.TryGetValue("X-Sync-Key", out var syncKeyValues))
        {
            var syncKey = syncKeyValues.FirstOrDefault();
            if (!string.IsNullOrEmpty(syncKey))
            {
                using var scope = scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<Sc2Otter.Data.ScoutDbContext>();
                var user = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.FirstOrDefaultAsync(db.Users, u => u.SyncKey == syncKey);
                if (user != null) return user.Id;
            }
        }
        return null;
    }

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

    public async Task<List<OpponentDetectedEvent>> GetOpponentDetails(List<int> opponentIds)
    {
        using var scope = scopeFactory.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IOpponentRepository>();

        var results = new List<OpponentDetectedEvent>();
        foreach (var id in opponentIds)
        {
            var opponent = await repo.GetWithDetailsAsync(id);
            if (opponent is null) continue;

            var stats = await repo.GetStatsAsync(id);
            results.Add(new OpponentDetectedEvent(
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
                League: opponent.League));
        }
        return results;
    }

    // --- Methods for LocalClient to push state to Web UI ---
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
