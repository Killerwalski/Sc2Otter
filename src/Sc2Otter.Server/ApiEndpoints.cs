namespace Sc2Otter.Server;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Sc2Otter.Core.Interfaces;
using Sc2Otter.Core.Models;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Threading;

public static class ApiEndpoints
{
    public static void MapApiEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/opponents");

        group.AddEndpointFilter(async (context, next) =>
        {
            if (context.HttpContext.User.Identity?.IsAuthenticated == true)
            {
                return await next(context);
            }

            if (context.HttpContext.Request.Headers.TryGetValue("X-Sync-Key", out var syncKeyValues))
            {
                var syncKey = syncKeyValues.FirstOrDefault();
                if (!string.IsNullOrEmpty(syncKey))
                {
                    var db = context.HttpContext.RequestServices.GetRequiredService<Sc2Otter.Data.ScoutDbContext>();
                    var user = await db.Users.FirstOrDefaultAsync(u => u.SyncKey == syncKey);
                    if (user != null)
                    {
                        context.HttpContext.Items["Sc2OtterUserId"] = user.Id;
                        return await next(context);
                    }
                }
            }

            return Results.Unauthorized();
        });

        group.MapGet("/get-or-create", async (string name, string? toonHandle, string? race, DateTime? seenAt, IOpponentRepository repo, CancellationToken ct) =>
        {
            try
            {
                if (seenAt.HasValue && seenAt.Value.Kind == DateTimeKind.Unspecified)
                {
                    seenAt = DateTime.SpecifyKind(seenAt.Value, DateTimeKind.Utc);
                }
                var opponent = await repo.GetOrCreateAsync(name, toonHandle, race, seenAt, ct);
                return Results.Ok(opponent);
            }
            catch (Exception ex)
            {
                return Results.Problem(ex.ToString());
            }
        });

        group.MapGet("/{id}", async (int id, IOpponentRepository repo, CancellationToken ct) =>
        {
            var opp = await repo.GetByIdAsync(id, ct);
            return opp != null ? Results.Ok(opp) : Results.NotFound();
        });

        group.MapGet("/{id}/details", async (int id, IOpponentRepository repo, CancellationToken ct) =>
        {
            var opp = await repo.GetWithDetailsAsync(id, ct);
            return opp != null ? Results.Ok(opp) : Results.NotFound();
        });

        group.MapPut("/{id}", async (int id, [FromBody] Opponent opponent, IOpponentRepository repo, CancellationToken ct) =>
        {
            opponent.Id = id; // Ensure ID matches
            await repo.UpdateOpponentAsync(opponent, ct);
            return Results.Ok();
        });

        group.MapGet("/search", async (string? query, string? raceFilter, string? tagFilter, string? modeFilter, IOpponentRepository repo, CancellationToken ct) =>
        {
            var results = await repo.SearchAsync(query, raceFilter, tagFilter, modeFilter, ct);
            return Results.Ok(results);
        });

        group.MapGet("/recent", async (int? count, IOpponentRepository repo, CancellationToken ct) =>
        {
            try
            {
                var results = await repo.GetRecentAsync(count ?? 10, ct);
                return Results.Ok(results);
            }
            catch (Exception ex)
            {
                return Results.Problem(ex.ToString());
            }
        });

        group.MapPost("/{id}/notes", async (int id, [FromBody] AddNoteRequest req, IOpponentRepository repo, CancellationToken ct) =>
        {
            try
            {
                var note = await repo.AddNoteAsync(id, req.Content, req.Source ?? "keyboard", req.MatchRecordId, req.AutoTags, ct);
                return Results.Ok(note);
            }
            catch (Exception ex)
            {
                return Results.Problem(detail: ex.ToString(), statusCode: 500);
            }
        });

        group.MapPut("/notes/{noteId}", async (int noteId, [FromBody] UpdateNoteRequest req, IOpponentRepository repo, CancellationToken ct) =>
        {
            await repo.UpdateNoteAsync(noteId, req.Content, ct);
            return Results.Ok();
        });

        group.MapDelete("/notes/{noteId}", async (int noteId, IOpponentRepository repo, CancellationToken ct) =>
        {
            await repo.DeleteNoteAsync(noteId, ct);
            return Results.Ok();
        });

        group.MapPost("/{id}/tags", async (int id, [FromBody] AddTagRequest req, IOpponentRepository repo, CancellationToken ct) =>
        {
            await repo.AddTagAsync(id, req.TagName, ct);
            return Results.Ok();
        });

        group.MapDelete("/{id}/tags/{tagName}", async (int id, string tagName, IOpponentRepository repo, CancellationToken ct) =>
        {
            await repo.RemoveTagAsync(id, tagName, ct);
            return Results.Ok();
        });

        group.MapPost("/{id}/matches", async (int id, [FromBody] RecordMatchRequest req, IOpponentRepository repo, CancellationToken ct) =>
        {
            try
            {
                if (req.PlayedAt.HasValue && req.PlayedAt.Value.Kind != DateTimeKind.Utc)
                {
                    req.PlayedAt = req.PlayedAt.Value.Kind == DateTimeKind.Local 
                        ? req.PlayedAt.Value.ToUniversalTime() 
                        : DateTime.SpecifyKind(req.PlayedAt.Value, DateTimeKind.Utc);
                }
                var match = await repo.RecordMatchAsync(id, req, ct);
                return Results.Ok(match);
            }
            catch (KeyNotFoundException ex)
            {
                return Results.NotFound(ex.Message);
            }
            catch (Exception ex)
            {
                return Results.Problem(detail: ex.ToString(), statusCode: 500);
            }
        });

        group.MapGet("/{id}/stats", async (int id, string? raceFilter, IOpponentRepository repo, CancellationToken ct) =>
        {
            var stats = await repo.GetStatsAsync(id, ct);
            return Results.Ok(stats);
        });

        app.MapGet("/api/debug-db", async (ScoutDbContext db) =>
        {
            var pending = await db.Database.GetPendingMigrationsAsync();
            var applied = await db.Database.GetAppliedMigrationsAsync();
            return Results.Ok(new { Pending = pending, Applied = applied });
        });

        app.MapGet("/api/apply-migration", async (ScoutDbContext db) =>
        {
            try
            {
                await db.Database.ExecuteSqlRawAsync("ALTER TABLE \"Notes\" ADD \"AutoTags\" text[] NOT NULL DEFAULT ARRAY[]::text[];");
                await db.Database.ExecuteSqlRawAsync("ALTER TABLE \"Notes\" ADD \"MatchRecordId\" integer;");
                await db.Database.ExecuteSqlRawAsync("CREATE INDEX \"IX_Notes_MatchRecordId\" ON \"Notes\" (\"MatchRecordId\");");
                await db.Database.ExecuteSqlRawAsync("ALTER TABLE \"Notes\" ADD CONSTRAINT \"FK_Notes_MatchRecords_MatchRecordId\" FOREIGN KEY (\"MatchRecordId\") REFERENCES \"MatchRecords\" (\"Id\");");
                return Results.Ok("Applied");
            }
            catch (Exception ex)
            {
                return Results.Problem(ex.ToString());
            }
        });
    }
}
