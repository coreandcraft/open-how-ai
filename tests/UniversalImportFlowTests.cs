using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace Journey.Tests;

public class UniversalImportFlowTests : IClassFixture<Factory>
{
    readonly Factory factory;

    public UniversalImportFlowTests(Factory factory) => this.factory = factory;

    [Fact]
    public async Task ImportsMultipleProvidersIntoOneProjectAndPreservesSessionHierarchy()
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
                    name = "Import tester",
                }
            )
        ).EnsureSuccessStatusCode();
        await AccountTestHelper.ConfirmAndLogin(factory, client, email, "JourneyTest!2026");
        var workspaces = await client.GetFromJsonAsync<JsonElement>("/api/workspaces");
        var wid = workspaces[0].GetProperty("id").GetString();
        using var form = new MultipartFormDataContent();
        foreach (var name in new[] { "claude-code.jsonl", "codex-rollout.jsonl" })
            form.Add(
                new ByteArrayContent(
                    File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "fixtures", name))
                ),
                "files",
                name
            );
        var preview = await client.PostAsync("/api/w/" + wid + "/imports/preview", form);
        preview.EnsureSuccessStatusCode();
        var data = await preview.Content.ReadFromJsonAsync<JsonElement>();
        var bid = data.GetProperty("batchId").GetString();
        var keys = data.GetProperty("conversations")
            .EnumerateArray()
            .Select(c => c.GetProperty("key").GetString())
            .ToArray();
        Assert.Equal(3, keys.Length);
        var invalid = await client.PostAsJsonAsync(
            "/api/imports/" + bid + "/commit",
            new { keys = new[] { "not-in-preview" } }
        );
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        (
            await client.PostAsJsonAsync(
                "/api/imports/" + bid + "/commit",
                new { keys, newProjectName = "Unified project" }
            )
        ).EnsureSuccessStatusCode();
        JsonElement status = default;
        for (int i = 0; i < 100; i++)
        {
            status = await client.GetFromJsonAsync<JsonElement>("/api/imports/" + bid);
            if (status.GetProperty("status").GetString()!.StartsWith("completed"))
                break;
            await Task.Delay(100);
        }
        Assert.Equal("completed", status.GetProperty("status").GetString());
        Assert.Equal(3, status.GetProperty("imported").GetInt32());
        var projects = await client.GetFromJsonAsync<JsonElement>("/api/w/" + wid + "/projects");
        Assert.Single(projects.EnumerateArray());
        var library = (
            await client.GetFromJsonAsync<JsonElement>(
                "/api/w/" + wid + "/library?project=" + projects[0].GetProperty("id").GetString()
            )
        ).GetProperty("items");
        Assert.Equal(3, library.GetArrayLength());
        Assert.All(
            library.EnumerateArray(),
            c =>
                Assert.Equal(
                    projects[0].GetProperty("id").GetString(),
                    c.GetProperty("applicationProjectId").GetString()
                )
        );
        var sourceGroups = await client.GetFromJsonAsync<JsonElement>("/api/w/" + wid + "/library-groups");
        Assert.Contains(sourceGroups.EnumerateArray(), g => g.GetProperty("provider").GetString() == "Codex"
            && g.GetProperty("name").GetString() == "C:/demo/cloudplay");
        var codexGroup = Assert.Single(sourceGroups.EnumerateArray(), g => g.GetProperty("provider").GetString() == "Codex");
        var groupedLibrary = await client.GetFromJsonAsync<JsonElement>("/api/w/" + wid + "/library?sourceGroup="
            + Uri.EscapeDataString(codexGroup.GetProperty("key").GetString()!));
        Assert.Equal("Codex", Assert.Single(groupedLibrary.GetProperty("items").EnumerateArray()).GetProperty("provider").GetString());
        var relations = await client.GetFromJsonAsync<JsonElement>(
            "/api/w/" + wid + "/source-relationships"
        );
        Assert.Contains(
            relations.EnumerateArray(),
            r => r.GetProperty("status").GetString() == "confirmed"
        );
        Assert.Contains(
            relations.EnumerateArray(),
            r => r.GetProperty("status").GetString() == "unresolved"
        );
        using var repeat = new MultipartFormDataContent();
        repeat.Add(
            new ByteArrayContent(
                File.ReadAllBytes(
                    Path.Combine(AppContext.BaseDirectory, "fixtures", "claude-code.jsonl")
                )
            ),
            "files",
            "renamed.jsonl"
        );
        var duplicate = await client.PostAsync("/api/w/" + wid + "/imports/preview", repeat);
        duplicate.EnsureSuccessStatusCode();
        var duplicateData = await duplicate.Content.ReadFromJsonAsync<JsonElement>();
        var duplicateConversations = duplicateData.GetProperty("conversations");
        Assert.All(duplicateConversations.EnumerateArray(),
            c => Assert.True(c.GetProperty("duplicate").GetBoolean()));
        var duplicateBatch = duplicateData.GetProperty("batchId").GetString();
        (await client.PostAsJsonAsync("/api/imports/" + duplicateBatch + "/commit", new
        {
            keys = duplicateConversations.EnumerateArray().Select(c => c.GetProperty("key").GetString()).ToArray(),
            updateExisting = true,
        })).EnsureSuccessStatusCode();
        for (int i = 0; i < 100; i++)
        {
            status = await client.GetFromJsonAsync<JsonElement>("/api/imports/" + duplicateBatch);
            if (status.GetProperty("status").GetString()!.StartsWith("completed"))
                break;
            await Task.Delay(100);
        }
        Assert.Equal("completed", status.GetProperty("status").GetString());
        Assert.Equal(0, status.GetProperty("imported").GetInt32());
        Assert.Equal(duplicateConversations.GetArrayLength(), status.GetProperty("duplicates").GetInt32());
        var unchanged = await client.GetFromJsonAsync<JsonElement>("/api/w/" + wid + "/library");
        Assert.Equal(3, unchanged.GetProperty("items").GetArrayLength());
    }
}
