using System.Collections.Concurrent;
using System.Text.Json;
using System.Threading.Channels;
using Journey.Data;
using Journey.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Primitives;

namespace Journey.Server.Features.Imports;

// Raw staging stays temporary. Shared status/results allow polling on any instance; if a
// restart loses active work, the browser can resubmit its retained files with the same options.
public sealed class ImportPreviewQueue(
    IServiceScopeFactory scopes,
    ImportProgressStore progressStore,
    ILogger<ImportPreviewQueue> logger
) : BackgroundService
{
    readonly record struct Key(string Owner, string Workspace, string Id);
    sealed record Job(Key Key, IFormCollection Form, List<FileStream> Files,
        long MaxBytes, ImportProgress Progress, IDisposable QueueTiming);

    readonly Channel<Job> queue = Channel.CreateUnbounded<Job>(
        new UnboundedChannelOptions { SingleReader = true }
    );
    // Bound temporary disk usage and parser concurrency, including uploads being staged.
    readonly SemaphoreSlim slots = new(2, 2);
    readonly ConcurrentDictionary<Key, ImportProgress> pending = new();
    readonly MemoryCache completed = new(new MemoryCacheOptions { SizeLimit = 16 });
    readonly Queue<Key> completionOrder = new();
    static readonly TimeSpan leaseDuration = TimeSpan.FromMinutes(2);
    static readonly TimeSpan retention = TimeSpan.FromHours(2);

    public async Task<IResult> Enqueue(
        string owner, string workspace, IFormCollection form, long maxBytes, CancellationToken ct, ImportProgress? uploadProgress = null
    )
    {
        if (!slots.Wait(0))
            return Results.Json(new { error = "Import previews are busy. Please try again shortly." },
                statusCode: StatusCodes.Status429TooManyRequests);

        var key = new Key(owner, workspace, Guid.NewGuid().ToString());
        var files = new List<FileStream>();
        var progress = uploadProgress ?? new ImportProgress();
        IDisposable? queueTiming = null;
        var queued = false;
        try
        {
            var uploads = new FormFileCollection();
            using (progress.Measure("uploadStaging"))
            foreach (var file in form.Files)
            {
                ct.ThrowIfCancellationRequested();
                // Request-owned multipart streams are disposed when the response ends.
                // Keep a separate, temporary copy; raw files/rules never enter durable storage here.
                var stream = ImportTemporaryFile.Create();
                files.Add(stream);
                await file.CopyToAsync(stream, ct);
                stream.Position = 0;
                uploads.Add(new FormFile(stream, 0, stream.Length, file.Name, file.FileName)
                {
                    Headers = new HeaderDictionary(),
                    ContentType = file.ContentType,
                });
            }
            var copy = new FormCollection(
                form.ToDictionary(x => x.Key, x => new StringValues(x.Value.ToArray())), uploads
            );
            progressStore.Begin(owner, workspace, key.Id, progress);
            progress.Stage("queued");
            await using (var scope = scopes.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<AppDb>();
                db.Add(new ImportPreviewJob
                {
                    Id = key.Id, OwnerId = owner, WorkspaceId = workspace,
                    ExpiresAt = DateTime.UtcNow + retention,
                    LeaseExpiresAt = DateTime.UtcNow + leaseDuration,
                    Progress = Json.Write(progress.Read()),
                });
                await db.SaveChangesAsync(ct);
            }
            pending[key] = progress;
            queueTiming = progress.Measure("queueWait");
            if (!queue.Writer.TryWrite(new Job(key, copy, files, maxBytes, progress, queueTiming)))
            {
                pending.TryRemove(key, out _);
                return Results.Json(new { error = "Import service is restarting. Please try again." },
                    statusCode: StatusCodes.Status503ServiceUnavailable);
            }
            queued = true;
            return Accepted(key, progress);
        }
        finally
        {
            if (!queued)
            {
                queueTiming?.Dispose();
                foreach (var file in files)
                    await file.DisposeAsync();
                slots.Release();
            }
        }
    }

    static IResult Accepted(Key key, ImportProgress progress) => Accepted(key, progress.Read());

    static IResult Accepted(Key key, ImportFileProgress progress) => Results.Accepted(
        $"/api/w/{key.Workspace}/imports/previews/{key.Id}",
        new { status = "processing", previewId = key.Id, fileProgress = progress }
    );

    static IResult Unavailable(bool interrupted = false) => Results.Json(new
    {
        error = interrupted
            ? "The server restarted while preparing this preview. Please try the upload again."
            : "This import preview is no longer available. Please try the upload again.",
        code = "preview_unavailable",
    }, statusCode: StatusCodes.Status410Gone);

    public async Task<IResult> Read(string owner, string workspace, string id, CancellationToken ct = default)
    {
        var key = new Key(owner, workspace, id);
        if (completed.TryGetValue<IResult>(key, out var result))
            return result!;
        if (pending.TryGetValue(key, out var progress))
            return Accepted(key, progress);
        // Completion can race the first cache read and pending lookup.
        if (completed.TryGetValue<IResult>(key, out result))
            return result!;
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDb>();
        var job = await db.Set<ImportPreviewJob>().AsNoTracking().SingleOrDefaultAsync(j =>
            j.Id == id && j.OwnerId == owner && j.WorkspaceId == workspace, ct);
        if (job == null || job.ExpiresAt <= DateTime.UtcNow)
            return Unavailable();
        if (job.StatusCode is int status && job.Response != null)
            return Results.Content(job.Response, "application/json", statusCode: status);
        // A request routed to another live instance must not discard a running upload.
        return job.LeaseExpiresAt > DateTime.UtcNow
            ? Accepted(key, Json.Read<ImportFileProgress>(job.Progress))
            : Unavailable(interrupted: true);
    }

    async Task PersistResult(Job job, IResult result)
    {
        // The builder and all error results expose a JSON value; never persist the upload form.
        var json = Json.Write(((IValueHttpResult)result).Value);
        var status = ((IStatusCodeHttpResult)result).StatusCode ?? 200;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDb>();
        await db.Set<ImportPreviewJob>().Where(j => j.Id == job.Key.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(j => j.Response, json)
                .SetProperty(j => j.StatusCode, status)
                .SetProperty(j => j.Progress, Json.Write(job.Progress.Read()))
                .SetProperty(j => j.ExpiresAt, DateTime.UtcNow + retention), timeout.Token);
    }

    async Task MaintainStatus(CancellationToken ct)
    {
        var nextCleanup = DateTime.MinValue;
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(10));
        do
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDb>();
                foreach (var (key, progress) in pending)
                    await db.Set<ImportPreviewJob>().Where(j => j.Id == key.Id && j.StatusCode == null)
                        .ExecuteUpdateAsync(s => s
                            .SetProperty(j => j.LeaseExpiresAt, DateTime.UtcNow + leaseDuration)
                            .SetProperty(j => j.ExpiresAt, DateTime.UtcNow + retention)
                            .SetProperty(j => j.Progress, Json.Write(progress.Read())), ct);
                if (DateTime.UtcNow >= nextCleanup)
                {
                    await db.Set<ImportPreviewJob>().Where(j => j.ExpiresAt <= DateTime.UtcNow)
                        .ExecuteDeleteAsync(ct);
                    nextCleanup = DateTime.UtcNow.AddHours(1);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Import preview status maintenance failed");
            }
        } while (await timer.WaitForNextTickAsync(ct));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var maintenanceStop = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        var maintenance = MaintainStatus(maintenanceStop.Token);
        try
        {
            await foreach (var job in queue.Reader.ReadAllAsync(stoppingToken))
            {
                job.QueueTiming.Dispose();
                IResult result;
                try
                {
                    await using var scope = scopes.CreateAsyncScope();
                    result = await scope.ServiceProvider.GetRequiredService<ImportPreviewBuilder>()
                        .Build(job.Key.Workspace, job.Form, job.MaxBytes, job.Progress, stoppingToken, logSummary: false);
                }
                catch (ImportSizeException ex)
                {
                    result = Results.Json(new { error = ex.Message }, statusCode: 413);
                }
                catch (Exception ex) when (ex is InvalidDataException or JsonException or ArgumentException)
                {
                    result = Results.BadRequest(new { error = ex.Message });
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    result = Unavailable(interrupted: true);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Import preview {PreviewId} failed", job.Key.Id);
                    result = Results.Json(new { error = "The import preview failed. Please try again." },
                        statusCode: StatusCodes.Status500InternalServerError);
                }
                finally
                {
                    foreach (var file in job.Files)
                        await file.DisposeAsync();
                    slots.Release();
                }
                if (job.Progress.Read().Stage != "ready")
                    job.Progress.Stage("stopped");
                job.Progress.Finish();
                job.Progress.Log(logger, "preview", job.Key.Id);
                try
                {
                    // Publish durably before removing pending; cache eviction/restarts are harmless.
                    await PersistResult(job, result);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Import preview {PreviewId} result persistence failed", job.Key.Id);
                }
                // Make room before insertion: MemoryCache can otherwise reject the newest result.
                if (completionOrder.Count == 16)
                    completed.Remove(completionOrder.Dequeue());
                completionOrder.Enqueue(job.Key);
                completed.Set(job.Key, result, new MemoryCacheEntryOptions
                {
                    Size = 1, AbsoluteExpirationRelativeToNow = retention,
                });
                pending.TryRemove(job.Key, out _);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
        finally
        {
            queue.Writer.TryComplete();
            while (queue.Reader.TryRead(out var job))
            {
                job.QueueTiming.Dispose();
                foreach (var file in job.Files)
                    await file.DisposeAsync();
                try { await PersistResult(job, Unavailable(interrupted: true)); }
                catch (Exception ex) { logger.LogError(ex, "Import preview interruption persistence failed"); }
                pending.TryRemove(job.Key, out _);
                slots.Release();
            }
            await maintenanceStop.CancelAsync();
            try { await maintenance; }
            catch (OperationCanceledException) when (maintenanceStop.IsCancellationRequested) { }
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        queue.Writer.TryComplete();
        await base.StopAsync(cancellationToken);
    }

    public override void Dispose()
    {
        base.Dispose();
        completed.Dispose();
    }
}
