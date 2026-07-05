namespace Sc2Otter.Core.Models;

public enum MatchResult
{
    Unknown,
    Win,
    Loss
}

public class MatchRecord
{
    public int Id { get; set; }
    public int OpponentId { get; set; }
    public MatchResult Result { get; set; } = MatchResult.Unknown;
    public string? MapName { get; set; }
    public string? MyRace { get; set; }
    public string? OpponentRace { get; set; }
    public string? GameMode { get; set; }
    public DateTime PlayedAt { get; set; } = DateTime.UtcNow;

    public Opponent Opponent { get; set; } = null!;
}
