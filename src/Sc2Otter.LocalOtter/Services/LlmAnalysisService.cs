using System;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Sc2Otter.LocalOtter.Services;

public class PlaystyleSummary
{
    [JsonPropertyName("archetype")]
    public string Archetype { get; set; } = string.Empty;
    
    [JsonPropertyName("summary")]
    public string Summary { get; set; } = string.Empty;
}

public class LlmAnalysisService
{
    private readonly HttpClient _httpClient;
    private readonly SettingsService _settingsService;
    private readonly ILogger<LlmAnalysisService> _logger;

    private const string SystemPrompt = 
        "You are a Grandmaster StarCraft 2 coach. Analyze the provided game telemetry JSON (economy intervals and army lost per minute). " +
        "Output ONLY a valid JSON object with two fields: " +
        "'archetype': A 2-3 word classification (e.g., 'Macro Defensive', '1-Base All-In', 'Heavy Harass', 'Standard Macro'). " +
        "'summary': A 1-2 sentence pragmatic summary of how this player played this game based on the stats.";

    public LlmAnalysisService(HttpClient httpClient, SettingsService settingsService, ILogger<LlmAnalysisService> logger)
    {
        _httpClient = httpClient;
        _settingsService = settingsService;
        _logger = logger;
    }

    private static readonly SemaphoreSlim _rateLimitSemaphore = new SemaphoreSlim(1, 1);
    private static DateTime _lastRequestTime = DateTime.MinValue;

    public async Task<PlaystyleSummary?> AnalyzeTelemetryAsync(string telemetryJson, CancellationToken ct = default)
    {
        var settings = _settingsService.Current;
        if (!settings.AiEnabled || string.IsNullOrWhiteSpace(settings.AiApiKey))
        {
            return null;
        }

        var provider = settings.AiProvider?.ToLowerInvariant() ?? "";
        int maxRetries = 5;
        int delayMs = 15000;

        for (int i = 0; i < maxRetries; i++)
        {
            try
            {
                await _rateLimitSemaphore.WaitAsync(ct);
                try
                {
                    // Enforce a minimum of 4.2 seconds between any LLM requests (approx 14 requests per minute)
                    var timeSinceLast = DateTime.UtcNow - _lastRequestTime;
                    var minDelay = TimeSpan.FromSeconds(4.2);
                    if (timeSinceLast < minDelay)
                    {
                        await Task.Delay(minDelay - timeSinceLast, ct);
                    }
                    _lastRequestTime = DateTime.UtcNow;
                }
                finally
                {
                    _rateLimitSemaphore.Release();
                }

                return provider switch
                {
                    "openai" => await CallOpenAiAsync(settings, telemetryJson, ct),
                    "gemini" => await CallGeminiAsync(settings, telemetryJson, ct),
                    "claude" => await CallClaudeAsync(settings, telemetryJson, ct),
                    _ => null
                };
            }
            catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
            {
                if (i == maxRetries - 1)
                {
                    _logger.LogWarning("Failed to analyze telemetry using LLM API after multiple retries due to rate limiting.");
                    return null;
                }
                
                _logger.LogWarning($"Rate limited by LLM API (429). Retrying in {delayMs / 1000} seconds...");
                await Task.Delay(delayMs, ct);
                delayMs *= 2; // Exponential backoff (15s, 30s, 60s, 120s)
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Failed to analyze telemetry using LLM API: {Message}", ex.Message);
                return null;
            }
        }
        return null;
    }

    private async Task<PlaystyleSummary?> CallOpenAiAsync(UserSettings settings, string telemetry, CancellationToken ct)
    {
        var model = string.IsNullOrWhiteSpace(settings.AiModel) ? "gpt-4o-mini" : settings.AiModel.Trim();
        var requestBody = new
        {
            model = model,
            messages = new[]
            {
                new { role = "system", content = SystemPrompt },
                new { role = "user", content = telemetry }
            },
            response_format = new { type = "json_object" }
        };

        var request = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/chat/completions")
        {
            Content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json")
        };
        request.Headers.Add("Authorization", $"Bearer {settings.AiApiKey?.Trim()}");

        var response = await _httpClient.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(ct);
            _logger.LogWarning($"OpenAI API returned {response.StatusCode}: {errorBody}");
            response.EnsureSuccessStatusCode();
        }

        var responseJson = await response.Content.ReadAsStringAsync(ct);
        var jsonDoc = JsonDocument.Parse(responseJson);
        var content = jsonDoc.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString();
        
        return JsonSerializer.Deserialize<PlaystyleSummary>(content ?? "{}");
    }

    private async Task<PlaystyleSummary?> CallGeminiAsync(UserSettings settings, string telemetry, CancellationToken ct)
    {
        var model = string.IsNullOrWhiteSpace(settings.AiModel) ? "gemini-2.0-flash" : settings.AiModel.Trim();
        if (model == "gemini-1.5-flash" || model == "gpt-1.5flash") 
        {
            model = "gemini-2.0-flash"; // Auto-upgrade deprecated or invalid config values
        }
        
        var url = $"https://generativelanguage.googleapis.com/v1beta/models/{model}:generateContent?key={settings.AiApiKey?.Trim()}";

        var requestBody = new
        {
            system_instruction = new
            {
                parts = new[] { new { text = SystemPrompt } }
            },
            contents = new[]
            {
                new { parts = new[] { new { text = telemetry } } }
            },
            generationConfig = new { response_mime_type = "application/json" }
        };

        var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json")
        };

        var response = await _httpClient.SendAsync(request, ct);
        
        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(ct);
            _logger.LogWarning($"Gemini API returned {response.StatusCode}: {errorBody}");
            response.EnsureSuccessStatusCode();
        }
        var responseJson = await response.Content.ReadAsStringAsync(ct);
        var jsonDoc = JsonDocument.Parse(responseJson);
        var content = jsonDoc.RootElement.GetProperty("candidates")[0].GetProperty("content").GetProperty("parts")[0].GetProperty("text").GetString();

        return JsonSerializer.Deserialize<PlaystyleSummary>(content ?? "{}");
    }

    private async Task<PlaystyleSummary?> CallClaudeAsync(UserSettings settings, string telemetry, CancellationToken ct)
    {
        var model = string.IsNullOrWhiteSpace(settings.AiModel) ? "claude-3-haiku-20240307" : settings.AiModel.Trim();
        var requestBody = new
        {
            model = model,
            max_tokens = 1000,
            system = SystemPrompt,
            messages = new[]
            {
                new { role = "user", content = telemetry }
            }
        };

        var request = new HttpRequestMessage(HttpMethod.Post, "https://api.anthropic.com/v1/messages")
        {
            Content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json")
        };
        request.Headers.Add("x-api-key", settings.AiApiKey?.Trim());
        request.Headers.Add("anthropic-version", "2023-06-01");

        var response = await _httpClient.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(ct);
            _logger.LogWarning($"Claude API returned {response.StatusCode}: {errorBody}");
            response.EnsureSuccessStatusCode();
        }

        var responseJson = await response.Content.ReadAsStringAsync(ct);
        var jsonDoc = JsonDocument.Parse(responseJson);
        var content = jsonDoc.RootElement.GetProperty("content")[0].GetProperty("text").GetString();

        return JsonSerializer.Deserialize<PlaystyleSummary>(content ?? "{}");
    }
}
