namespace Sc2Otter.LocalOtter.Services;

using Microsoft.AspNetCore.SignalR;
using Sc2Otter.Core.Events;
using Sc2Otter.Core.Interfaces;
using Sc2Otter.Core.Models;

public class GameStateMonitor : BackgroundService
{
    private readonly ISc2GameClient sc2Client;
    private readonly IServiceScopeFactory scopeFactory;
    private readonly ScoutHubClient hubClient;
    private readonly SettingsService settings;
    private readonly Sc2PulseClient sc2PulseClient;
    private readonly ILogger<GameStateMonitor> logger;

    private Sc2GameState _currentState = Sc2GameState.WaitingForSc2;
    private readonly HashSet<string> _currentOpponentNames = [];
    private readonly Dictionary<string, int> _currentOpponentIds = new();
    private List<OpponentDetectedEvent> _currentOpponents = [];
    private string? _lastMapName;
    private bool _forceStatePush = true;

    // Replays written more recently than this are considered "from the current session".
    private const int ReplayMaxAgeMinutes = 15;

    public Sc2GameState CurrentState => _currentState;
    public IReadOnlyList<OpponentDetectedEvent> CurrentOpponents => _currentOpponents;

    public GameStateMonitor(
        ISc2GameClient sc2Client,
        IServiceScopeFactory scopeFactory,
        ScoutHubClient hubClient,
        SettingsService settings,
        Sc2PulseClient sc2PulseClient,
        ILogger<GameStateMonitor> logger)
    {
        this.sc2Client = sc2Client;
        this.scopeFactory = scopeFactory;
        this.hubClient = hubClient;
        this.settings = settings;
        this.sc2PulseClient = sc2PulseClient;
        this.logger = logger;

        this.hubClient.OnRefreshRequested += () => _forceStatePush = true;
        this.hubClient.OnConnected += () => _forceStatePush = true;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("GameStateMonitor started. Polling SC2 API every 2 seconds.");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await PollGameStateAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Error polling SC2 game state");
            }

