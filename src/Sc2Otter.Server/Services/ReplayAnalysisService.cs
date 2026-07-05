namespace Sc2Otter.Server.Services;

using System.Diagnostics;
using System.Text.Json;
using Sc2Otter.Core.Interfaces;

public class ReplayAnalysisService(
    IServiceScopeFactory scopeFactory,
    SettingsService settingsService,
    ILogger<ReplayAnalysisService> logger)
{
    private readonly string _pythonScriptPath = Path.Combine(AppContext.BaseDirectory, "PythonSidecar", "replay_analyzer.py");

    public async Task AnalyzeReplayAsync(string replayPath, CancellationToken ct = default)
    {
        if (!File.Exists(replayPath))
        {
            logger.LogWarning("Replay file not found: {Path}", replayPath);
            return;
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
                return;
            }

            var outputTask = process.StandardOutput.ReadToEndAsync(ct);
            var errorTask = process.StandardError.ReadToEndAsync(ct);

            await process.WaitForExitAsync(ct);
            
            var output = await outputTask;
            var error = await errorTask;

            if (process.ExitCode != 0)
            {
                logger.LogError("Python script failed with exit code {Code}. Error: {Error}", process.ExitCode, error);
                return;
            }

            // Parse output
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var result = JsonSerializer.Deserialize<ReplayAnalysisResult>(output, options);

            if (result == null || !result.Success)
            {
                logger.LogError("Python script returned failure: {Error}", result?.Error);
                return;
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
                    if (playerResult.Tags.Count == 0 && playerResult.Notes.Count == 0) continue;
                    
                    if (namesToIgnore.Contains(playerResult.Name))
                    {
                        logger.LogInformation("Skipping analysis save for ignored player: {Name}", playerResult.Name);
                        continue;
                    }

                    var opponent = await repo.GetOrCreateAsync(playerResult.Name, playerResult.Race, ct);
                    
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
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error executing replay analysis");
        }
    }

    private class ReplayAnalysisResult
    {
        public bool Success { get; set; }
        public string? Error { get; set; }
        public List<PlayerAnalysisResult>? Data { get; set; }
    }

    private class PlayerAnalysisResult
    {
        public string Name { get; set; } = "";
        public string Race { get; set; } = "";
        public List<string> Tags { get; set; } = [];
        public List<string> Notes { get; set; } = [];
    }
}
