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

    public async Task<Opponent?> FindByToonHandleOrNameAsync(string name, string? toonHandle, string? race = null, CancellationToken ct = default)
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
        // All query params must be URL-encoded to handle names with spaces, special chars, etc.
        var url = $"/api/opponents/search?query={Uri.EscapeDataString(query ?? "")}&raceFilter={Uri.EscapeDataString(raceFilter ?? "")}";
        return await http.GetFromJsonAsync<List<Opponent>>(url, ct) ?? [];
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
        // Fixed: was missing the leading '/' which caused requests to resolve relative to base address incorrectly.
        return await http.GetFromJsonAsync<List<OpponentTag>>("/api/tags", ct) ?? [];
    }

    public async Task<bool> IsMatchAlreadyAnalyzedAsync(int opponentId, DateTime playedAt, CancellationToken ct = default)
    {
        // For LocalOtter we fetch the opponent details and check client-side since there is no
        // dedicated server endpoint for this check. This is only called during bulk scan so the
        // extra round-trip is acceptable.
        try
        {
            var opp = await GetWithDetailsAsync(opponentId, ct);
            if (opp == null) return false;

            var exactDuplicate = opp.MatchRecords.FirstOrDefault(m => m.PlayedAt == playedAt);
            if (exactDuplicate != null && exactDuplicate.FullMatchData != null) return true;

            var fuzzy = opp.MatchRecords.FirstOrDefault(m =>
                m.FullMatchData != null && Math.Abs((playedAt - m.PlayedAt).TotalMinutes) < 60);
            if (fuzzy != null) return true;
        }
        catch
        {
            // Ignore connectivity errors — safest to proceed and let the server deduplicate.
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

    public Task<MatchRecord?> GetMatchByIdAsync(int matchId, CancellationToken ct = default)
    {
        // Not implemented — LocalOtter never needs to fetch a single match by ID directly.
        // Match detail is accessed server-side via the Blazor UI.
        return Task.FromResult<MatchRecord?>(null);
    }

    public Task<List<MatchRecord>> GetMatchesByDateAsync(DateTime playedAt, CancellationToken ct = default)
    {
        return Task.FromResult(new List<MatchRecord>());
    }

    public async Task DeleteOpponentAsync(int opponentId, CancellationToken ct = default)
    {
        var response = await http.DeleteAsync($"/api/opponents/{opponentId}", ct);
        response.EnsureSuccessStatusCode();
    }

    public async Task<(int TotalGames, int Wins, int Losses)> GetStatsAsync(int opponentId, CancellationToken ct = default)
    {
        return await http.GetFromJsonAsync<(int, int, int)>($"/api/opponents/{opponentId}/stats", ct);
    }

    public Task WipeDatabaseAsync(CancellationToken ct = default)
    {
        // Wipe is only available via the server-side admin UI — not exposed to LocalOtter.
        return Task.CompletedTask;
    }
}
