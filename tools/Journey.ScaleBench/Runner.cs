using System.Data.Common;
using System.Diagnostics;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Journey.Data;
using Journey.Data.PostgreSql;
using Journey.Domain;
using Journey.Service.Features.Publications;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Npgsql;

namespace Journey.ScaleBench;

public static class Runner
{
    static readonly (string Name, string Path)[] BaselineScenarios =
    [
        ("library", "/api/w/cloudplay/library"),
        ("search-hit", "/api/w/cloudplay/library?q=benchmark"),
        ("search-miss", "/api/w/cloudplay/library?q=notpresent9981"),
        ("long-conversation", "/api/conversations/scale-long"),
        ("journey-list", "/api/w/cloudplay/journeys"),
        ("journey-detail", "/api/journeys/scale-j0"),
        ("comparisons", "/api/w/cloudplay/comparisons"),
        ("public-snapshot", "/api/public/scale-public"),
    ];

    static readonly (string Name, string Path)[] PilotScenarios =
    [
        .. BaselineScenarios,
        ("journey-large", "/api/journeys/" + Corpus.LargeJourney),
        ("summary", "/api/w/cloudplay/summary"),
    ];

    public static async Task Main(string[] args)
    {
        if (args.Any(a => a.Equals("--seed-existing", StringComparison.OrdinalIgnoreCase)))
        {
            await SeedExisting();
            return;
        }
        var rewriteAt = Array.FindIndex(args, a => a.Equals("--rewrite-publication", StringComparison.OrdinalIgnoreCase));
        if (rewriteAt >= 0)
        {
            var slug = rewriteAt + 1 < args.Length ? args[rewriteAt + 1] : null;
            if (string.IsNullOrWhiteSpace(slug))
                throw new InvalidOperationException("Pass the publication slug after --rewrite-publication.");
            await RewritePublication(slug);
            return;
        }

        var connection = Environment.GetEnvironmentVariable("JOURNEY_BENCH_POSTGRES")
            ?? throw new InvalidOperationException("Set JOURNEY_BENCH_POSTGRES to a local PostgreSQL admin connection. A NEW database is created; existing app databases are never seeded.");
        var samples = int.TryParse(Environment.GetEnvironmentVariable("JOURNEY_BENCH_SAMPLES"), out var s) ? s : 100;
        var profile = Environment.GetEnvironmentVariable("JOURNEY_BENCH_PROFILE") ?? "baseline";
        var database = "journey_scale_" + Guid.NewGuid().ToString("N");
        var admin = new NpgsqlConnectionStringBuilder(connection) { Database = "postgres" };
        await using (var postgres = new NpgsqlConnection(admin.ConnectionString))
        {
            await postgres.OpenAsync();
            await using var command = new NpgsqlCommand($"CREATE DATABASE \"{database}\"", postgres);
            await command.ExecuteNonQueryAsync();
        }
        admin.Database = database;
        Console.WriteLine($"Isolated benchmark database: {database}");

        var commands = new CommandRecorder();
        using var factory = new BenchFactory(admin.ConnectionString, commands);
        factory.UseKestrel(0);
        factory.StartServer();
        using var client = factory.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(120);
        client.DefaultRequestHeaders.Add("X-Journey", "1");
        (await client.PostAsJsonAsync("/api/auth/login", new { email = "alex@journey.local", password = "JourneyDemo!2026" })).EnsureSuccessStatusCode();

        var results = new List<object>();
        var plans = new StringBuilder();
        var process = Process.GetCurrentProcess();
        int conversations, messages, journeys;
        if (profile == "pilot")
        {
            var seeded = await Corpus.SeedPilot(factory);
            (conversations, messages, journeys) = seeded;
            await Analyze(admin.ConnectionString);
            Console.WriteLine($"Pilot corpus: {conversations} conversations, {messages} messages, {journeys} journeys.");
            foreach (var concurrency in new[] { 1, 8 })
            foreach (var scenario in PilotScenarios)
            {
                var row = await MeasureClosed(client, commands, process, scenario, samples, concurrency, conversations, messages, journeys);
                results.Add(row);
                Console.WriteLine(JsonSerializer.Serialize(row));
                if (concurrency == 1)
                    plans.Append(await commands.ExplainSlowest(admin.ConnectionString, scenario.Name, conversations));
            }
            var rps = int.TryParse(Environment.GetEnvironmentVariable("JOURNEY_BENCH_RPS"), out var r) ? r : 20;
            var duration = int.TryParse(Environment.GetEnvironmentVariable("JOURNEY_BENCH_DURATION"), out var d) ? d : 60;
            results.Add(await MeasureArrival(client, process, PilotScenarios, rps, duration, "steady", conversations, messages, journeys));
            results.Add(await MeasureArrival(client, process, PilotScenarios, rps * 4, Math.Max(15, duration / 3), "spike", conversations, messages, journeys));
            results.AddRange(await MeasureAdverse(client, factory));
        }
        else
        {
            int previous = 0;
            int[] sizes = [100, 1000];
            foreach (var size in sizes)
            {
                await Corpus.SeedBaseline(factory, previous, size);
                previous = size;
                conversations = size;
                messages = size * 20 + 1000;
                journeys = size / 10;
                await Analyze(admin.ConnectionString);
                Console.WriteLine($"Seeded {size} conversations, {messages} messages, {journeys} journeys and {size} comparisons.");
                foreach (var concurrency in new[] { 1, 2, 4, 8 })
                foreach (var scenario in BaselineScenarios)
                {
                    var row = await MeasureClosed(client, commands, process, scenario, samples, concurrency, conversations, messages, journeys);
                    results.Add(row);
                    Console.WriteLine(JsonSerializer.Serialize(row));
                    if (concurrency == 1)
                        plans.Append(await commands.ExplainSlowest(admin.ConnectionString, scenario.Name, size));
                }
            }
        }

        var output = args.FirstOrDefault() ?? "artifacts/scale-baseline.json";
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
        await File.WriteAllTextAsync(output, JsonSerializer.Serialize(new
        {
            generatedAtUtc = DateTime.UtcNow,
            database,
            profile,
            runtime = Environment.Version.ToString(),
            processorCount = Environment.ProcessorCount,
            gc = System.Runtime.GCSettings.IsServerGC ? "server" : "workstation",
            methodology = profile == "pilot"
                ? $"Release build. Pilot corpus (zipf conversation sizes, mixed-entropy text, 1,000-node journey, skewed second workspace). Closed-loop plus open-loop arrival-rate and spike on loopback Kestrel. Import workers disabled. Excludes TLS, Azure Blob, App Service, database failover and multi-hour soak."
                : $"Release build. Actual ASP.NET routes over loopback HTTP (Kestrel in-process) against local PostgreSQL. Server GC. Client streams responses without buffering, so process-wide allocations are dominated by the server. Synthetic data, five warm-up reads, {samples} samples per cell, closed-loop concurrency 1/2/4/8. Import workers disabled. Excludes TLS, compression, browser, Azure Blob, AI inference, import contention and sustained arrival-rate load. Existing database retained; no automatic deletion.",
            results,
        }, new JsonSerializerOptions { WriteIndented = true }));
        await File.WriteAllTextAsync(Path.ChangeExtension(output, ".plans.txt"), plans.ToString());
        Console.WriteLine($"Saved {output}");
    }

