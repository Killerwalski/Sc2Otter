namespace Sc2Otter.Data.Repositories;

using Microsoft.EntityFrameworkCore;
using Sc2Otter.Core.Interfaces;
using Sc2Otter.Core.Models;

public class OpponentRepository(ScoutDbContext db) : IOpponentRepository
{
    public async Task<Opponent?> FindByNameAsync(string name, CancellationToken ct = default)
    {
        return await db.Opponents
            .Include(o => o.TagAssignments).ThenInclude(ta => ta.Tag)
            .FirstOrDefaultAsync(o => EF.Functions.Like(o.Name, name), ct);
    }

    public async Task<Opponent> GetOrCreateAsync(string name, string? race, DateTime? seenAt = null, CancellationToken ct = default)
    {
        var time = seenAt ?? DateTime.UtcNow;
        var opponent = await FindByNameAsync(name, ct);
        if (opponent is not null)
        {
            if (race is not null) opponent.Race = race;
            // Only update LastSeen if the new time is newer than the existing LastSeen
            if (opponent.LastSeen < time) opponent.LastSeen = time;
            // Only update FirstSeen if the new time is older than the existing FirstSeen
            if (opponent.FirstSeen > time) opponent.FirstSeen = time;
            await db.SaveChangesAsync(ct);
            return opponent;
        }

        opponent = new Opponent
        {
            Name = name,
            Race = race,
            FirstSeen = time,
            LastSeen = time
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
            .Include(o => o.TagAssignments).ThenInclude(ta => ta.Tag)
            .Include(o => o.MatchRecords.OrderByDescending(m => m.PlayedAt))
            .FirstOrDefaultAsync(o => o.Id == id, ct);
    }

    public async Task<List<Opponent>> SearchAsync(string? query = null, string? raceFilter = null, string? tagFilter = null, string? modeFilter = null, CancellationToken ct = default)
    {
        var q = db.Opponents
            .Include(o => o.TagAssignments).ThenInclude(ta => ta.Tag)
            .Include(o => o.MatchRecords)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(query))
            q = q.Where(o => EF.Functions.Like(o.Name, $"%{query}%"));

        if (!string.IsNullOrWhiteSpace(raceFilter))
            q = q.Where(o => o.Race == raceFilter);

        if (!string.IsNullOrWhiteSpace(tagFilter))
            q = q.Where(o => o.TagAssignments.Any(ta => ta.Tag.Name == tagFilter));

        if (!string.IsNullOrWhiteSpace(modeFilter))
            q = q.Where(o => o.MatchRecords.Any(m => m.GameMode == modeFilter));

        return await q.OrderByDescending(o => o.LastSeen).ToListAsync(ct);
    }

    public async Task<List<Opponent>> GetRecentAsync(int count = 10, CancellationToken ct = default)
    {
        return await db.Opponents
            .Include(o => o.TagAssignments).ThenInclude(ta => ta.Tag)
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
        var opponent = await db.Opponents.Include(o => o.TagAssignments).ThenInclude(ta => ta.Tag).FirstOrDefaultAsync(o => o.Id == opponentId, ct);
        if (opponent is null) return;

        var tag = await db.Tags.FirstOrDefaultAsync(t => t.Name == tagName, ct);
        if (tag is null)
        {
            tag = new OpponentTag { Name = tagName };
            db.Tags.Add(tag);
        }

        var assignment = opponent.TagAssignments.FirstOrDefault(ta => ta.Tag.Name == tagName);
        if (assignment is null)
        {
            opponent.TagAssignments.Add(new OpponentTagAssignment { Tag = tag, Count = 1 });
            await db.SaveChangesAsync(ct);
        }
        else
        {
            assignment.Count++;
            await db.SaveChangesAsync(ct);
        }
    }

    public async Task RemoveTagAsync(int opponentId, string tagName, CancellationToken ct = default)
    {
        var opponent = await db.Opponents.Include(o => o.TagAssignments).ThenInclude(ta => ta.Tag).FirstOrDefaultAsync(o => o.Id == opponentId, ct);
        if (opponent is null) return;

        var assignment = opponent.TagAssignments.FirstOrDefault(ta => ta.Tag.Name == tagName);
        if (assignment is not null)
        {
            opponent.TagAssignments.Remove(assignment);
            await db.SaveChangesAsync(ct);
        }
    }

    public async Task<List<OpponentTag>> GetAllTagsAsync(CancellationToken ct = default)
    {
        return await db.Tags.OrderBy(t => t.Name).ToListAsync(ct);
    }

    public async Task<MatchRecord> RecordMatchAsync(int opponentId, MatchResult result, string? mapName = null, string? myRace = null, string? opponentRace = null, string? gameMode = null, DateTime? playedAt = null, Action<MatchRecord>? updateStats = null, CancellationToken ct = default)
    {
        var recordTime = playedAt ?? DateTime.UtcNow;
        var record = new MatchRecord
        {
            OpponentId = opponentId,
            Result = result,
            MapName = mapName,
            MyRace = myRace,
            OpponentRace = opponentRace,
            GameMode = gameMode,
            PlayedAt = recordTime
        };
        
        updateStats?.Invoke(record);

        db.MatchRecords.Add(record);

        var opponent = await db.Opponents.FindAsync([opponentId], ct);
        if (opponent is not null)
        {
            if (opponent.LastSeen < recordTime)
            {
                opponent.LastSeen = recordTime;
            }
            if (opponentRace is not null) opponent.Race = opponentRace;
        }

        await db.SaveChangesAsync(ct);
        return record;
    }

    public async Task<MatchRecord?> GetMatchByIdAsync(int matchId, CancellationToken ct = default)
    {
        return await db.MatchRecords
            .Include(m => m.Opponent)
            .FirstOrDefaultAsync(m => m.Id == matchId, ct);
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

    public async Task WipeDatabaseAsync(CancellationToken ct = default)
    {
        await db.Opponents.ExecuteDeleteAsync(ct);
        await db.MatchRecords.ExecuteDeleteAsync(ct);
        await db.Notes.ExecuteDeleteAsync(ct);
        await db.Tags.ExecuteDeleteAsync(ct);
    }
}
