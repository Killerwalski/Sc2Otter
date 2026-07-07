namespace Sc2Otter.Core.Models;

public class TagDefinition
{
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public string Category { get; set; } = "";
    public string? Race { get; set; }
}

public static class TagDictionary
{
    public static readonly List<TagDefinition> AllTags = new()
    {
        // Macro & Economy
        new() { Name = "Hatchery First", Description = "Built a Hatchery before a Spawning Pool.", Category = "Macro & Economy", Race = "Zerg" },
        new() { Name = "Nexus First", Description = "Built a Nexus before a Gateway or Forge.", Category = "Macro & Economy", Race = "Protoss" },
        new() { Name = "CC First", Description = "Built a Command Center before a Barracks.", Category = "Macro & Economy", Race = "Terran" },
        new() { Name = "One baser", Description = "Player did not start an expansion (Hatchery/Nexus/CC) within the first 4 minutes of the game.", Category = "Macro & Economy", Race = null },

        // Cheese & Aggression
        new() { Name = "Cheese", Description = "Automatically applied if any of the following are detected: Fast Pool, Cannon Rush, or any Proxy building.", Category = "Cheese & Aggression", Race = null },
        new() { Name = "Fast Pool", Description = "Spawning pool was started before 1:10 (typically indicates a 14 pool or earlier).", Category = "Cheese & Aggression", Race = "Zerg" },
        new() { Name = "Cannon Rusher", Description = "Built a Photon Cannon far away from their starting base (distance > 55) before the 6 minute mark.", Category = "Cheese & Aggression", Race = "Protoss" },
        
        // Composition & Tech
        new() { Name = "Random", Description = "Player queued as Random race.", Category = "Unit Composition & Tech", Race = null },
        new() { Name = "Bio player", Description = "Built more than 15 bio units (Marine/Marauder), and bio units outnumber factory units 2-to-1.", Category = "Unit Composition & Tech", Race = "Terran" },
        new() { Name = "Mech", Description = "Built more than 10 factory units (Hellion, Cyclone, Tank, Thor, Mine), and factory units outnumber bio units by at least 5.", Category = "Unit Composition & Tech", Race = "Terran" },
        new() { Name = "Mutas", Description = "Built more than 8 Mutalisks.", Category = "Unit Composition & Tech", Race = "Zerg" },
        new() { Name = "Multi reaper", Description = "Built more than 1 Reaper in the first 5 minutes.", Category = "Unit Composition & Tech", Race = "Terran" },
        new() { Name = "Battlecruiser", Description = "Built more than 1 Battlecruiser.", Category = "Unit Composition & Tech", Race = "Terran" },
        new() { Name = "Fast BCs", Description = "First Battlecruiser was produced before the 7 minute mark.", Category = "Unit Composition & Tech", Race = "Terran" },
        new() { Name = "Fast DTs", Description = "Dark Shrine was started before the 7 minute mark.", Category = "Unit Composition & Tech", Race = "Protoss" },
        new() { Name = "Warp prism", Description = "Built at least one Warp Prism.", Category = "Unit Composition & Tech", Race = "Protoss" },
        new() { Name = "Nydus Network", Description = "Built a Nydus Network.", Category = "Unit Composition & Tech", Race = "Zerg" },

        // Mass Units
        new() { Name = "Mass Queen", Description = "Built 8 or more Queens.", Category = "Mass Unit", Race = "Zerg" },
        new() { Name = "Mass Ravager", Description = "Built 12 or more Ravagers.", Category = "Mass Unit", Race = "Zerg" },
        new() { Name = "Mass Lurker", Description = "Built 6 or more Lurkers.", Category = "Mass Unit", Race = "Zerg" },
        new() { Name = "Mass Swarm Host", Description = "Built 8 or more Swarm Hosts.", Category = "Mass Unit", Race = "Zerg" },
        new() { Name = "Mass Infestor", Description = "Built 6 or more Infestors.", Category = "Mass Unit", Race = "Zerg" },
        new() { Name = "Mass Ultralisk", Description = "Built 5 or more Ultralisks.", Category = "Mass Unit", Race = "Zerg" },
        
        new() { Name = "Mass Adept", Description = "Built 8 or more Adepts.", Category = "Mass Unit", Race = "Protoss" },
        new() { Name = "Mass Void Ray", Description = "Built 6 or more Void Rays.", Category = "Mass Unit", Race = "Protoss" },
        new() { Name = "Mass Oracle", Description = "Built 5 or more Oracles.", Category = "Mass Unit", Race = "Protoss" },
        new() { Name = "Mass Carrier", Description = "Built 5 or more Carriers.", Category = "Mass Unit", Race = "Protoss" },
        new() { Name = "Mass Tempest", Description = "Built 6 or more Tempests.", Category = "Mass Unit", Race = "Protoss" },
        new() { Name = "Mass Archon", Description = "Built 10 or more Archons.", Category = "Mass Unit", Race = "Protoss" },

        new() { Name = "Mass Hellion", Description = "Built 12 or more Hellions (or Hellbats).", Category = "Mass Unit", Race = "Terran" },
        new() { Name = "Mass Widow Mine", Description = "Built 8 or more Widow Mines.", Category = "Mass Unit", Race = "Terran" },
        new() { Name = "Mass Cyclone", Description = "Built 8 or more Cyclones.", Category = "Mass Unit", Race = "Terran" },
        new() { Name = "Mass Liberator", Description = "Built 5 or more Liberators.", Category = "Mass Unit", Race = "Terran" },
        new() { Name = "Mass Banshee", Description = "Built 5 or more Banshees.", Category = "Mass Unit", Race = "Terran" },
        new() { Name = "Mass Ghost", Description = "Built 8 or more Ghosts.", Category = "Mass Unit", Race = "Terran" }
    };

    // O(1) lookup dictionary built once at startup — keyed by lower-case tag name.
    // Use Get() instead of querying AllTags directly to benefit from this index.
    private static readonly Dictionary<string, TagDefinition> _tagIndex =
        AllTags.ToDictionary(t => t.Name.ToLowerInvariant(), t => t);

    /// <summary>
    /// Looks up a tag definition by name (case-insensitive). Returns null if not found.
    /// O(1) — uses a pre-built dictionary instead of a linear scan.
    /// </summary>
    public static TagDefinition? Get(string name) =>
        _tagIndex.TryGetValue(name.ToLowerInvariant(), out var def) ? def : null;
}
