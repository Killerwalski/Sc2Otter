namespace Sc2Otter.Core.Models;

public class Opponent
{
    public int Id { get; set; }
    
    public int UserId { get; set; }
    public AppUser User { get; set; } = null!;
    public string Name { get; set; } = string.Empty;
    public string? ToonHandle { get; set; }
    public string? Race { get; set; }
    public DateTime FirstSeen { get; set; } = DateTime.UtcNow;
    public DateTime LastSeen { get; set; } = DateTime.UtcNow;
    public int? Mmr { get; set; }
    public string? League { get; set; }

    public ICollection<OpponentNote> Notes { get; set; } = new List<OpponentNote>();
    public ICollection<MatchRecord> MatchRecords { get; set; } = new List<MatchRecord>();
    public ICollection<OpponentTag> Tags { get; set; } = new List<OpponentTag>();
    public ICollection<OpponentTagAssignment> TagAssignments { get; set; } = new List<OpponentTagAssignment>();
}
