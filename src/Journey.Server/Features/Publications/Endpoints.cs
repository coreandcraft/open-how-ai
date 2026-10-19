using System.IO.Compression;
using System.Security.Claims;
using System.Text.Json;
using Journey.Domain;
using Journey.Data;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Journey.Server.Features.Publications;

public static class PublicationsEndpoints
{
    private static async Task<List<Annotation>> SelectedAnnotations(
        AppDb db,
        PublishRequest input,
        string workspaceId,
        string? conversationId,
        string? journeyId
    )
    {
        var ids = (input.AnnotationIds ?? []).Distinct().ToArray();
        var annotations = await db
            .Annotations.Include(a => a.Comments)
            .Where(a => ids.Contains(a.Id) && a.WorkspaceId == workspaceId)
            .ToListAsync();
        if (
            annotations.Count != ids.Length
            || annotations.Any(a =>
                conversationId != null
                    ? a.ConversationId != conversationId || a.JourneyId != null
                    : a.JourneyId != null && a.JourneyId != journeyId
            )
        )
            throw new InvalidDataException(
                "Selected annotations are outside this publication's scope."
            );
        return annotations;
    }

    private static async Task AuthorizeSource(string kind, string id, Access access)
    {
        if (kind == "journeys")
            await access.Journey(id, "publish");
        else if (kind == "conversations")
            await access.RequireConversation(id, "publish");
        else
            throw new KeyNotFoundException();
    }

