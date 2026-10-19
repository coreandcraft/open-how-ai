using Journey.Data;
using Microsoft.EntityFrameworkCore;

namespace Journey.Service.Features.Imports;

// Uses a separate DbContext so a slow parser/blob read cannot prevent renewal or cancellation.
public sealed class ImportRunMonitor : IAsyncDisposable
{
    readonly CancellationTokenSource work;
    readonly CancellationTokenSource timer;
    readonly Task heartbeat;
    volatile bool leaseLost;
    public CancellationToken Token => work.Token;
    public bool CancelledByUser { get; private set; }

    public ImportRunMonitor(IServiceScopeFactory scopes, string batchId, string owner,
        ILogger logger, CancellationToken hostToken, TimeSpan? interval = null)
    {
        work = CancellationTokenSource.CreateLinkedTokenSource(hostToken);
        timer = CancellationTokenSource.CreateLinkedTokenSource(hostToken);
        heartbeat = Watch();
        async Task Watch()
        {
            using var ticks = new PeriodicTimer(interval ?? TimeSpan.FromSeconds(10));
            try
            {
                while (await ticks.WaitForNextTickAsync(timer.Token))
                {
                    await using var scope = scopes.CreateAsyncScope();
                    var db = scope.ServiceProvider.GetRequiredService<AppDb>();
                    var state = await db.Batches.AsNoTracking().Where(b => b.Id == batchId)
                        .Select(b => new { b.Status, b.LeaseOwner }).SingleOrDefaultAsync(timer.Token);
                    if (state?.Status == "cancelled")
                    {
                        CancelledByUser = true;
                        work.Cancel();
                        return;
                    }
                    if (state?.LeaseOwner != owner)
                    {
                        leaseLost = true;
                        work.Cancel();
                        return;
                    }
                    if (state.Status is not ("processing" or "queued"))
                        return;
                    var renewed = await db.Batches
                        .Where(b => b.Id == batchId && b.LeaseOwner == owner && b.Status == "processing")
                        .ExecuteUpdateAsync(s => s.SetProperty(b => b.LeaseExpiresAt,
                            DateTime.UtcNow + ImportLease.Duration), timer.Token);
                    // A concurrent cancel/completion/claim is observed on the next tick.
                    if (renewed == 0)
                        continue;
                }
            }
            catch (OperationCanceledException) when (timer.IsCancellationRequested) { }
            catch (Exception ex)
            {
                logger.LogError(ex, "Cannot renew import {ImportId}; stopping this worker", batchId);
                leaseLost = true;
                work.Cancel();
            }
        }
    }

    public void ThrowIfLeaseLost()
    {
        if (leaseLost)
            throw new ImportLeaseLostException();
    }

    public async ValueTask DisposeAsync()
    {
        await timer.CancelAsync();
        await heartbeat;
        timer.Dispose();
        work.Dispose();
    }
}