    static AppDb OpenLocalDb()
    {
        var provider = Environment.GetEnvironmentVariable("JOURNEY_SEED_PROVIDER") ?? "Sqlite";
        var connection = Environment.GetEnvironmentVariable("JOURNEY_SEED_CONNECTION");
        var options = new DbContextOptionsBuilder<AppDb>();
        if (provider.Equals("PostgreSQL", StringComparison.OrdinalIgnoreCase))
        {
            options.UseJourneyPostgreSql(connection ?? throw new InvalidOperationException("Set JOURNEY_SEED_CONNECTION for PostgreSQL."));
        }
        else
        {
            var path = string.IsNullOrWhiteSpace(connection)
                ? Path.GetFullPath(Path.Combine("src", "Journey.Server", "journey.db"))
                : connection;
            var target = path.Contains('=', StringComparison.Ordinal) ? path : "Data Source=" + path;
            options.UseSqlite(target, sqlite => sqlite.CommandTimeout(300));
        }
        return new AppDb(options.Options);
    }

    static async Task SeedExisting()
    {
        await using var db = OpenLocalDb();
        var target = db.Database.GetConnectionString() ?? "local database";
        if (!await db.Users.AnyAsync(u => u.Email == "alex@journey.local"))
            throw new InvalidOperationException("Demo users are missing. Start the app once so Seed.Run can create them, then seed again.");
        if (await db.Conversations.AnyAsync(c => c.Id == "scale-c0"))
        {
            Console.WriteLine(
                $"Pilot corpus already present in {target}: {await db.Conversations.CountAsync()} conversations, {await db.Messages.CountAsync()} messages, {await db.Journeys.CountAsync()} journeys."
            );
            return;
        }

        Console.WriteLine($"Seeding pilot corpus into {target}...");
        var seeded = await Corpus.SeedPilot(db);
        Console.WriteLine($"Seeded {seeded.Conversations} conversations, {seeded.Messages} messages, {seeded.Journeys} journeys.");
    }

