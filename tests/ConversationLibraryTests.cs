using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Journey.Data;
using Journey.Domain;
using Microsoft.Extensions.DependencyInjection;

namespace Journey.Tests;

public class ConversationLibraryTests : IClassFixture<Factory>
{
    readonly Factory factory;

    public ConversationLibraryTests(Factory factory) => this.factory = factory;

    async Task<HttpClient> Login(string who)
    {
        var c = factory.CreateClient();
        c.DefaultRequestHeaders.Add("X-Journey", "1");
        var r = await c.PostAsJsonAsync(
            "/api/auth/login",
            new { email = who + "@journey.local", password = "JourneyDemo!2026" }
        );
        r.EnsureSuccessStatusCode();
        return c;
    }

    [Fact]
    public async Task LibraryNamesTheProjectAndOwnerCanDelete()
    {
        using var c = await Login("alex");
        var created = await c.PostAsJsonAsync(
            "/api/w/cloudplay/projects",
            new { name = "Transcripts" }
        );
        created.EnsureSuccessStatusCode();
        var projectId = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetString();
        (await c.PutAsJsonAsync("/api/conversations/demo-c0/project", new { projectId })).EnsureSuccessStatusCode();
        var library = await c.GetFromJsonAsync<JsonElement>("/api/w/cloudplay/library?limit=50");
        var match = library
            .GetProperty("items")
            .EnumerateArray()
            .Single(x => x.GetProperty("id").GetString() == "demo-c0");
        Assert.Equal("Transcripts", match.GetProperty("projectName").GetString());
        Assert.Equal(HttpStatusCode.NoContent, (await c.DeleteAsync("/api/conversations/demo-c0")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await c.GetAsync("/api/conversations/demo-c0")).StatusCode);
        var after = await c.GetFromJsonAsync<JsonElement>("/api/w/cloudplay/library?limit=50");
        Assert.DoesNotContain(
            after.GetProperty("items").EnumerateArray(),
            x => x.GetProperty("id").GetString() == "demo-c0"
        );
    }

