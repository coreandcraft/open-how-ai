using System.Diagnostics;
using Journey.Domain;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace Journey.Service.Features.Imports;

public record ImportFileProgress(
    string Stage = "uploading",
    int Processed = 0,
    int Total = 0,
    int Skipped = 0,
    long CompressedBytes = 0,
    long UncompressedBytes = 0,
    long UsedBytes = 0,
    long LimitBytes = 0,
    double ElapsedMilliseconds = 0,
    IReadOnlyDictionary<string, ImportStageTiming>? Timings = null,
    IReadOnlyDictionary<string, long>? Metrics = null
);

public record ImportStageTiming(double Milliseconds, int Operations, int Active = 0);

public sealed class ImportProgressStore : IDisposable
{
    readonly MemoryCache cache = new(new MemoryCacheOptions { SizeLimit = 1024 });

    public ImportProgress Begin(string owner, string workspace, string id, ImportProgress? existing = null)
    {
        var progress = existing ?? new ImportProgress();
        cache.Set(
            (owner, workspace, id),
            progress,
            new MemoryCacheEntryOptions { Size = 1, SlidingExpiration = TimeSpan.FromHours(2) }
        );
        return progress;
    }

    public ImportFileProgress Read(string owner, string workspace, string id) =>
        cache.TryGetValue<ImportProgress>((owner, workspace, id), out var p) ? p!.Read() : new();

    public void Dispose() => cache.Dispose();
}

public sealed class ImportProgress
{
    readonly object gate = new();
    ImportFileProgress value = new();
    readonly Dictionary<string, (long Ticks, int Count)> timings = new();
    readonly Dictionary<long, (string Stage, long Start)> active = new();
    readonly Dictionary<string, long> metrics = new();
    long started, sequence;
    long? finished;

    public ImportFileProgress Read()
    {
        lock (gate)
        {
            if (started == 0)
                return value;
            var now = finished ?? Stopwatch.GetTimestamp();
            var result = timings.ToDictionary(x => x.Key,
                x => new ImportStageTiming(x.Value.Ticks * 1000d / Stopwatch.Frequency, x.Value.Count));
            foreach (var (stage, start) in active.Values)
            {
                var previous = result.GetValueOrDefault(stage) ?? new(0, 0);
                result[stage] = previous with
                {
                    Milliseconds = previous.Milliseconds + (now - start) * 1000d / Stopwatch.Frequency,
                    Active = previous.Active + 1,
                };
            }
            return value with
            {
                ElapsedMilliseconds = (now - started) * 1000d / Stopwatch.Frequency,
                Timings = result, Metrics = new Dictionary<string, long>(metrics),
            };
        }
    }

    public IDisposable Measure(string stage)
    {
        lock (gate)
        {
            var now = Stopwatch.GetTimestamp();
            if (started == 0)
                started = now;
            var id = ++sequence;
            active[id] = (stage, now);
            return new Measurement(this, id);
        }
    }

    sealed class Measurement(ImportProgress owner, long id) : IDisposable
    {
        int disposed;
        public void Dispose()
        {
            if (Interlocked.Exchange(ref disposed, 1) != 0)
                return;
            lock (owner.gate)
            {
                if (!owner.active.Remove(id, out var measurement))
                    return;
                var previous = owner.timings.GetValueOrDefault(measurement.Stage);
                owner.timings[measurement.Stage] =
                    (previous.Ticks + Stopwatch.GetTimestamp() - measurement.Start, previous.Count + 1);
            }
        }
    }

    public void Count(string metric, long amount = 1)
    {
        lock (gate)
            metrics[metric] = metrics.GetValueOrDefault(metric) + amount;
    }

    public void Finish()
    {
        lock (gate)
            finished ??= Stopwatch.GetTimestamp();
    }

    public void Log(ILogger logger, string phase, string id)
    {
        var snapshot = Read();
        logger.LogInformation("Import {ImportPhase} {ImportId} finished after {DurationMs} ms with status {ImportStatus}. Metrics: {ImportMetrics}",
            phase, id, snapshot.ElapsedMilliseconds, snapshot.Stage, Json.Write(snapshot.Metrics));
        foreach (var timing in snapshot.Timings ?? new Dictionary<string, ImportStageTiming>())
            logger.LogInformation("Import {ImportPhase} {ImportId} stage {ImportStage}: {DurationMs} ms across {Operations} operations",
                phase, id, timing.Key, timing.Value.Milliseconds, timing.Value.Operations);
    }

    public void Stage(string stage)
    {
        lock (gate)
            value = value with { Stage = stage };
    }

    public void Total(int total)
    {
        lock (gate)
            value = value with { Total = total };
    }

    public void Processed(bool skipped = false)
    {
        lock (gate)
            value = value with
            {
                Processed = value.Processed + 1,
                Skipped = value.Skipped + (skipped ? 1 : 0),
            };
    }

    public void Bytes(long compressed, long uncompressed, long used, long limit)
    {
        lock (gate)
            value = value with
            {
                CompressedBytes = compressed,
                UncompressedBytes = uncompressed,
                UsedBytes = used,
                LimitBytes = limit,
            };
    }
}