    static async Task RewritePublication(string slug)
    {
        await using var db = OpenLocalDb();
        var publication = await db.Publications.SingleOrDefaultAsync(p => p.Slug == slug)
            ?? throw new InvalidOperationException("Publication not found: " + slug);
        if (publication.JourneyId == null)
            throw new InvalidOperationException("Only journey publications can be rewritten.");
        var journey = await db.Journeys.SingleAsync(j => j.Id == publication.JourneyId);
        using var old = JsonDocument.Parse(publication.Snapshot);
        var messageIds = new HashSet<string>();
        var curators = new List<string>();
        void TakeMessages(JsonElement messages)
        {
            if (messages.ValueKind != JsonValueKind.Array)
                return;
            foreach (var message in messages.EnumerateArray())
            {
                if (message.TryGetProperty("id", out var id) && id.GetString() is string value)
                    messageIds.Add(value);
            }
        }
        if (old.RootElement.TryGetProperty("steps", out var steps))
        {
            foreach (var step in steps.EnumerateArray())
            {
                if (step.TryGetProperty("commentary", out var commentary)
                    && commentary.ValueKind == JsonValueKind.String
                    && step.TryGetProperty("id", out var stepId)
                    && stepId.GetString() is string curator)
                    curators.Add(curator);
                if (step.TryGetProperty("messages", out var messages))
                    TakeMessages(messages);
            }
        }
        if (old.RootElement.TryGetProperty("sources", out var sources))
        {
            foreach (var source in sources.EnumerateArray())
                if (source.TryGetProperty("messages", out var messages))
                    TakeMessages(messages);
        }
        var conversations = await Publishing.Sources(
            db,
            journey.WorkspaceId,
            Publishing.ConversationIds(journey.Graph),
            messageIds.ToArray()
        );
        publication.Snapshot = Json.Write(
            Publishing.Snapshot(
                journey,
                conversations,
                new PublishRequest(messageIds.ToArray(), [], publication.Visibility, true, curators.ToArray())
            )
        );
        await db.SaveChangesAsync();
        using var rewritten = JsonDocument.Parse(publication.Snapshot);
        Console.WriteLine(
            $"Rewrote {slug}: {publication.Snapshot.Length} bytes, {rewritten.RootElement.GetProperty("steps").GetArrayLength()} steps, {rewritten.RootElement.GetProperty("connections").GetArrayLength()} connections."
        );
    }

