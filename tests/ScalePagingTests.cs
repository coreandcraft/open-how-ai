using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Journey.Data;
using Journey.Domain;
using Journey.Service.Features.Imports;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Journey.Tests;

public class ScalePagingTests : IClassFixture<CountingFactory>
{
    readonly CountingFactory factory;

    public ScalePagingTests(CountingFactory f) => factory = f;

    async Task<HttpClient> Login(string who)
    {
        var c = factory.CreateClient();
        c.DefaultRequestHeaders.Add("X-Journey", "1");
        (
            await c.PostAsJsonAsync(
                "/api/auth/login",
                new { email = who + "@journey.local", password = "JourneyDemo!2026" }
            )
        ).EnsureSuccessStatusCode();
        return c;
    }

    async Task Seed(Func<AppDb, Task> action)
    {
        using var scope = factory.Services.CreateScope();
        await action(scope.ServiceProvider.GetRequiredService<AppDb>());
    }

    static Conversation Conversation(string id, string workspaceId, int messages, string text = "message")
    {
        var created = DateTime.UtcNow;
        return new Conversation
        {
            Id = id,
            WorkspaceId = workspaceId,
            SourceKey = id,
            Title = id,
            Provider = "Bench",
            CreatedAt = created,
            Messages = Enumerable
                .Range(0, messages)
                .Select(i => new ConversationMessage
                {
                    Id = id + "-m" + i,
                    ConversationId = id,
                    Order = i,
                    Role = i % 2 == 0 ? "user" : "assistant",
                    Blocks = [new MessageContentBlock { Kind = "text", Text = text + " " + i }],
                })
                .ToList(),
        };
    }

    [Fact]
    public async Task LibraryPagesWithoutGapsOrOverlap()
    {
        using var alex = await Login("alex");
        var summary = await alex.GetFromJsonAsync<JsonElement>("/api/w/cloudplay/summary");
        var total = summary.GetProperty("conversations").GetInt32();
        Assert.True(total > 3);
        var ids = new List<string>();
        string? cursor = null;
        do
        {
            var page = await alex.GetFromJsonAsync<JsonElement>(
                "/api/w/cloudplay/library?limit=3" + (cursor == null ? "" : "&cursor=" + cursor)
            );
            var items = page.GetProperty("items");
            Assert.InRange(items.GetArrayLength(), 1, 3);
            ids.AddRange(items.EnumerateArray().Select(i => i.GetProperty("id").GetString()!));
            cursor = page.GetProperty("nextCursor").GetString();
        } while (cursor != null);
        Assert.Equal(total, ids.Count);
        Assert.Equal(total, ids.Distinct().Count());
        Assert.Equal(
            HttpStatusCode.BadRequest,
            (await alex.GetAsync("/api/w/cloudplay/library?cursor=not-a-cursor")).StatusCode
        );
    }

