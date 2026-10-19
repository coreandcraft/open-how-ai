using System.IO.Compression;
using System.Security.Claims;
using System.Text.Json;
using Journey.Domain;
using Journey.Data;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Journey.Server.Features.Review;

public static class ReviewEndpoints
{
    public static void MapReview(this WebApplication app, RouteGroupBuilder api)
    {
        api.MapAnnotationArtifacts();
        api.MapGet(
            "/w/{wid}/annotations",
            async (
                string wid,
                string? conversationId,
                string? journeyId,
                string? nodeId,
                AppDb db,
                Access a
            ) =>
            {
                await a.Require(wid);
                if (journeyId != null)
                    await a.Journey(journeyId);
                var uid = a.UserId;
                return await db
                    .Annotations.AsNoTracking()
                    .AsSplitQuery()
                    .Include(x => x.Comments)
                    .Include(x => x.Artifacts)
                    .Where(x =>
                        x.WorkspaceId == wid
                        && (conversationId == null || x.ConversationId == conversationId)
                        && (journeyId == null || x.JourneyId == journeyId)
                        && (nodeId == null || x.NodeId == nodeId)
                        && (
                            x.JourneyId == null
                            || db.Journeys.Any(j =>
                                j.Id == x.JourneyId && (j.Visibility != "Private" || j.OwnerId == uid)
                            )
                        )
                    )
                    .OrderByDescending(x => x.CreatedAt)
                    .ThenByDescending(x => x.Id)
                    .Take(MaxAnnotations)
                    .ToListAsync();
            }
        );
        api.MapPost(
            "/w/{wid}/annotations",
            async (string wid, AnnotationInput input, AppDb db, Access a, Events events) =>
            {
                await a.Require(wid, "comment");
                var an = new Annotation
                {
                    WorkspaceId = wid,
                    AuthorId = a.UserId,
                    ConversationId = input.ConversationId,
                    MessageId = input.MessageId,
                    JourneyId = input.JourneyId,
                    NodeId = input.NodeId,
                    Kind = input.Kind ?? "comment",
                };
                if (input.JourneyId != null)
                {
                    var linkedJourney = await a.Journey(input.JourneyId);
                    if (linkedJourney.WorkspaceId != wid)
                        throw new InvalidDataException("Workspace mismatch");
                }
                if (input.ConversationId != null)
                {
                    if (await a.RequireConversation(input.ConversationId) != wid)
                        throw new InvalidDataException("Workspace mismatch");
                    if (
                        !await db.Messages.AnyAsync(m =>
                            m.Id == input.MessageId && m.ConversationId == input.ConversationId
                        )
                    )
                        throw new InvalidDataException("Message missing");
                    if (input.Anchor != null)
                    {
                        var text =
                            await db.Set<MessageContentBlock>()
                                .Where(b =>
                                    b.Id == input.Anchor.BlockId
                                    && b.ConversationMessageId == input.MessageId
                                )
                                .Select(b => b.Text)
                                .SingleOrDefaultAsync()
                            ?? throw new InvalidDataException("Block missing");
                        var recovered = Anchoring.Recover(input.Anchor, text);
                        an.Orphaned = recovered == null;
                        an.Anchor = Json.Write(recovered ?? input.Anchor);
                    }
                }
                else if (input.JourneyId != null)
                {
                    var j = await a.Journey(input.JourneyId);
                    if (
                        j.WorkspaceId != wid
                        || !JsonDocument
                            .Parse(j.Graph)
                            .RootElement.Get("nodes")
                            .Items()
                            .Any(n => n.Get("id").Str() == input.NodeId)
                    )
                        throw new InvalidDataException("Node missing");
                }
                else
                    throw new InvalidDataException("An annotation needs a message or node.");
                if (string.IsNullOrWhiteSpace(input.Body))
                    throw new InvalidDataException("Comment cannot be empty");
                db.Annotations.Add(an);
                var co = new Comment
                {
                    AnnotationId = an.Id,
                    AuthorId = a.UserId,
                    Body = input.Body,
                };
                db.Comments.Add(co);
                await Notify(db, wid, a.UserId, an, co);
                await events.Send(wid, a.UserId, "Added a comment");
                return an;
            }
        );
        api.MapPost(
            "/annotations/{id}/replies",
            async (string id, BodyInput input, AppDb db, Access a, Events events) =>
            {
                var an = await db.Annotations.FindAsync(id) ?? throw new KeyNotFoundException();
                await a.Require(an.WorkspaceId, "comment");
                if (an.JourneyId != null)
                    await a.Journey(an.JourneyId);
                if (string.IsNullOrWhiteSpace(input.Body))
                    throw new InvalidDataException("Reply cannot be empty");
                var co = new Comment
                {
                    AnnotationId = id,
                    AuthorId = a.UserId,
                    Body = input.Body,
                };
                db.Comments.Add(co);
                await Notify(db, an.WorkspaceId, a.UserId, an, co);
                await events.Send(an.WorkspaceId, a.UserId, "Replied to a discussion");
                return co;
            }
        );
        api.MapPatch(
            "/annotations/{id}",
            async (string id, ResolveInput input, AppDb db, Access a, Events events) =>
            {
                var an = await db.Annotations.FindAsync(id) ?? throw new KeyNotFoundException();
                await a.Require(an.WorkspaceId, "comment");
                if (an.JourneyId != null)
                    await a.Journey(an.JourneyId);
                an.Resolved = input.Resolved;
                await events.Send(
                    an.WorkspaceId,
                    a.UserId,
                    input.Resolved ? "Resolved discussion" : "Reopened discussion"
                );
                return an;
            }
        );
        api.MapPut(
            "/comments/{id}",
            async (string id, BodyInput input, AppDb db, Access a) =>
            {
                var co = await db.Comments.FindAsync(id) ?? throw new KeyNotFoundException();
                var an =
                    await db.Annotations.FindAsync(co.AnnotationId)
                    ?? throw new KeyNotFoundException();
                var role = await a.Require(an.WorkspaceId, "comment");
                if (an.JourneyId != null)
                    await a.Journey(an.JourneyId);
                if (co.AuthorId != a.UserId && role is not ("Owner" or "Admin"))
                    throw new UnauthorizedAccessException();
                co.Body = input.Body;
                co.UpdatedAt = DateTime.UtcNow;
                await db.SaveChangesAsync();
                return co;
            }
        );
        api.MapDelete(
            "/comments/{id}",
            async (string id, AppDb db, Access a) =>
            {
                var co = await db.Comments.FindAsync(id) ?? throw new KeyNotFoundException();
                var an =
                    await db.Annotations.FindAsync(co.AnnotationId)
                    ?? throw new KeyNotFoundException();
                var role = await a.Require(an.WorkspaceId, "comment");
                if (an.JourneyId != null)
                    await a.Journey(an.JourneyId);
                if (co.AuthorId != a.UserId && role is not ("Owner" or "Admin"))
                    throw new UnauthorizedAccessException();
                db.Comments.Remove(co);
                await db.SaveChangesAsync();
                return Results.NoContent();
            }
        );
    }

    const int MaxAnnotations = 1000;

    static async Task Notify(AppDb db, string wid, string uid, Annotation an, Comment co)
    {
        var members = await (
            from m in db.Memberships
            join u in db.Users on m.UserId equals u.Id
            where m.WorkspaceId == wid
            select u
        ).ToListAsync();
        foreach (
            var u in members.Where(u =>
                u.Id != uid
                && (
                    co.Body.Contains("@" + u.Name, StringComparison.OrdinalIgnoreCase)
                    || co.Body.Contains("@" + u.Email, StringComparison.OrdinalIgnoreCase)
                )
            )
        )
        {
            db.Set<Mention>().Add(new() { CommentId = co.Id, UserId = u.Id });
            db.Notifications.Add(
                new()
                {
                    WorkspaceId = wid,
                    UserId = u.Id,
                    Text = "You were mentioned in a discussion",
                    Link =
                        an.ConversationId != null
                            ? $"explorer/{an.ConversationId}?message={an.MessageId}&thread={an.Id}"
                            : $"canvas/{an.JourneyId}",
                }
            );
        }
    }
}
