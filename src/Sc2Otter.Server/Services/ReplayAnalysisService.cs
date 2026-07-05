namespace Sc2Otter.Server.Services;

using System.Diagnostics;
using System.Text.Json;
using Sc2Otter.Core.Interfaces;
using Sc2Otter.Core.Models;

public class ReplayAnalysisService(
    IServiceScopeFactory scopeFactory,
    SettingsService settingsService,
    ILogger<ReplayAnalysisService> logger)
{
    private readonly string _pythonScriptPath = Path.Combine(AppContext.BaseDirectory, "PythonSidecar", "replay_analyzer.py");

    public async Task<bool> AnalyzeReplayAsync(string replayPath, CancellationToken ct = default)
    {
        if (!File.Exists(replayPath))
        {
            logger.LogWarning("Replay file not found: {Path}", replayPath);
            return false;
        }

        var myName = settingsService.Current.MySc2Name;
        logger.LogInformation("Starting Python sidecar analysis for: {ReplayPath}", replayPath);

        try
        {
            var processStartInfo = new ProcessStartInfo
            {
                FileName = "python",
                ArgumentList = { _pythonScriptPath, replayPath },
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(processStartInfo);
            if (process == null)
            {
                logger.LogError("Failed to start python process.");
                return false;
            }

            var outputTask = process.StandardOutput.ReadToEndAsync(ct);
            var errorTask = process.StandardError.ReadToEndAsync(ct);

            await process.WaitForExitAsync(ct);
            
            var output = await outputTask;
            var error = await errorTask;

            if (process.ExitCode != 0)
            {
                logger.LogError("Python script failed with exit code {Code}. Error: {Error}", process.ExitCode, error);
                return false;
            }

            // Parse output
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var result = JsonSerializer.Deserialize<ReplayAnalysisResult>(output, options);

            if (result == null || !result.Success)
            {
                logger.LogError("Python script returned failure: {Error}", result?.Error);
                return false;
            }

            if (result.Data != null)
            {
                using var scope = scopeFactory.CreateScope();
                var repo = scope.ServiceProvider.GetRequiredService<IOpponentRepository>();
                
                var namesToIgnore = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                if (!string.IsNullOrWhiteSpace(myName))
                {
                    foreach (var name in myName.Split(',', StringSplitOptions.RemoveEmptyEntries))
                    {
                        namesToIgnore.Add(name.Trim());
                    }
                }

                foreach (var playerResult in result.Data)
                {
                    if (namesToIgnore.Contains(playerResult.Name))
                    {
                        logger.LogInformation("Skipping analysis save for ignored player: {Name}", playerResult.Name);
                        continue;
                    }

                    var opponent = await repo.GetOrCreateAsync(playerResult.Name, playerResult.Race, result.StartTime, ct);
                    
                    foreach (var tag in playerResult.Tags)
                    {
                        await repo.AddTagAsync(opponent.Id, tag, ct);
                        logger.LogInformation("Added tag '{Tag}' to {Player}", tag, playerResult.Name);
                    }
                    
                    foreach (var note in playerResult.Notes)
                    {
                        // Add some context to the note
                        var fullNote = $"[Auto-Replay] {note}";
                        await repo.AddNoteAsync(opponent.Id, fullNote, "replay", ct);
                        logger.LogInformation("Added note to {Player}: {Note}", playerResult.Name, fullNote);
                    }
                    
                    if (!string.IsNullOrWhiteSpace(result.GameMode) && result.GameMode != "1v1" && !result.GameMode.EndsWith("p"))
                    {
                        await repo.AddTagAsync(opponent.Id, result.GameMode, ct);
                    }
                    
                    var ourResult = MatchResult.Unknown;
                    if (!string.IsNullOrWhiteSpace(playerResult.Result))
                    {
                        var opponentWon = playerResult.Result.Equals("Win", StringComparison.OrdinalIgnoreCase);
                        ourResult = opponentWon ? MatchResult.Loss : MatchResult.Win;
                    }
                    
                    await repo.RecordMatchAsync(opponent.Id, ourResult, result.MapName, null, playerResult.Race, result.GameMode, result.StartTime, ct);
                }
                return true;
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error executing replay analysis");
        }
        return false;
    }

    private class ReplayAnalysisResult
    {
        public bool Success { get; set; }
        public string? Error { get; set; }
        public string? MapName { get; set; }
        public DateTime? StartTime { get; set; }
        public string? GameMode { get; set; }
        public List<PlayerAnalysisResult>? Data { get; set; }
    }

    private class PlayerAnalysisResult
    {
        public string Name { get; set; } = "";
        public string Race { get; set; } = "";
        public string? Result { get; set; }
        public List<string> Tags { get; set; } = [];
        public List<string> Notes { get; set; } = [];
    }
}