    static async Task<object> MeasureClosed(
        HttpClient client,
        CommandRecorder commands,
        Process process,
        (string Name, string Path) scenario,
        int samples,
        int concurrency,
        int conversations,
        int messages,
        int journeys
    )
    {
        for (var i = 0; i < 5; i++)
            await Fetch(client, scenario.Path, CancellationToken.None);
        commands.Begin(scenario.Name);
        GC.Collect();
        GC.WaitForPendingFinalizers();
        var allocated = GC.GetTotalAllocatedBytes(true);
        var pause = GC.GetTotalPauseDuration();
        var gen0 = GC.CollectionCount(0);
        var gen1 = GC.CollectionCount(1);
        var gen2 = GC.CollectionCount(2);
        process.Refresh();
        var cpu = process.TotalProcessorTime;
        var measured = new System.Collections.Concurrent.ConcurrentBag<(double Ms, long Bytes, int Status)>();
        var timer = Stopwatch.StartNew();
        await Parallel.ForEachAsync(
            Enumerable.Range(0, samples),
            new ParallelOptions { MaxDegreeOfParallelism = concurrency },
            async (_, ct) =>
            {
                var clock = Stopwatch.StartNew();
                try
                {
                    var (bytes, status) = await Fetch(client, scenario.Path, ct);
                    measured.Add((clock.Elapsed.TotalMilliseconds, bytes, status));
                }
                catch (Exception)
                {
                    measured.Add((clock.Elapsed.TotalMilliseconds, 0, 0));
                }
            }
        );
        timer.Stop();
        process.Refresh();
        var cpuMs = (process.TotalProcessorTime - cpu).TotalMilliseconds;
        var (queries, dbMs) = commands.End();
        var ordered = measured.Select(m => m.Ms).Order().ToArray();
        double Percentile(double p) =>
            ordered.Length == 0 ? 0 : Math.Round(ordered[Math.Max(0, (int)Math.Ceiling(ordered.Length * p) - 1)], 2);
        return new
        {
            mode = "closed",
            conversations,
            messages,
            journeys,
            scenario = scenario.Name,
            concurrency,
            samples = measured.Count,
            p50Ms = Percentile(.50),
            p95Ms = Percentile(.95),
            p99Ms = Percentile(.99),
            meanBytes = measured.Count == 0 ? 0 : Math.Round(measured.Average(m => m.Bytes)),
            failures = measured.Count(m => m.Status != 200),
            requestsPerSecond = Math.Round(measured.Count / timer.Elapsed.TotalSeconds, 2),
            cpuMsPerRequest = Math.Round(cpuMs / Math.Max(1, measured.Count), 2),
            coresBusy = Math.Round(cpuMs / timer.Elapsed.TotalMilliseconds, 2),
            queriesPerRequest = Math.Round(queries / (double)Math.Max(1, measured.Count), 2),
            dbMsPerRequest = Math.Round(dbMs / Math.Max(1, measured.Count), 2),
            allocatedMiBPerRequest = Math.Round((GC.GetTotalAllocatedBytes(true) - allocated) / 1048576d / Math.Max(1, measured.Count), 2),
            gcPauseMsPerRequest = Math.Round((GC.GetTotalPauseDuration() - pause).TotalMilliseconds / Math.Max(1, measured.Count), 3),
            gen0 = GC.CollectionCount(0) - gen0,
            gen1 = GC.CollectionCount(1) - gen1,
            gen2 = GC.CollectionCount(2) - gen2,
            processWorkingSetMiB = Math.Round(process.WorkingSet64 / 1048576d, 2),
        };
    }

