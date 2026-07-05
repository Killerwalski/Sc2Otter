namespace Sc2Otter.Core.Models;

public class OpponentNote
{
    public int Id { get; set; }
    public int OpponentId { get; set; }
    public string Content { get; set; } = string.Empty;
    public string Source { get; set; } = "keyboard";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }

    public Opponent Opponent { get; set; } = null!;
}
