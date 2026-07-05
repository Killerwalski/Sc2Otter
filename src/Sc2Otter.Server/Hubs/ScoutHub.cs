namespace Sc2Otter.Server.Hubs;

using Microsoft.AspNetCore.SignalR;
using Sc2Otter.Core.Events;
using Sc2Otter.Core.Interfaces;

public class ScoutHub(IServiceScopeFactory scopeFactory, ILogger<ScoutHub> logger) : Hub
{
    public async Task SaveNote(int opponentId, string content, string source = "keyboard")
    {
        using var scope = scopeFactory.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IOpponentRepository>();

        var note = await repo.AddNoteAsync(opponentId, content, source);
        var noteDto = new OpponentNoteDto(note.Id, note.Content, note.CreatedAt, note.Source);

        logger.LogInformation("Note saved for opponent {OpponentId}: {Content}", opponentId, content);

        // Broadcast the new note to all connected clients
        await Clients.All.SendAsync("NoteAdded", opponentId, noteDto);
    }

    public async Task UpdateNote(int noteId, string content)
    {
        using var scope = scopeFactory.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IOpponentRepository>();

        await repo.UpdateNoteAsync(noteId, content);

        logger.LogInformation("Note {NoteId} updated", noteId);

        await Clients.All.SendAsync("NoteUpdated", noteId, content);
    }

    public async Task DeleteNote(int noteId)
    {
        using var scope = scopeFactory.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IOpponentRepository>();

        await repo.DeleteNoteAsync(noteId);

        logger.LogInformation("Note {NoteId} deleted", noteId);

        await Clients.All.SendAsync("NoteDeleted", noteId);
    }

    public async Task AddTag(int opponentId, string tagName)
    {
        using var scope = scopeFactory.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IOpponentRepository>();

        await repo.AddTagAsync(opponentId, tagName);

        logger.LogInformation("Tag '{Tag}' added to opponent {OpponentId}", tagName, opponentId);

        await Clients.All.SendAsync("TagAdded", opponentId, tagName);
    }

    public async Task RemoveTag(int opponentId, string tagName)
    {
        using var scope = scopeFactory.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IOpponentRepository>();

        await repo.RemoveTagAsync(opponentId, tagName);

        logger.LogInformation("Tag '{Tag}' removed from opponent {OpponentId}", tagName, opponentId);

        await Clients.All.SendAsync("TagRemoved", opponentId, tagName);
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

    public override async Task OnConnectedAsync()
    {
        logger.LogInformation("Client connected: {ConnectionId}", Context.ConnectionId);
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        logger.LogInformation("Client disconnected: {ConnectionId}", Context.ConnectionId);
        await base.OnDisconnectedAsync(exception);
    }
}
