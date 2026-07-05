namespace Sc2Otter.Tests;

using Sc2Otter.Core.Models;

public class Sc2PlayerTests
{
    [Theory]
    [InlineData("Terr", "Terran")]
    [InlineData("terran", "Terran")]
    [InlineData("Prot", "Protoss")]
    [InlineData("protoss", "Protoss")]
    [InlineData("Zerg", "Zerg")]
    [InlineData("Rand", "Random")]
    [InlineData("random", "Random")]
    [InlineData("Unknown", "Unknown")]
    [InlineData(null, "Unknown")]
    public void Race_IsNormalized_WhenSet(string? inputRace, string? expectedRace)
    {
        // Arrange
        var player = new Sc2Player();

        // Act
        player.Race = inputRace;

        // Assert
        Assert.Equal(expectedRace, player.Race);
    }
}
