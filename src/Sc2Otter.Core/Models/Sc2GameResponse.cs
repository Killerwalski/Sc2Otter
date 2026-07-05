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

    private string _race = string.Empty;

    [JsonPropertyName("race")]
    public string Race 
    { 
        get => _race;
        set
        {
            if (string.IsNullOrWhiteSpace(value))
                _race = "Unknown";
            else if (value.StartsWith("Terr", StringComparison.OrdinalIgnoreCase))
                _race = "Terran";
            else if (value.StartsWith("Zerg", StringComparison.OrdinalIgnoreCase))
                _race = "Zerg";
            else if (value.StartsWith("Prot", StringComparison.OrdinalIgnoreCase))
                _race = "Protoss";
            else if (value.StartsWith("Rand", StringComparison.OrdinalIgnoreCase))
                _race = "Random";
            else
                _race = value;
        }
    }

    [JsonPropertyName("result")]
    public string Result { get; set; } = string.Empty;
}
