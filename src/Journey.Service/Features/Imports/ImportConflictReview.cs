using Journey.Data;
using Journey.Domain;
using Journey.Service.Features.Review;
using Microsoft.EntityFrameworkCore;

namespace Journey.Service.Features.Imports;

public static class ImportConflictReview
{
    public static async Task<object> Accept(
        AppDb db,
        ImportConflict conflict,
        string actor,
        CancellationToken ct
    )
    {
        if (conflict.Status != "unresolved")
            throw new InvalidDataException("This conflict has already been reviewed.");
        var conversation = await db.Conversations.SingleAsync(
            c => c.Id == conflict.ConversationId && c.WorkspaceId == conflict.WorkspaceId,
            ct
        );
        var message = await db
            .Messages.Include(m => m.Blocks)
            .SingleAsync(
                m => m.Id == conflict.MessageId && m.ConversationId == conversation.Id,
                ct
            );
        if (
            (conflict.BaseHash != "" && conflict.BaseHash != message.ContentHash)
            || (
                conflict.BaseHash == ""
                && await db.Set<ImportedMessageVersion>()
                    .AnyAsync(v => v.MessageId == message.Id, ct)
            )
        )
            throw new DbUpdateConcurrencyException(
                "The message changed since this conflict was recorded. Reimport the source before reviewing it."
            );
        var incoming = Json.Read<ConversationMessage>(conflict.Incoming);
        var annotations = await db
            .Annotations.Where(a =>
                a.WorkspaceId == conflict.WorkspaceId && a.MessageId == message.Id
            )
            .ToListAsync(ct);
        db.Add(
            new ImportedMessageVersion
            {
                WorkspaceId = conflict.WorkspaceId,
                MessageId = message.Id,
                ConflictId = conflict.Id,
                Payload = Json.Write(message),
                Anchors = Json.Write(
                    annotations.Select(a => new
                    {
                        a.Id,
                        a.Anchor,
                        a.Orphaned,
                    })
                ),
                ActorId = actor,
            }
        );
        var oldBlocks = message.Blocks.OrderBy(b => b.Order).ToArray();
        var newBlocks = incoming.Blocks.OrderBy(b => b.Order).ToArray();
        // Preserve IDs for existing block slots, retaining removed blocks in the version record.
        for (var i = 0; i < newBlocks.Length; i++)
        {
            var target =
                i < oldBlocks.Length
                    ? oldBlocks[i]
                    : new MessageContentBlock { ConversationMessageId = message.Id };
            target.Order = i;
            target.Kind = newBlocks[i].Kind;
            target.Text = newBlocks[i].Text;
            target.Metadata = newBlocks[i].Metadata;
            if (i >= oldBlocks.Length)
                message.Blocks.Add(target);
        }
        foreach (var removed in oldBlocks.Skip(newBlocks.Length))
        {
            message.Blocks.Remove(removed);
            db.Remove(removed);
        }
        int recovered = 0,
            orphaned = 0;
        foreach (var annotation in annotations.Where(a => a.Anchor != "{}"))
        {
            var anchor = Json.Read<AnnotationAnchor>(annotation.Anchor);
            var matches = message
                .Blocks.Select(b => Anchoring.Recover(anchor with { BlockId = b.Id }, b.Text))
                .Where(a => a != null)
                .ToArray();
            if (matches.Length == 1)
            {
                annotation.Anchor = Json.Write(matches[0]);
                annotation.Orphaned = false;
                recovered++;
            }
            else
            {
                annotation.Orphaned = true;
                orphaned++;
            }
        }
        message.Role = incoming.Role;
        message.Model = incoming.Model;
        message.Timestamp = incoming.Timestamp;
        message.Metadata = incoming.Metadata;
        message.ContentHash = incoming.ContentHash;
        message.ExternalParentId = incoming.ExternalParentId;
        message.ParentId =
            incoming.ParentId != message.Id
            && await db.Messages.AnyAsync(
                m => m.Id == incoming.ParentId && m.ConversationId == conversation.Id,
                ct
            )
                ? incoming.ParentId
                : null;
        // Curator graph edges and comparison links retain stable message IDs.
        conflict.Status = "accepted";
        db.Activities.Add(
            new ActivityEvent
            {
                WorkspaceId = conflict.WorkspaceId,
                ActorId = actor,
                Text =
                    "Accepted imported message revision; "
                    + orphaned
                    + " annotation(s) need review",
            }
        );
        await db.SaveChangesAsync(ct);
        var refreshed = await db
            .Conversations.Include(c => c.Messages)
                .ThenInclude(m => m.Blocks)
            .SingleAsync(c => c.Id == conversation.Id, ct);
        await SourceRecovery.Rebuild(db, refreshed, ct);
        return new { recovered, orphaned };
    }
}
