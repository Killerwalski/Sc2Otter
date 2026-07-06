namespace Sc2Otter.LocalClient.Services;

using Microsoft.AspNetCore.SignalR;
using Sc2Otter.Core.Events;
using Sc2Otter.Core.Interfaces;
using Sc2Otter.Core.Models;
using Sc2Otter.LocalClient.Services;

public class GameStateMonitor(
    ISc2GameClient sc2Client,
    IServiceScopeFactory scopeFactory,
    ScoutHubClient hubClient,
    SettingsService settings,
    Sc2PulseClient sc2PulseClient,
    ILogger<GameStateMonitor> logger) : BackgroundService
{
    private Sc2GameState _currentState = Sc2GameState.WaitingForSc2;
    private readonly HashSet<string> _currentOpponentNames = [];
    private readonly Dictionary<string, int> _currentOpponentIds = new();
    private List<OpponentDetectedEvent> _currentOpponents = [];
    private string? _lastMapName;

    public Sc2GameState CurrentState => _currentState;
    public IReadOnlyList<OpponentDetectedEvent> CurrentOpponents => _currentOpponents;

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

        if (newState != _currentState)
        {
            await TransitionTo(newState, gameInfo, screens, ct);
        }
        else if (newState is Sc2GameState.LoadingScreen or Sc2GameState.InGame)
        {
            var myName = settings.Current.MySc2Name;
            var currentHumanPlayers = (gameInfo?.Players ?? [])
                .Where(p => !p.Type.Equals("computer", StringComparison.OrdinalIgnoreCase))
                .Where(p => string.IsNullOrWhiteSpace(myName) || !p.Name.Equals(myName, StringComparison.OrdinalIgnoreCase))
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
        var hasResults = (gameInfo?.Players ?? []).Any(p =>
            string.Equals(p.Result, "Victory", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(p.Result, "Defeat", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(p.Result, "Win", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(p.Result, "Loss", StringComparison.OrdinalIgnoreCase));

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

        // If no explicit in-game screen is reported, but a game is active, we are in-game!
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
                    await HandlePostGame(gameInfo, ct);
                    
                    // Reload opponents to fetch updated stats (wins/losses)
                    newlyDetectedOpponents = await DetectOpponentsAsync(gameInfo, ct, ignoreCache: true);
                    if (newlyDetectedOpponents.Count > 0)
                    {
                        _currentOpponents = newlyDetectedOpponents;
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

    private async Task<List<OpponentDetectedEvent>> DetectOpponentsAsync(Sc2GameResponse? gameInfo, CancellationToken ct, bool ignoreCache = false)
    {
        if (gameInfo is null) return [];

        if (!ignoreCache)
        {
            // If the API is still caching the previous game, it will have results. Ignore it.
            var isOldCachedGame = (gameInfo.Players ?? []).Any(p =>
                string.Equals(p.Result, "Victory", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(p.Result, "Defeat", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(p.Result, "Win", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(p.Result, "Loss", StringComparison.OrdinalIgnoreCase));

            if (isOldCachedGame) return [];
        }

        var myName = settings.Current.MySc2Name;

        // Identify opponents (players who are not "me")
        var allHumanPlayers = (gameInfo.Players ?? [])
            .Where(p => !p.Type.Equals("computer", StringComparison.OrdinalIgnoreCase))
            .ToList();
            
        var humanPlayers = allHumanPlayers
            .Where(p => string.IsNullOrWhiteSpace(myName) || !p.Name.Equals(myName, StringComparison.OrdinalIgnoreCase))
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

            var opponent = await repo.GetOrCreateAsync(player.Name, player.Race, seenAt: null, ct: ct);
            newIds[player.Name] = opponent.Id;
            
            // Tag game mode
            if (!string.IsNullOrWhiteSpace(gameMode))
            {
                await repo.AddTagAsync(opponent.Id, gameMode, ct);
            }

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
        foreach (var name in newNames) _currentOpponentNames.Add(name);

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

            // Note: from SC2's perspective, "Victory" means THAT player won.
            // Since we're recording against our opponents, if the opponent got Victory, WE lost.
            // But actually, the API reports results from the perspective of each player.
            // We need to invert: if opponent result is Victory, we record it as a Loss for us.
            var ourResult = result switch
            {
                MatchResult.Win => MatchResult.Loss,  // Opponent won = we lost
                MatchResult.Loss => MatchResult.Win,  // Opponent lost = we won
                _ => MatchResult.Unknown
            };

            await repo.RecordMatchAsync(opponentId, new RecordMatchRequest {
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
        
        // Analyze the replay! Give it a brief delay to ensure the file is written and unlocked by SC2
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(3000, ct); // Wait 3 seconds
                var replayPath = TryGetLatestReplayPath();
                if (replayPath != null)
                {
                    using var analysisScope = scopeFactory.CreateScope();
                    var analyzer = analysisScope.ServiceProvider.GetRequiredService<ReplayAnalysisService>();
                    await analyzer.AnalyzeReplayAsync(replayPath, ct);
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to analyze replay post-game");
            }
        }, ct);
    }

    private string? TryGetLatestReplayPath()
    {
        try
        {
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

            if (latestReplay != null && (DateTime.UtcNow - latestReplay.LastWriteTimeUtc).TotalMinutes < 15)
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