    [Fact]
    public async Task ViewerCannotDeleteAConversation()
    {
        using var c = await Login("riley");
        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await c.DeleteAsync("/api/conversations/demo-c1")).StatusCode
        );
    }

    [Fact]
    public async Task LibrarySortsByMessageCountAcrossPages()
    {
        await Seed(async db =>
        {
            db.Conversations.Add(Sample("sort-low", 1));
            db.Conversations.Add(Sample("sort-high", 4));
            await db.SaveChangesAsync();
        });
        using var c = await Login("alex");
        var first = await c.GetFromJsonAsync<JsonElement>(
            "/api/w/cloudplay/library?provider=SortProbe&sort=messages&limit=1"
        );
        var firstItem = Assert.Single(first.GetProperty("items").EnumerateArray());
        Assert.Equal("sort-high", firstItem.GetProperty("id").GetString());
        Assert.Equal(4, firstItem.GetProperty("messageCount").GetInt32());
        var cursor = first.GetProperty("nextCursor").GetString();
        Assert.False(string.IsNullOrEmpty(cursor));
        var second = await c.GetFromJsonAsync<JsonElement>(
            "/api/w/cloudplay/library?provider=SortProbe&sort=messages&limit=1&cursor="
                + Uri.EscapeDataString(cursor!)
        );
        var secondItem = Assert.Single(second.GetProperty("items").EnumerateArray());
        Assert.Equal("sort-low", secondItem.GetProperty("id").GetString());
        Assert.Equal(1, secondItem.GetProperty("messageCount").GetInt32());
        Assert.Equal(JsonValueKind.Null, second.GetProperty("nextCursor").ValueKind);
        Assert.Equal(
            HttpStatusCode.BadRequest,
            (await c.GetAsync("/api/w/cloudplay/library?sort=title")).StatusCode
        );
    }

    [Fact]
    public async Task OwnerCanDeleteEveryConversationInAProject()
    {
        using var owner = await Login("alex");
        var created = await owner.PostAsJsonAsync(
            "/api/w/cloudplay/projects",
            new { name = "Bulk delete" }
        );
        created.EnsureSuccessStatusCode();
        var projectId = (await created.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("id")
            .GetString()!;
        await Seed(async db =>
        {
            db.Conversations.Add(Sample("bulk-a", 2, projectId));
            db.Conversations.Add(Sample("bulk-b", 1, projectId));
            db.Conversations.Add(Sample("bulk-keep", 1));
            await db.SaveChangesAsync();
        });
        using var viewer = await Login("riley");
        Assert.Equal(
            HttpStatusCode.Forbidden,
            (
                await viewer.DeleteAsync(
                    "/api/w/cloudplay/projects/" + projectId + "/conversations"
                )
            ).StatusCode
        );
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await owner.DeleteAsync("/api/w/cloudplay/projects/missing/conversations")).StatusCode
        );
        var deleted = await owner.DeleteAsync(
            "/api/w/cloudplay/projects/" + projectId + "/conversations"
        );
        deleted.EnsureSuccessStatusCode();
        Assert.Equal(
            2,
            (await deleted.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("deleted").GetInt32()
        );
        Assert.Equal(HttpStatusCode.NotFound, (await owner.GetAsync("/api/conversations/bulk-a")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await owner.GetAsync("/api/conversations/bulk-b")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await owner.GetAsync("/api/conversations/bulk-keep")).StatusCode);
        var remaining = await owner.GetFromJsonAsync<JsonElement>(
            "/api/w/cloudplay/library?project=" + projectId
        );
        Assert.Empty(remaining.GetProperty("items").EnumerateArray());
    }

    [Fact]
    public async Task SourceGroupsFilterAcrossPagesAndKeepProvidersAndAccountsSeparate()
    {
        await Seed(async db =>
        {
            foreach (var (id, provider, identity, path, name, external) in new[]
            {
                ("group-a", "Codex", "account-a", "C:/Dev/Shared", (string?)null, (string?)null),
                ("group-b", "Codex", "account-a", "C:/Dev/Shared", (string?)null, (string?)null),
                ("group-c", "Codex", "account-b", "C:/Dev/Shared", (string?)null, (string?)null),
                ("group-d", "Claude Code", "account-a", "C:/Dev/Shared", (string?)null, (string?)null),
                ("group-e", "Codex", "account-a", "C:/Other", "Named project", "external-project"),
                ("group-f", "Codex", "account-a", "C:/Other", (string?)null, "external-project"),
            })
            {
                var conversation = Sample(id, 1);
                conversation.Provider = provider;
                conversation.SourceIdentity = identity;
                conversation.SourcePath = path;
                conversation.SourceProjectName = name;
                conversation.ExternalProjectId = external;
                db.Add(conversation);
            }
            await db.SaveChangesAsync();
        });
        using var client = await Login("alex");
        var groups = await client.GetFromJsonAsync<JsonElement>("/api/w/cloudplay/library-groups");
        var shared = groups.EnumerateArray().Where(g => g.GetProperty("name").GetString() == "C:/Dev/Shared").ToArray();
        Assert.Equal(3, shared.Length);
        var group = Assert.Single(shared, g => g.GetProperty("provider").GetString() == "Codex"
            && g.GetProperty("sourceIdentity").GetString() == "account-a");
        Assert.Equal(2, group.GetProperty("count").GetInt32());
        var key = group.GetProperty("key").GetString()!;
        var url = "/api/w/cloudplay/library?sourceGroup=" + Uri.EscapeDataString(key) + "&limit=1";
        var first = await client.GetFromJsonAsync<JsonElement>(url);
        var item = Assert.Single(first.GetProperty("items").EnumerateArray());
        Assert.Equal(key, item.GetProperty("sourceGroupKey").GetString());
        Assert.Equal("account-a", item.GetProperty("sourceIdentity").GetString());
        var next = first.GetProperty("nextCursor").GetString()!;
        var second = await client.GetFromJsonAsync<JsonElement>(url + "&cursor=" + Uri.EscapeDataString(next));
        var secondItem = Assert.Single(second.GetProperty("items").EnumerateArray());
        Assert.NotEqual(item.GetProperty("id").GetString(), secondItem.GetProperty("id").GetString());
        Assert.Null(second.GetProperty("nextCursor").GetString());
        foreach (var expected in new[] { ("Named project", "group-e"), ("external-project", "group-f") })
        {
            var source = Assert.Single(groups.EnumerateArray(), g => g.GetProperty("name").GetString() == expected.Item1);
            var filtered = await client.GetFromJsonAsync<JsonElement>(
                "/api/w/cloudplay/library?sourceGroup=" + Uri.EscapeDataString(source.GetProperty("key").GetString()!));
            Assert.Equal(expected.Item2, Assert.Single(filtered.GetProperty("items").EnumerateArray()).GetProperty("id").GetString());
        }
        var empty = await client.GetFromJsonAsync<JsonElement>(url + "&provider=Claude");
        Assert.Empty(empty.GetProperty("items").EnumerateArray());
        Assert.Equal(HttpStatusCode.BadRequest,
            (await client.GetAsync("/api/w/cloudplay/library?sourceGroup=invalid!")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await client.GetAsync("/api/w/another-workspace/library-groups")).StatusCode);
    }

    async Task Seed(Func<AppDb, Task> action)
    {
        using var scope = factory.Services.CreateScope();
        await action(scope.ServiceProvider.GetRequiredService<AppDb>());
    }

    static Conversation Sample(string id, int messages, string? projectId = null) =>
        new()
        {
            Id = id,
            WorkspaceId = "cloudplay",
            SourceKey = id,
            Title = id,
            Provider = id.StartsWith("sort-") ? "SortProbe" : "Bench",
            ApplicationProjectId = projectId,
            Messages = Enumerable
                .Range(0, messages)
                .Select(i => new ConversationMessage
                {
                    Id = id + "-m" + i,
                    ConversationId = id,
                    Order = i,
                    Role = "user",
                    Blocks = [new MessageContentBlock { Kind = "text", Text = "hello " + i }],
                })
                .ToList(),
        };
}
