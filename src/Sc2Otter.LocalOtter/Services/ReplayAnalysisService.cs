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

    // Shared options instance — avoids allocating a new object on every replay analysis call.
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

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

        // Fire-and-forget pip install — doesn't block the constructor or startup.
        _ = Task.Run(async () =>
        {
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
                if (pipProcess != null)
                    await pipProcess.WaitForExitAsync();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to automatically install sc2reader via pip. Please ensure Python is installed on your system.");
            }
        });
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

            var result = JsonSerializer.Deserialize<ReplayAnalysisResult>(output, JsonOptions);
            FixStartTimeKind(result);

            if (result == null || !result.Success)
            {
                _logger.LogError("Python script returned failure: {Error}", result?.Error);
                return false;
            }

            if (result.Data != null)
            {
                await ProcessResultDataAsync(result, myName, ct);
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
        public string? ToonHandle { get; set; }
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

    public async Task AnalyzeReplaysBulkAsync(IEnumerable<string> replayPaths, Func<string, bool, Task> onProgress, CancellationToken ct = default)
    {
        var myName = _settingsService.Current.MySc2Name;
        _logger.LogInformation("Starting bulk Python sidecar analysis in daemon mode...");

        var processStartInfo = new ProcessStartInfo
        {
            FileName = "python",
            ArgumentList = { _pythonScriptPath, "--daemon" },
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(processStartInfo);
        if (process == null)
        {
            _logger.LogError("Failed to start python process in daemon mode.");
            return;
        }

        try
        {
            foreach (var replayPath in replayPaths)
            {
                if (ct.IsCancellationRequested) break;

                if (!File.Exists(replayPath))
                {
                    _logger.LogWarning("Replay file not found: {Path}", replayPath);
                    await onProgress(replayPath, false);
                    continue;
                }

                await process.StandardInput.WriteLineAsync(replayPath);
                await process.StandardInput.FlushAsync();

                var outputTask = process.StandardOutput.ReadLineAsync(ct).AsTask();
                var completedTask = await Task.WhenAny(outputTask, Task.Delay(15000, ct));

                if (completedTask != outputTask)
                {
                    _logger.LogError("Python process timed out while analyzing {Path}", replayPath);
                    await onProgress(replayPath, false);
                    continue;
                }

                var outputLine = await outputTask;
                if (string.IsNullOrWhiteSpace(outputLine))
                {
                    _logger.LogError("Python process closed unexpectedly while analyzing {Path}", replayPath);
                    await onProgress(replayPath, false);
                    continue;
                }

                var result = JsonSerializer.Deserialize<ReplayAnalysisResult>(outputLine, JsonOptions);
                FixStartTimeKind(result);

                if (result == null || !result.Success)
                {
                    _logger.LogError("Python script returned failure for {Path}: {Error}", replayPath, result?.Error);
                    await onProgress(replayPath, false);
                    continue;
                }

                if (result.Data != null)
                {
                    await ProcessResultDataAsync(result, myName, ct);
                }

                await onProgress(replayPath, true);
            }
        }
        finally
        {
            try
            {
                await process.StandardInput.WriteLineAsync("exit");
                await process.StandardInput.FlushAsync();

                using var cts = new CancellationTokenSource(2000);
                await process.WaitForExitAsync(cts.Token);
            }
            catch { }

            if (!process.HasExited)
            {
                process.Kill();
            }
        }
    }

    /// <summary>
    /// Ensures the StartTime datetime kind is set to UTC. sc2reader returns unspecified kind;
    /// EF Core / Npgsql requires UTC for timestamp columns.
    /// </summary>
    private static void FixStartTimeKind(ReplayAnalysisResult? result)
    {
        if (result != null && result.StartTime.HasValue && result.StartTime.Value.Kind == DateTimeKind.Unspecified)
        {
            result.StartTime = DateTime.SpecifyKind(result.StartTime.Value, DateTimeKind.Utc);
        }
    }

    private async Task ProcessResultDataAsync(ReplayAnalysisResult result, string? myName, CancellationToken ct)
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

        var myResult = result.Data!.FirstOrDefault(p => namesToIgnore.Contains(p.Name));
        if (myResult != null)
        {
            myResult.IsMe = true;
        }

        foreach (var playerResult in result.Data!)
        {
            if (namesToIgnore.Contains(playerResult.Name))
            {
                continue;
            }

            var opponent = await repo.GetOrCreateAsync(playerResult.Name, playerResult.ToonHandle, playerResult.Race, result.StartTime, ct);

            bool alreadyAnalyzed = await repo.IsMatchAlreadyAnalyzedAsync(opponent.Id, result.StartTime ?? DateTime.UtcNow, ct);

            if (!alreadyAnalyzed)
            {
                foreach (var tag in playerResult.Tags)
                {
                    await repo.AddTagAsync(opponent.Id, tag, ct);
                    _logger.LogInformation("Added tag '{Tag}' to {Player}", tag, playerResult.Name);
                }
            }

            var ourResult = MatchResult.Unknown;
            if (!string.IsNullOrWhiteSpace(playerResult.Result))
            {
                var opponentWon = playerResult.Result.Equals("Win", StringComparison.OrdinalIgnoreCase);
                ourResult = opponentWon ? MatchResult.Loss : MatchResult.Win;
            }

            var fullMatchDataStr = JsonSerializer.Serialize(result.Data);

            var req = new RecordMatchRequest
            {
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

            var matchRecord = await repo.RecordMatchAsync(opponent.Id, req, ct);

            if (!alreadyAnalyzed)
            {
                if (playerResult.Notes.Any())
                {
                    var noteLines = playerResult.Notes.Select(n => $"- {n}");
                    var fullNote = $"[Auto-Replay]\n{string.Join("\n", noteLines)}";
                    await repo.AddNoteAsync(opponent.Id, fullNote, "replay", matchRecord.Id, playerResult.Tags, ct);
                    _logger.LogInformation("Added auto-note to {Player}: {Note}", playerResult.Name, fullNote);
                }

                if (!string.IsNullOrWhiteSpace(result.GameMode))
                {
                    await repo.AddTagAsync(opponent.Id, result.GameMode, ct);
                }
            }
        }
    }
}
