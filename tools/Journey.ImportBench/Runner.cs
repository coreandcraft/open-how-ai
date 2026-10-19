using System.Diagnostics;
using System.IO.Compression;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Journey.ImportBench;

public static class Runner
{
    public static async Task Main(string[] args)
    {
        long bytes = args.Length > 0 ? long.Parse(args[0]) : 500_000_000;
        int files = args.Length > 1 ? int.Parse(args[1]) : 147;
        if (bytes is < 1000 or > 2_000_000_000 || files is < 1 or > 500 || bytes / files < 200)
            throw new ArgumentException("Use 1,000–2,000,000,000 expanded bytes and 1–500 files.");
        var runRoot = Path.Combine(Path.GetTempPath(), "journey-import-bench-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(runRoot);
        try
        {
            var archive = Path.Combine(runRoot, "synthetic.zip");
            using (var zip = ZipFile.Open(archive, ZipArchiveMode.Create))
            {
                var block = Enumerable.Repeat((byte)'a', 81920).ToArray();
                for (int i = 0; i < files; i++)
                {
                    var prefix = Encoding.UTF8.GetBytes($"{{\"id\":\"bench-{i}\",\"messages\":[{{\"role\":\"user\",\"content\":\"");
                    var suffix = " password=synthetic-secret contact@example.test\"}]}"u8.ToArray(); // privacy: synthetic-credential=synthetic-secret
                    using var entry = zip.CreateEntry($"transcript-{i}.json", CompressionLevel.NoCompression).Open();
                    entry.Write(prefix);
                    var remaining = bytes / files + (i < bytes % files ? 1 : 0) - prefix.Length - suffix.Length;
                    while (remaining > 0)
                    {
                        var count = (int)Math.Min(block.Length, remaining);
                        entry.Write(block, 0, count);
                        remaining -= count;
                    }
                    entry.Write(suffix);
                }
            }
            using var app = new BenchFactory(runRoot);
            using var client = app.CreateClient();
            client.Timeout = TimeSpan.FromMinutes(15);
            client.DefaultRequestHeaders.Add("X-Journey", "1");
            (await client.PostAsJsonAsync("/api/auth/login", new
                { email = "alex@journey.local", password = "JourneyDemo!2026" })).EnsureSuccessStatusCode();
            using var form = new MultipartFormDataContent();
            form.Add(new StreamContent(File.OpenRead(archive)), "files", "synthetic.zip");
            form.Add(new StringContent("{\"enabled\":true,\"scanPatterns\":true}"), "sanitization");
            var watch = Stopwatch.StartNew();
            using var accepted = await client.PostAsync("/api/w/cloudplay/imports/preview?background=true", form);
            accepted.EnsureSuccessStatusCode();
            var receiptMs = watch.Elapsed.TotalMilliseconds;
            JsonElement preview = default;
            while (true)
            {
                using var response = await client.GetAsync(accepted.Headers.Location);
                if (response.StatusCode == HttpStatusCode.Accepted) { await Task.Delay(100); continue; }
                response.EnsureSuccessStatusCode();
                preview = await response.Content.ReadFromJsonAsync<JsonElement>();
                break;
            }
            var result = new
            {
                generatedAtUtc = DateTime.UtcNow, expandedBytes = bytes, uploadedBytes = new FileInfo(archive).Length,
                files, sanitizerEnabled = true, patternScanningEnabled = true,
                wallMilliseconds = watch.Elapsed.TotalMilliseconds, uploadAndStagingMilliseconds = receiptMs,
                conversations = preview.GetProperty("conversations").GetArrayLength(),
                errors = preview.GetProperty("errors").GetArrayLength(),
                skipped = preview.GetProperty("skippedFiles").GetArrayLength(),
                progress = preview.GetProperty("fileProgress"),
                scope = "Synthetic preview only; local TestServer, SQLite and private disk. Excludes Azure network/database latency and commit.",
            };
            var output = Path.GetFullPath(Path.Combine("artifacts", "import-benchmark-" + Guid.NewGuid().ToString("N") + ".json"));
            Directory.CreateDirectory(Path.GetDirectoryName(output)!);
            await File.WriteAllTextAsync(output, JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
            Console.WriteLine(JsonSerializer.Serialize(result));
            Console.WriteLine(output);
            if (result.errors != 0 || result.skipped != 0 || result.conversations != files)
                throw new InvalidOperationException("The benchmark did not preview every synthetic conversation.");
        }
        finally
        {
            // This is the unique directory created above, never an input/user directory.
            Directory.Delete(runRoot, true);
        }
    }

    sealed class BenchFactory(string root) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder) => builder
            .UseContentRoot(ServerRoot())
            .UseEnvironment("Testing")
            .ConfigureLogging(logging => logging.ClearProviders())
            .ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Journey"] = "Data Source=" + Path.Combine(root, "benchmark.db") + ";Pooling=False",
                ["Database:Provider"] = "Sqlite", ["Storage:Provider"] = "Local",
                ["Imports:StoragePath"] = Path.Combine(root, "sources"),
                ["Mcp:Enabled"] = "false", ["GitHub:Enabled"] = "false",
            }));

        static string ServerRoot()
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory != null && !File.Exists(Path.Combine(directory.FullName, "JourneyAI.sln")))
                directory = directory.Parent;
            return directory == null ? throw new DirectoryNotFoundException("Run this benchmark from the JourneyAI checkout.")
                : Path.Combine(directory.FullName, "src", "Journey.Server");
        }
    }
}
