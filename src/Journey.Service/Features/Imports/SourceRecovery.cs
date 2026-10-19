using System.Text.Json;
using Journey.Data;
using Journey.Domain;
using Microsoft.EntityFrameworkCore;

namespace Journey.Service.Features.Imports;

public class PrivateAssetImporter : IConversationImporter
{
    public string Provider => "Supplied attachment";
    public ImportCapability Capability =>
        new(
            "private-asset",
            Provider,
            "implemented",
            "2",
            [
                ".png",
                ".jpg",
                ".gif",
                ".webp",
                ".wav",
                ".mp3",
                ".ogg",
                ".pdf",
                ".mp4",
                ".m4v",
                ".mov",
                ".webm",
                ".mkv",
                ".avi",
            ],
            "Exact local references only; private downloads, no remote fetching or automatic publication."
        );

    public bool CanImport(SourceInput s) => Recognized(s.Bytes);

    public static bool Recognized(ReadOnlySpan<byte> bytes) =>
        bytes.StartsWith(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 })
        || bytes.StartsWith(new byte[] { 255, 216, 255 })
        || bytes.StartsWith("GIF87a"u8)
        || bytes.StartsWith("GIF89a"u8)
        || bytes.StartsWith("%PDF-"u8)
        || bytes.StartsWith("OggS"u8)
        || bytes.StartsWith("ID3"u8)
        || VideoAssetSignature.Recognized(bytes)
        || (
            bytes.Length >= 12
            && bytes[..4].SequenceEqual("RIFF"u8)
            && (
                bytes.Slice(8, 4).SequenceEqual("WEBP"u8)
                || bytes.Slice(8, 4).SequenceEqual("WAVE"u8)
            )
        );

    public Task<ImportPreview> PreviewAsync(SourceInput s, CancellationToken ct) =>
        Task.FromResult(new ImportPreview(Provider, [], []));

    public Task<ImportResult> ImportAsync(SourceInput s, CancellationToken ct) =>
        Task.FromResult(new ImportResult([]));
}

public static class SourceRecovery
{
    public static IEnumerable<string> References(JsonElement raw)
    {
        if (raw.ValueKind == JsonValueKind.Object)
            foreach (var p in raw.EnumerateObject())
            {
                if (
                    p.Name is "asset_pointer" or "file_id" or "path" or "url"
                    && p.Value.Str() is string reference
                )
                    yield return reference;
                else
                    foreach (var nested in References(p.Value))
                        yield return nested;
            }
        else if (raw.ValueKind == JsonValueKind.Array)
            foreach (var item in raw.EnumerateArray())
            foreach (var nested in References(item))
                yield return nested;
    }

    public static string Normalize(string path) => path.Replace('\\', '/').TrimStart('.', '/');

    public static string? Match(
        string reference,
        string sourceName,
        IReadOnlyList<(string Id, string Name)> files
    )
    {
        if (!TryLocalPath(reference, out var path))
            return null;
        if (path.Split('/', '\\').Any(x => x == "..") || path.Contains(':'))
            return null;
        path = Normalize(path);
        var parent = Normalize(sourceName);
        var slash = parent.LastIndexOf('/');
        var relative = slash < 0 ? path : parent[..(slash + 1)] + path;
        var matches = files
            .Where(f => Normalize(f.Name) == path || Normalize(f.Name) == relative)
            .Select(f => f.Id)
            .Distinct()
            .ToArray();
        if (matches.Length == 1)
            return matches[0];
        if (matches.Length > 1)
            return null;
        var id = FileId(path);
        if (id == null)
            return null;
        var byId = files
            .Where(f => ChatGptLibraryFiles.NamesFile(f.Name, id))
            .Select(f => f.Id)
            .Distinct()
            .ToArray();
        return byId.Length == 1 ? byId[0] : null;
    }

    static bool TryLocalPath(string reference, out string path)
    {
        if (reference.StartsWith("file-service://", StringComparison.Ordinal))
        {
            path = reference["file-service://".Length..];
            return true;
        }
        if (reference.StartsWith("sediment://", StringComparison.Ordinal))
        {
            path = reference["sediment://".Length..];
            return true;
        }
        if (reference.Contains("://"))
        {
            path = "";
            return false;
        }
        path = reference;
        return true;
    }

    static string? FileId(string path)
    {
        string? found = null;
        foreach (var segment in path.Split('/', '\\'))
        {
            if (segment.Length == 0)
                continue;
            var dot = segment.LastIndexOf('.');
            var stem = dot > 0 ? segment[..dot] : segment;
            if (ChatGptLibraryFiles.IsFileId(segment))
                found = segment;
            else if (ChatGptLibraryFiles.IsFileId(stem))
                found = stem;
        }
        return found;
    }

