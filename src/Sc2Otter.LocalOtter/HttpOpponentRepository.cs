namespace Sc2Otter.LocalOtter.Services;

using Sc2Otter.Core.Interfaces;
using Sc2Otter.Core.Models;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;

public class HttpOpponentRepository(HttpClient http) : IOpponentRepository
{
    public async Task<Opponent?> FindByNameAsync(string name, CancellationToken ct = default)
    {
        return await http.GetFromJsonAsync<Opponent>($"/api/opponents/search?query={Uri.EscapeDataString(name)}", ct);
    }

    public async Task<Opponent?> FindByToonHandleOrNameAsync(string name, string? toonHandle, CancellationToken ct = default)
    {
        return await FindByNameAsync(name, ct);
    }

    public async Task<Opponent> GetOrCreateAsync(string name, string? toonHandle, string? race, DateTime? seenAt = null, CancellationToken ct = default)
    {
        var url = $"/api/opponents/get-or-create?name={Uri.EscapeDataString(name)}&race={Uri.EscapeDataString(race ?? "")}";
        if (!string.IsNullOrEmpty(toonHandle)) url += $"&toonHandle={Uri.EscapeDataString(toonHandle)}";
        if (seenAt.HasValue)
        {
            var utcTime = seenAt.Value.Kind == DateTimeKind.Utc ? seenAt.Value : DateTime.SpecifyKind(seenAt.Value, DateTimeKind.Utc);
            url += $"&seenAt={Uri.EscapeDataString(utcTime.ToString("o"))}";
        }
        var response = await http.GetAsync(url, ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<Opponent>(cancellationToken: ct) ?? throw new Exception("Failed to deserialize");
    }

    public async Task<Opponent?> GetByIdAsync(int id, CancellationToken ct = default)
    {
        return await http.GetFromJsonAsync<Opponent>($"/api/opponents/{id}", ct);
    }

    public async Task<Opponent?> GetWithDetailsAsync(int id, CancellationToken ct = default)
    {
        return await http.GetFromJsonAsync<Opponent>($"/api/opponents/{id}/details", ct);
    }

    public async Task UpdateOpponentAsync(Opponent opponent, CancellationToken ct = default)
    {
        var response = await http.PutAsJsonAsync($"/api/opponents/{opponent.Id}", opponent, ct);
        response.EnsureSuccessStatusCode();
    }

    public async Task<List<Opponent>> SearchAsync(string? query = null, string? raceFilter = null, string? tagFilter = null, string? modeFilter = null, CancellationToken ct = default)
    {
        return await http.GetFromJsonAsync<List<Opponent>>($"/api/opponents/search?query={query}&raceFilter={raceFilter}", ct) ?? [];
    }

    public async Task<List<Opponent>> GetRecentAsync(int count = 10, CancellationToken ct = default)
    {
        return await http.GetFromJsonAsync<List<Opponent>>($"/api/opponents/recent?count={count}", ct) ?? [];
    }

    public async Task<OpponentNote> AddNoteAsync(int opponentId, string content, string source = "keyboard", int? matchRecordId = null, List<string>? autoTags = null, CancellationToken ct = default)
    {
        var req = new AddNoteRequest { Content = content, Source = source, MatchRecordId = matchRecordId, AutoTags = autoTags ?? new() };
        var res = await http.PostAsJsonAsync($"/api/opponents/{opponentId}/notes", req, ct);
        if (!res.IsSuccessStatusCode)
        {
            var err = await res.Content.ReadAsStringAsync(ct);
            throw new Exception($"Server returned {res.StatusCode}: {err}");
        }
        return (await res.Content.ReadFromJsonAsync<OpponentNote>(cancellationToken: ct))!;
    }

    public async Task UpdateNoteAsync(int noteId, string content, CancellationToken ct = default)
    {
        var req = new UpdateNoteRequest { Content = content };
        await http.PutAsJsonAsync($"/api/opponents/notes/{noteId}", req, ct);
    }

    public async Task DeleteNoteAsync(int noteId, CancellationToken ct = default)
    {
        await http.DeleteAsync($"/api/opponents/notes/{noteId}", ct);
    }

    public async Task AddTagAsync(int opponentId, string tagName, CancellationToken ct = default)
    {
        var req = new AddTagRequest { TagName = tagName };
        await http.PostAsJsonAsync($"/api/opponents/{opponentId}/tags", req, ct);
    }

    public async Task RemoveTagAsync(int opponentId, string tagName, CancellationToken ct = default)
    {
        await http.DeleteAsync($"/api/opponents/{opponentId}/tags/{Uri.EscapeDataString(tagName)}", ct);
    }

    public async Task<List<OpponentTag>> GetAllTagsAsync(CancellationToken ct = default)
    {
        return await http.GetFromJsonAsync<List<OpponentTag>>("api/tags", ct) ?? [];
    }

    public async Task<bool> IsMatchAlreadyAnalyzedAsync(int opponentId, DateTime playedAt, CancellationToken ct = default)
    {
        // For LocalOtter, we could query the server, but the easiest way is to just fetch the opponent's recent matches
        // and see if there's a duplicate. Alternatively we could add a dedicated endpoint.
        // For now, we can just fetch the MatchRecords if they exist, or since it's just to prevent double-tagging on bulk scan,
        // we can fetch the opponent details.
        try
        {
            var opp = await GetWithDetailsAsync(opponentId, ct);
            if (opp == null) return false;
            
            var exactDuplicate = opp.MatchRecords.FirstOrDefault(m => m.PlayedAt == playedAt);
            if (exactDuplicate != null && exactDuplicate.FullMatchData != null) return true;
            
            var fuzzy = opp.MatchRecords.FirstOrDefault(m => m.FullMatchData != null && Math.Abs((playedAt - m.PlayedAt).TotalMinutes) < 60);
            if (fuzzy != null) return true;
        }
        catch
        {
            // Ignore
        }
        return false;
    }

    public async Task<MatchRecord> RecordMatchAsync(int opponentId, RecordMatchRequest req, CancellationToken ct = default)
    {
        var res = await http.PostAsJsonAsync($"/api/opponents/{opponentId}/matches", req, ct);
        if (!res.IsSuccessStatusCode)
        {
            var err = await res.Content.ReadAsStringAsync(ct);
            throw new Exception($"Server returned {res.StatusCode}: {err}");
        }
        return await res.Content.ReadFromJsonAsync<MatchRecord>(cancellationToken: ct) ?? throw new Exception();
    }
    
    public async Task<MatchRecord?> GetMatchByIdAsync(int matchId, CancellationToken ct = default)
    {
        return null;
    }

    public async Task<(int TotalGames, int Wins, int Losses)> GetStatsAsync(int opponentId, CancellationToken ct = default)
    {
        return await http.GetFromJsonAsync<(int, int, int)>($"/api/opponents/{opponentId}/stats", ct);
    }

    public async Task WipeDatabaseAsync(CancellationToken ct = default)
    {
    }
}