    public static void MapPublications(this WebApplication app, RouteGroupBuilder api)
    {
        app.MapDiscussions(api);
        api.MapGet(
            "/{kind:regex(^journeys$|^conversations$)}/{id}/publications",
            async (string kind, string id, AppDb db, Access access) =>
            {
                await AuthorizeSource(kind, id, access);
                return await db
                    .Publications.Where(p =>
                        kind == "journeys" ? p.JourneyId == id : p.ConversationId == id
                    )
                    .OrderByDescending(p => p.CreatedAt)
                    .Take(MaxHistory)
                    .Select(p => new
                    {
                        p.Id,
                        p.Slug,
                        p.Visibility,
                        p.CreatedAt,
                        p.DiscussionEnabled,
                        IsLegacy = p.CreatedById == null,
                        CanManageDiscussion = p.CreatedById == access.UserId,
                    })
                    .ToListAsync();
            }
        );
        api.MapDelete(
            "/{kind:regex(^journeys$|^conversations$)}/{id}/publications/{publicationId}",
            async (string kind, string id, string publicationId, AppDb db, Access access) =>
            {
                await AuthorizeSource(kind, id, access);
                var publication =
                    await db.Publications.SingleOrDefaultAsync(p =>
                        p.Id == publicationId
                        && (kind == "journeys" ? p.JourneyId == id : p.ConversationId == id)
                    ) ?? throw new KeyNotFoundException();
                db.Publications.Remove(publication);
                await db.SaveChangesAsync();
                return Results.NoContent();
            }
        );
        api.MapPost(
            "/journeys/{id}/publication-preview",
            async (string id, PublishRequest input, AppDb db, Access a) =>
            {
                var j = await a.Journey(id, "publish");
                var cs = await JourneySources(db, j, input);
                var snapshot = Publishing.Snapshot(
                    j,
                    cs,
                    input,
                    await SelectedAnnotations(db, input, j.WorkspaceId, null, j.Id)
                );
                return new { snapshot, findings = Publishing.Findings(Json.Write(snapshot)) };
            }
        );
        api.MapPost(
            "/journeys/{id}/publish",
            async (string id, PublishRequest input, AppDb db, Access a) =>
            {
                var j = await a.Journey(id, "publish");
                if (!input.Confirm || input.Visibility is not ("Public" or "Unlisted"))
                    throw new InvalidDataException(
                        "Explicit publication confirmation is required."
                    );
                var cs = await JourneySources(db, j, input);
                var snapshot = Json.Write(
                    Publishing.Snapshot(
                        j,
                        cs,
                        input,
                        await SelectedAnnotations(db, input, j.WorkspaceId, null, j.Id)
                    )
                );
                var p = new JourneyPublication
                {
                    JourneyId = id,
                    CreatedById = a.UserId,
                    DiscussionEnabled = input.DiscussionEnabled,
                    WorkspaceId = j.WorkspaceId,
                    Visibility = input.Visibility,
                    Snapshot = snapshot,
                };
                db.Publications.Add(p);
                await db.SaveChangesAsync();
                return new { p.Slug };
            }
        );
        api.MapPost(
            "/conversations/{id}/publication-preview",
            async (string id, PublishRequest input, AppDb db, Access access) =>
            {
                var c = await ConversationSource(db, access, id, input);
                var snapshot = Publishing.ConversationSnapshot(
                    c,
                    input,
                    await SelectedAnnotations(db, input, c.WorkspaceId, c.Id, null)
                );
                return new { snapshot, findings = Publishing.Findings(Json.Write(snapshot)) };
            }
        );
        api.MapPost(
            "/conversations/{id}/publish",
            async (string id, PublishRequest input, AppDb db, Access access) =>
            {
                var c = await ConversationSource(db, access, id, input);
                if (!input.Confirm || input.Visibility is not ("Public" or "Unlisted"))
                    throw new InvalidDataException("Explicit sharing confirmation is required.");
                var publication = new JourneyPublication
                {
                    ConversationId = c.Id,
                    CreatedById = access.UserId,
                    DiscussionEnabled = input.DiscussionEnabled,
                    WorkspaceId = c.WorkspaceId,
                    Visibility = input.Visibility,
                    Snapshot = Json.Write(
                        Publishing.ConversationSnapshot(
                            c,
                            input,
                            await SelectedAnnotations(db, input, c.WorkspaceId, c.Id, null)
                        )
                    ),
                };
                db.Publications.Add(publication);
                await db.SaveChangesAsync();
                return new { publication.Slug };
            }
        );
        app.MapGet(
                "/api/public/{slug}",
                async (string slug, AppDb db, HttpContext http) =>
                {
                    var head =
                        await db.Publications.Where(x => x.Slug == slug)
                            .Select(x => new { x.Id, Bytes = x.Snapshot.Length })
                            .SingleOrDefaultAsync()
                        ?? throw new KeyNotFoundException();
                    // Deleting a publication must take effect immediately, and a repaired
                    // snapshot must not keep serving the previous body, so the validator
                    // includes the snapshot size.
                    var etag = "\"" + head.Id + ":" + head.Bytes.ToString(System.Globalization.CultureInfo.InvariantCulture) + "\"";
                    http.Response.Headers.CacheControl = "public, no-cache";
                    http.Response.Headers.ETag = etag;
                    if (http.Request.Headers.IfNoneMatch.Contains(etag))
                        return Results.StatusCode(StatusCodes.Status304NotModified);
                    var snapshot = await db.Publications.Where(x => x.Id == head.Id)
                        .Select(x => x.Snapshot)
                        .SingleOrDefaultAsync() ?? throw new KeyNotFoundException();
                    return Results.Text(snapshot, "application/json");
                }
            )
            .RequireRateLimiting(PublicPolicy);
        app.MapGet(
                "/api/public",
                async (AppDb db) =>
                    await db
                        .Publications.Where(x => x.Visibility == "Public")
                        .Select(x => new { x.Slug, x.CreatedAt })
                        .ToListAsync()
            )
            .RequireRateLimiting(PublicPolicy);
    }

    const int MaxHistory = 200;

    static Task<List<Conversation>> JourneySources(
        AppDb db,
        Journey.Domain.Journey journey,
        PublishRequest input
    ) =>
        Publishing.Sources(
            db,
            journey.WorkspaceId,
            Publishing.ConversationIds(journey.Graph),
            input.MessageIds ?? []
        );

    static async Task<Conversation> ConversationSource(
        AppDb db,
        Access access,
        string id,
        PublishRequest input
    )
    {
        var workspaceId = await access.RequireConversation(id, "publish");
        return (await Publishing.Sources(db, workspaceId, [id], input.MessageIds ?? [])).Single();
    }

    public const string PublicPolicy = "public";

    public static void AddPublicRateLimit(this WebApplicationBuilder builder) =>
        builder.Services.AddRateLimiter(o =>
            o.AddPolicy(
                PublicPolicy,
                c =>
                    System.Threading.RateLimiting.RateLimitPartition.GetFixedWindowLimiter(
                        c.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                        _ => new System.Threading.RateLimiting.FixedWindowRateLimiterOptions
                        {
                            PermitLimit = builder.Environment.IsEnvironment("Testing") ? 10000 : 120,
                            Window = TimeSpan.FromMinutes(1),
                            QueueLimit = 0,
                        }
                    )
            )
        );
}