    public static async Task Rebuild(
        AppDb db,
        Conversation c,
        CancellationToken ct,
        ImportSource? suppliedSource = null
    )
    {
        var source = suppliedSource ?? await db.Sources.FindAsync([c.SourceId], ct);
        if (source == null)
            return;
        var assets = await db
            .Sources.Where(s =>
                s.WorkspaceId == c.WorkspaceId
                && s.BatchId == source.BatchId
                && s.AdapterId == "private-asset"
                && db.Batches.Any(b => b.Id == s.BatchId && b.IncludeAttachments)
            )
            .Select(s => new
            {
                s.Id,
                s.Name,
                s.Hash,
            })
            .ToListAsync(ct);
        var fileNames = assets.Select(s => (s.Id, s.Name)).ToArray();
        var attachments = await db.Set<ImportedAttachment>()
            .Where(a => a.ConversationId == c.Id)
            .ToListAsync(ct);
        var executions = await db.Set<AgentExecution>()
            .Where(a => a.ConversationId == c.Id)
            .ToListAsync(ct);
        foreach (var execution in executions)
        {
            execution.CallBlockId = null;
            execution.ResultBlockId = null;
            execution.ToolName = null;
            execution.Arguments = "";
            execution.Result = "";
        }
        var seenAttachments = new HashSet<string>();
        var seenExecutions = new HashSet<string>();
        foreach (var m in c.Messages.OrderBy(m => m.Order))
        foreach (var b in m.Blocks.OrderBy(b => b.Order))
        {
            JsonElement raw;
            try
            {
                raw = JsonDocument.Parse(b.Metadata).RootElement.Clone();
            }
            catch (JsonException)
            {
                continue;
            }
            foreach (var reference in References(raw).Distinct())
            {
                seenAttachments.Add(b.Id + "\n" + reference);
                var a = attachments.FirstOrDefault(a =>
                    a.BlockId == b.Id && a.Reference == reference
                );
                if (a == null)
                {
                    a = new ImportedAttachment
                    {
                        WorkspaceId = c.WorkspaceId,
                        ConversationId = c.Id,
                        BlockId = b.Id,
                        Reference = reference,
                    };
                    db.Add(a);
                    attachments.Add(a);
                }
                var matched = Match(reference, source.Name, fileNames);
                if (matched == null && a.SourceId != null)
                    continue;
                a.SourceId = matched;
                var asset = assets.FirstOrDefault(s => s.Id == a.SourceId);
                a.Availability =
                    asset != null ? "available"
                    : reference.Contains("://")
                    && !reference.StartsWith("file-service://")
                    && !reference.StartsWith("sediment://")
                        ? "remote-not-fetched"
                    : "missing-or-ambiguous";
                a.Name = asset?.Name ?? reference;
                a.Hash = asset?.Hash ?? "";
            }
            var type = raw.Get("type").Str() ?? b.Kind;
            if (type == "function" && b.Kind == "tool_call")
                type = "tool_call";
            var result = type is "tool_result" or "function_call_output";
            if (!result && type is not ("tool_use" or "tool_call" or "function_call" or "tool"))
                continue;
            var callId =
                raw.Get("call_id").Str()
                ?? raw.Get("tool_use_id").Str()
                ?? raw.Get("callID").Str()
                ?? raw.Get("id").Str();
            if (callId == null)
                continue;
            seenExecutions.Add(callId);
            var execution = executions.FirstOrDefault(e => e.ExternalCallId == callId);
            if (execution == null)
            {
                execution = new AgentExecution
                {
                    WorkspaceId = c.WorkspaceId,
                    ConversationId = c.Id,
                    ExternalCallId = callId,
                };
                executions.Add(execution);
                db.Add(execution);
            }
            if (result)
            {
                execution.ResultBlockId = b.Id;
                execution.Result = b.Metadata;
            }
            else
            {
                execution.CallBlockId = b.Id;
                execution.ToolName =
                    raw.Get("name").Str()
                    ?? raw.Get("tool").Str()
                    ?? raw.Get("function").Get("name").Str();
                execution.Arguments = b.Metadata;
                if (
                    raw.Get("result").ValueKind
                    is not (JsonValueKind.Undefined or JsonValueKind.Null)
                )
                {
                    execution.ResultBlockId = b.Id;
                    execution.Result = raw.Get("result").GetRawText();
                }
            }
            execution.Status =
                execution.CallBlockId != null && execution.ResultBlockId != null
                    ? "call-and-result-recorded"
                    : "unresolved";
        }
        db.RemoveRange(
            attachments.Where(a => !seenAttachments.Contains(a.BlockId + "\n" + a.Reference))
        );
        db.RemoveRange(executions.Where(e => !seenExecutions.Contains(e.ExternalCallId)));
        if (c.SourcePath != null)
        {
            var key = Value.Hash(c.Provider + "\n" + c.SourceIdentity + "\n" + c.SourcePath);
            if (
                !await db.Set<SourceWorkspace>()
                    .AnyAsync(w => w.WorkspaceId == c.WorkspaceId && w.IdentityKey == key, ct)
            )
                db.Add(
                    new SourceWorkspace
                    {
                        WorkspaceId = c.WorkspaceId,
                        Provider = c.Provider,
                        SourceIdentity = c.SourceIdentity,
                        IdentityKey = key,
                        LocalPath = c.SourcePath,
                        ExternalProjectId = c.ExternalProjectId,
                        Evidence = c.Provenance,
                    }
                );
        }
        if (c.ExternalProjectId != null)
        {
            var project = await db.Set<SourceProject>()
                .SingleOrDefaultAsync(
                    p =>
                        p.WorkspaceId == c.WorkspaceId
                        && p.Provider == c.Provider
                        && p.SourceIdentity == c.SourceIdentity
                        && p.ExternalId == c.ExternalProjectId,
                    ct
                );
            if (project != null)
                c.SourceProjectName = project.Name;
        }
        await db.SaveChangesAsync(ct);
    }

