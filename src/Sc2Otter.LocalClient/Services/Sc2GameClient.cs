namespace Sc2Otter.LocalClient.Services;

using System.Text.Json;
using Sc2Otter.Core.Interfaces;
using Sc2Otter.Core.Models;

public class Sc2GameClient(HttpClient httpClient, ILogger<Sc2GameClient> logger) : ISc2GameClient
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public async Task<Sc2GameResponse?> GetGameInfoAsync(CancellationToken ct = default)
    {
        try
        {
            var response = await httpClient.GetAsync("/game", ct);
            response.EnsureSuccessStatusCode();
            var json = await response.Content.ReadAsStringAsync(ct);
            return JsonSerializer.Deserialize<Sc2GameResponse>(json, JsonOptions);
        }
        catch (HttpRequestException)
        {
            return null;
        }
        catch (TaskCanceledException)
        {
            return null;
        }
        catch (JsonException ex)
        {
            logger.LogWarning(ex, "Failed to deserialize SC2 game response");
            return null;
        }
    }

    public async Task<Sc2UiResponse?> GetUiStateAsync(CancellationToken ct = default)
    {
        try
        {
            var response = await httpClient.GetAsync("/ui", ct);
            response.EnsureSuccessStatusCode();
            var json = await response.Content.ReadAsStringAsync(ct);
            return JsonSerializer.Deserialize<Sc2UiResponse>(json, JsonOptions);
        }
        catch (HttpRequestException)
        {
            return null;
        }
        catch (TaskCanceledException)
        {
            return null;
        }
        catch (JsonException ex)
        {
            logger.LogWarning(ex, "Failed to deserialize SC2 UI response");
            return null;
        }
    }

    public async Task<bool> IsGameRunningAsync(CancellationToken ct = default)
    {
        try
        {
            var response = await httpClient.GetAsync("/game", ct);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }
}
