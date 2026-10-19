using Journey.Data;
using Journey.Domain;
using Microsoft.EntityFrameworkCore;

namespace Journey.Service.Features.Imports;

public static class IncrementalImport
{
    public static async Task Save(
        AppDb db,
        ImportBatch batch,
        ImportSource source,
        Conversation incoming,
        CancellationToken ct,
        bool recover = true
    )
    {
        if (
            await db.Conversations.AnyAsync(
                c => c.WorkspaceId == batch.WorkspaceId && c.SourceKey == incoming.SourceKey,
                ct
            )
        )
        {
            batch.Duplicates++;
            if (!recover)
                return;
            var duplicate = await db
                .Conversations.AsSplitQuery()
                .Include(c => c.Messages)
                    .ThenInclude(m => m.Blocks)
                .FirstAsync(
                    c => c.WorkspaceId == batch.WorkspaceId && c.SourceKey == incoming.SourceKey,
                    ct
                );
            await SourceRecovery.Rebuild(db, duplicate, ct, source);
            return;
        }
        Conversation? existing = null;
        if (incoming.ProviderId != null)
            existing = await db
                .Conversations.AsSplitQuery()
                .Include(c => c.Messages)
                    .ThenInclude(m => m.Blocks)
                .FirstOrDefaultAsync(
                    c =>
                        c.WorkspaceId == batch.WorkspaceId
                        && c.Provider == incoming.Provider
                        && c.SourceIdentity == incoming.SourceIdentity
                        && c.ProviderId == incoming.ProviderId,
                    ct
                );
        if (existing != null && !batch.UpdateExisting)
        {
            batch.Duplicates++;
            return;
        }
        if (existing == null)
        {
            incoming.WorkspaceId = batch.WorkspaceId;
            incoming.SourceId = source.Id;
            incoming.ApplicationProjectId = batch.ApplicationProjectId;
            db.Conversations.Add(incoming);
            batch.Imported++;
            existing = incoming;
        }
        else
        {
            var oldByExternal = existing
                .Messages.Where(m => m.ProviderId != null)
                .GroupBy(m => m.ProviderId!)
                .ToDictionary(g => g.Key, g => g.First());
            var idMap = incoming.Messages.ToDictionary(
                m => m.Id,
                m =>
                    m.ProviderId != null && oldByExternal.TryGetValue(m.ProviderId, out var old)
                        ? old.Id
                        : m.Id
            );
            foreach (var message in incoming.Messages)
            {
                message.ParentId =
                    message.ParentId != null ? idMap.GetValueOrDefault(message.ParentId) : null;
                if (
                    message.ProviderId != null
                    && oldByExternal.TryGetValue(message.ProviderId, out var old)
                )
                {
                    if (
                        ImportedMessageFingerprint.Compute(old)
                        != ImportedMessageFingerprint.Compute(message)
                    )
                    {
                        db.Add(
                            new ImportConflict
                            {
                                WorkspaceId = batch.WorkspaceId,
                                BatchId = batch.Id,
                                ConversationId = existing.Id,
                                MessageId = old.Id,
                                Incoming = Json.Write(message),
                                BaseHash = old.ContentHash,
                            }
                        );
                        batch.Conflicts++;
                    }
                }
                else if (
                    message.ProviderId == null
                    && existing.Messages.Any(m => m.ContentHash == message.ContentHash)
                )
                    continue;
                else
                {
                    message.ConversationId = existing.Id;
                    message.Order = existing.Messages.Count;
                    existing.Messages.Add(message);
                }
            }
            existing.SourceKey = incoming.SourceKey;
            existing.SourceUpdatedAt = incoming.SourceUpdatedAt;
            // Original source, editorial associations and existing message/block IDs are not replaced.
            batch.Updated++;
        }
        if (
            incoming.ExternalProjectId != null
            && !await db.Set<SourceProject>()
                .AnyAsync(
                    p =>
                        p.WorkspaceId == batch.WorkspaceId
                        && p.Provider == incoming.Provider
                        && p.SourceIdentity == incoming.SourceIdentity
                        && p.ExternalId == incoming.ExternalProjectId,
                    ct
                )
        )
            db.Add(
                new SourceProject
                {
                    WorkspaceId = batch.WorkspaceId,
                    Provider = incoming.Provider,
                    SourceIdentity = incoming.SourceIdentity,
                    ExternalId = incoming.ExternalProjectId,
                    Name = incoming.SourceProjectName ?? incoming.ExternalProjectId,
                    SourceId = source.Id,
                    Metadata = incoming.Metadata,
                }
            );
        if (
            incoming.ParentConversationExternalId != null
            && !await db.Set<SourceRelationship>()
                .AnyAsync(
                    r =>
                        r.ConversationId == existing.Id
                        && r.ExternalTargetId == incoming.ParentConversationExternalId,
                    ct
                )
        )
            db.Add(
                new SourceRelationship
                {
                    WorkspaceId = batch.WorkspaceId,
                    ConversationId = existing.Id,
                    ExternalTargetId = incoming.ParentConversationExternalId,
                    Evidence = incoming.Provenance,
                }
            );
        await db.SaveChangesAsync(ct);
        if (recover)
            await SourceRecovery.Rebuild(db, existing, ct, source);
    }

