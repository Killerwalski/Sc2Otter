namespace Sc2Otter.Core.Events;

/// <summary>
/// A lightweight snapshot of a single opponent note, sent over SignalR.
/// </summary>
public record OpponentNoteDto(int Id, string Content, DateTime CreatedAt, string Source);

/// <summary>
/// Raised when one or more opponents are detected at the start of a game (or on forced refresh).
/// Carries enough data to render the overlay without an additional DB round-trip.
/// </summary>
public record OpponentDetectedEvent(
    int OpponentId,
    string Name,
    string? Race,
    /// <summary>e.g. "1v1", "2v2" — null when fetched out-of-game via GetOpponentDetails.</summary>
    string? GameMode,
    List<OpponentNoteDto> Notes,
    List<string> Tags,
    int TotalGames,
    int Wins,
    int Losses,
    int? Mmr,
    string? League);
