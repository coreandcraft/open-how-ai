using Journey.Domain;
using Journey.Data;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace Journey.Server.Features.Publications;

public record DiscussionInput(string Body, string Kind = "Comment", string? ParentId = null);
public record DiscussionSetting(bool Enabled);

public static class DiscussionEndpoints
{
    public static void MapDiscussions(this WebApplication app, RouteGroupBuilder api)
    {
        app.MapGet("/api/public/{slug}/discussion", async (string slug, AppDb db, HttpContext http, int page = 0, string? kind = null) =>
        {
            var publication = await db.Publications.Where(p => p.Slug == slug)
                .Select(p => new { p.Id, p.DiscussionEnabled, p.CreatedById })
                .SingleOrDefaultAsync()
                ?? throw new KeyNotFoundException();
            var uid = http.User.FindFirstValue(ClaimTypes.NameIdentifier);
            var user = uid == null ? null : await db.Users.FindAsync(uid);
            var roots = db.PublicationComments.Where(c => c.PublicationId == publication.Id && c.ParentId == null);
            if (kind is "Comment" or "Question") roots = roots.Where(c => c.Kind == kind);
            var total = publication.DiscussionEnabled ? await roots.CountAsync() : 0;
            var rootIds = publication.DiscussionEnabled
                ? await roots.OrderByDescending(c => c.CreatedAt).ThenBy(c => c.Id)
                    .Skip(Math.Clamp(page, 0, 100000) * 20).Take(20).Select(c => c.Id).ToListAsync()
                : [];
            var entries = await db.PublicationComments
                .Where(c => c.PublicationId == publication.Id && (rootIds.Contains(c.Id) || rootIds.Contains(c.ParentId!)))
                .OrderBy(c => c.CreatedAt).ThenBy(c => c.Id)
                .Select(c => new { c.Id, c.ParentId, c.Body, c.Kind, c.CreatedAt,
                    Author = c.Author.Name, IsSharer = c.AuthorId == publication.CreatedById })
                .ToListAsync();
            return Results.Ok(new { enabled = publication.DiscussionEnabled,
                canManage = user != null && publication.CreatedById == user.Id,
                user = user == null ? null : new { user.Name }, entries, total });
        }).RequireRateLimiting(PublicationsEndpoints.PublicPolicy);

        api.MapPatch("/public/{slug}/discussion", async (string slug, DiscussionSetting input, AppDb db, Access access) =>
        {
            await using var transaction = await db.Database.BeginTransactionAsync();
            var publication = await db.Publications.SingleOrDefaultAsync(p => p.Slug == slug)
                ?? throw new KeyNotFoundException();
            if (publication.CreatedById != access.UserId) throw new UnauthorizedAccessException();
            if (publication.JourneyId != null) await access.Journey(publication.JourneyId, "publish");
            else if (publication.ConversationId != null) await access.RequireConversation(publication.ConversationId, "publish");
            else throw new KeyNotFoundException();
            publication.DiscussionEnabled = input.Enabled;
            await db.SaveChangesAsync();
            await transaction.CommitAsync();
            return Results.NoContent();
        });

        api.MapPost("/public/{slug}/discussion", async (string slug, DiscussionInput input, AppDb db, Access access) =>
        {
            await using var transaction = await db.Database.BeginTransactionAsync();
            var publication = await db.Publications.SingleOrDefaultAsync(p => p.Slug == slug)
                ?? throw new KeyNotFoundException();
            if (!publication.DiscussionEnabled) throw new UnauthorizedAccessException("Discussion is turned off.");
            var user = await db.Users.FindAsync(access.UserId) ?? throw new UnauthorizedAccessException();
            var body = input.Body?.Trim() ?? "";
            if (body.Length == 0 || body.Length > 4000 || input.Kind is not ("Comment" or "Question"))
                throw new InvalidDataException("Enter a comment or question between 1 and 4000 characters.");
            if (input.ParentId != null && !await db.PublicationComments.AnyAsync(c =>
                c.Id == input.ParentId && c.PublicationId == publication.Id && c.ParentId == null))
                throw new InvalidDataException("The reply must belong to a thread on this shared link.");
            var comment = new PublicationComment { PublicationId = publication.Id, AuthorId = user.Id,
                Body = body, Kind = input.ParentId == null ? input.Kind : "Comment", ParentId = input.ParentId };
            db.PublicationComments.Add(comment);
            await db.SaveChangesAsync();
            await transaction.CommitAsync();
            return Results.Ok(new { comment.Id });
        });
    }
}