    public static async Task Resolve(AppDb db, string workspaceId, CancellationToken ct)
    {
        foreach (
            var r in await db.Set<SourceRelationship>()
                .Where(r => r.WorkspaceId == workspaceId && r.Status == "unresolved")
                .ToListAsync(ct)
        )
        {
            var child = await db.Conversations.FindAsync([r.ConversationId], ct);
            if (child == null)
                continue;
            var parent = await db.Conversations.FirstOrDefaultAsync(
                c =>
                    c.WorkspaceId == workspaceId
                    && c.Provider == child.Provider
                    && c.SourceIdentity == child.SourceIdentity
                    && c.ProviderId == r.ExternalTargetId,
                ct
            );
            if (parent != null && parent.Id != child.Id)
            {
                r.TargetConversationId = parent.Id;
                r.Status = "confirmed";
            }
        }
        await db.SaveChangesAsync(ct);
    }
}

public class ImportWorker(
    IServiceScopeFactory scopes,
    ILogger<ImportWorker> logger,
    ImportProgressStore progressStore
) : BackgroundService
{
    readonly string owner = $"{Environment.MachineName}:{Environment.ProcessId}:{Guid.NewGuid():N}";

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                using var scope = scopes.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDb>();
                var batch = await ImportLease.Next(db, owner, ct);
                if (batch != null)
                {
                    try
                    {
                        await Process(
                            db,
                            scope.ServiceProvider.GetRequiredService<ImportRegistry>(),
                            scope.ServiceProvider.GetRequiredService<ImportSourceStore>(),
                            batch,
                            ct
                        );
                    }
                    catch (ImportLeaseLostException)
                    {
                        logger.LogWarning(
                            "Import {BatchId} was taken over by another worker",
                            batch.Id
                        );
                    }
                    finally
                    {
                        await ImportLease.Release(db, batch.Id, owner);
                    }
                    continue;
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Import worker error");
            }
            await Task.Delay(750, ct);
        }
    }

    async Task Process(AppDb db, ImportRegistry registry, ImportSourceStore sourceStore,
        ImportBatch batch, CancellationToken hostToken)
    {
        var progress = progressStore.Begin("", batch.WorkspaceId, batch.Id);
        await using var monitor = new ImportRunMonitor(scopes, batch.Id, owner, logger, hostToken);
        try
        {
            await ProcessCore(db, registry, sourceStore, batch, monitor.Token, progress);
            monitor.ThrowIfLeaseLost();
        }
        catch (OperationCanceledException) when (!hostToken.IsCancellationRequested)
        {
            monitor.ThrowIfLeaseLost();
            db.ChangeTracker.Clear();
            await db.Entry(batch).ReloadAsync(hostToken);
            if (batch.Status != "cancelled")
                throw;
            progress.Stage("cancelled");
        }
        finally
        {
            if (progress.Read().Stage is "processing" or "recovering")
                progress.Stage(batch.Status == "cancelled" ? "cancelled" : "stopped");
            progress.Finish();
            progress.Log(logger, "commit", batch.Id);
        }
    }

    async Task ProcessCore(
        AppDb db,
        ImportRegistry registry,
        ImportSourceStore sourceStore,
        ImportBatch batch,
        CancellationToken ct,
        ImportProgress fileProgress
    )
    {
        var selected = Json.Read<List<string>>(batch.Selected).ToHashSet();
        var sanitization = Json.Read<SanitizationOptions>(batch.Sanitization);
        List<string> sourceIds;
        using (fileProgress.Measure("sourceMetadata"))
            sourceIds = await db.Sources.Where(s => s.BatchId == batch.Id && !s.Container)
                .Select(s => s.Id).ToListAsync(ct);
        fileProgress.Total(sourceIds.Count);
        fileProgress.Stage("processing");
        foreach (var sourceId in sourceIds)
        {
            await ImportLease.Checkpoint(db, batch, owner, ct);
            if (batch.Status == "cancelled")
            {
                fileProgress.Stage("cancelled");
                return;
            }
            var source = await db.Sources.SingleAsync(s => s.Id == sourceId, ct);
            try
            {
                if (source.AdapterId == "private-asset")
                {
                    fileProgress.Processed();
                    db.Entry(source).State = EntityState.Detached;
                    continue;
                }
                var processed = await ImportLease.ProcessedKeys(db, source, ct);
                await foreach (var input in StreamingImportInputs.Read(source, sourceStore, ct, fileProgress))
                {
                    ct.ThrowIfCancellationRequested();
                    IConversationImporter adapter;
                    using (fileProgress.Measure("detection"))
                        adapter = registry.Detect(input,
                            source.AdapterId == "mixed-records" ? null : source.AdapterId);
                    ImportResult result;
                    using (fileProgress.Measure("parsing"))
                        result = await adapter.ImportAsync(input, ct);
                    ct.ThrowIfCancellationRequested();
                    fileProgress.Count("parsedBytes", input.Bytes.Length);
                    foreach (
                        var project in (result.Projects ?? []).Where(p =>
                            selected.Contains(ClaudeProjectImporter.Key(p))
                            && !processed.Contains(ClaudeProjectImporter.Key(p))
                        )
                    )
                    {
                        var key = ClaudeProjectImporter.Key(project);
                        project.Name = ImportSanitization.Text(project.Name, sanitization);
                        project.Metadata = ImportSanitization.Payload(
                            project.Metadata,
                            sanitization
                        );
                        var old = await db.Set<SourceProject>()
                            .SingleOrDefaultAsync(
                                p =>
                                    p.WorkspaceId == batch.WorkspaceId
                                    && p.Provider == project.Provider
                                    && p.SourceIdentity == project.SourceIdentity
                                    && p.ExternalId == project.ExternalId,
                                ct
                            );
                        if (old == null)
                        {
                            project.WorkspaceId = batch.WorkspaceId;
                            project.SourceId = source.Id;
                            db.Add(project);
                            batch.Imported++;
                        }
                        else if (old.Metadata == project.Metadata)
                            batch.Duplicates++;
                        else
                        {
                            old.Name = project.Name;
                            old.Metadata = project.Metadata;
                            old.SourceId = source.Id;
                            batch.Updated++;
                        }
                        db.Add(new ImportProcessedKey { SourceId = source.Id, Key = key });
                        await db.SaveChangesAsync(ct);
                        processed.Add(key);
                    }
                    foreach (
                        var c in result.Conversations.Where(c =>
                            selected.Contains(c.SourceKey) && !processed.Contains(c.SourceKey)
                        )
                    )
                    {
                        await ImportLease.Checkpoint(db, batch, owner, ct);
                        if (batch.Status == "cancelled")
                        {
                            fileProgress.Stage("cancelled");
                            return;
                        }
                        using var persistenceTiming = fileProgress.Measure("persistence");
                        await using var transaction = await db.Database.BeginTransactionAsync(ct);
                        ImportSanitization.Apply(c, sanitization);
                        await IncrementalImport.Save(db, batch, source, c, ct, recover: false);
                        db.Add(new ImportProcessedKey { SourceId = source.Id, Key = c.SourceKey });
                        await db.SaveChangesAsync(ct);
                        await transaction.CommitAsync(ct);
                        processed.Add(c.SourceKey);
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
                                            or SourceRelationship
                                            or ImportConflict
                                            or ImportProcessedKey
                                )
                                .ToArray()
                        )
                            entry.State = EntityState.Detached;
                    }
                }
            }
            catch (Exception ex)
                when (ex is not ImportLeaseLostException
                    && (ex is not OperationCanceledException || !ct.IsCancellationRequested))
            {
                db.ChangeTracker.Clear();
                db.Attach(batch);
                batch.Error += source.Name + ": " + ex.Message + "\n";
                await db.SaveChangesAsync(ct);
            }
            fileProgress.Processed();
            db.Entry(source).State = EntityState.Detached;
        }
        fileProgress.Stage("recovering");
        await ImportLease.Checkpoint(db, batch, owner, ct);
        using (fileProgress.Measure("recovery"))
        {
            if (batch.Status != "cancelled")
            {
                await IncrementalImport.Resolve(db, batch.WorkspaceId, ct);
                await SourceRecovery.Batch(db, batch, ct,
                    () => ImportLease.Checkpoint(db, batch, owner, ct));
            }
        }
        await ImportLease.Checkpoint(db, batch, owner, ct);
        if (batch.Status != "cancelled")
            batch.Status = batch.Error.Length > 0 ? "completed-with-errors" : "completed";
        await db.SaveChangesAsync(ct);
        fileProgress.Stage(batch.Status);
    }
}
