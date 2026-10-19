using System.Buffers.Text;
using System.Globalization;
using System.IO.Compression;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Journey.Domain;
using Journey.Data;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Journey.Server.Features.Conversations;

public static class ConversationsEndpoints
{
    public static void MapConversations(this WebApplication app, RouteGroupBuilder api)
    {
        api.MapGet(
            "/w/{wid}/library",
            async (
                string wid,
                string? q,
                string? provider,
                string? project,
                string? sourceGroup,
                string? cursor,
                int? limit,
                string? sort,
                AppDb db,
                Access a
            ) =>
            {
                await a.Require(wid);
                if (sort is not (null or "" or "newest" or "messages"))
                    throw new InvalidDataException("Sort by newest or message count.");
                var take = Paging.Limit(limit);
                var query = db.Conversations.Where(x => x.WorkspaceId == wid);
                if (!string.IsNullOrWhiteSpace(provider))
                    query = query.Where(x => x.Provider == provider);
                if (!string.IsNullOrWhiteSpace(project))
                    query = query.Where(x => x.ApplicationProjectId == project);
                if (!string.IsNullOrEmpty(sourceGroup))
                {
                    var group = DecodeSourceGroup(sourceGroup);
                    query = query.Where(x =>
                        x.Provider == group.Provider
                        && x.SourceIdentity == group.SourceIdentity
                        && (x.SourceProjectName ?? x.ExternalProjectId ?? x.SourcePath ?? "") == group.Name
                    );
                }
                if (!string.IsNullOrWhiteSpace(q))
                {
                    // Uncorrelated ID sets let PostgreSQL drive the text match from the trigram
                    // index once, instead of probing every block per conversation.
                    var inBlocks = db
                        .Set<MessageContentBlock>()
                        .Where(b => b.Text.Contains(q))
                        .Join(db.Messages, b => b.ConversationMessageId, m => m.Id, (b, m) => m.ConversationId);
                    var inComments = db
                        .Comments.Where(co => co.Body.Contains(q))
                        .Join(
                            db.Annotations.Where(an => an.WorkspaceId == wid && an.ConversationId != null),
                            co => co.AnnotationId,
                            an => an.Id,
                            (co, an) => an.ConversationId!
                        );
                    query = query.Where(x =>
                        x.Title.Contains(q)
                        || x.Provider.Contains(q)
                        || (x.Model != null && x.Model.Contains(q))
                        || inBlocks.Contains(x.Id)
                        || inComments.Contains(x.Id)
                    );
                }
                var byMessages = sort == "messages";
                if (byMessages && DecodeMessageCursor(cursor) is { } after)
                    query = query.Where(x =>
                        x.Messages.Count < after.Count
                        || (x.Messages.Count == after.Count && string.Compare(x.Id, after.Id) < 0)
                    );
                var rows = await (byMessages
                        ? query.OrderByDescending(x => x.Messages.Count).ThenByDescending(x => x.Id)
                        : query.Newest(cursor)
                    )
                    .Take(take + 1)
                    .Select(x => new LibraryItem(
                        x.Id,
                        x.Title,
                        x.Provider,
                        x.SourceIdentity,
                        x.Model,
                        x.SourceFormat,
                        x.SourceId,
                        x.ApplicationProjectId,
                        db.Set<ApplicationProject>()
                            .Where(p => p.Id == x.ApplicationProjectId)
                            .Select(p => p.Name)
                            .FirstOrDefault(),
                        x.ExternalProjectId,
                        x.SourceProjectName,
                        x.SourcePath,
                        x.Demo,
                        x.CreatedAt,
                        x.Messages.Count
                    ))
                    .ToListAsync();
                if (!byMessages)
                    return Paging.Of(rows, take, r => (r.CreatedAt, r.Id));
                if (rows.Count <= take)
                    return new Page<LibraryItem>(rows, null);
                rows.RemoveRange(take, rows.Count - take);
                var last = rows[^1];
                return new Page<LibraryItem>(rows, EncodeMessageCursor(last.MessageCount, last.Id));
            }
        );
        api.MapGet(
            "/w/{wid}/library-groups",
            async (string wid, AppDb db, Access a) =>
            {
                await a.Require(wid);
                var groups = await db.Conversations
                    .Where(x => x.WorkspaceId == wid)
                    .GroupBy(x => new
                    {
                        x.Provider,
                        x.SourceIdentity,
                        Name = x.SourceProjectName ?? x.ExternalProjectId ?? x.SourcePath ?? "",
                    })
                    .Select(g => new { g.Key.Provider, g.Key.SourceIdentity, g.Key.Name, Count = g.Count() })
                    .OrderBy(g => g.Provider).ThenBy(g => g.Name).ThenBy(g => g.SourceIdentity)
                    .ToListAsync();
                return groups.Select(g => new
                {
                    key = EncodeSourceGroup(g.Provider, g.SourceIdentity, g.Name),
                    g.Provider,
                    g.SourceIdentity,
                    g.Name,
                    g.Count,
                });
            }
        );
        api.MapGet(
            "/w/{wid}/summary",
            async (string wid, AppDb db, Access a) =>
            {
                await a.Require(wid);
                var uid = a.UserId;
                var providers = await db
                    .Conversations.Where(x => x.WorkspaceId == wid)
                    .GroupBy(x => x.Provider)
                    .Select(g => new { provider = g.Key, count = g.Count() })
                    .OrderBy(p => p.provider)
                    .ToListAsync();
                var journeys = await db.Journeys.CountAsync(j =>
                    j.WorkspaceId == wid && (j.Visibility != "Private" || j.OwnerId == uid)
                );
                return new { conversations = providers.Sum(p => p.count), journeys, providers };
            }
        );
        api.MapGet(
            "/conversations/{id}",
            async (string id, int? limit, Access a) =>
            {
                var (c, next) = await a.ConversationPage(id, limit);
                return View(c, next);
            }
        );
        api.MapDelete(
            "/conversations/{id}",
            async (string id, AppDb db, Access a, Events events) =>
            {
                var wid = await a.RequireConversation(id, "edit");
                await DeleteConversations(db, db.Conversations.Where(c => c.Id == id));
                await events.Send(wid, a.UserId, "Deleted a conversation");
                return Results.NoContent();
            }
        );
        api.MapDelete(
            "/w/{wid}/projects/{projectId}/conversations",
            async (string wid, string projectId, AppDb db, Access a, Events events) =>
            {
                await a.Require(wid, "edit");
                if (
                    !await db.Set<ApplicationProject>()
                        .AnyAsync(p => p.Id == projectId && p.WorkspaceId == wid)
                )
                    throw new KeyNotFoundException();
                var deleted = await DeleteConversations(
                    db,
                    db.Conversations.Where(c =>
                        c.WorkspaceId == wid && c.ApplicationProjectId == projectId
                    )
                );
                if (deleted > 0)
                    await events.Send(
                        wid,
                        a.UserId,
                        deleted == 1
                            ? "Deleted a conversation"
                            : "Deleted " + deleted + " conversations"
                    );
                return Results.Ok(new { deleted });
            }
        );
        api.MapGet(
            "/conversations/{id}/messages",
            async (string id, string? cursor, int? limit, Access a) =>
            {
                var (items, next) = await a.MessagePage(id, cursor, limit);
                return new Page<ConversationMessage>(items, next);
            }
        );
        api.MapGet(
            "/conversations/{id}/journeys",
            async (string id, AppDb db, Access a) =>
            {
                var wid = await a.RequireConversation(id);
                var uid = a.UserId;
                // The text match narrows candidates in SQL; parsing confirms a real node reference.
                var candidates = await db
                    .Journeys.Where(j =>
                        j.WorkspaceId == wid
                        && (j.Visibility != "Private" || j.OwnerId == uid)
                        && j.Graph.Contains(id)
                    )
                    .OrderByDescending(j => j.CreatedAt)
                    .Select(j => new { j.Id, j.Title, j.Graph })
                    .ToListAsync();
                return candidates
                    .Where(j =>
                        JsonDocument
                            .Parse(j.Graph)
                            .RootElement.Get("nodes")
                            .Items()
                            .Any(n => n.Get("data").Get("conversationId").Str() == id)
                    )
                    .Select(j => new { j.Id, j.Title });
            }
        );
        api.MapGet(
            "/sources/{id}",
            async (string id, AppDb db, Access a, ImportSourceStore store) =>
            {
                var s = await db.Sources.FindAsync(id) ?? throw new KeyNotFoundException();
                await a.Require(s.WorkspaceId);
                return Results.Stream(store.Open(s), "application/octet-stream", Path.GetFileName(s.Name), enableRangeProcessing: true);
            }
        );
    }

