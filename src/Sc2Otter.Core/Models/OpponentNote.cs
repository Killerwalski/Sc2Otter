namespace Sc2Otter.Core.Models;

public class OpponentNote
{
    public int Id { get; set; }
    public int OpponentId { get; set; }
    public string Content { get; set; } = string.Empty;
    public string Source { get; set; } = "keyboard";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }

    public int? MatchRecordId { get; set; }
    public MatchRecord? MatchRecord { get; set; }
    
    public List<string> AutoTags { get; set; } = new();

    public Opponent Opponent { get; set; } = null!;
}
