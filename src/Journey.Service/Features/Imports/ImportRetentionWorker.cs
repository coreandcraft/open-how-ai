using Journey.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace Journey.Service.Features.Imports;

public sealed class ImportRetentionWorker(
    IServiceScopeFactory scopes,
    IConfiguration config,
    ILogger<ImportRetentionWorker> logger
) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                if (
                    int.TryParse(config["Imports:SourceFileRetentionDays"], out var days)
                    && days > 0
                )
                {
                    using var scope = scopes.CreateScope();
                    var db = scope.ServiceProvider.GetRequiredService<AppDb>();
                    var store = scope.ServiceProvider.GetRequiredService<ImportSourceStore>();
                    var cutoff = DateTime.UtcNow.AddDays(-days);
                    // Only successfully completed batches; failed, queued, and cancelled jobs remain resumable.
                    var expired = await db
                        .Sources.Where(s =>
                            s.PurgedAt == null
                            && s.CreatedAt < cutoff
                            && s.AdapterId != "private-asset"
                            && db.Batches.Any(b => b.Id == s.BatchId && b.Status == "completed")
                        )
                        .Take(100)
                        .ToListAsync(ct);
                    foreach (var source in expired)
                    {
                        if (source.StoredExternally)
                            store.Delete(source);
                        source.Bytes = [];
                        source.PurgedAt = DateTime.UtcNow;
                    }
                    await db.SaveChangesAsync(ct);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Source-file retention failed");
            }
            await Task.Delay(TimeSpan.FromHours(1), ct);
        }
    }
}
