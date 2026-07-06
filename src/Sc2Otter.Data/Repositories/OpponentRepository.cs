namespace Sc2Otter.Data.Repositories;

using Microsoft.EntityFrameworkCore;
using Sc2Otter.Core.Interfaces;
using Sc2Otter.Core.Models;

public class OpponentRepository(ScoutDbContext db, ICurrentUserService currentUserService) : IOpponentRepository
{
    private int UserId => currentUserService.UserId ?? throw new UnauthorizedAccessException("User not authenticated.");
    public async Task<Opponent?> FindByNameAsync(string name, CancellationToken ct = default)
    {
        return await db.Opponents
            .Include(o => o.TagAssignments).ThenInclude(ta => ta.Tag)
            .FirstOrDefaultAsync(o => o.UserId == UserId && EF.Functions.Like(o.Name, name), ct);
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
            UserId = UserId,
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
        return await db.Opponents.FirstOrDefaultAsync(o => o.UserId == UserId && o.Id == id, ct);
    }

    public async Task<Opponent?> GetWithDetailsAsync(int id, CancellationToken ct = default)
    {
        return await db.Opponents
            .Include(o => o.Notes.OrderByDescending(n => n.CreatedAt))
            .Include(o => o.MatchRecords.OrderByDescending(m => m.PlayedAt).Take(5))
            .Include(o => o.TagAssignments).ThenInclude(ta => ta.Tag)
            .FirstOrDefaultAsync(o => o.UserId == UserId && o.Id == id, ct);
    }

    public async Task UpdateOpponentAsync(Opponent opponent, CancellationToken ct = default)
    {
        if (opponent.UserId != UserId) throw new UnauthorizedAccessException();
        db.Opponents.Update(opponent);
        await db.SaveChangesAsync(ct);
    }

    public async Task<List<Opponent>> SearchAsync(string? query = null, string? raceFilter = null, string? tagFilter = null, string? modeFilter = null, CancellationToken ct = default)
    {
        var q = db.Opponents
            .Where(o => o.UserId == UserId)
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
            .Where(o => o.UserId == UserId)
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
        var note = await db.Notes.Include(n => n.Opponent).FirstOrDefaultAsync(n => n.Id == noteId && n.Opponent.UserId == UserId, ct);
        if (note is null) return;
        note.Content = content;
        note.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
    }

    public async Task DeleteNoteAsync(int noteId, CancellationToken ct = default)
    {
        var note = await db.Notes.Include(n => n.Opponent).FirstOrDefaultAsync(n => n.Id == noteId && n.Opponent.UserId == UserId, ct);
        if (note is null) return;
        db.Notes.Remove(note);
        await db.SaveChangesAsync(ct);
    }

    public async Task AddTagAsync(int opponentId, string tagName, CancellationToken ct = default)
    {
        var opponent = await db.Opponents.Include(o => o.TagAssignments).ThenInclude(ta => ta.Tag).FirstOrDefaultAsync(o => o.UserId == UserId && o.Id == opponentId, ct);
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
        var opponent = await db.Opponents.Include(o => o.TagAssignments).ThenInclude(ta => ta.Tag).FirstOrDefaultAsync(o => o.UserId == UserId && o.Id == opponentId, ct);
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

    public async Task<MatchRecord> RecordMatchAsync(int opponentId, RecordMatchRequest req, CancellationToken ct = default)
    {
        var match = new MatchRecord
        {
            OpponentId = opponentId,
            Result = req.Result,
            MapName = req.MapName,
            MyRace = req.MyRace,
            OpponentRace = req.OpponentRace,
            GameMode = req.GameMode,
            PlayedAt = req.PlayedAt ?? DateTime.UtcNow,
            FullMatchData = req.FullMatchData,
            MyUnitsMade = req.MyUnitsMade,
            MyWorkersCreated = req.MyWorkersCreated,
            MySupplyBlockTime = req.MySupplyBlockTime,
            MyAvgUnspentMinerals = req.MyAvgUnspentMinerals,
            MyAvgMineralIncome = req.MyAvgMineralIncome,
            OpponentUnitsMade = req.OpponentUnitsMade,
            OpponentWorkersCreated = req.OpponentWorkersCreated,
            OpponentSupplyBlockTime = req.OpponentSupplyBlockTime,
            OpponentAvgUnspentMinerals = req.OpponentAvgUnspentMinerals,
            OpponentAvgMineralIncome = req.OpponentAvgMineralIncome
        };
        

        db.MatchRecords.Add(match);

        var opponent = await db.Opponents.FirstOrDefaultAsync(o => o.UserId == UserId && o.Id == opponentId, ct);
        if (opponent is not null)
        {
            if (req.PlayedAt > opponent.LastSeen)
            {
                opponent.LastSeen = req.PlayedAt ?? DateTime.UtcNow;
                opponent.Race = req.OpponentRace ?? opponent.Race;
            }
            db.Opponents.Update(opponent);
        }

        await db.SaveChangesAsync(ct);
        return match;
    }

    public async Task<MatchRecord?> GetMatchByIdAsync(int matchId, CancellationToken ct = default)
    {
        return await db.MatchRecords
            .Include(m => m.Opponent)
            .FirstOrDefaultAsync(m => m.Id == matchId && m.Opponent.UserId == UserId, ct);
    }

    public async Task<(int TotalGames, int Wins, int Losses)> GetStatsAsync(int opponentId, CancellationToken ct = default)
    {
        var records = await db.MatchRecords.Where(m => m.OpponentId == opponentId && m.Opponent.UserId == UserId).ToListAsync(ct);
        return (
            records.Count,
            records.Count(r => r.Result == MatchResult.Win),
            records.Count(r => r.Result == MatchResult.Loss)
        );
    }

    public async Task WipeDatabaseAsync(CancellationToken ct = default)
    {
        await db.Opponents.Where(o => o.UserId == UserId).ExecuteDeleteAsync(ct);
    }
}
