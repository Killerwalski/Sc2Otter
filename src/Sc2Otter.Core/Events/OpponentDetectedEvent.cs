namespace Sc2Otter.Core.Events;

public record OpponentNoteDto(int Id, string Content, DateTime CreatedAt, string Source);

public record OpponentDetectedEvent(
    int OpponentId,
    string Name,
    string? Race,
    string? GameMode,
    List<OpponentNoteDto> Notes,
    List<string> Tags,
    int TotalGames,
    int Wins,
    int Losses);
