using System;
using System.Linq;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Sc2Otter.Data;
using Sc2Otter.Data.Models;
using Sc2Otter.Core.Models;
using Microsoft.Extensions.Configuration;

var config = new ConfigurationBuilder()
    .AddUserSecrets("dotnet-Sc2Otter.LocalClient-320828d8-7fe7-4658-84b0-f0136b399590")
    .Build();

var connStr = config.GetConnectionString("DefaultConnection");

var options = new DbContextOptionsBuilder<Sc2OtterDbContext>()
    .UseNpgsql(connStr)
    .Options;

using var db = new Sc2OtterDbContext(options);

var opponent = db.Opponents.Include(o => o.MatchRecords).FirstOrDefault(o => o.Name == "isobro");
if (opponent == null) {
    Console.WriteLine("Not found");
    return;
}

foreach (var match in opponent.MatchRecords) {
    Console.WriteLine($"Match {match.Id} - {match.MapName}");
    Console.WriteLine(match.FullMatchData);
}