    static async Task<object> MeasureArrival(
        HttpClient client,
        Process process,
        (string Name, string Path)[] scenarios,
        int rps,
        int seconds,
        string label,
        int conversations,
        int messages,
        int journeys
    )
    {
        var measured = new System.Collections.Concurrent.ConcurrentBag<(double ServiceMs, double TotalMs, int Status)>();
        var interval = 1d / rps;
        var start = Stopwatch.StartNew();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(seconds));
        var tasks = new List<Task>();
        var scheduled = 0;
        process.Refresh();
        var cpu = process.TotalProcessorTime;
        try
        {
            while (!cts.IsCancellationRequested)
            {
                var intended = TimeSpan.FromSeconds(scheduled * interval);
                var wait = intended - start.Elapsed;
                if (wait > TimeSpan.Zero)
                    await Task.Delay(wait, cts.Token);
                var path = scenarios[scheduled % scenarios.Length].Path;
                var due = intended;
                scheduled++;
                tasks.Add(
                    Task.Run(async () =>
                    {
                        var clock = Stopwatch.StartNew();
                        try
                        {
                            var (_, status) = await Fetch(client, path, CancellationToken.None);
                            measured.Add((clock.Elapsed.TotalMilliseconds, (start.Elapsed - due).TotalMilliseconds, status));
                        }
                        catch
                        {
                            measured.Add((clock.Elapsed.TotalMilliseconds, (start.Elapsed - due).TotalMilliseconds, 0));
                        }
                    })
                );
            }
        }
        catch (OperationCanceledException) { }
        await Task.WhenAll(tasks);
        start.Stop();
        process.Refresh();
        var cpuMs = (process.TotalProcessorTime - cpu).TotalMilliseconds;
        var totals = measured.Select(m => m.TotalMs).Order().ToArray();
        var services = measured.Select(m => m.ServiceMs).Order().ToArray();
        double Pct(double[] xs, double p) =>
            xs.Length == 0 ? 0 : Math.Round(xs[Math.Max(0, (int)Math.Ceiling(xs.Length * p) - 1)], 2);
        var row = new
        {
            mode = label,
            conversations,
            messages,
            journeys,
            scenario = "mixed",
            targetRps = rps,
            seconds,
            issued = scheduled,
            completed = measured.Count,
            p50ServiceMs = Pct(services, .50),
            p95ServiceMs = Pct(services, .95),
            p99ServiceMs = Pct(services, .99),
            p50TotalMs = Pct(totals, .50),
            p95TotalMs = Pct(totals, .95),
            p99TotalMs = Pct(totals, .99),
            failures = measured.Count(m => m.Status is not (200 or 304)),
            achievedRps = Math.Round(measured.Count / start.Elapsed.TotalSeconds, 2),
            coresBusy = Math.Round(cpuMs / start.Elapsed.TotalMilliseconds, 2),
            processWorkingSetMiB = Math.Round(process.WorkingSet64 / 1048576d, 2),
        };
        Console.WriteLine(JsonSerializer.Serialize(row));
        return row;
    }

    static async Task<List<object>> MeasureAdverse(HttpClient client, BenchFactory factory)
    {
        var rows = new List<object>();
        using var sam = factory.CreateClient();
        sam.DefaultRequestHeaders.Add("X-Journey", "1");
        (await sam.PostAsJsonAsync("/api/auth/login", new { email = "sam@journey.local", password = "JourneyDemo!2026" })).EnsureSuccessStatusCode();
        var hidden = await sam.GetAsync("/api/conversations/scale-personal-0");
        var missing = await sam.GetAsync("/api/conversations/does-not-exist");
        rows.Add(new { mode = "adverse", scenario = "outsider-404", hidden = (int)hidden.StatusCode, missing = (int)missing.StatusCode, same = await hidden.Content.ReadAsStringAsync() == await missing.Content.ReadAsStringAsync() });
        using var anonymous = factory.CreateClient();
        var ok = await anonymous.GetAsync("/api/public/scale-public");
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDb>();
            await db.Publications.Where(p => p.Slug == "scale-public").ExecuteDeleteAsync();
        }
        var gone = await anonymous.GetAsync("/api/public/scale-public");
        rows.Add(new { mode = "adverse", scenario = "deleted-publication", before = (int)ok.StatusCode, after = (int)gone.StatusCode });
        foreach (var row in rows)
            Console.WriteLine(JsonSerializer.Serialize(row));
        return rows;
    }

    static async Task<(long Bytes, int Status)> Fetch(HttpClient client, string path, CancellationToken ct)
    {
        using var response = await client.GetAsync(path, HttpCompletionOption.ResponseHeadersRead, ct);
        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        var buffer = System.Buffers.ArrayPool<byte>.Shared.Rent(81920);
        long total = 0;
        try
        {
            int read;
            while ((read = await stream.ReadAsync(buffer, ct)) > 0)
                total += read;
        }
        finally
        {
            System.Buffers.ArrayPool<byte>.Shared.Return(buffer);
        }
        return (total, (int)response.StatusCode);
    }

    static async Task Analyze(string connection)
    {
        await using var postgres = new NpgsqlConnection(connection);
        await postgres.OpenAsync();
        await using var command = new NpgsqlCommand("ANALYZE", postgres);
        await command.ExecuteNonQueryAsync();
    }

}