    record LibraryItem(
        string Id,
        string Title,
        string Provider,
        string SourceIdentity,
        string? Model,
        string SourceFormat,
        string SourceId,
        string? ApplicationProjectId,
        string? ProjectName,
        string? ExternalProjectId,
        string? SourceProjectName,
        string? SourcePath,
        bool Demo,
        DateTime CreatedAt,
        int MessageCount
    )
    {
        public string SourceGroupKey => EncodeSourceGroup(
            Provider, SourceIdentity, SourceProjectName ?? ExternalProjectId ?? SourcePath ?? ""
        );
    }

    record SourceGroup(string Provider, string SourceIdentity, string Name);

    static string EncodeSourceGroup(string provider, string identity, string name) =>
        Base64Url.EncodeToString(JsonSerializer.SerializeToUtf8Bytes(new SourceGroup(provider, identity, name)));

    static SourceGroup DecodeSourceGroup(string key)
    {
        try
        {
            var group = JsonSerializer.Deserialize<SourceGroup>(Base64Url.DecodeFromChars(key));
            if (group is { Provider: not null, SourceIdentity: not null, Name: not null })
                return group;
        }
        catch (Exception e) when (e is FormatException or JsonException or ArgumentException) { }
        throw new InvalidDataException("Invalid source group.");
    }

