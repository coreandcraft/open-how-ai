using System.Data.Common;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Journey.Data;
using Journey.Domain;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace Journey.Tests;

public sealed class MessageQueryCounter : DbCommandInterceptor
{
    int count;
    int total;
    public int Count => Volatile.Read(ref count);

    /// <summary>Every reader command, so bounded endpoints can assert a constant query count.</summary>
    public int Total => Volatile.Read(ref total);

    public void Reset()
    {
        Interlocked.Exchange(ref count, 0);
        Interlocked.Exchange(ref total, 0);
    }

    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<DbDataReader> result,
        CancellationToken ct = default
    )
    {
        Interlocked.Increment(ref total);
        if (command.CommandText.Contains("\"Messages\"") || command.CommandText.Contains("\"MessageContentBlock\""))
            Interlocked.Increment(ref count);
        return ValueTask.FromResult(result);
    }
}

public class CountingFactory : Factory
{
    public MessageQueryCounter Messages { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureTestServices(services =>
        {
            foreach (
                var worker in services
                    .Where(d =>
                        d.ServiceType == typeof(Microsoft.Extensions.Hosting.IHostedService)
                        && d.ImplementationType?.Name is "ImportWorker" or "ImportRetentionWorker"
                    )
                    .ToList()
            )
                services.Remove(worker);
            services.ConfigureDbContext<AppDb>(o => o.AddInterceptors(Messages));
        });
    }
}

public class ScaleBoundaryTests : IClassFixture<CountingFactory>
{
    readonly CountingFactory factory;

    public ScaleBoundaryTests(CountingFactory f) => factory = f;

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

    [Fact]
    public async Task OutsiderConversationReadLooksMissingAndNeverLoadsMessages()
    {
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDb>();
            if (!await db.Conversations.AnyAsync(c => c.Id == "private-scale"))
            {
                db.Conversations.Add(
                    new Conversation
                    {
                        Id = "private-scale",
                        WorkspaceId = "personal",
                        SourceKey = "private-scale",
                        Title = "Private",
                        Messages =
                        [
                            new ConversationMessage
                            {
                                Id = "private-scale-m0",
                                ConversationId = "private-scale",
                                Blocks = [new MessageContentBlock { Text = "secret" }],
                            },
                        ],
                    }
                );
                await db.SaveChangesAsync();
            }
        }
        using var sam = await Login("sam");
        factory.Messages.Reset();
        var response = await sam.GetAsync("/api/conversations/private-scale");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(0, factory.Messages.Count);
        var missing = await sam.GetAsync("/api/conversations/does-not-exist");
        Assert.Equal(missing.StatusCode, response.StatusCode);
        Assert.Equal(
            await missing.Content.ReadAsStringAsync(),
            await response.Content.ReadAsStringAsync()
        );
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await sam.GetAsync("/api/conversations/private-scale/messages")).StatusCode
        );
    }

    [Fact]
    public async Task ConversationResponseOmitsRawPayloadAndOrdersMessages()
    {
        using var alex = await Login("alex");
        factory.Messages.Reset();
        var json = await alex.GetFromJsonAsync<JsonElement>("/api/conversations/demo-c0");
        Assert.True(factory.Messages.Count > 0);
        Assert.False(json.TryGetProperty("rawPayload", out _));
        Assert.True(json.TryGetProperty("provenance", out _));
        var orders = json.GetProperty("messages")
            .EnumerateArray()
            .Select(m => m.GetProperty("order").GetInt32())
            .ToList();
        Assert.NotEmpty(orders);
        Assert.Equal(orders.Order(), orders);
    }

    [Fact]
    public async Task ProjectAssignmentPersistsWithoutLoadingContent()
    {
        using var alex = await Login("alex");
        var project = await (
            await alex.PostAsJsonAsync("/api/w/cloudplay/projects", new { name = "Scale project" })
        ).Content.ReadFromJsonAsync<JsonElement>();
        var projectId = project.GetProperty("id").GetString();
        factory.Messages.Reset();
        (
            await alex.PutAsJsonAsync("/api/conversations/demo-c1/project", new { projectId })
        ).EnsureSuccessStatusCode();
        Assert.Equal(0, factory.Messages.Count);
        var c = await alex.GetFromJsonAsync<JsonElement>("/api/conversations/demo-c1");
        Assert.Equal(projectId, c.GetProperty("applicationProjectId").GetString());
    }

    [Fact]
    public async Task ConcurrentJourneySavesProduceExactlyOneConflict()
    {
        using var first = await Login("alex");
        using var second = await Login("alex");
        var j = await first.GetFromJsonAsync<JsonElement>("/api/journeys/cloud-gaming");
        object Update(string title) =>
            new
            {
                title,
                description = j.GetProperty("description").GetString(),
                visibility = j.GetProperty("visibility").GetString(),
                graph = j.GetProperty("graph").GetString(),
                version = j.GetProperty("version").GetInt32(),
            };
        var responses = await Task.WhenAll(
            first.PutAsJsonAsync("/api/journeys/cloud-gaming", Update("First editor")),
            second.PutAsJsonAsync("/api/journeys/cloud-gaming", Update("Second editor"))
        );
        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.OK);
        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task PublicSnapshotRevalidatesAndStopsAfterDeletion()
    {
        using var alex = await Login("alex");
        var published = await alex.PostAsJsonAsync(
            "/api/conversations/demo-c0/publish",
            new
            {
                messageIds = new[] { "demo-m0-0" },
                redactions = Array.Empty<string>(),
                visibility = "Unlisted",
                confirm = true,
            }
        );
        published.EnsureSuccessStatusCode();
        var slug = (await published.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("slug")
            .GetString();
        using var anonymous = factory.CreateClient();
        var first = await anonymous.GetAsync("/api/public/" + slug);
        first.EnsureSuccessStatusCode();
        Assert.Contains("no-cache", first.Headers.CacheControl!.ToString());
        Assert.True(first.Headers.CacheControl.Public);
        var etag = first.Headers.ETag!;

        using var conditional = new HttpRequestMessage(HttpMethod.Get, "/api/public/" + slug);
        conditional.Headers.IfNoneMatch.Add(etag);
        var revalidated = await anonymous.SendAsync(conditional);
        Assert.Equal(HttpStatusCode.NotModified, revalidated.StatusCode);

        var history = await alex.GetFromJsonAsync<JsonElement>(
            "/api/conversations/demo-c0/publications"
        );
        var id = history
            .EnumerateArray()
            .Single(p => p.GetProperty("slug").GetString() == slug)
            .GetProperty("id")
            .GetString();
        (
            await alex.DeleteAsync("/api/conversations/demo-c0/publications/" + id)
        ).EnsureSuccessStatusCode();
        using var after = new HttpRequestMessage(HttpMethod.Get, "/api/public/" + slug);
        after.Headers.IfNoneMatch.Add(etag);
        Assert.Equal(HttpStatusCode.NotFound, (await anonymous.SendAsync(after)).StatusCode);
    }
}