/// <summary>Counts database commands and keeps the slowest one per scenario for EXPLAIN.</summary>
public sealed class CommandRecorder : DbCommandInterceptor
{
    readonly object gate = new();
    readonly Dictionary<string, (double Ms, string Sql, (string Name, object? Value)[] Parameters)> slowest = [];
    string? scenario;
    long count;
    double totalMs;

    public void Begin(string name)
    {
        lock (gate)
        {
            scenario = name;
            count = 0;
            totalMs = 0;
            slowest.Remove(name);
        }
    }

    public (long Count, double Ms) End()
    {
        lock (gate)
        {
            var result = (count, totalMs);
            scenario = null;
            return result;
        }
    }

    void Record(DbCommand command, TimeSpan duration)
    {
        lock (gate)
        {
            if (scenario == null)
                return;
            count++;
            totalMs += duration.TotalMilliseconds;
            if (!slowest.TryGetValue(scenario, out var current) || duration.TotalMilliseconds > current.Ms)
                slowest[scenario] = (duration.TotalMilliseconds, command.CommandText,
                    command.Parameters.Cast<DbParameter>().Select(p => (p.ParameterName, p.Value)).ToArray());
        }
    }

    public override ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command, CommandExecutedEventData eventData, DbDataReader result, CancellationToken ct = default)
    {
        Record(command, eventData.Duration);
        return ValueTask.FromResult(result);
    }

    public override ValueTask<object?> ScalarExecutedAsync(DbCommand command, CommandExecutedEventData eventData, object? result, CancellationToken ct = default)
    {
        Record(command, eventData.Duration);
        return ValueTask.FromResult(result);
    }

    public override ValueTask<int> NonQueryExecutedAsync(DbCommand command, CommandExecutedEventData eventData, int result, CancellationToken ct = default)
    {
        Record(command, eventData.Duration);
        return ValueTask.FromResult(result);
    }

    public async Task<string> ExplainSlowest(string connection, string name, int size)
    {
        (double Ms, string Sql, (string Name, object? Value)[] Parameters) entry;
        lock (gate)
            if (!slowest.TryGetValue(name, out entry))
                return "";
        var text = new StringBuilder($"=== {name} @ {size} conversations (slowest command {entry.Ms:F1} ms) ===\n{entry.Sql}\n\n");
        await using var postgres = new NpgsqlConnection(connection);
        await postgres.OpenAsync();
        await using var command = new NpgsqlCommand("EXPLAIN (ANALYZE, BUFFERS) " + entry.Sql, postgres);
        foreach (var (parameter, value) in entry.Parameters)
            command.Parameters.AddWithValue(parameter, value ?? DBNull.Value);
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            text.AppendLine(reader.GetString(0));
        return text.AppendLine().ToString();
    }
}

public sealed class BenchFactory(string connection, CommandRecorder commands) : WebApplicationFactory<global::Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder) => builder
        .UseContentRoot(Path.GetFullPath("src/Journey.Server"))
        .UseEnvironment("Testing")
        .ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Database:Provider"] = "PostgreSQL", ["ConnectionStrings:Journey"] = connection,
            ["Imports:StoragePath"] = Path.Combine(Path.GetTempPath(), "journey-scale-scratch"),
            ["Logging:LogLevel:Default"] = "Error", ["Logging:LogLevel:Microsoft"] = "Error"
        }))
        .ConfigureTestServices(services =>
        {
            foreach (var worker in services.Where(d => d.ServiceType == typeof(IHostedService)
                         && d.ImplementationType?.Name is "ImportWorker" or "ImportRetentionWorker").ToList())
                services.Remove(worker);
            services.ConfigureDbContext<AppDb>(o => o.AddInterceptors(commands));
        });
}
