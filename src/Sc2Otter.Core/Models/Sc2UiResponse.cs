namespace Sc2Otter.Core.Models;

using System.Text.Json.Serialization;

public class Sc2UiResponse
{
    [JsonPropertyName("activeScreens")]
    public List<string> ActiveScreens { get; set; } = [];
}
