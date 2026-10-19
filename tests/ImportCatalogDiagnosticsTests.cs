using System.Net.Http.Json;
using System.Text.Json;
using Journey.Service.Features.Imports;
using Microsoft.Data.Sqlite;

namespace Journey.Tests;

public class ImportCatalogDiagnosticsTests : IClassFixture<Factory>
{
    readonly Factory factory;

    public ImportCatalogDiagnosticsTests(Factory factory) => this.factory = factory;

    static byte[] Catalog()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".db");
        try
        {
            using (var connection = new SqliteConnection("Data Source=" + path + ";Pooling=False"))
            {
                connection.Open();
                using var command = connection.CreateCommand();
                command.CommandText =
                    "CREATE TABLE local_thread_catalog(thread_id TEXT, display_title TEXT); "
                    + "INSERT INTO local_thread_catalog VALUES ('chat-a','PRIVATE TITLE'); "
                    + "CREATE TABLE thread_timeline_ledger(payload_json TEXT)";
                command.ExecuteNonQuery();
            }
            return File.ReadAllBytes(path);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Theory]
    [InlineData("codex-dev - Copy.db")]
    [InlineData("renamed.sqlite")]
    public void CatalogExplainsMissingTranscriptWithoutInventingMessages(string name)
    {
        var source = new SourceInput(name, Catalog());
        var registry = new ImportRegistry();
        Assert.Empty(registry.Candidates(source));
        var error = Assert.Throws<InvalidDataException>(() => registry.Detect(source));
        Assert.Contains("chat catalog", error.Message);
        Assert.Contains("titles and IDs", error.Message);
        Assert.Contains(".codex/sessions", error.Message);
        Assert.DoesNotContain("PRIVATE TITLE", error.Message);
    }

    [Theory]
    [InlineData("codex-dev - Copy.db-wal")]
    [InlineData("codex-dev - Copy.db-shm")]
    [InlineData("folder/history.SQLITE-WAL")]
    public void SidecarsExplainSnapshotRequirement(string name)
    {
        var error = Assert.Throws<InvalidDataException>(() =>
            new ImportRegistry().Detect(new SourceInput(name, [0, 1, 2, 3]))
        );
        Assert.Contains("sidecar", error.Message);
        Assert.Contains("does not replay", error.Message);
    }

    [Fact]
    public async Task PreviewReportsCatalogAndSidecarsAndContinuesWithSupportedTranscript()
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
                    name = "Catalog tester",
                }
            )
        ).EnsureSuccessStatusCode();
        await AccountTestHelper.ConfirmAndLogin(factory, client, email, "JourneyTest!2026");
        var workspaces = await client.GetFromJsonAsync<JsonElement>("/api/workspaces");
        var wid = workspaces[0].GetProperty("id").GetString();
        using var form = new MultipartFormDataContent();
        form.Add(new ByteArrayContent(Catalog()), "files", "codex-dev - Copy.db");
        form.Add(new ByteArrayContent([0, 1, 2, 3]), "files", "codex-dev - Copy.db-wal");
        form.Add(new ByteArrayContent([0, 1, 2, 3]), "files", "codex-dev - Copy.db-shm");
        form.Add(
            new ByteArrayContent(
                File.ReadAllBytes(
                    Path.Combine(AppContext.BaseDirectory, "fixtures", "codex-rollout.jsonl")
                )
            ),
            "files",
            "codex-rollout.jsonl"
        );
        var response = await client.PostAsync("/api/w/" + wid + "/imports/preview", form);
        response.EnsureSuccessStatusCode();
        var preview = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Single(preview.GetProperty("conversations").EnumerateArray());
        Assert.Equal(
            "Codex",
            preview.GetProperty("conversations")[0].GetProperty("provider").GetString()
        );
        var errors = preview
            .GetProperty("errors")
            .EnumerateArray()
            .Select(x => x.GetString()!)
            .ToArray();
        Assert.Contains(errors, x => x.Contains("chat catalog"));
        Assert.Equal(2, errors.Count(x => x.Contains("sidecar")));
        Assert.Equal(3, preview.GetProperty("skippedFiles").GetArrayLength());
        Assert.Equal(4, preview.GetProperty("filesProcessed").GetInt32());
        Assert.Equal("ready", preview.GetProperty("fileProgress").GetProperty("stage").GetString());
    }
}
