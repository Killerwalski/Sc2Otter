namespace Sc2Otter.Core.Models;

using System;
using System.Collections.Generic;

public class AddNoteRequest { public string Content { get; set; } = ""; public string? Source { get; set; } }
public class UpdateNoteRequest { public string Content { get; set; } = ""; }
public class AddTagRequest { public string TagName { get; set; } = ""; }

public class RecordMatchRequest
{
    public MatchResult Result { get; set; }
    public string? MapName { get; set; }
    public string? MyRace { get; set; }
    public string? OpponentRace { get; set; }
    public string? GameMode { get; set; }
    public DateTime? PlayedAt { get; set; }
    public string? FullMatchData { get; set; }
    public string? MyUnitsMade { get; set; }
    public int MyWorkersCreated { get; set; }
    public int MySupplyBlockTime { get; set; }
    public int MyAvgUnspentMinerals { get; set; }
    public int MyAvgMineralIncome { get; set; }
    public string? OpponentUnitsMade { get; set; }
    public int OpponentWorkersCreated { get; set; }
    public int OpponentSupplyBlockTime { get; set; }
    public int OpponentAvgUnspentMinerals { get; set; }
    public int OpponentAvgMineralIncome { get; set; }
}