    static async Task<int> DeleteConversations(AppDb db, IQueryable<Conversation> conversations)
    {
        var ids = conversations.Select(c => c.Id);
        await using var transaction = await db.Database.BeginTransactionAsync();
        var annotations = db.Annotations.Where(x => ids.Contains(x.ConversationId)).Select(x => x.Id);
        var comments = db.Comments.Where(c => annotations.Contains(c.AnnotationId)).Select(c => c.Id);
        await db.Set<Mention>().Where(m => comments.Contains(m.CommentId)).ExecuteDeleteAsync();
        var artifacts = db
            .Set<AnnotationArtifact>()
            .Where(x => annotations.Contains(x.AnnotationId))
            .Select(x => x.Id);
        await db.Set<ArtifactBlob>().Where(b => artifacts.Contains(b.Id)).ExecuteDeleteAsync();
        await db.Set<AnnotationArtifact>().Where(x => annotations.Contains(x.AnnotationId)).ExecuteDeleteAsync();
        await db.Comments.Where(c => annotations.Contains(c.AnnotationId)).ExecuteDeleteAsync();
        await db.Annotations.Where(x => ids.Contains(x.ConversationId)).ExecuteDeleteAsync();
        await db.Set<ImportedAttachment>().Where(x => ids.Contains(x.ConversationId)).ExecuteDeleteAsync();
        await db.Set<AgentExecution>().Where(x => ids.Contains(x.ConversationId)).ExecuteDeleteAsync();
        await db.Set<ConversationBranch>().Where(x => ids.Contains(x.ConversationId)).ExecuteDeleteAsync();
        await db.Set<ConversationAttachment>().Where(x => ids.Contains(x.ConversationId)).ExecuteDeleteAsync();
        await db.Set<ImportConflict>().Where(x => ids.Contains(x.ConversationId)).ExecuteDeleteAsync();
        await db
            .Set<SourceRelationship>()
            .Where(x => ids.Contains(x.ConversationId) || ids.Contains(x.TargetConversationId))
            .ExecuteDeleteAsync();
        var messages = db.Messages.Where(m => ids.Contains(m.ConversationId)).Select(m => m.Id);
        await db.Set<ImportedMessageVersion>().Where(v => messages.Contains(v.MessageId)).ExecuteDeleteAsync();
        var publications = db.Publications.Where(p => ids.Contains(p.ConversationId)).Select(p => p.Id);
        await db.PublicationComments.Where(c => publications.Contains(c.PublicationId)).ExecuteDeleteAsync();
        await db.Publications.Where(p => ids.Contains(p.ConversationId)).ExecuteDeleteAsync();
        var deleted = await db.Conversations.Where(c => ids.Contains(c.Id)).ExecuteDeleteAsync();
        await transaction.CommitAsync();
        return deleted;
    }

    static string EncodeMessageCursor(int count, string id) =>
        Base64Url.EncodeToString(
            Encoding.UTF8.GetBytes("m:" + count.ToString(CultureInfo.InvariantCulture) + ":" + id)
        );

    static (int Count, string Id)? DecodeMessageCursor(string? cursor)
    {
        if (string.IsNullOrEmpty(cursor))
            return null;
        try
        {
            var text = Encoding.UTF8.GetString(Base64Url.DecodeFromChars(cursor));
            if (!text.StartsWith("m:", StringComparison.Ordinal))
                throw new InvalidDataException("Invalid cursor.");
            var rest = text[2..];
            var split = rest.IndexOf(':');
            if (
                split > 0
                && int.TryParse(rest[..split], NumberStyles.None, CultureInfo.InvariantCulture, out var count)
            )
                return (count, rest[(split + 1)..]);
        }
        catch (InvalidDataException)
        {
            throw;
        }
        catch (FormatException) { }
        catch (ArgumentOutOfRangeException) { }
        throw new InvalidDataException("Invalid cursor.");
    }

    static object View(Conversation c, string? nextCursor) =>
        new
        {
            nextCursor,
            c.Id,
            c.CreatedAt,
            c.WorkspaceId,
            c.SourceId,
            c.SourceIdentity,
            c.ApplicationProjectId,
            c.ExternalProjectId,
            c.SourceProjectName,
            c.SourcePath,
            c.ParentConversationExternalId,
            c.Provenance,
            c.Diagnostics,
            c.SourceKey,
            c.Provider,
            c.SourceFormat,
            c.ProviderId,
            c.Title,
            c.Model,
            c.SourceCreatedAt,
            c.SourceUpdatedAt,
            c.Demo,
            c.Metadata,
            c.Messages,
        };
}
