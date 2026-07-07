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

    public async Task<PlaystyleSummary?> AnalyzeTelemetryAsync(string telemetryJson, CancellationToken ct = default)
    {
        var settings = _settingsService.Current;
        if (!settings.AiEnabled || string.IsNullOrWhiteSpace(settings.AiApiKey))
        {
            return null;
        }

        try
        {
            var provider = settings.AiProvider?.ToLowerInvariant() ?? "";
            return provider switch
            {
                "openai" => await CallOpenAiAsync(settings, telemetryJson, ct),
                "gemini" => await CallGeminiAsync(settings, telemetryJson, ct),
                "claude" => await CallClaudeAsync(settings, telemetryJson, ct),
                _ => null
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to analyze telemetry using LLM API.");
            return null;
        }
    }

    private async Task<PlaystyleSummary?> CallOpenAiAsync(UserSettings settings, string telemetry, CancellationToken ct)
    {
        var requestBody = new
        {
            model = string.IsNullOrWhiteSpace(settings.AiModel) ? "gpt-4o-mini" : settings.AiModel,
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
        request.Headers.Add("Authorization", $"Bearer {settings.AiApiKey}");

        var response = await _httpClient.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();

        var responseJson = await response.Content.ReadAsStringAsync(ct);
        var jsonDoc = JsonDocument.Parse(responseJson);
        var content = jsonDoc.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString();
        
        return JsonSerializer.Deserialize<PlaystyleSummary>(content ?? "{}");
    }

    private async Task<PlaystyleSummary?> CallGeminiAsync(UserSettings settings, string telemetry, CancellationToken ct)
    {
        var model = string.IsNullOrWhiteSpace(settings.AiModel) ? "gemini-1.5-flash" : settings.AiModel;
        var url = $"https://generativelanguage.googleapis.com/v1beta/models/{model}:generateContent?key={settings.AiApiKey}";

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
        response.EnsureSuccessStatusCode();

        var responseJson = await response.Content.ReadAsStringAsync(ct);
        var jsonDoc = JsonDocument.Parse(responseJson);
        var content = jsonDoc.RootElement.GetProperty("candidates")[0].GetProperty("content").GetProperty("parts")[0].GetProperty("text").GetString();

        return JsonSerializer.Deserialize<PlaystyleSummary>(content ?? "{}");
    }

    private async Task<PlaystyleSummary?> CallClaudeAsync(UserSettings settings, string telemetry, CancellationToken ct)
    {
        var requestBody = new
        {
            model = string.IsNullOrWhiteSpace(settings.AiModel) ? "claude-3-haiku-20240307" : settings.AiModel,
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
        request.Headers.Add("x-api-key", settings.AiApiKey);
        request.Headers.Add("anthropic-version", "2023-06-01");

        var response = await _httpClient.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();

        var responseJson = await response.Content.ReadAsStringAsync(ct);
        var jsonDoc = JsonDocument.Parse(responseJson);
        var content = jsonDoc.RootElement.GetProperty("content")[0].GetProperty("text").GetString();

        return JsonSerializer.Deserialize<PlaystyleSummary>(content ?? "{}");
    }
}
