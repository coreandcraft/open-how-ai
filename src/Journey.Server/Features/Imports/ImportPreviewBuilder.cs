using System.Text.Json;
using Journey.Data;
using Journey.Domain;
using Microsoft.EntityFrameworkCore;

namespace Journey.Server.Features.Imports;

// Runs with its own scope for queued previews; never captures the HTTP request or its DbContext.
public sealed class ImportPreviewBuilder(AppDb db, ImportRegistry registry, ImportSourceStore store,
    ILogger<ImportPreviewBuilder> logger)
{
    sealed record SavedSource(ImportSource Source, Stream Local) : IAsyncDisposable
    {
        public ValueTask DisposeAsync() => Local.DisposeAsync();
    }

    public async Task<IResult> Build(
        string wid,
        IFormCollection form,
        long maxBytes,
        ImportProgress fileProgress,
        CancellationToken ct,
        bool logSummary = true
    )
    {
        fileProgress.Count("sourceReadbacks", 0);
        long uploaded = 0;
        foreach (var file in form.Files)
        {
            ImportLimits.Check(uploaded, file.Length, maxBytes);
            uploaded += file.Length;
        }
        var identity = form["sourceIdentity"].ToString().Trim();
        if (identity == "")
            identity = "default";
        var mapping = form["mapping"].ToString();
        if (mapping == "")
            mapping = "{}";
        var selectedAdapter = form["adapterId"].ToString();
        var batch = new ImportBatch { WorkspaceId = wid };
        var sanitization = string.IsNullOrEmpty(form["sanitization"])
            ? new SanitizationOptions()
            : Json.Read<SanitizationOptions>(form["sanitization"].ToString());
        if (sanitization == null)
            throw new InvalidDataException("Provide valid sanitization options.");
        ImportSanitization.Validate(sanitization.Rules);
        // New imports sanitize before storage. Never persist the sensitive search terms,
        // and never apply rules again during preview, commit or retry.
        batch.Sanitization = "{}";
        var storedIdentity = ImportSanitization.Text(identity, sanitization);
        var storedMapping = ImportSanitization.Payload(mapping, sanitization);
        var previews = new List<ConversationPreview>();
        var attachmentPreviews = new List<object>();
        var retainedOriginals = new List<string>();
        var availableKeys = new HashSet<string>();
        var projectPreviews = new List<object>();
        var errors = new List<string>();
        var skippedFiles = new List<object>();
        var detected = new List<object>();
        var library = new Dictionary<string, ChatGptLibraryFiles.Entry>(
            StringComparer.OrdinalIgnoreCase
        );
        var libraryAmbiguous = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        long total = 0;
        var stored = new List<ImportSource>();
        bool saved = false;
        try
        {
            fileProgress.Stage("counting");
            var invalidArchives = new HashSet<IFormFile>();
            var archives = new HashSet<IFormFile>();
            var totalFiles = 0;
            // Uploaded bytes and each extracted archive entry both count toward the quota.
            long compressed = uploaded;
            long uncompressed = 0;
            long used = uploaded;
            void ReportSizes() =>
                fileProgress.Bytes(compressed, uncompressed, used, maxBytes);
            ReportSizes();
            using (fileProgress.Measure("archiveScan"))
            foreach (var file in form.Files)
            {
                await using var scan = file.OpenReadStream();
                if (!await ArchiveInspector.IsArchive(scan, ct))
                {
                    totalFiles++;
                    uncompressed += file.Length;
                    ReportSizes();
                    continue;
                }
                archives.Add(file);
                var entries = 0;
                try
                {
                    await foreach (var entry in ArchiveInspector.Entries(scan, ct))
                    {
                        ImportLimits.Check(used, entry.Size, maxBytes);
                        used += entry.Size;
                        uncompressed += entry.Size;
                        ReportSizes();
                        entries++;
                    }
                    totalFiles += entries;
                }
                catch (Exception ex)
                    when (ex is InvalidDataException or ArgumentException)
                {
                    invalidArchives.Add(file);
                    totalFiles++;
                }
            }
            fileProgress.Total(totalFiles);
            fileProgress.Stage("processing");
            void Skip(string name, string reason)
            {
                skippedFiles.Add(
                    new { name = ImportSanitization.Text(name, sanitization), reason }
                );
                fileProgress.Processed(true);
            }
            async Task<SavedSource> Save(string name, Stream content, long size, bool container)
            {
                ImportLimits.Check(total, size, maxBytes);
                var source = new ImportSource
                {
                    WorkspaceId = wid, BatchId = batch.Id,
                    Name = ImportSanitization.Text(name, sanitization, ct),
                    SourceIdentity = storedIdentity, Mapping = storedMapping, Container = container,
                };
                Stream? local = null;
                try
                {
                    if (sanitization.Enabled && size <= ImportSanitization.MaxSourceBytes)
                    {
                        SanitizedSource prepared;
                        using (fileProgress.Measure("sanitization"))
                            prepared = await ImportSanitization.PrepareSource(content, name, sanitization, ct);
                        if (prepared.OriginalRetained)
                            retainedOriginals.Add(source.Name);
                        local = new MemoryStream(prepared.Bytes, false);
                    }
                    else
                    {
                        if (sanitization.Enabled)
                        {
                            if (!sanitization.AllowOriginalFiles)
                                throw new OriginalFileConsentRequiredException();
                            retainedOriginals.Add(source.Name);
                        }
                        if (!container)
                        {
                            local = ImportTemporaryFile.Create();
                            using (fileProgress.Measure("extraction"))
                                await ImportTemporaryFile.Copy(content, local, maxBytes - total, ct);
                        }
                    }
                    using (fileProgress.Measure("sourceStorage"))
                        await store.Save(source, local ?? content, maxBytes - total, maxBytes, ct);
                    fileProgress.Count("storedBytes", source.Size);
                    if (local != null)
                        local.Position = 0;
                    stored.Add(source);
                    total += source.Size;
                    db.Sources.Add(source);
                    return new SavedSource(source, local ?? Stream.Null);
                }
                catch
                {
                    if (local != null)
                        await local.DisposeAsync();
                    throw;
                }
            }
            async Task<bool> Parse(ImportSource source, Stream local)
            {
                try
                {
                    await foreach (
                        var input in StreamingImportInputs.Read(source, local, ct, fileProgress)
                    )
                    {
                        IConversationImporter adapter;
                        using (fileProgress.Measure("detection"))
                        {
                            var candidates = registry.Candidates(input);
                            if (source.AdapterId == "")
                                detected.Add(new { input.Name, candidates });
                            adapter = registry.Detect(input, selectedAdapter, candidates);
                        }
                        source.AdapterId =
                            source.AdapterId == ""
                            || source.AdapterId == adapter.Capability.Id
                                ? adapter.Capability.Id
                                : "mixed-records";
                        ImportPreview result;
                        using (fileProgress.Measure("parsing"))
                            result = await adapter.PreviewAsync(input, ct);
                        fileProgress.Count("parsedBytes", input.Bytes.Length);
                        fileProgress.Count("conversations", result.Conversations.Count);
                        if (adapter is PrivateAssetImporter)
                            attachmentPreviews.Add(
                                new
                                {
                                    source.Name,
                                    source.Size,
                                    source.Hash,
                                }
                            );
                        foreach (var c in result.Conversations)
                        {
                            availableKeys.Add(c.SourceKey);
                            previews.Add(new ConversationPreview(c, adapter.Capability,
                                retainedOriginals.Contains(source.Name)));
                        }
                        foreach (var project in result.Projects ?? [])
                        {
                            var key = ClaudeProjectImporter.Key(project);
                            availableKeys.Add(key);
                            projectPreviews.Add(
                                new
                                {
                                    key,
                                    project.Name,
                                    project.Provider,
                                    project.ExternalId,
                                }
                            );
                        }
                        errors.AddRange(
                            result.Warnings.Select(w => input.Name + ": " + w)
                        );
                    }
                    return true;
                }
                catch (Exception ex)
                    when (ex
                            is InvalidDataException
                                or JsonException
                                or ArgumentException
                                or Microsoft.Data.Sqlite.SqliteException
                    )
                {
                    errors.Add(source.Name + ": " + ex.Message);
                    return false;
                }
            }
            async Task ProcessFile(string name, Stream content, long size)
            {
                if (ZstandardImport.IsTranscript(name))
                {
                    try
                    {
                        // Keep a seekable copy for retaining the original compressed source.
                        // Decode before sanitizing so no unsanitized container is retained by
                        // sanitized imports. Both uploaded and expanded bytes count to quota.
                        await using var compressedSource = ImportTemporaryFile.Create();
                        await ImportTemporaryFile.Copy(content, compressedSource, maxBytes, ct);
                        await using var expanded = ImportTemporaryFile.Create();
                        await ZstandardImport.Decode(compressedSource, expanded, used, maxBytes, ct);
                        used += expanded.Length;
                        uncompressed += expanded.Length - size;
                        ReportSizes();
                        if (!sanitization.Enabled)
                        {
                            compressedSource.Position = 0;
                            await using var container = await Save(name, compressedSource, size, true);
                        }
                        await ProcessFile(name[..^4], expanded, expanded.Length);
                    }
                    catch (InvalidDataException ex)
                    {
                        errors.Add(ImportSanitization.Text(name + ": " + ex.Message, sanitization));
                        Skip(name, "Unsupported or unreadable file skipped.");
                    }
                    return;
                }
                if (ChatGptLibraryFiles.IsPayload(name))
                {
                    ImportSource? payload = null;
                    try
                    {
                        var header = new byte[4];
                        var read = await content.ReadAtLeastAsync(header, 4, false, ct);
                        using var replay = new PrefixReadStream(header[..read], content);
                        await using var savedSource = await Save(name, replay, size, false);
                        payload = savedSource.Source;
                        try
                        {
                            await Parse(payload, savedSource.Local);
                        }
                        catch (Exception ex)
                            when (ex
                                    is InvalidDataException
                                        or JsonException
                                        or ArgumentException
                                        or Microsoft.Data.Sqlite.SqliteException
                            )
                        {
                            errors.Add(payload.Name + ": " + ex.Message);
                        }
                    }
                    catch (Exception ex)
                        when (ex
                                is InvalidDataException
                                    or JsonException
                                    or ArgumentException
                                    or Microsoft.Data.Sqlite.SqliteException
                        )
                    {
                        if (payload != null)
                        {
                            store.Delete(payload);
                            stored.Remove(payload);
                            db.Entry(payload).State = EntityState.Detached;
                            retainedOriginals.Remove(payload.Name);
                        }
                        Skip(name, "Unsupported or unreadable file skipped.");
                    }
                    return;
                }
                if (ChatGptLibraryFiles.IsIndex(name))
                {
                    try
                    {
                        var bytes = await ArchiveInspector.ReadBounded(
                            content,
                            size > 0 ? size : maxBytes,
                            ct
                        );
                        ChatGptLibraryFiles.Add(library, libraryAmbiguous, bytes);
                        fileProgress.Processed();
                    }
                    catch (Exception ex)
                        when (ex
                                is InvalidDataException
                                    or JsonException
                                    or ArgumentException
                        )
                    {
                        Skip(name, "Unsupported or unreadable file skipped.");
                    }
                    return;
                }
                ImportSource? source = null;
                var recordsBefore =
                    previews.Count + projectPreviews.Count + attachmentPreviews.Count;
                bool parsed;
                try
                {
                    var header = new byte[4];
                    var read = await content.ReadAtLeastAsync(header, 4, false, ct);
                    if (ArchiveInspector.IsArchive(header[..read]))
                    {
                        Skip(name, "Nested archive skipped.");
                        return;
                    }
                    using var replay = new PrefixReadStream(header[..read], content);
                    await using var savedSource = await Save(name, replay, size, false);
                    source = savedSource.Source;
                    parsed = await Parse(source, savedSource.Local);
                }
                catch (Exception ex)
                    when (ex
                            is InvalidDataException
                                or JsonException
                                or ArgumentException
                                or Microsoft.Data.Sqlite.SqliteException
                    )
                {
                    parsed = false;
                }
                if (!parsed)
                {
                    // Retain partial supported records, but never queue an unsupported source.
                    if (
                        source != null
                        && previews.Count
                            + projectPreviews.Count
                            + attachmentPreviews.Count
                            == recordsBefore
                    )
                    {
                        store.Delete(source);
                        stored.Remove(source);
                        db.Entry(source).State = EntityState.Detached;
                        retainedOriginals.Remove(source.Name);
                    }
                    Skip(name, "Unsupported or unreadable file skipped.");
                }
                else
                    fileProgress.Processed();
            }
            foreach (var file in form.Files)
            {
                if (invalidArchives.Contains(file))
                {
                    Skip(file.FileName, "Unsupported or unreadable archive skipped.");
                    continue;
                }
                await using var content = file.OpenReadStream();
                if (archives.Contains(file))
                {
                    // Sanitized imports never retain the original container.
                    if (!sanitization.Enabled)
                    {
                        await using var container = await Save(file.FileName, content, file.Length, true);
                        content.Position = 0;
                    }
                    await foreach (var entry in ArchiveInspector.Entries(content, ct))
                    {
                        await ProcessFile(entry.Name, entry.Content, entry.Size);
                    }
                }
                else
                    await ProcessFile(file.FileName, content, file.Length);
            }
            foreach (var source in stored.ToList())
            {
                if (!ChatGptLibraryFiles.IsPayload(source.Name))
                    continue;
                var previous = source.Name;
                var renamed = ChatGptLibraryFiles.RenamedPath(source.Name, library);
                if (renamed != null && source.AdapterId is "" or "private-asset")
                {
                    source.AdapterId = "private-asset";
                    source.Name = ImportSanitization.Text(renamed, sanitization);
                    errors.RemoveAll(e =>
                        e.StartsWith(previous + ": ", StringComparison.Ordinal)
                    );
                    fileProgress.Processed();
                    continue;
                }
                if (renamed == null && source.AdapterId == "")
                {
                    store.Delete(source);
                    stored.Remove(source);
                    db.Entry(source).State = EntityState.Detached;
                    retainedOriginals.Remove(previous);
                    errors.RemoveAll(e =>
                        e.StartsWith(previous + ": ", StringComparison.Ordinal)
                    );
                    Skip(previous, "Unsupported or unreadable file skipped.");
                    continue;
                }
                fileProgress.Processed();
            }
            attachmentPreviews.Clear();
            foreach (var source in stored.Where(s => s.AdapterId == "private-asset"))
                attachmentPreviews.Add(
                    new
                    {
                        source.Name,
                        source.Size,
                        source.Hash,
                    }
                );
            await ConversationPreview.FindDuplicates(db, wid, storedIdentity, previews, fileProgress, ct);
            batch.Total = previews.Count;
            batch.Selected = Json.Write(availableKeys);
            db.Batches.Add(batch);
            using (fileProgress.Measure("previewPersistence"))
                await db.SaveChangesAsync(ct);
            saved = true;
            fileProgress.Stage("ready");
            fileProgress.Finish();
            return Results.Ok(
                new
                {
                    batchId = batch.Id,
                    conversations = previews,
                    projects = projectPreviews,
                    attachments = attachmentPreviews,
                    retainedOriginals,
                    errors,
                    detected,
                    filesProcessed = fileProgress.Read().Processed,
                    fileProgress = fileProgress.Read(),
                    skippedFiles,
                }
            );
        }
        catch (OriginalFileConsentRequiredException ex)
        {
            return Results.Conflict(
                new { error = ex.Message, code = "original_file_consent_required" }
            );
        }
        finally
        {
            if (!saved)
                fileProgress.Stage("stopped");
            try
            {
                if (!saved)
                    foreach (var source in stored)
                        store.Delete(source);
            }
            finally
            {
                fileProgress.Finish();
                if (logSummary)
                    fileProgress.Log(logger, "preview", batch.Id);
            }
        }
    }
}
