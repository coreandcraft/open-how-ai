using Journey.Data;
using Journey.Domain;
using Journey.Service.Features.Imports;
using Microsoft.EntityFrameworkCore;

namespace Journey.Server.Features.Imports;

public static class ImportRecoveryEndpoints
{
    public static void MapImportRecovery(this WebApplication app, RouteGroupBuilder api)
    {
        api.MapGet(
            "/conversations/{id}/recovered-source",
            async (string id, AppDb db, Access access, CancellationToken ct) =>
            {
                await access.RequireConversation(id);
                var c =
                    await db
                        .Conversations.AsSplitQuery()
                        .Include(x => x.Messages)
                        .ThenInclude(m => m.Blocks)
                        .SingleOrDefaultAsync(x => x.Id == id, ct)
                    ?? throw new KeyNotFoundException();
                await SourceRecovery.Rebuild(db, c, ct);
                await db.SaveChangesAsync(ct);
                var attachments = await db.Set<ImportedAttachment>()
                    .Where(a => a.ConversationId == c.Id && a.WorkspaceId == c.WorkspaceId)
                    .ToListAsync(ct);
                return Results.Ok(
                    new
                    {
                        attachments = attachments.Select(a => new
                        {
                            a.Id,
                            a.Name,
                            a.Availability,
                            a.BlockId,
                            a.Reference,
                            a.SourceId,
                            view = SuppliedFileTypes.Kind(a.Name),
                        }),
                        executions = await db.Set<AgentExecution>()
                            .Where(a => a.ConversationId == c.Id && a.WorkspaceId == c.WorkspaceId)
                            .ToListAsync(ct),
                        workspaces = await db.Set<SourceWorkspace>()
                            .Where(w =>
                                w.WorkspaceId == c.WorkspaceId
                                && w.Provider == c.Provider
                                && w.SourceIdentity == c.SourceIdentity
                                && w.LocalPath == c.SourcePath
                            )
                            .ToListAsync(ct),
                    }
                );
            }
        );
        api.MapGet(
            "/imported-attachments/{id}/content",
            async (
                string id,
                HttpRequest request,
                HttpResponse response,
                AppDb db,
                Access access,
                ImportSourceStore store,
                CancellationToken ct
            ) =>
            {
                var source = await OpenAttachment(id, db, access, ct);
                var download = request.Query["download"] == "1";
                var stream = store.Open(source);
                string? sniffed = null;
                if (!download && !SuppliedFileTypes.ServesInline(source.Name) && stream.CanSeek)
                {
                    var header = new byte[64];
                    var read = stream.Read(header, 0, header.Length);
                    stream.Position = 0;
                    sniffed = SuppliedFileTypes.MediaTypeFromSignature(header.AsSpan(0, read));
                }
                response.Headers.CacheControl = "private, no-store";
                response.Headers["X-Content-Type-Options"] = "nosniff";
                response.Headers.ContentDisposition = SuppliedFileTypes.ContentDisposition(
                    source.Name,
                    download,
                    sniffed != null
                );
                var media = download
                    ? "application/octet-stream"
                    : SuppliedFileTypes.ServesInline(source.Name)
                        ? SuppliedFileTypes.MediaType(source.Name)
                        : sniffed ?? "application/octet-stream";
                return Results.Stream(stream, media, enableRangeProcessing: stream.CanSeek);
            }
        );
        api.MapGet(
            "/imported-attachments/{id}/text",
            async (
                string id,
                AppDb db,
                Access access,
                ImportSourceStore store,
                CancellationToken ct
            ) =>
            {
                var source = await OpenAttachment(id, db, access, ct);
                await using var stream = store.Open(source);
                var text = SuppliedFileTypes.ReadText(stream, source.Name);
                return string.IsNullOrEmpty(text)
                    ? Results.NotFound()
                    : Results.Text(text, "text/plain; charset=utf-8");
            }
        );
        api.MapPost(
            "/import-conflicts/{id}/accept",
            async (string id, AppDb db, Access access, CancellationToken ct) =>
            {
                await using var transaction = await db.Database.BeginTransactionAsync(ct);
                var conflict =
                    await db.Set<ImportConflict>().FindAsync([id], ct)
                    ?? throw new KeyNotFoundException();
                await access.Require(conflict.WorkspaceId, "edit");
                var result = await ImportConflictReview.Accept(db, conflict, access.UserId, ct);
                await transaction.CommitAsync(ct);
                return Results.Ok(result);
            }
        );
        api.MapGet(
            "/conversations/{id}/versions",
            async (string id, AppDb db, Access access, CancellationToken ct) =>
            {
                var workspaceId = await access.RequireConversation(id);
                return await db.Set<ImportedMessageVersion>()
                    .Where(v =>
                        v.WorkspaceId == workspaceId
                        && db.Messages.Any(m => m.Id == v.MessageId && m.ConversationId == id)
                    )
                    .OrderByDescending(v => v.CreatedAt)
                    .Select(v => new
                    {
                        v.Id,
                        v.MessageId,
                        v.ConflictId,
                        v.Payload,
                        v.CreatedAt,
                    })
                    .ToListAsync(ct);
            }
        );
    }

    static async Task<ImportSource> OpenAttachment(
        string id,
        AppDb db,
        Access access,
        CancellationToken ct
    )
    {
        var attachment =
            await db.Set<ImportedAttachment>().FindAsync([id], ct)
            ?? throw new KeyNotFoundException();
        await access.RequireConversation(attachment.ConversationId);
        await access.Require(attachment.WorkspaceId);
        return await db.Sources.SingleOrDefaultAsync(
                s =>
                    s.Id == attachment.SourceId
                    && s.WorkspaceId == attachment.WorkspaceId
                    && s.AdapterId == "private-asset",
                ct
            ) ?? throw new KeyNotFoundException();
    }
}