    [Fact]
    public async Task LibrarySearchMatchesMessageTextAndComments()
    {
        await Seed(async db =>
        {
            db.Conversations.Add(Conversation("search-needle", "cloudplay", 2, "quokka-signal"));
            await db.SaveChangesAsync();
        });
        using var alex = await Login("alex");
        var byText = await alex.GetFromJsonAsync<JsonElement>("/api/w/cloudplay/library?q=quokka-signal");
        Assert.Equal(
            "search-needle",
            Assert.Single(byText.GetProperty("items").EnumerateArray()).GetProperty("id").GetString()
        );
        (
            await alex.PostAsJsonAsync(
                "/api/w/cloudplay/annotations",
                new
                {
                    conversationId = "demo-c3",
                    messageId = "demo-m3-0",
                    body = "pangolin-thread",
                }
            )
        ).EnsureSuccessStatusCode();
        var byComment = await alex.GetFromJsonAsync<JsonElement>(
            "/api/w/cloudplay/library?q=pangolin-thread"
        );
        Assert.Equal(
            "demo-c3",
            Assert.Single(byComment.GetProperty("items").EnumerateArray()).GetProperty("id").GetString()
        );
        using var sam = await Login("sam");
        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await sam.GetAsync("/api/w/personal/library?q=quokka-signal")).StatusCode
        );
    }

    [Fact]
    public async Task ConversationMessagesArriveInBoundedOrderedPages()
    {
        await Seed(async db =>
        {
            db.Conversations.Add(Conversation("paged-messages", "cloudplay", 7));
            await db.SaveChangesAsync();
        });
        using var alex = await Login("alex");
        var first = await alex.GetFromJsonAsync<JsonElement>("/api/conversations/paged-messages?limit=3");
        var orders = first
            .GetProperty("messages")
            .EnumerateArray()
            .Select(m => m.GetProperty("order").GetInt32())
            .ToList();
        Assert.Equal(3, orders.Count);
        var cursor = first.GetProperty("nextCursor").GetString();
        while (cursor != null)
        {
            var page = await alex.GetFromJsonAsync<JsonElement>(
                "/api/conversations/paged-messages/messages?limit=3&cursor=" + cursor
            );
            orders.AddRange(page.GetProperty("items").EnumerateArray().Select(m => m.GetProperty("order").GetInt32()));
            cursor = page.GetProperty("nextCursor").GetString();
        }
        Assert.Equal(Enumerable.Range(0, 7), orders);
        var all = await alex.GetFromJsonAsync<JsonElement>("/api/conversations/paged-messages");
        Assert.Equal(7, all.GetProperty("messages").GetArrayLength());
        Assert.Equal(JsonValueKind.Null, all.GetProperty("nextCursor").ValueKind);
    }

    [Fact]
    public async Task JourneyListCarriesPreviewInsteadOfGraphAndBackfillsOldRows()
    {
        await Seed(db =>
            db
                .Journeys.Where(j => j.Id == "cloud-gaming")
                .ExecuteUpdateAsync(s => s.SetProperty(j => j.Preview, "").SetProperty(j => j.NodeCount, 0))
        );
        using var alex = await Login("alex");
        var list = await alex.GetFromJsonAsync<JsonElement>("/api/w/cloudplay/journeys");
        var item = Assert.Single(
            list.GetProperty("items").EnumerateArray(),
            j => j.GetProperty("id").GetString() == "cloud-gaming"
        );
        Assert.False(item.TryGetProperty("graph", out _));
        Assert.Equal(12, item.GetProperty("nodeCount").GetInt32());
        var preview = JsonDocument.Parse(item.GetProperty("preview").GetString()!).RootElement;
        Assert.Equal(12, preview.GetProperty("nodes").GetArrayLength());
        Assert.True(preview.GetProperty("edges").GetArrayLength() > 0);
        await Seed(async db =>
            Assert.NotEqual("", (await db.Journeys.SingleAsync(j => j.Id == "cloud-gaming")).Preview)
        );

        var options = await alex.GetFromJsonAsync<JsonElement>("/api/w/cloudplay/journey-options");
        Assert.Contains(options.EnumerateArray(), o => o.GetProperty("id").GetString() == "cloud-gaming");
        using var riley = await Login("riley");
        var rileyOptions = await riley.GetFromJsonAsync<JsonElement>("/api/w/cloudplay/journey-options");
        Assert.DoesNotContain(
            rileyOptions.EnumerateArray(),
            o => o.GetProperty("id").GetString() == "private-research"
        );
    }

    [Fact]
    public async Task ConversationJourneysListsOnlyVisibleJourneysThatReferenceIt()
    {
        using var alex = await Login("alex");
        var forAlex = await alex.GetFromJsonAsync<JsonElement>("/api/conversations/demo-c0/journeys");
        var ids = forAlex.EnumerateArray().Select(j => j.GetProperty("id").GetString()).ToList();
        Assert.Contains("cloud-gaming", ids);
        Assert.Contains("private-research", ids);
        using var riley = await Login("riley");
        var forRiley = await riley.GetFromJsonAsync<JsonElement>("/api/conversations/demo-c0/journeys");
        Assert.DoesNotContain(
            forRiley.EnumerateArray(),
            j => j.GetProperty("id").GetString() == "private-research"
        );
        await Seed(async db =>
        {
            db.Conversations.Add(Conversation("orphan-conversation", "cloudplay", 1));
            await db.SaveChangesAsync();
        });
        Assert.Empty(
            (await alex.GetFromJsonAsync<JsonElement>("/api/conversations/orphan-conversation/journeys"))
                .EnumerateArray()
        );
    }

    [Fact]
    public async Task ListQueriesStayConstantAsTheWorkspaceGrows()
    {
        using var alex = await Login("alex");
        async Task<(int Queries, int Items)> Measure(string path)
        {
            await alex.GetAsync(path);
            factory.Messages.Reset();
            var response = await alex.GetFromJsonAsync<JsonElement>(path);
            var queries = factory.Messages.Total;
            return (queries, response.GetProperty("items").GetArrayLength());
        }
        var paths = new[]
        {
            "/api/w/cloudplay/library?limit=5",
            "/api/w/cloudplay/journeys?limit=5",
            "/api/w/cloudplay/comparisons?limit=5",
        };
        var before = new List<(int, int)>();
        foreach (var path in paths)
            before.Add(await Measure(path));
        await Seed(async db =>
        {
            for (var i = 0; i < 60; i++)
            {
                db.Conversations.Add(Conversation("growth-" + i, "cloudplay", 12));
                db.Journeys.Add(
                    new Journey.Domain.Journey
                    {
                        WorkspaceId = "cloudplay",
                        OwnerId = "alex",
                        Title = "Growth " + i,
                        Visibility = "Team",
                    }
                );
                db.Comparisons.Add(
                    new ComparisonSession
                    {
                        WorkspaceId = "cloudplay",
                        Title = "Growth " + i,
                        LeftId = "demo-c0",
                        RightId = "demo-c1",
                    }
                );
            }
            await db.SaveChangesAsync();
        });
        for (var i = 0; i < paths.Length; i++)
        {
            var after = await Measure(paths[i]);
            Assert.Equal(before[i].Item1, after.Queries);
            Assert.Equal(5, after.Items);
        }
    }

    [Fact]
    public async Task ImportLeaseHasOneOwnerAndIsReclaimedAfterExpiry()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = new AppDb(new DbContextOptionsBuilder<AppDb>().UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync();
        var batch = new ImportBatch { WorkspaceId = "w", Status = "queued" };
        db.Batches.Add(batch);
        await db.SaveChangesAsync();
        var now = DateTime.UtcNow;

        Assert.True(await ImportLease.TryClaim(db, batch.Id, "worker-a", now, default));
        Assert.False(await ImportLease.TryClaim(db, batch.Id, "worker-b", now, default));
        await ImportLease.Checkpoint(db, batch, "worker-a", default);
        Assert.Equal("processing", batch.Status);

        var expired = now + ImportLease.Duration + TimeSpan.FromSeconds(1);
        Assert.True(await ImportLease.TryClaim(db, batch.Id, "worker-b", expired, default));
        await Assert.ThrowsAsync<ImportLeaseLostException>(() =>
            ImportLease.Checkpoint(db, batch, "worker-a", default)
        );

        await ImportLease.Release(db, batch.Id, "worker-a");
        await db.Entry(batch).ReloadAsync();
        Assert.Equal("worker-b", batch.LeaseOwner);
        await ImportLease.Release(db, batch.Id, "worker-b");
        await db.Entry(batch).ReloadAsync();
        Assert.Null(batch.LeaseOwner);
        Assert.True(await ImportLease.TryClaim(db, batch.Id, "worker-a", now, default));
    }
}
