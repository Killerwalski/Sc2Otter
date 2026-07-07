namespace Sc2Otter.Core.Models;

/// <summary>Request to add a note to an opponent's profile.</summary>
public class AddNoteRequest
{
    public string Content { get; set; } = "";
    public string? Source { get; set; }
    public int? MatchRecordId { get; set; }
    public List<string> AutoTags { get; set; } = new();
}

/// <summary>Request to update the text of an existing note.</summary>
public class UpdateNoteRequest
{
    public string Content { get; set; } = "";
}

/// <summary>Request to assign a tag to an opponent.</summary>
public class AddTagRequest
{
    public string TagName { get; set; } = "";
}

/// <summary>
/// Request to record a completed match result.
/// Stats fields (Workers, SupplyBlock, etc.) are populated from replay analysis.
/// </summary>
public class RecordMatchRequest
{
    public MatchResult Result { get; set; }
    public string? MapName { get; set; }
    public string? MyRace { get; set; }
    public string? OpponentRace { get; set; }
    public string? GameMode { get; set; }
    public DateTime? PlayedAt { get; set; }
    public string? FullMatchData { get; set; }

    // My stats (populated from replay)
    public string? MyUnitsMade { get; set; }
    public int MyWorkersCreated { get; set; }
    public int MySupplyBlockTime { get; set; }
    public int MyAvgUnspentMinerals { get; set; }
    public int MyAvgMineralIncome { get; set; }

    // Opponent stats (populated from replay)
    public string? OpponentUnitsMade { get; set; }
    public int OpponentWorkersCreated { get; set; }
    public int OpponentSupplyBlockTime { get; set; }
    public int OpponentAvgUnspentMinerals { get; set; }
    public int OpponentAvgMineralIncome { get; set; }
    
    // AI Playstyle analysis
    public string? PlaystyleArchetype { get; set; }
    public string? PlaystyleSummary { get; set; }
}
