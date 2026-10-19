using Journey.Data;
using Journey.Domain;
using Microsoft.EntityFrameworkCore;

namespace Journey.Service.Features.Imports;

public sealed class ImportLeaseLostException() : Exception("Another worker took over this import.");

/// <summary>
/// Single-owner claims on import batches so several app instances can run the worker. A worker that
/// stops renewing (crash, deploy) loses the batch once the lease expires and another instance resumes it.
/// </summary>
public static class ImportLease
{
    public static readonly TimeSpan Duration = TimeSpan.FromMinutes(5);
    static readonly TimeSpan RenewWhenBelow = TimeSpan.FromMinutes(4);

    public static async Task<ImportBatch?> Next(AppDb db, string owner, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var candidates = await Claimable(db.Batches, now)
            .OrderBy(b => b.CreatedAt)
            .Select(b => b.Id)
            .Take(10)
            .ToListAsync(ct);
        foreach (var id in candidates)
            if (await TryClaim(db, id, owner, now, ct))
                return await db.Batches.SingleAsync(b => b.Id == id, ct);
        return null;
    }

    public static async Task<bool> TryClaim(
        AppDb db,
        string id,
        string owner,
        DateTime now,
        CancellationToken ct
    ) =>
        await Claimable(db.Batches.Where(b => b.Id == id), now)
            .ExecuteUpdateAsync(
                s =>
                    s.SetProperty(b => b.Status, "processing")
                        .SetProperty(b => b.LeaseOwner, owner)
                        .SetProperty(b => b.LeaseExpiresAt, now + Duration),
                ct
            ) == 1;

    /// <summary>Reloads the batch and extends the lease, or throws if another worker owns it now.</summary>
    public static async Task Checkpoint(AppDb db, ImportBatch batch, string owner, CancellationToken ct)
    {
        await db.Entry(batch).ReloadAsync(ct);
        if (batch.LeaseOwner != owner)
            throw new ImportLeaseLostException();
        var now = DateTime.UtcNow;
        if (batch.LeaseExpiresAt > now + RenewWhenBelow)
            return;
        var renewed = await db
            .Batches.Where(b => b.Id == batch.Id && b.LeaseOwner == owner)
            .ExecuteUpdateAsync(s => s.SetProperty(b => b.LeaseExpiresAt, now + Duration), ct);
        if (renewed == 0)
            throw new ImportLeaseLostException();
        await db.Entry(batch).ReloadAsync(ct);
    }

    public static Task Release(AppDb db, string id, string owner) =>
        db
            .Batches.Where(b => b.Id == id && b.LeaseOwner == owner)
            .ExecuteUpdateAsync(s =>
                s.SetProperty(b => b.LeaseOwner, (string?)null)
                    .SetProperty(b => b.LeaseExpiresAt, (DateTime?)null)
            );

    /// <summary>Keys from the processed-key table plus any recorded by older versions in the source row.</summary>
    public static async Task<HashSet<string>> ProcessedKeys(
        AppDb db,
        ImportSource source,
        CancellationToken ct
    )
    {
        var keys = (
            await db
                .Set<ImportProcessedKey>()
                .Where(k => k.SourceId == source.Id)
                .Select(k => k.Key)
                .ToListAsync(ct)
        ).ToHashSet();
        keys.UnionWith(Json.Read<List<string>>(source.ProcessedKeys));
        return keys;
    }

    static IQueryable<ImportBatch> Claimable(IQueryable<ImportBatch> batches, DateTime now) =>
        batches.Where(b =>
            (b.Status == "queued" || b.Status == "processing")
            && (b.LeaseExpiresAt == null || b.LeaseExpiresAt < now)
        );
}