            try
            {
                var interval = settings.Current.PollingIntervalMs;
                await Task.Delay(interval < 500 ? 2000 : interval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private async Task PollGameStateAsync(CancellationToken ct)
    {
        var gameInfo = await sc2Client.GetGameInfoAsync(ct);
        var uiState = await sc2Client.GetUiStateAsync(ct);

        if (gameInfo is null || uiState is null)
        {
            if (_currentState != Sc2GameState.WaitingForSc2)
            {
                await TransitionTo(Sc2GameState.WaitingForSc2, null, null, ct);
            }
            return;
        }

        // Skip replays
        if (gameInfo.IsReplay)
        {
            if (_currentState != Sc2GameState.InMenus)
            {
                await TransitionTo(Sc2GameState.InMenus, null, null, ct);
            }
            return;
        }

        var screens = uiState?.ActiveScreens ?? [];
        var newState = DetermineState(screens, gameInfo);

        if (newState != _currentState || _forceStatePush)
        {
            _forceStatePush = false;
            await TransitionTo(newState, gameInfo, screens, ct);
        }
        else if (newState is Sc2GameState.LoadingScreen or Sc2GameState.InGame)
        {
            var myNames = GetMyNames();
            var currentHumanPlayers = (gameInfo?.Players ?? [])
                .Where(p => !p.Type.Equals("computer", StringComparison.OrdinalIgnoreCase))
                .Where(p => myNames.Count == 0 || !myNames.Contains((p.Name ?? "").Trim()))
                .Select(p => p.Name)
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .ToList();

            if (currentHumanPlayers.Count > 0 &&
                (currentHumanPlayers.Count != _currentOpponentNames.Count || !currentHumanPlayers.All(_currentOpponentNames.Contains)))
            {
                logger.LogInformation("Players changed during {State}. Reloading opponents.", newState);
                var newlyDetectedOpponents = await DetectOpponentsAsync(gameInfo, ct);
                if (newlyDetectedOpponents.Count > 0)
                {
                    _currentOpponents = newlyDetectedOpponents;
                    await hubClient.PushOpponentsDetectedAsync(newlyDetectedOpponents, ct);
                }
            }
        }
    }

    private Sc2GameState DetermineState(List<string> screens, Sc2GameResponse? gameInfo)
    {
        var hasResults = HasGameResults(gameInfo);
        var hasPlayers = (gameInfo?.Players ?? []).Count > 0;
        var isGameActive = hasPlayers && !hasResults;

        // Check for loading screen
        if (screens.Any(s => s.Contains("Loading", StringComparison.OrdinalIgnoreCase) ||
                             s.Contains("InitGame", StringComparison.OrdinalIgnoreCase)))
        {
            return Sc2GameState.LoadingScreen;
        }

        // Check for score/results screen
        if (screens.Any(s => s.Contains("Score", StringComparison.OrdinalIgnoreCase) ||
                             s.Contains("Result", StringComparison.OrdinalIgnoreCase)))
        {
            return Sc2GameState.PostGame;
        }

        // Explicit in-game screen (sometimes reported, sometimes not)
        if (screens.Any(s => s.Equals("ScreenInGame", StringComparison.OrdinalIgnoreCase) ||
                             s.Contains("InGame", StringComparison.OrdinalIgnoreCase)))
        {
            return hasResults ? Sc2GameState.PostGame : Sc2GameState.InGame;
        }

        // If no explicit in-game screen is reported but a game is active, we are in-game.
        // SC2 API often reports empty active screens when actually playing a match.
        if (isGameActive)
        {
            return Sc2GameState.InGame;
        }

        // Default: in menus if SC2 is responding
        return Sc2GameState.InMenus;
    }

    private async Task TransitionTo(Sc2GameState newState, Sc2GameResponse? gameInfo,
        List<string>? screens, CancellationToken ct)
    {
        var previousState = _currentState;
        _currentState = newState;
        logger.LogInformation("Game state: {Previous} → {New}", previousState, newState);

        List<OpponentDetectedEvent>? newlyDetectedOpponents = null;

        switch (newState)
        {
            case Sc2GameState.LoadingScreen:
            case Sc2GameState.InGame:
                if (gameInfo is not null && (_currentOpponentIds.Count == 0 || newlyDetectedOpponents?.Count == 0))
                {
                    newlyDetectedOpponents = await DetectOpponentsAsync(gameInfo, ct);
                    if (newlyDetectedOpponents.Count > 0)
                    {
                        _currentOpponents = newlyDetectedOpponents;
                        await hubClient.PushOpponentsDetectedAsync(newlyDetectedOpponents, ct);
                    }
                }
                else
                {
                    newlyDetectedOpponents = _currentOpponents;
                }
                break;

            case Sc2GameState.PostGame:
                if (gameInfo is not null)
                {
                    if (newState != previousState)
                    {
                        await HandlePostGame(gameInfo, ct);

                        // Reload opponents to fetch updated stats (wins/losses)
                        newlyDetectedOpponents = await DetectOpponentsAsync(gameInfo, ct, ignoreCache: true);
                        if (newlyDetectedOpponents.Count > 0)
                        {
                            _currentOpponents = newlyDetectedOpponents;
                        }
                    }
                    else
                    {
                        newlyDetectedOpponents = _currentOpponents;
                    }
                }
                break;

            case Sc2GameState.WaitingForSc2:
                _currentOpponentNames.Clear();
                _currentOpponentIds.Clear();
                _currentOpponents = [];
                break;

            case Sc2GameState.InMenus when previousState is Sc2GameState.PostGame or Sc2GameState.InGame:
                _currentOpponentNames.Clear();
                _currentOpponentIds.Clear();
                _currentOpponents = [];
                break;
        }

        var stateEvent = new GameStateChangedEvent(newState, newlyDetectedOpponents, DateTime.UtcNow);
        await hubClient.PushGameStateAsync(stateEvent, ct);
    }

    /// <summary>
    /// Identifies opponents from the current game info, upserts them in the repository,
    /// optionally fetches MMR from SC2 Pulse, and returns a list of <see cref="OpponentDetectedEvent"/>.
    /// </summary>
    /// <param name="ignoreCache">
    /// When true, skips the stale-cached-game check. Pass true when calling after post-game
    /// so that the final player results are included even though the API still reports results.
    /// </param>
    private async Task<List<OpponentDetectedEvent>> DetectOpponentsAsync(Sc2GameResponse? gameInfo, CancellationToken ct, bool ignoreCache = false)
    {
        if (gameInfo is null) return [];

        if (!ignoreCache)
        {
            // If the API is still caching the previous game it will have results — ignore it.
            if (HasGameResults(gameInfo)) return [];
        }

        var myNames = GetMyNames();

        // Identify opponents (players who are not "me")
        var allHumanPlayers = (gameInfo.Players ?? [])
            .Where(p => !p.Type.Equals("computer", StringComparison.OrdinalIgnoreCase))
            .ToList();

        var humanPlayers = allHumanPlayers
            .Where(p => myNames.Count == 0 || !myNames.Contains((p.Name ?? "").Trim()))
            .ToList();

        if (humanPlayers.Count == 0) return [];

        // Determine game mode based on player count
        var totalPlayers = allHumanPlayers.Count;
        var gameMode = totalPlayers switch
        {
            2 => "1v1",
            4 => "2v2",
            6 => "3v3",
            8 => "4v4",
            _ => $"{totalPlayers}p"
        };

        var detectedOpponents = new List<OpponentDetectedEvent>();
        var newNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var newIds = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        using var scope = scopeFactory.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IOpponentRepository>();

        foreach (var player in humanPlayers)
        {
            if (string.IsNullOrWhiteSpace(player.Name)) continue;

            newNames.Add(player.Name);

            var opponent = await repo.GetOrCreateAsync(player.Name, null, player.Race, seenAt: null, ct: ct);
            newIds[player.Name] = opponent.Id;

            // Load full details for display
            var details = await repo.GetWithDetailsAsync(opponent.Id, ct);
            var stats = await repo.GetStatsAsync(opponent.Id, ct);

            // Try fetching MMR if we don't have it saved recently
            if (!opponent.Mmr.HasValue || (DateTime.UtcNow - opponent.LastSeen).TotalDays > 1)
            {
                var (mmr, league) = await sc2PulseClient.GetOpponentMmrAsync(player.Name, ct);
                if (mmr.HasValue)
                {
                    opponent.Mmr = mmr;
                    opponent.League = league;
                    await repo.UpdateOpponentAsync(opponent, ct);
                }
            }

            var opponentEvent = new OpponentDetectedEvent(
                OpponentId: opponent.Id,
                Name: player.Name,
                Race: player.Race,
                GameMode: gameMode,
                Notes: details?.Notes.Select(n => new OpponentNoteDto(n.Id, n.Content, n.CreatedAt, n.Source)).ToList() ?? [],
                Tags: details?.TagAssignments.Select(ta => ta.Count > 1 ? $"{ta.Tag.Name} x{ta.Count}" : ta.Tag.Name).ToList() ?? [],
                TotalGames: stats.TotalGames,
                Wins: stats.Wins,
                Losses: stats.Losses,
                Mmr: opponent.Mmr,
                League: opponent.League);

            detectedOpponents.Add(opponentEvent);

            logger.LogInformation("Opponent detected: {Name} ({Race}) - {Games} games, {Wins}W/{Losses}L",
                player.Name, player.Race, stats.TotalGames, stats.Wins, stats.Losses);
        }

        _currentOpponentNames.Clear();
        _currentOpponentNames.UnionWith(newNames);

        _currentOpponentIds.Clear();
        foreach (var kvp in newIds) _currentOpponentIds[kvp.Key] = kvp.Value;

        _lastMapName = null; // Will be populated when we can determine the map

        return detectedOpponents;
    }

    private async Task HandlePostGame(Sc2GameResponse gameInfo, CancellationToken ct)
    {
        _lastMapName ??= TryGetMapNameFromReplay();

        using var scope = scopeFactory.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IOpponentRepository>();

        foreach (var player in gameInfo.Players)
        {
            if (!_currentOpponentIds.TryGetValue(player.Name, out var opponentId)) continue;

            var result = player.Result.ToLowerInvariant() switch
            {
                "victory" or "win" => MatchResult.Win,
                "defeat" or "loss" => MatchResult.Loss,
                _ => MatchResult.Unknown
            };

            // From SC2's perspective "Victory" means THAT player won.
            // Since we record from OUR perspective: if opponent got Victory, WE lost.
            var ourResult = result switch
            {
                MatchResult.Win => MatchResult.Loss,   // Opponent won = we lost
                MatchResult.Loss => MatchResult.Win,   // Opponent lost = we won
                _ => MatchResult.Unknown
            };

            await repo.RecordMatchAsync(opponentId, new RecordMatchRequest
            {
                Result = ourResult,
                MapName = _lastMapName,
                MyRace = null,
                OpponentRace = player.Race,
                GameMode = null
            }, ct);

            logger.LogInformation("Match recorded vs {Name}: {Result}", player.Name, ourResult);
        }

        // Notify UI of post-game
        await hubClient.PushPostGameResultsAsync(gameInfo.Players, ct);

        // Analyze the replay — brief delay to let SC2 finish writing and releasing the file
        _ = Task.Run(async () =>
        {
            try
            {
                // Retry finding the replay up to 5 times (15 seconds total)
                for (int i = 0; i < 5; i++)
                {
                    await Task.Delay(3000, ct);
                    var replayPath = TryGetLatestReplayPath();
                    if (replayPath != null)
                    {
                        using var analysisScope = scopeFactory.CreateScope();
                        var analyzer = analysisScope.ServiceProvider.GetRequiredService<ReplayAnalysisService>();
                        var success = await analyzer.AnalyzeReplayAsync(replayPath, ct);

                        // If the python script ran successfully, stop retrying.
                        if (success) break;
                    }
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to analyze replay post-game");
            }
        }, ct);
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    /// <summary>Returns true if the game API response contains any final Win/Loss results.</summary>
    private static bool HasGameResults(Sc2GameResponse? gameInfo) =>
        (gameInfo?.Players ?? []).Any(p =>
            string.Equals(p.Result, "Victory", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(p.Result, "Defeat", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(p.Result, "Win", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(p.Result, "Loss", StringComparison.OrdinalIgnoreCase));

    /// <summary>Parses the MySc2Name setting into a case-insensitive HashSet of names.</summary>
    private HashSet<string> GetMyNames() =>
        (settings.Current.MySc2Name ?? "")
            .Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(n => n.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

    private string? TryGetLatestReplayPath()
    {
        try
        {
            var customDir = settings.Current.ReplayDirectory;
            if (!string.IsNullOrWhiteSpace(customDir) && Directory.Exists(customDir))
            {
                var recent = new DirectoryInfo(customDir).GetFiles("*.SC2Replay", SearchOption.AllDirectories)
                    .OrderByDescending(f => f.LastWriteTimeUtc)
                    .FirstOrDefault();

                if (recent != null && (DateTime.UtcNow - recent.LastWriteTimeUtc).TotalMinutes < ReplayMaxAgeMinutes)
                {
                    return recent.FullName;
                }
            }

            var sc2Docs = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "StarCraft II", "Accounts");
            if (!Directory.Exists(sc2Docs)) return null;

            var replaysDir = new DirectoryInfo(sc2Docs);
            FileInfo? latestReplay = null;

            foreach (var accountDir in replaysDir.GetDirectories())
            {
                foreach (var profileDir in accountDir.GetDirectories())
                {
                    var multiDir = new DirectoryInfo(Path.Combine(profileDir.FullName, "Replays", "Multiplayer"));
                    if (multiDir.Exists)
                    {
                        var recent = multiDir.GetFiles("*.SC2Replay")
                            .OrderByDescending(f => f.LastWriteTimeUtc)
                            .FirstOrDefault();

                        if (recent != null && (latestReplay == null || recent.LastWriteTimeUtc > latestReplay.LastWriteTimeUtc))
                        {
                            latestReplay = recent;
                        }
                    }
                }
            }

            if (latestReplay != null && (DateTime.UtcNow - latestReplay.LastWriteTimeUtc).TotalMinutes < ReplayMaxAgeMinutes)
            {
                return latestReplay.FullName;
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to get latest replay path");
        }
        return null;
    }

    private string? TryGetMapNameFromReplay()
    {
        var path = TryGetLatestReplayPath();
        if (path == null) return null;

        var name = Path.GetFileNameWithoutExtension(path);
        var match = System.Text.RegularExpressions.Regex.Match(name, @"^(.*?)( \(\d+\))?$");
        if (match.Success)
        {
            return match.Groups[1].Value.Trim();
        }
        return name;
    }
}
