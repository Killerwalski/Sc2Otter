namespace Sc2Otter.Core.Models;

using System.Text.Json.Serialization;

public class Sc2GameResponse
{
    [JsonPropertyName("players")]
    public List<Sc2Player> Players { get; set; } = [];

    [JsonPropertyName("isReplay")]
    public bool IsReplay { get; set; }

    [JsonPropertyName("displayTime")]
    public double DisplayTime { get; set; }
}

public class Sc2Player
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    [JsonPropertyName("race")]
    public string Race { get; set; } = string.Empty;

    [JsonPropertyName("result")]
    public string Result { get; set; } = string.Empty;
}
