using System.Buffers.Text;
using System.Globalization;
using System.Linq.Expressions;
using System.Security.Claims;
using System.Text;
using Journey.Domain;
using Journey.Data;
using Journey.Service.Features.Imports;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace Journey.Service.Features.Authorization;

public class Access(AppDb db, IHttpContextAccessor http)
{
    public const int DefaultMessagePage = 200;
    public const int MaxMessagePage = 500;

    /// <summary>Approximate block-text budget per message page, so one long message cannot balloon a response.</summary>
    public const int MessagePageBudget = 512 * 1024;

    public string UserId =>
        http.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier)
        ?? throw new UnauthorizedAccessException();

    public static bool Allows(string role, string action) =>
        action switch
        {
            "read" => true,
            "comment" => role is "Owner" or "Admin" or "Editor" or "Commenter",
            "edit" => role is "Owner" or "Admin" or "Editor",
            "manage" or "publish" => role is "Owner" or "Admin",
            _ => false,
        };

    public async Task<string> Require(string wid, string action = "read")
    {
        var role = await Role(wid);
        if (role == null || !Allows(role, action))
            throw new UnauthorizedAccessException("Workspace permission denied.");
        return role;
    }

    Task<string?> Role(string wid)
    {
        var uid = UserId;
        return db
            .Memberships.Where(x => x.WorkspaceId == wid && x.UserId == uid)
            .Select(x => x.Role)
            .SingleOrDefaultAsync();
    }

    /// <summary>
    /// Like <see cref="Require"/> for a resource looked up by ID: outsiders get not-found so IDs
    /// from other workspaces cannot be probed; members without the permission get forbidden.
    /// </summary>
    async Task RequireMember(string wid, string action)
    {
        var role = await Role(wid) ?? throw new KeyNotFoundException();
        if (!Allows(role, action))
            throw new UnauthorizedAccessException("Workspace permission denied.");
    }

    public async Task<global::Journey.Domain.Journey> Journey(string id, string action = "read")
    {
        var j = await db.Journeys.FindAsync(id) ?? throw new KeyNotFoundException();
        if (j.Visibility == "Private" && j.OwnerId != UserId)
            throw new KeyNotFoundException();
        await RequireMember(j.WorkspaceId, action);
        return j;
    }

    /// <summary>Checks access from conversation metadata only and returns its workspace ID.</summary>
    public async Task<string> RequireConversation(string id, string action = "read")
    {
        var workspaceId =
            await db.Conversations.Where(x => x.Id == id)
                .Select(x => x.WorkspaceId)
                .SingleOrDefaultAsync()
            ?? throw new KeyNotFoundException();
        await RequireMember(workspaceId, action);
        return workspaceId;
    }

    /// <summary>
    /// Untracked conversation with every ordered message and block. Prefer <see cref="ConversationPage"/>
    /// for anything request-driven. <see cref="Domain.Conversation.RawPayload"/> is never loaded; read raw
    /// source through the import source store.
    /// </summary>
    public async Task<Conversation> Conversation(string id, string action = "read")
    {
        await RequireConversation(id, action);
        var c = await Header(id);
        c.Messages = await db
            .Messages.AsNoTracking()
            .AsSplitQuery()
            .Where(m => m.ConversationId == id)
            .OrderBy(m => m.Order)
            .ThenBy(m => m.Id)
            .Select(MessageContent)
            .ToListAsync();
        LiftCursorTimestamps(c.Messages);
        return c;
    }

    /// <summary>Conversation metadata with the first page of messages.</summary>
    public async Task<(Conversation Conversation, string? NextCursor)> ConversationPage(
        string id,
        int? limit,
        string action = "read"
    )
    {
        await RequireConversation(id, action);
        var c = await Header(id);
        (c.Messages, var next) = await LoadMessages(id, null, limit);
        return (c, next);
    }

    /// <summary>Messages after <paramref name="cursor"/>, in conversation order.</summary>
    public async Task<(List<ConversationMessage> Items, string? NextCursor)> MessagePage(
        string id,
        string? cursor,
        int? limit
    )
    {
        await RequireConversation(id);
        return await LoadMessages(id, cursor, limit);
    }

    async Task<Conversation> Header(string id) =>
        await db.Conversations.AsNoTracking().Where(x => x.Id == id).Select(ConversationHeader).SingleOrDefaultAsync()
        ?? throw new KeyNotFoundException();

    async Task<(List<ConversationMessage>, string?)> LoadMessages(string id, string? cursor, int? limit)
    {
        var take = Math.Clamp(limit ?? DefaultMessagePage, 1, MaxMessagePage);
        var query = db.Messages.AsNoTracking().AsSplitQuery().Where(m => m.ConversationId == id);
        if (DecodeMessageCursor(cursor) is { } after)
        {
            var (order, mid) = after;
            query = query.Where(m => m.Order > order || (m.Order == order && string.Compare(m.Id, mid) > 0));
        }
        var rows = await query
            .OrderBy(m => m.Order)
            .ThenBy(m => m.Id)
            .Take(take + 1)
            .Select(MessageContent)
            .ToListAsync();
        var kept = 0;
        long size = 0;
        while (kept < Math.Min(take, rows.Count))
        {
            size += rows[kept].Metadata.Length + rows[kept].Blocks.Sum(b => (long)b.Text.Length + b.Metadata.Length);
            if (kept > 0 && size > MessagePageBudget)
                break;
            kept++;
        }
        if (kept == rows.Count)
        {
            LiftCursorTimestamps(rows);
            return (rows, null);
        }
        rows.RemoveRange(kept, rows.Count - kept);
        LiftCursorTimestamps(rows);
        var last = rows[^1];
        return (
            rows,
            Base64Url.EncodeToString(
                Encoding.UTF8.GetBytes(last.Order.ToString(CultureInfo.InvariantCulture) + ":" + last.Id)
            )
        );
    }

    static void LiftCursorTimestamps(List<ConversationMessage> messages)
    {
        foreach (var message in messages)
            CursorTranscript.Lift(message);
    }

    static (int, string)? DecodeMessageCursor(string? cursor)
    {
        if (string.IsNullOrEmpty(cursor))
            return null;
        try
        {
            var text = Encoding.UTF8.GetString(Base64Url.DecodeFromChars(cursor));
            var split = text.IndexOf(':');
            if (split > 0 && int.TryParse(text[..split], CultureInfo.InvariantCulture, out var order))
                return (order, text[(split + 1)..]);
        }
        catch (FormatException) { }
        throw new InvalidDataException("Invalid cursor.");
    }

    static readonly Expression<Func<Conversation, Conversation>> ConversationHeader = c => new Conversation
    {
        Id = c.Id,
        CreatedAt = c.CreatedAt,
        WorkspaceId = c.WorkspaceId,
        SourceId = c.SourceId,
        SourceIdentity = c.SourceIdentity,
        ApplicationProjectId = c.ApplicationProjectId,
        ExternalProjectId = c.ExternalProjectId,
        SourceProjectName = c.SourceProjectName,
        SourcePath = c.SourcePath,
        ParentConversationExternalId = c.ParentConversationExternalId,
        Provenance = c.Provenance,
        Diagnostics = c.Diagnostics,
        SourceKey = c.SourceKey,
        Provider = c.Provider,
        SourceFormat = c.SourceFormat,
        ProviderId = c.ProviderId,
        Title = c.Title,
        Model = c.Model,
        SourceCreatedAt = c.SourceCreatedAt,
        SourceUpdatedAt = c.SourceUpdatedAt,
        Demo = c.Demo,
        Metadata = c.Metadata,
    };

    static readonly Expression<Func<ConversationMessage, ConversationMessage>> MessageContent =
        m => new ConversationMessage
        {
            Id = m.Id,
            CreatedAt = m.CreatedAt,
            ConversationId = m.ConversationId,
            ProviderId = m.ProviderId,
            ParentId = m.ParentId,
            ExternalParentId = m.ExternalParentId,
            ContentHash = m.ContentHash,
            Order = m.Order,
            Role = m.Role,
            Model = m.Model,
            Timestamp = m.Timestamp,
            Metadata = m.Metadata,
            Blocks = m
                .Blocks.OrderBy(b => b.Order)
                .Select(b => new MessageContentBlock
                {
                    Id = b.Id,
                    CreatedAt = b.CreatedAt,
                    ConversationMessageId = b.ConversationMessageId,
                    Order = b.Order,
                    Kind = b.Kind,
                    Text = b.Text,
                    Metadata = b.Metadata,
                })
                .ToList(),
        };
}
