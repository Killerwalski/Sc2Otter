namespace Sc2Otter.Core.Interfaces;

using Sc2Otter.Core.Models;

public interface IOpponentRepository
{
    Task<Opponent?> FindByNameAsync(string name, CancellationToken ct = default);
    Task<Opponent> GetOrCreateAsync(string name, string? race, DateTime? seenAt = null, CancellationToken ct = default);
    Task<Opponent?> GetByIdAsync(int id, CancellationToken ct = default);
    Task<Opponent?> GetWithDetailsAsync(int id, CancellationToken ct = default);
    Task<List<Opponent>> SearchAsync(string? query = null, string? raceFilter = null, string? tagFilter = null, CancellationToken ct = default);
    Task<List<Opponent>> GetRecentAsync(int count = 10, CancellationToken ct = default);
    Task<OpponentNote> AddNoteAsync(int opponentId, string content, string source = "keyboard", CancellationToken ct = default);
    Task UpdateNoteAsync(int noteId, string content, CancellationToken ct = default);
    Task DeleteNoteAsync(int noteId, CancellationToken ct = default);
    Task AddTagAsync(int opponentId, string tagName, CancellationToken ct = default);
    Task RemoveTagAsync(int opponentId, string tagName, CancellationToken ct = default);
    Task<List<OpponentTag>> GetAllTagsAsync(CancellationToken ct = default);
    Task<MatchRecord> RecordMatchAsync(int opponentId, MatchResult result, string? mapName = null, string? myRace = null, string? opponentRace = null, string? gameMode = null, DateTime? playedAt = null, CancellationToken ct = default);
    Task<(int TotalGames, int Wins, int Losses)> GetStatsAsync(int opponentId, CancellationToken ct = default);
    Task WipeDatabaseAsync(CancellationToken ct = default);
}
