namespace Sc2Otter.LocalOtter.Services;

using System.Diagnostics;
using System.Text.Json;
using Sc2Otter.Core.Interfaces;
using Sc2Otter.Core.Models;

public class ReplayAnalysisService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly SettingsService _settingsService;
    private readonly ILogger<ReplayAnalysisService> _logger;
    private readonly string _pythonScriptPath;

    public ReplayAnalysisService(
        IServiceScopeFactory scopeFactory,
        SettingsService settingsService,
        ILogger<ReplayAnalysisService> logger)
    {
        _scopeFactory = scopeFactory;
        _settingsService = settingsService;
        _logger = logger;

        _pythonScriptPath = Path.Combine(Path.GetTempPath(), "replay_analyzer.py");
        using var stream = typeof(ReplayAnalysisService).Assembly.GetManifestResourceStream("Sc2Otter.LocalOtter.PythonSidecar.replay_analyzer.py");
        if (stream != null)
        {
            using var fileStream = File.Create(_pythonScriptPath);
            stream.CopyTo(fileStream);
        }

        try
        {
            var pipProcess = Process.Start(new ProcessStartInfo
            {
                FileName = "python",
                ArgumentList = { "-m", "pip", "install", "sc2reader" },
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            });
            pipProcess?.WaitForExit();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to automatically install sc2reader via pip. Please ensure Python is installed on your system.");
        }
    }

    public async Task<bool> AnalyzeReplayAsync(string replayPath, CancellationToken ct = default)
    {
        if (!File.Exists(replayPath))
        {
            _logger.LogWarning("Replay file not found: {Path}", replayPath);
            return false;
        }

        var myName = _settingsService.Current.MySc2Name;
        _logger.LogInformation("Starting Python sidecar analysis for: {ReplayPath}", replayPath);

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
                _logger.LogError("Failed to start python process.");
                return false;
            }

            var outputTask = process.StandardOutput.ReadToEndAsync(ct);
            var errorTask = process.StandardError.ReadToEndAsync(ct);

            await process.WaitForExitAsync(ct);
            
            var output = await outputTask;
            var error = await errorTask;

            if (process.ExitCode != 0)
            {
                _logger.LogError("Python script failed with exit code {Code}. Error: {Error}", process.ExitCode, error);
                return false;
            }

            // Parse output
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var result = JsonSerializer.Deserialize<ReplayAnalysisResult>(output, options);

            if (result != null && result.StartTime.HasValue && result.StartTime.Value.Kind == DateTimeKind.Unspecified)
            {
                result.StartTime = DateTime.SpecifyKind(result.StartTime.Value, DateTimeKind.Utc);
            }

            if (result == null || !result.Success)
            {
                _logger.LogError("Python script returned failure: {Error}", result?.Error);
                return false;
            }

            if (result.Data != null)
            {
                using var scope = _scopeFactory.CreateScope();
                var repo = scope.ServiceProvider.GetRequiredService<IOpponentRepository>();
                
                var namesToIgnore = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                if (!string.IsNullOrWhiteSpace(myName))
                {
                    foreach (var name in myName.Split(',', StringSplitOptions.RemoveEmptyEntries))
                    {
                        namesToIgnore.Add(name.Trim());
                    }
                }

                var myResult = result.Data.FirstOrDefault(p => namesToIgnore.Contains(p.Name));
                if (myResult != null)
                {
                    myResult.IsMe = true;
                }

                foreach (var playerResult in result.Data)
                {
                    if (namesToIgnore.Contains(playerResult.Name))
                    {
                        _logger.LogInformation("Skipping analysis save for ignored player: {Name}", playerResult.Name);
                        continue;
                    }

                    var opponent = await repo.GetOrCreateAsync(playerResult.Name, playerResult.Race, result.StartTime, ct);
                    
                    foreach (var tag in playerResult.Tags)
                    {
                        await repo.AddTagAsync(opponent.Id, tag, ct);
                        _logger.LogInformation("Added tag '{Tag}' to {Player}", tag, playerResult.Name);
                    }
                    
                    foreach (var note in playerResult.Notes)
                    {
                        // Add some context to the note
                        var fullNote = $"[Auto-Replay] {note}";
                        await repo.AddNoteAsync(opponent.Id, fullNote, "replay", ct);
                        _logger.LogInformation("Added note to {Player}: {Note}", playerResult.Name, fullNote);
                    }
                    
                    if (!string.IsNullOrWhiteSpace(result.GameMode))
                    {
                        await repo.AddTagAsync(opponent.Id, result.GameMode, ct);
                    }
                    
                    var ourResult = MatchResult.Unknown;
                    if (!string.IsNullOrWhiteSpace(playerResult.Result))
                    {
                        var opponentWon = playerResult.Result.Equals("Win", StringComparison.OrdinalIgnoreCase);
                        ourResult = opponentWon ? MatchResult.Loss : MatchResult.Win;
                    }
                    
                    var fullMatchDataStr = JsonSerializer.Serialize(result.Data);
                    
                    var req = new RecordMatchRequest {
                        Result = ourResult,
                        MapName = result.MapName,
                        MyRace = myResult?.Race,
                        OpponentRace = playerResult.Race,
                        GameMode = result.GameMode,
                        PlayedAt = result.StartTime,
                        FullMatchData = fullMatchDataStr
                    };

                    if (myResult?.Stats != null)
                    {
                        req.MyWorkersCreated = myResult.Stats.WorkersCreated;
                        req.MySupplyBlockTime = myResult.Stats.SupplyBlockTime;
                        req.MyAvgUnspentMinerals = myResult.Stats.AvgUnspentMinerals;
                        req.MyAvgMineralIncome = myResult.Stats.AvgMineralIncome;
                    }
                    if (myResult?.UnitsMade != null)
                    {
                        req.MyUnitsMade = JsonSerializer.Serialize(myResult.UnitsMade);
                    }
                    if (playerResult.Stats != null)
                    {
                        req.OpponentWorkersCreated = playerResult.Stats.WorkersCreated;
                        req.OpponentSupplyBlockTime = playerResult.Stats.SupplyBlockTime;
                        req.OpponentAvgUnspentMinerals = playerResult.Stats.AvgUnspentMinerals;
                        req.OpponentAvgMineralIncome = playerResult.Stats.AvgMineralIncome;
                    }
                    if (playerResult.UnitsMade != null)
                    {
                        req.OpponentUnitsMade = JsonSerializer.Serialize(playerResult.UnitsMade);
                    }

                    await repo.RecordMatchAsync(opponent.Id, req, ct);
                }
                return true;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing replay analysis");
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
        public int TeamId { get; set; }
        public bool IsMe { get; set; }
        public List<string> Tags { get; set; } = [];
        public List<string> Notes { get; set; } = [];
        public Dictionary<string, int>? UnitsMade { get; set; }
        public PlayerStatsResult? Stats { get; set; }
    }

    private class PlayerStatsResult
    {
        public int WorkersCreated { get; set; }
        public int SupplyBlockTime { get; set; }
        public int AvgUnspentMinerals { get; set; }
        public int AvgMineralIncome { get; set; }
    }
}
