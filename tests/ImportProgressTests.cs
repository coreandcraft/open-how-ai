using System.IO.Compression;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Journey.Service.Features.Imports;

namespace Journey.Tests;

public class ImportProgressTests : IClassFixture<Factory>
{
    readonly Factory factory;

    public ImportProgressTests(Factory factory) => this.factory = factory;

    [Fact]
    public void ProgressIsLiveAndIsolatedByAccountAndWorkspace()
    {
        using var store = new ImportProgressStore();
        var run = store.Begin("a", "w", "id");
        run.Total(10);
        run.Stage("processing");
        run.Processed();
        Assert.Equal(1, store.Read("a", "w", "id").Processed);
        run.Processed(true);
        Assert.Equal(new ImportFileProgress("processing", 2, 10, 1), store.Read("a", "w", "id"));
        run.Bytes(10, 40, 50, ImportLimits.DefaultBytes);
        var sized = store.Read("a", "w", "id");
        Assert.Equal(10, sized.CompressedBytes);
        Assert.Equal(40, sized.UncompressedBytes);
        Assert.Equal(50, sized.UsedBytes);
        Assert.Equal(ImportLimits.DefaultBytes, sized.LimitBytes);
        Assert.Equal(0, store.Read("b", "w", "id").Total);
        Assert.Equal(0, store.Read("a", "other", "id").Total);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NestedArchivesAndUnsupportedFilesAreSkippedAndSupportedFilesCommit(
        bool sanitize
    )
    {
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Journey", "1");
        var email = Guid.NewGuid() + "@fixture.test";
        (
            await client.PostAsJsonAsync(
                "/api/auth/register",
                new
                {
                    email,
                    password = "JourneyTest!2026",
                    name = "Progress tester",
                }
            )
        ).EnsureSuccessStatusCode();
        await AccountTestHelper.ConfirmAndLogin(factory, client, email, "JourneyTest!2026");
        var wid = (await client.GetFromJsonAsync<JsonElement>("/api/workspaces"))[0]
            .GetProperty("id")
            .GetString();
        using var buffer = new MemoryStream();
        using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, true))
        {
            using (var nested = zip.CreateEntry("nested.zip").Open())
                nested.Write(new byte[] { 80, 75, 5, 6, 0, 0, 0, 0 });
            using (var bad = zip.CreateEntry("unsupported.json").Open())
                bad.Write("not json"u8);
            using (var valid = zip.CreateEntry("conversation.json").Open())
                valid.Write(
                    Encoding.UTF8.GetBytes(
                        "{\"messages\":[{\"role\":\"user\",\"content\":\"hello\"}]}"
                    )
                );
        }
        var payload = buffer.ToArray();
        var progressId = Guid.NewGuid().ToString();
        using var form = new MultipartFormDataContent();
        form.Add(new ByteArrayContent(payload), "files", "archive.zip");
        form.Add(
            new StringContent(
                JsonSerializer.Serialize(new { enabled = sanitize, allowOriginalFiles = true })
            ),
            "sanitization"
        );
        var response = await client.PostAsync(
            $"/api/w/{wid}/imports/preview?progressId={progressId}",
            form
        );
        response.EnsureSuccessStatusCode();
        var preview = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(2, preview.GetProperty("skippedFiles").GetArrayLength());
        var conversation = Assert.Single(preview.GetProperty("conversations").EnumerateArray());
        var progress = await client.GetFromJsonAsync<ImportFileProgress>(
            $"/api/w/{wid}/imports/progress/{progressId}"
        );
        Assert.Equal("ready", progress!.Stage);
        Assert.Equal(3, progress.Processed);
        Assert.Equal(3, progress.Total);
        Assert.Equal(2, progress.Skipped);
        using var read = new ZipArchive(new MemoryStream(payload), ZipArchiveMode.Read);
        var uncompressed = read.Entries.Sum(entry => entry.Length);
        Assert.Equal(payload.LongLength, progress.CompressedBytes);
        Assert.Equal(uncompressed, progress.UncompressedBytes);
        Assert.Equal(payload.LongLength + uncompressed, progress.UsedBytes);
        Assert.Equal(ImportLimits.DefaultBytes, progress.LimitBytes);
        var bid = preview.GetProperty("batchId").GetString();
        (
            await client.PostAsJsonAsync(
                $"/api/imports/{bid}/commit",
                new { keys = new[] { conversation.GetProperty("key").GetString() } }
            )
        ).EnsureSuccessStatusCode();
        JsonElement status = default;
        for (var i = 0; i < 100; i++)
        {
            status = await client.GetFromJsonAsync<JsonElement>($"/api/imports/{bid}");
            if (status.GetProperty("status").GetString()!.StartsWith("completed"))
                break;
            await Task.Delay(100);
        }
        Assert.Equal("completed", status.GetProperty("status").GetString());
        Assert.Equal(1, status.GetProperty("imported").GetInt32());
        Assert.Equal(1, status.GetProperty("fileProgress").GetProperty("processed").GetInt32());
    }
}
