using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Sc2Otter.Data;
using Microsoft.Extensions.DependencyInjection;

public class Program
{
    public static async Task Main()
    {
        var services = new ServiceCollection();
        services.AddDbContext<ScoutDbContext>(options =>
            options.UseNpgsql("Host=localhost;Port=5432;Database=sc2otter_dev;Username=postgres;Password=postgres"));

        var provider = services.BuildServiceProvider();
        var db = provider.GetRequiredService<ScoutDbContext>();

        var barcode = await db.Opponents
            .Include(o => o.TagAssignments).ThenInclude(ta => ta.Tag)
            .Include(o => o.MatchRecords)
            .FirstOrDefaultAsync(o => o.Name == "lllllllll");

        if (barcode == null)
        {
            Console.WriteLine("Barcode not found.");
            return;
        }

        Console.WriteLine($"Opponent: {barcode.Name} (ID: {barcode.Id}), Toon: {barcode.ToonHandle}");
        Console.WriteLine($"Total Matches: {barcode.MatchRecords.Count}");
        foreach (var m in barcode.MatchRecords)
        {
            Console.WriteLine($"  Match {m.Id}: PlayedAt={m.PlayedAt}, FullMatchData is null? {m.FullMatchData == null}");
        }

        Console.WriteLine($"Tags:");
        foreach (var ta in barcode.TagAssignments)
        {
            Console.WriteLine($"  Tag: {ta.Tag.Name}, Count: {ta.Count}");
        }
    }
}
