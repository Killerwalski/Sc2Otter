namespace Sc2Otter.Server.Services;

using Microsoft.AspNetCore.SignalR;
using Sc2Otter.Core.Events;
using Sc2Otter.Core.Interfaces;
using Sc2Otter.Core.Models;
using Sc2Otter.Server.Hubs;

public class GameStateMonitor(
    ISc2GameClient sc2Client,
    IServiceScopeFactory scopeFactory,
    IHubContext<ScoutHub> hubContext,
    ILogger<GameStateMonitor> logger) : BackgroundService
{
    private Sc2GameState _currentState = Sc2GameState.WaitingForSc2;
    private readonly HashSet<string> _currentOpponentNames = [];
    private readonly Dictionary<string, int> _currentOpponentIds = new();
    private string? _lastMapName;

    public Sc2GameState CurrentState => _currentState;

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

            await Task.Delay(2000, stoppingToken);
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

        var screens = uiState.ActiveScreens;
        var newState = DetermineState(screens, gameInfo);

        if (newState != _currentState)
        {
            await TransitionTo(newState, gameInfo, screens, ct);
        }
    }

    private Sc2GameState DetermineState(List<string> screens, Sc2GameResponse gameInfo)
    {
        // Check for loading screen
        if (screens.Any(s => s.Contains("Loading", StringComparison.OrdinalIgnoreCase) ||
                             s.Contains("InitGame", StringComparison.OrdinalIgnoreCase)))
        {
            return Sc2GameState.LoadingScreen;
        }

        // Check for in-game
        if (screens.Any(s => s.Equals("ScreenInGame", StringComparison.OrdinalIgnoreCase) ||
                             s.Contains("InGame", StringComparison.OrdinalIgnoreCase)))
        {
            // Check if game has ended (results available)
            var hasResults = gameInfo.Players.Any(p =>
                p.Result.Equals("Victory", StringComparison.OrdinalIgnoreCase) ||
                p.Result.Equals("Defeat", StringComparison.OrdinalIgnoreCase) ||
                p.Result.Equals("Win", StringComparison.OrdinalIgnoreCase) ||
                p.Result.Equals("Loss", StringComparison.OrdinalIgnoreCase));

            return hasResults ? Sc2GameState.PostGame : Sc2GameState.InGame;
        }

        // Check for score/results screen
        if (screens.Any(s => s.Contains("Score", StringComparison.OrdinalIgnoreCase) ||
                             s.Contains("Result", StringComparison.OrdinalIgnoreCase)))
        {
            return Sc2GameState.PostGame;
        }

        // Default: in menus if SC2 is responding
        return gameInfo.Players.Count > 0 ? Sc2GameState.InGame : Sc2GameState.InMenus;
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
                if (gameInfo is not null && _currentOpponentIds.Count == 0)
                {
                    newlyDetectedOpponents = await DetectOpponentsAsync(gameInfo, ct);
                    if (newlyDetectedOpponents.Count > 0)
                    {
                        await hubContext.Clients.All.SendAsync("OpponentsDetected", newlyDetectedOpponents, ct);
                    }
                }
                break;

            case Sc2GameState.PostGame when gameInfo is not null:
                await HandlePostGame(gameInfo, ct);
                break;

            case Sc2GameState.WaitingForSc2:
                _currentOpponentNames.Clear();
                _currentOpponentIds.Clear();
                break;

            case Sc2GameState.InMenus when previousState is Sc2GameState.PostGame or Sc2GameState.InGame:
                _currentOpponentNames.Clear();
                _currentOpponentIds.Clear();
                break;
        }

        var stateEvent = new GameStateChangedEvent(newState, newlyDetectedOpponents, DateTime.UtcNow);
        await hubContext.Clients.All.SendAsync("GameStateChanged", stateEvent, ct);
    }

    private async Task<List<OpponentDetectedEvent>> DetectOpponentsAsync(Sc2GameResponse gameInfo, CancellationToken ct)
    {
        // Identify opponents (players who are not "me" — we detect "me" as the first user-type player)
        var humanPlayers = gameInfo.Players
            .Where(p => !p.Type.Equals("computer", StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (humanPlayers.Count == 0) return [];

        // Determine game mode based on player count
        var totalPlayers = humanPlayers.Count;
        var gameMode = totalPlayers switch
        {
            2 => "1v1",
            4 => "2v2",
            6 => "3v3",
            8 => "4v4",
            _ => $"{totalPlayers}p"
        };

        // In a team game, the first player is typically you. In 1v1, one of the two is you.
        // We track ALL players and let the user see all of them.
        // The user can identify themselves by their own name.
        _currentOpponentNames.Clear();
        _currentOpponentIds.Clear();

        var detectedOpponents = new List<OpponentDetectedEvent>();

        using var scope = scopeFactory.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IOpponentRepository>();

        foreach (var player in humanPlayers)
        {
            if (string.IsNullOrWhiteSpace(player.Name)) continue;

            _currentOpponentNames.Add(player.Name);

            var opponent = await repo.GetOrCreateAsync(player.Name, player.Race, ct);
            _currentOpponentIds[player.Name] = opponent.Id;

            // Load full details for display
            var details = await repo.GetWithDetailsAsync(opponent.Id, ct);
            var stats = await repo.GetStatsAsync(opponent.Id, ct);

            var opponentEvent = new OpponentDetectedEvent(
                OpponentId: opponent.Id,
                Name: player.Name,
                Race: player.Race,
                GameMode: gameMode,
                Notes: details?.Notes.Select(n => new OpponentNoteDto(n.Id, n.Content, n.CreatedAt, n.Source)).ToList() ?? [],
                Tags: details?.Tags.Select(t => t.Name).ToList() ?? [],
                TotalGames: stats.TotalGames,
                Wins: stats.Wins,
                Losses: stats.Losses);

            detectedOpponents.Add(opponentEvent);

            logger.LogInformation("Opponent detected: {Name} ({Race}) - {Games} games, {Wins}W/{Losses}L",
                player.Name, player.Race, stats.TotalGames, stats.Wins, stats.Losses);
        }

        _lastMapName = null; // Will be populated when we can determine the map

        return detectedOpponents;
    }

    private async Task HandlePostGame(Sc2GameResponse gameInfo, CancellationToken ct)
    {
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

            await repo.RecordMatchAsync(
                opponentId, ourResult, _lastMapName,
                myRace: null, opponentRace: player.Race,
                gameMode: null, ct);

            logger.LogInformation("Match recorded vs {Name}: {Result}", player.Name, ourResult);
        }

        // Notify UI of post-game
        await hubContext.Clients.All.SendAsync("PostGameResults", gameInfo.Players, ct);
    }
}
