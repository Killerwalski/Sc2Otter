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

    public int? MyWorkersCreated { get; set; }
    public int? OpponentWorkersCreated { get; set; }
    public int? MySupplyBlockTime { get; set; }
    public int? OpponentSupplyBlockTime { get; set; }
    public int? MyAvgUnspentMinerals { get; set; }
    public int? OpponentAvgUnspentMinerals { get; set; }
    public int? MyAvgMineralIncome { get; set; }
    public int? OpponentAvgMineralIncome { get; set; }

    public string? MyUnitsMade { get; set; }
    public string? OpponentUnitsMade { get; set; }
    public string? FullMatchData { get; set; }

    public string? PlaystyleArchetype { get; set; }
    public string? PlaystyleSummary { get; set; }

    public Opponent Opponent { get; set; } = null!;
}
