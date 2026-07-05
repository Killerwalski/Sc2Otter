namespace Sc2Otter.Data;

using Microsoft.EntityFrameworkCore;
using Sc2Otter.Core.Models;

public class ScoutDbContext(DbContextOptions<ScoutDbContext> options) : DbContext(options)
{
    public DbSet<Opponent> Opponents => Set<Opponent>();
    public DbSet<OpponentNote> Notes => Set<OpponentNote>();
    public DbSet<OpponentTag> Tags => Set<OpponentTag>();
    public DbSet<MatchRecord> MatchRecords => Set<MatchRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Opponent>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).HasMaxLength(100).IsRequired();
            entity.HasIndex(e => e.Name);
            entity.HasMany(e => e.Notes).WithOne(n => n.Opponent).HasForeignKey(n => n.OpponentId).OnDelete(DeleteBehavior.Cascade);
            entity.HasMany(e => e.MatchRecords).WithOne(m => m.Opponent).HasForeignKey(m => m.OpponentId).OnDelete(DeleteBehavior.Cascade);
            entity.HasMany(e => e.Tags).WithMany(t => t.Opponents)
                .UsingEntity<OpponentTagAssignment>(
                    j => j.HasOne(e => e.Tag).WithMany(t => t.TagAssignments).HasForeignKey(e => e.TagId),
                    j => j.HasOne(e => e.Opponent).WithMany(o => o.TagAssignments).HasForeignKey(e => e.OpponentId)
                );
        });

        modelBuilder.Entity<OpponentNote>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Content).IsRequired();
        });

        modelBuilder.Entity<OpponentTag>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).HasMaxLength(50).IsRequired();
            entity.HasIndex(e => e.Name).IsUnique();
        });

        modelBuilder.Entity<MatchRecord>(entity =>
        {
            entity.HasKey(e => e.Id);
        });

        // Seed default tags
        modelBuilder.Entity<OpponentTag>().HasData(
            new OpponentTag { Id = 1, Name = "cheese", Color = "#FF6B35" },
            new OpponentTag { Id = 2, Name = "macro", Color = "#4ECDC4" },
            new OpponentTag { Id = 3, Name = "aggressive", Color = "#FF1744" },
            new OpponentTag { Id = 4, Name = "defensive", Color = "#42A5F5" },
            new OpponentTag { Id = 5, Name = "all-in", Color = "#FF5252" },
            new OpponentTag { Id = 6, Name = "timing attack", Color = "#FFC107" },
            new OpponentTag { Id = 7, Name = "cannon rush", Color = "#FF9800" },
            new OpponentTag { Id = 8, Name = "proxy", Color = "#E040FB" },
            new OpponentTag { Id = 9, Name = "fast expand", Color = "#66BB6A" },
            new OpponentTag { Id = 10, Name = "turtle", Color = "#78909C" }
        );
    }
}
