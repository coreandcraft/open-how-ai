using System.IO.Compression;
using System.Security.Claims;
using System.Text.Json;
using Journey.Domain;
using Journey.Data;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Journey.Server.Features.Journeys;

public static class JourneysEndpoints
{
    public static void MapJourneys(this WebApplication app, RouteGroupBuilder api)
    {
        api.MapGet(
            "/w/{wid}/journeys",
            async (string wid, string? cursor, int? limit, AppDb db, Access a) =>
            {
                await a.Require(wid);
                var uid = a.UserId;
                var take = Paging.Limit(limit);
                var rows = await db
                    .Journeys.Where(x =>
                        x.WorkspaceId == wid && (x.Visibility != "Private" || x.OwnerId == uid)
                    )
                    .Newest(cursor)
                    .Take(take + 1)
                    .Select(j => new JourneySummary(
                        j.Id,
                        j.WorkspaceId,
                        j.OwnerId,
                        j.Title,
                        j.Description,
                        j.Visibility,
                        j.Version,
                        j.CreatedAt,
                        j.NodeCount,
                        j.Preview
                    ))
                    .ToListAsync();
                await BackfillPreviews(db, rows);
                return Paging.Of(rows, take, r => (r.CreatedAt, r.Id));
            }
        );
        api.MapGet(
            "/w/{wid}/journey-options",
            async (string wid, AppDb db, Access a) =>
            {
                await a.Require(wid);
                var uid = a.UserId;
                return await db
                    .Journeys.Where(x =>
                        x.WorkspaceId == wid && (x.Visibility != "Private" || x.OwnerId == uid)
                    )
                    .OrderBy(x => x.Title)
                    .ThenBy(x => x.Id)
                    .Take(MaxOptions)
                    .Select(x => new { x.Id, x.Title, x.Version })
                    .ToListAsync();
            }
        );
        api.MapPost(
            "/w/{wid}/journeys",
            async (string wid, NameInput input, AppDb db, Access a) =>
            {
                await a.Require(wid, "edit");
                var j = new Journey.Domain.Journey
                {
                    WorkspaceId = wid,
                    OwnerId = a.UserId,
                    Title = input.Name,
                };
                db.Journeys.Add(j);
                await db.SaveChangesAsync();
                return j;
            }
        );
        api.MapPost(
            "/w/{wid}/journeys/build",
            async (string wid, BuildJourneyInput input, AppDb db, Access a, CancellationToken ct) =>
            {
                await a.Require(wid, "edit");
                if (string.IsNullOrWhiteSpace(input.Name) || input.Name.Trim().Length > 150)
                    throw new InvalidDataException("Enter a journey name of at most 150 characters.");
                if (input.ConversationIds == null || input.ConversationIds.Length == 0 ||
                    input.ConversationIds.Length > JourneyDraftBuilder.MaxConversations ||
                    input.ConversationIds.Any(string.IsNullOrWhiteSpace))
                    throw new InvalidDataException("Select between 1 and 100 conversations.");
                var graph = await JourneyDraftBuilder.Generate(db, wid, input.ConversationIds.Distinct().ToArray(), ct);
                var journey = new Journey.Domain.Journey
                {
                    WorkspaceId = wid, OwnerId = a.UserId, Title = input.Name.Trim(),
                    Visibility = "Private", Graph = graph,
                };
                db.Journeys.Add(journey);
                await db.SaveChangesAsync(ct);
                return Results.Ok(journey);
            }
        );
        api.MapDelete(
            "/journeys/{id}",
            async (string id, AppDb db, Access a) =>
            {
                var j = await a.Journey(id, "edit");
                db.Journeys.Remove(j);
                await db.Publications.Where(p => p.JourneyId == id).ExecuteDeleteAsync();
                await db.Comparisons.Where(c => c.JourneyId == id).ExecuteDeleteAsync();
                await db.Annotations.Where(an => an.JourneyId == id).ExecuteDeleteAsync();
                await db.SaveChangesAsync();
                return Results.NoContent();
            }
        );
        api.MapGet("/journeys/{id}", async (string id, Access a) => await a.Journey(id));
        api.MapPut(
            "/journeys/{id}",
            async (string id, JourneyUpdate input, AppDb db, Access a, Events events) =>
            {
                var j = await a.Journey(id, "edit");
                if (input.Version != j.Version)
                    return Results.Conflict(
                        new { error = "Another editor saved changes. Reload to merge." }
                    );
                if (input.Visibility is not ("Private" or "Team"))
                    throw new InvalidDataException("Use publishing to create public snapshots.");
                var graph = JsonDocument.Parse(input.Graph).RootElement;
                var nodes = graph.Get("nodes").Items().ToList();
                var ids = nodes.Select(n => n.Get("id").Str()).ToHashSet();
                if (ids.Contains(null) || ids.Count != nodes.Count)
                    throw new InvalidDataException("Invalid node IDs");
                var cids = nodes
                    .Select(n => n.Get("data").Get("conversationId").Str())
                    .OfType<string>()
                    .Distinct()
                    .ToArray();
                if (
                    cids.Length > 0
                    && await db.Conversations.CountAsync(c =>
                        cids.Contains(c.Id) && c.WorkspaceId == j.WorkspaceId
                    ) != cids.Length
                )
                    throw new InvalidDataException("Conversation must belong to this workspace.");
                foreach (var e in graph.Get("edges").Items())
                    if (
                        !ids.Contains(e.Get("source").Str()) || !ids.Contains(e.Get("target").Str())
                    )
                        throw new InvalidDataException("Edge endpoint missing.");
                j.Title = input.Title;
                j.Description = input.Description;
                j.Visibility = input.Visibility;
                j.Graph = input.Graph;
                j.Version++;
                await events.Send(j.WorkspaceId, a.UserId, "Updated a journey");
                return Results.Ok(j);
            }
        );
    }

    const int MaxOptions = 500;

    record JourneySummary(
        string Id,
        string WorkspaceId,
        string OwnerId,
        string Title,
        string Description,
        string Visibility,
        int Version,
        DateTime CreatedAt,
        int NodeCount,
        string Preview
    );

    /// <summary>Fills previews for journeys saved before the column existed, once per row.</summary>
    static async Task BackfillPreviews(AppDb db, List<JourneySummary> rows)
    {
        var missing = rows.Where(r => r.Preview == "").Select(r => r.Id).ToArray();
        if (missing.Length == 0)
            return;
        var graphs = await db
            .Journeys.Where(j => missing.Contains(j.Id))
            .Select(j => new { j.Id, j.Graph })
            .ToDictionaryAsync(j => j.Id, j => j.Graph);
        for (var i = 0; i < rows.Count; i++)
        {
            if (rows[i].Preview != "" || !graphs.TryGetValue(rows[i].Id, out var graph))
                continue;
            var (preview, nodeCount) = GraphPreview.Build(graph);
            var id = rows[i].Id;
            await db
                .Journeys.Where(j => j.Id == id && j.Preview == "")
                .ExecuteUpdateAsync(s => s.SetProperty(j => j.Preview, preview).SetProperty(j => j.NodeCount, nodeCount));
            rows[i] = rows[i] with { Preview = preview, NodeCount = nodeCount };
        }
    }
}
