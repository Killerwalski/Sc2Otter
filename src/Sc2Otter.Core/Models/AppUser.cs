namespace Sc2Otter.Core.Models;

using System.ComponentModel.DataAnnotations;
using System.Collections.Generic;

public class AppUser
{
    public int Id { get; set; }

    [Required]
    public string DiscordId { get; set; } = string.Empty;

    public string Username { get; set; } = string.Empty;

    public string? AvatarUrl { get; set; }

    public string SyncKey { get; set; } = string.Empty;

    // Navigation property for all opponents recorded by this user
    public ICollection<Opponent> Opponents { get; set; } = new List<Opponent>();
}
