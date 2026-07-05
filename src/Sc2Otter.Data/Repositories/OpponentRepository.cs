namespace Sc2Otter.Data.Repositories;

using Microsoft.EntityFrameworkCore;
using Sc2Otter.Core.Interfaces;
using Sc2Otter.Core.Models;

public class OpponentRepository(ScoutDbContext db) : IOpponentRepository
{
    public async Task<Opponent?> FindByNameAsync(string name, CancellationToken ct = default)
    {
        return await db.Opponents
            .Include(o => o.Tags)
            .FirstOrDefaultAsync(o => EF.Functions.Like(o.Name, name), ct);
    }

    public async Task<Opponent> GetOrCreateAsync(string name, string? race, CancellationToken ct = default)
    {
        var opponent = await FindByNameAsync(name, ct);
        if (opponent is not null)
        {
            if (race is not null) opponent.Race = race;
            opponent.LastSeen = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);
            return opponent;
        }

        opponent = new Opponent
        {
            Name = name,
            Race = race,
            FirstSeen = DateTime.UtcNow,
            LastSeen = DateTime.UtcNow
        };
        db.Opponents.Add(opponent);
        await db.SaveChangesAsync(ct);
        return opponent;
    }

    public async Task<Opponent?> GetByIdAsync(int id, CancellationToken ct = default)
    {
        return await db.Opponents.FindAsync([id], ct);
    }

    public async Task<Opponent?> GetWithDetailsAsync(int id, CancellationToken ct = default)
    {
        return await db.Opponents
            .Include(o => o.Notes.OrderByDescending(n => n.CreatedAt))
            .Include(o => o.Tags)
            .Include(o => o.MatchRecords.OrderByDescending(m => m.PlayedAt))
            .FirstOrDefaultAsync(o => o.Id == id, ct);
    }

    public async Task<List<Opponent>> SearchAsync(string? query = null, string? raceFilter = null, string? tagFilter = null, CancellationToken ct = default)
    {
        var q = db.Opponents.Include(o => o.Tags).AsQueryable();

        if (!string.IsNullOrWhiteSpace(query))
            q = q.Where(o => EF.Functions.Like(o.Name, $"%{query}%"));

        if (!string.IsNullOrWhiteSpace(raceFilter))
            q = q.Where(o => o.Race == raceFilter);

        if (!string.IsNullOrWhiteSpace(tagFilter))
            q = q.Where(o => o.Tags.Any(t => t.Name == tagFilter));

        return await q.OrderByDescending(o => o.LastSeen).ToListAsync(ct);
    }

    public async Task<List<Opponent>> GetRecentAsync(int count = 10, CancellationToken ct = default)
    {
        return await db.Opponents
            .Include(o => o.Tags)
            .Include(o => o.MatchRecords)
            .OrderByDescending(o => o.LastSeen)
            .Take(count)
            .ToListAsync(ct);
    }

    public async Task<OpponentNote> AddNoteAsync(int opponentId, string content, string source = "keyboard", CancellationToken ct = default)
    {
        var note = new OpponentNote
        {
            OpponentId = opponentId,
            Content = content,
            Source = source,
            CreatedAt = DateTime.UtcNow
        };
        db.Notes.Add(note);

        var opponent = await db.Opponents.FindAsync([opponentId], ct);
        if (opponent is not null) opponent.LastSeen = DateTime.UtcNow;

        await db.SaveChangesAsync(ct);
        return note;
    }

    public async Task UpdateNoteAsync(int noteId, string content, CancellationToken ct = default)
    {
        var note = await db.Notes.FindAsync([noteId], ct);
        if (note is null) return;
        note.Content = content;
        note.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
    }

    public async Task DeleteNoteAsync(int noteId, CancellationToken ct = default)
    {
        var note = await db.Notes.FindAsync([noteId], ct);
        if (note is null) return;
        db.Notes.Remove(note);
        await db.SaveChangesAsync(ct);
    }

    public async Task AddTagAsync(int opponentId, string tagName, CancellationToken ct = default)
    {
        var opponent = await db.Opponents.Include(o => o.Tags).FirstOrDefaultAsync(o => o.Id == opponentId, ct);
        if (opponent is null) return;

        var tag = await db.Tags.FirstOrDefaultAsync(t => t.Name == tagName, ct);
        if (tag is null)
        {
            tag = new OpponentTag { Name = tagName };
            db.Tags.Add(tag);
        }

        if (!opponent.Tags.Any(t => t.Name == tagName))
        {
            opponent.Tags.Add(tag);
            await db.SaveChangesAsync(ct);
        }
    }

    public async Task RemoveTagAsync(int opponentId, string tagName, CancellationToken ct = default)
    {
        var opponent = await db.Opponents.Include(o => o.Tags).FirstOrDefaultAsync(o => o.Id == opponentId, ct);
        if (opponent is null) return;

        var tag = opponent.Tags.FirstOrDefault(t => t.Name == tagName);
        if (tag is not null)
        {
            opponent.Tags.Remove(tag);
            await db.SaveChangesAsync(ct);
        }
    }

    public async Task<List<OpponentTag>> GetAllTagsAsync(CancellationToken ct = default)
    {
        return await db.Tags.OrderBy(t => t.Name).ToListAsync(ct);
    }

    public async Task<MatchRecord> RecordMatchAsync(int opponentId, MatchResult result, string? mapName = null, string? myRace = null, string? opponentRace = null, string? gameMode = null, CancellationToken ct = default)
    {
        var record = new MatchRecord
        {
            OpponentId = opponentId,
            Result = result,
            MapName = mapName,
            MyRace = myRace,
            OpponentRace = opponentRace,
            GameMode = gameMode,
            PlayedAt = DateTime.UtcNow
        };
        db.MatchRecords.Add(record);

        var opponent = await db.Opponents.FindAsync([opponentId], ct);
        if (opponent is not null)
        {
            opponent.LastSeen = DateTime.UtcNow;
            if (opponentRace is not null) opponent.Race = opponentRace;
        }

        await db.SaveChangesAsync(ct);
        return record;
    }

    public async Task<(int TotalGames, int Wins, int Losses)> GetStatsAsync(int opponentId, CancellationToken ct = default)
    {
        var records = await db.MatchRecords.Where(m => m.OpponentId == opponentId).ToListAsync(ct);
        return (
            records.Count,
            records.Count(r => r.Result == MatchResult.Win),
            records.Count(r => r.Result == MatchResult.Loss)
        );
    }
}
