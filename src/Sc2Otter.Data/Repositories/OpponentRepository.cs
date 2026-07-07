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
            .FirstOrDefaultAsync(o => o.UserId == UserId && o.Name.ToLower() == name.ToLower(), ct);
    }

    public async Task<Opponent?> FindByToonHandleOrNameAsync(string name, string? toonHandle, CancellationToken ct = default)
    {
        Opponent? opponent = null;
        
        if (!string.IsNullOrWhiteSpace(toonHandle))
        {
            opponent = await db.Opponents
                .Include(o => o.TagAssignments).ThenInclude(ta => ta.Tag)
                .FirstOrDefaultAsync(o => o.UserId == UserId && o.ToonHandle == toonHandle, ct);
        }

        if (opponent is null)
        {
            // Fallback: search by name and race if toonHandle didn't match (e.g. some local queries don't have toonHandle)
            opponent = await db.Opponents
                .Include(o => o.TagAssignments).ThenInclude(ta => ta.Tag)
                .FirstOrDefaultAsync(o => o.UserId == UserId && o.Name.ToLower() == name.ToLower() && (race == null || o.Race == race), ct);
        }

        return opponent;
    }

    public async Task<Opponent> GetOrCreateAsync(string name, string? toonHandle, string? race, DateTime? seenAt = null, CancellationToken ct = default)
    {
        var time = seenAt ?? DateTime.UtcNow;
        var opponent = await FindByToonHandleOrNameAsync(name, toonHandle, ct);
        
        if (opponent is not null)
        {
            if (race is not null) opponent.Race = race;
            // Link toonHandle if we found them by Name and they didn't have one
            if (!string.IsNullOrWhiteSpace(toonHandle) && string.IsNullOrWhiteSpace(opponent.ToonHandle))
            {
                opponent.ToonHandle = toonHandle;
            }

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
            ToonHandle = toonHandle,
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
            .Include(o => o.Notes.OrderByDescending(n => n.CreatedAt)).ThenInclude(n => n.MatchRecord)
            .Include(o => o.MatchRecords.OrderByDescending(m => m.PlayedAt).Take(5))
            .Include(o => o.TagAssignments).ThenInclude(ta => ta.Tag)
            .FirstOrDefaultAsync(o => o.UserId == UserId && o.Id == id, ct);
    }

    public async Task UpdateOpponentAsync(Opponent opponent, CancellationToken ct = default)
    {
        var existing = await db.Opponents.FirstOrDefaultAsync(o => o.Id == opponent.Id && o.UserId == UserId, ct);
        if (existing is null) throw new UnauthorizedAccessException();

        existing.Name = opponent.Name;
        existing.Race = opponent.Race;
        existing.FirstSeen = opponent.FirstSeen;
        existing.LastSeen = opponent.LastSeen;
        existing.Mmr = opponent.Mmr;
        existing.League = opponent.League;

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
            q = q.Where(o => o.Name.ToLower().Contains(query.ToLower()));

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

    public async Task<OpponentNote> AddNoteAsync(int opponentId, string content, string source = "keyboard", int? matchRecordId = null, List<string>? autoTags = null, CancellationToken ct = default)
    {
        var existingNote = await db.Notes.FirstOrDefaultAsync(n => n.OpponentId == opponentId && n.Content == content && n.Source == source, ct);
        if (existingNote != null)
        {
            return existingNote;
        }

        var note = new OpponentNote
        {
            OpponentId = opponentId,
            Content = content,
            Source = source,
            MatchRecordId = matchRecordId,
            AutoTags = autoTags ?? new(),
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

    public async Task<bool> IsMatchAlreadyAnalyzedAsync(int opponentId, DateTime playedAt, CancellationToken ct = default)
    {
        var exactDuplicate = await db.MatchRecords.FirstOrDefaultAsync(m => m.OpponentId == opponentId && m.PlayedAt == playedAt, ct);
        if (exactDuplicate != null && exactDuplicate.FullMatchData != null) return true;
        
        var fuzzy = await db.MatchRecords.FirstOrDefaultAsync(m => m.OpponentId == opponentId && m.FullMatchData != null && Math.Abs((playedAt - m.PlayedAt).TotalMinutes) < 60, ct);
        if (fuzzy != null) return true;
        
        return false;
    }

    public async Task<MatchRecord> RecordMatchAsync(int opponentId, RecordMatchRequest req, CancellationToken ct = default)
    {
        var opponent = await db.Opponents.FirstOrDefaultAsync(o => o.UserId == UserId && o.Id == opponentId, ct);
        if (opponent is null) throw new KeyNotFoundException($"Opponent {opponentId} not found.");

        var playedAt = req.PlayedAt ?? DateTime.UtcNow;

        var exactDuplicate = req.PlayedAt.HasValue 
            ? await db.MatchRecords.FirstOrDefaultAsync(m => m.OpponentId == opponentId && m.PlayedAt == req.PlayedAt.Value, ct)
            : null;

        var existingMatch = exactDuplicate ?? await db.MatchRecords
            .Where(m => m.OpponentId == opponentId && m.FullMatchData == null)
            .OrderByDescending(m => m.PlayedAt)
            .FirstOrDefaultAsync(ct);

        bool isDuplicate = false;
        if (exactDuplicate != null)
        {
            isDuplicate = true;
            existingMatch = exactDuplicate;
        }
        else if (existingMatch != null && req.FullMatchData != null && Math.Abs((playedAt - existingMatch.PlayedAt).TotalMinutes) < 60)
        {
            isDuplicate = true;
        }

        if (isDuplicate && existingMatch != null)
        {
            existingMatch.Result = req.Result != MatchResult.Unknown ? req.Result : existingMatch.Result;
            existingMatch.MapName = req.MapName ?? existingMatch.MapName;
            existingMatch.MyRace = req.MyRace ?? existingMatch.MyRace;
            existingMatch.OpponentRace = req.OpponentRace ?? existingMatch.OpponentRace;
            existingMatch.GameMode = req.GameMode ?? existingMatch.GameMode;
            if (req.PlayedAt.HasValue) existingMatch.PlayedAt = req.PlayedAt.Value;
            
            if (req.FullMatchData != null)
            {
                existingMatch.FullMatchData = req.FullMatchData;
                existingMatch.MyUnitsMade = req.MyUnitsMade;
                existingMatch.MyWorkersCreated = req.MyWorkersCreated;
                existingMatch.MySupplyBlockTime = req.MySupplyBlockTime;
                existingMatch.MyAvgUnspentMinerals = req.MyAvgUnspentMinerals;
                existingMatch.MyAvgMineralIncome = req.MyAvgMineralIncome;
                existingMatch.OpponentUnitsMade = req.OpponentUnitsMade;
                existingMatch.OpponentWorkersCreated = req.OpponentWorkersCreated;
                existingMatch.OpponentSupplyBlockTime = req.OpponentSupplyBlockTime;
                existingMatch.OpponentAvgUnspentMinerals = req.OpponentAvgUnspentMinerals;
                existingMatch.OpponentAvgMineralIncome = req.OpponentAvgMineralIncome;
            }

            if (existingMatch.PlayedAt > opponent.LastSeen)
            {
                opponent.LastSeen = existingMatch.PlayedAt;
                opponent.Race = req.OpponentRace ?? opponent.Race;
            }
            
            db.MatchRecords.Update(existingMatch);
            await db.SaveChangesAsync(ct);
            return existingMatch;
        }

        var match = new MatchRecord
        {
            OpponentId = opponentId,
            Result = req.Result,
            MapName = req.MapName,
            MyRace = req.MyRace,
            OpponentRace = req.OpponentRace,
            GameMode = req.GameMode,
            PlayedAt = playedAt,
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

        if (playedAt > opponent.LastSeen)
        {
            opponent.LastSeen = playedAt;
            opponent.Race = req.OpponentRace ?? opponent.Race;
        }
        db.Opponents.Update(opponent);

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
