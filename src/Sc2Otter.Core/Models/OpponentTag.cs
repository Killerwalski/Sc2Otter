namespace Sc2Otter.Core.Models;

public class OpponentTag
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Color { get; set; }

    public ICollection<Opponent> Opponents { get; set; } = new List<Opponent>();
}