    public static async Task Batch(
        AppDb db,
        ImportBatch batch,
        CancellationToken ct,
        Func<Task>? checkpoint = null
    )
    {
        // Also revisits prior conversations when separately supplied exact project metadata arrives.
        // Processed keys persist the incoming batch/source even for updates whose original SourceId
        // is intentionally retained. This also makes deferred recovery safe after cancellation/retry.
        var contexts = await (
            from key in db.Set<ImportProcessedKey>()
            join source in db.Sources on key.SourceId equals source.Id
            join conversation in db.Conversations on key.Key equals conversation.SourceKey
            where source.BatchId == batch.Id && conversation.WorkspaceId == batch.WorkspaceId
            select new { conversation.Id, key.SourceId }
        ).ToListAsync(ct);
        var recoverySources = contexts.GroupBy(c => c.Id)
            .ToDictionary(g => g.Key, g => g.First().SourceId);
        var touched = recoverySources.Keys.ToArray();
        var incomingSourceIds = recoverySources.Values.Distinct().ToArray();
        var incomingSources = await db.Sources.AsNoTracking()
            .Where(s => incomingSourceIds.Contains(s.Id))
            .Select(s => new ImportSource { Id = s.Id, BatchId = s.BatchId, Name = s.Name })
            .ToDictionaryAsync(s => s.Id, ct);
        var ids = await db
            .Conversations.Where(c =>
                c.WorkspaceId == batch.WorkspaceId
                && (
                    touched.Contains(c.Id)
                    || db.Sources.Any(s => s.Id == c.SourceId && s.BatchId == batch.Id)
                    || db.Set<SourceProject>()
                        .Any(p =>
                            p.WorkspaceId == c.WorkspaceId
                            && p.Provider == c.Provider
                            && p.SourceIdentity == c.SourceIdentity
                            && p.ExternalId == c.ExternalProjectId
                            && db.Sources.Any(s => s.Id == p.SourceId && s.BatchId == batch.Id)
                        )
                )
            )
            .Select(c => c.Id)
            .ToListAsync(ct);
        foreach (var id in ids)
        {
            ct.ThrowIfCancellationRequested();
            if (checkpoint != null)
                await checkpoint();
            if (batch.Status == "cancelled")
                break;
            var c = await db
                .Conversations.AsSplitQuery()
                .Include(c => c.Messages)
                    .ThenInclude(m => m.Blocks)
                .SingleAsync(c => c.Id == id, ct);
            var incoming = recoverySources.TryGetValue(id, out var sourceId)
                ? incomingSources.GetValueOrDefault(sourceId) : null;
            await Rebuild(db, c, ct, incoming);
            foreach (
                var entry in db
                    .ChangeTracker.Entries()
                    .Where(e =>
                        e.Entity
                            is Conversation
                                or ConversationMessage
                                or MessageContentBlock
                                or AgentExecution
                                or ImportedAttachment
                                or SourceWorkspace
                                or SourceProject
                    )
                    .ToArray()
            )
                entry.State = EntityState.Detached;
        }
    }
}
