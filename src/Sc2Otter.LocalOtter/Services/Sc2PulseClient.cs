namespace Sc2Otter.LocalOtter.Services;

using System.Text.Json;
using System.Text.Json.Serialization;

public class Sc2PulseClient(HttpClient httpClient, ILogger<Sc2PulseClient> logger)
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public async Task<(int? Mmr, string? League)> GetOpponentMmrAsync(string opponentName, CancellationToken ct = default)
    {
        try
        {
            var response = await httpClient.GetAsync($"https://sc2pulse.nephest.com/sc2/api/character/search?term={Uri.EscapeDataString(opponentName)}", ct);
            if (!response.IsSuccessStatusCode) return (null, null);

            var json = await response.Content.ReadAsStringAsync(ct);
            var results = JsonSerializer.Deserialize<List<Sc2PulseResult>>(json, JsonOptions);
            
            if (results == null || results.Count == 0) return (null, null);

            // Best effort matching: pick the most active player with this name
            var bestMatch = results.OrderByDescending(r => r.TotalGamesPlayed).FirstOrDefault();
            
            if (bestMatch != null && bestMatch.RatingMax.HasValue && bestMatch.LeagueMax.HasValue)
            {
                var league = bestMatch.LeagueMax.Value switch
                {
                    0 => "Bronze",
                    1 => "Silver",
                    2 => "Gold",
                    3 => "Platinum",
                    4 => "Diamond",
                    5 => "Master",
                    6 => "Grandmaster",
                    _ => "Unknown"
                };
                
                return (bestMatch.RatingMax, league);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to fetch MMR from SC2Pulse for {OpponentName}", opponentName);
        }

        return (null, null);
    }
}

public class Sc2PulseResult
{
    [JsonPropertyName("leagueMax")]
    public int? LeagueMax { get; set; }
    
    [JsonPropertyName("ratingMax")]
    public int? RatingMax { get; set; }
    
    [JsonPropertyName("totalGamesPlayed")]
    public int? TotalGamesPlayed { get; set; }
}
