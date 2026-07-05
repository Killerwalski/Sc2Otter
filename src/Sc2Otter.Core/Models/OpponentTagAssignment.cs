namespace Sc2Otter.Core.Models;

public class OpponentTagAssignment
{
    public int OpponentId { get; set; }
    public Opponent Opponent { get; set; } = null!;

    public int TagId { get; set; }
    public OpponentTag Tag { get; set; } = null!;

    public int Count { get; set; } = 1;
}
