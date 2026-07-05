namespace Sc2Otter.Core.Events;

using Sc2Otter.Core.Models;

public record GameStateChangedEvent(
    Sc2GameState State,
    List<OpponentDetectedEvent>? Opponents,
    DateTime Timestamp);
