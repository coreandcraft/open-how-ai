using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Journey.Data;
using Journey.Domain;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace Journey.Tests;

public class McpTests
{
    static WebApplicationFactory<Program> Enabled(Factory root) =>
        root.WithWebHostBuilder(b =>
            b.UseSetting("Mcp:Enabled", "true")
                .ConfigureAppConfiguration(
                    (_, c) =>
                        c.AddInMemoryCollection(
                            new Dictionary<string, string?> { ["Mcp:Enabled"] = "true" }
                        )
                )
        );

    static async Task<HttpClient> Login(WebApplicationFactory<Program> app, string who = "alex")
    {
        var c = app.CreateClient();
        c.DefaultRequestHeaders.Add("X-Journey", "1");
        (
            await c.PostAsJsonAsync(
                "/api/auth/login",
                new { email = who + "@journey.local", password = "JourneyDemo!2026" }
            )
        ).EnsureSuccessStatusCode();
        return c;
    }

    static async Task<string> Journey(HttpClient c)
    {
        var result = await c.PostAsJsonAsync(
            "/api/w/cloudplay/journeys",
            new { name = "MCP test journey" }
        );
        result.EnsureSuccessStatusCode();
        return (await result.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("id")
            .GetString()!;
    }

    static async Task<(string Id, string Token)> Grant(HttpClient c, string jid, bool write = true)
    {
        var r = await c.PostAsJsonAsync(
            "/api/integrations/mcp/w/cloudplay/connections",
            new
            {
                name = "Test client",
                journeyIds = new[] { jid },
                allowProposals = write,
            }
        );
        r.EnsureSuccessStatusCode();
        var json = await r.Content.ReadFromJsonAsync<JsonElement>();
        return (json.GetProperty("id").GetString()!, json.GetProperty("token").GetString()!);
    }

    static async Task<McpClient> Connect(WebApplicationFactory<Program> app, string token)
    {
        var http = app.CreateClient(new() { HandleCookies = false });
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await McpClient.CreateAsync(
            new HttpClientTransport(
                new()
                {
                    Endpoint = new Uri(http.BaseAddress!, "/mcp"),
                    TransportMode = HttpTransportMode.StreamableHttp,
                    EnableStandaloneGetStream = false,
                },
                http
            )
        );
    }

    static async Task<JsonElement> Call(McpClient c, string name, Dictionary<string, object?> args)
    {
        var r = await c.CallToolAsync(name, args);
        Assert.True(
            r.IsError != true,
            string.Join("\n", r.Content.OfType<TextContentBlock>().Select(t => t.Text))
        );
        return JsonSerializer.Deserialize<JsonElement>(
            Assert.Single(r.Content.OfType<TextContentBlock>()).Text
        );
    }

    [Fact]
    public async Task OfficialClientDiscoversTenToolsAndProposalsRequireHumanApproval()
    {
        using var root = new Factory();
        using var app = Enabled(root);
        using var browser = await Login(app);
        var jid = await Journey(browser);
        var grant = await Grant(browser, jid);
        // Seed two nodes; all subsequent operations go through real HTTP and SDK transport.
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDb>();
            var j = await db.Journeys.FindAsync(jid);
            j!.Graph =
                """{"nodes":[{"id":"a","data":{"kind":"decision","title":"Decision A","body":"Reason"}},{"id":"b","data":{"kind":"note","title":"Note B"}}],"edges":[],"steps":["a","b"]}""";
            await db.SaveChangesAsync();
        }
        await using var client = await Connect(app, grant.Token);
        var tools = await client.ListToolsAsync();
        Assert.Equal(10, tools.Count);
        Assert.Equal(
            new[]
            {
                "get_journey",
                "get_journey_evidence",
                "get_journey_timeline",
                "get_related_conversations",
                "propose_annotation",
                "propose_journey_entry",
                "propose_milestone",
                "search_decisions",
                "search_journeys",
                "suggest_relationship",
            },
            tools.Select(t => t.Name).Order().ToArray()
        );
        var before = await Call(client, "get_journey", new() { ["journeyId"] = jid });
        Assert.Equal(2, before.GetProperty("nodes").GetArrayLength());
        Assert.Single(
            (await Call(client, "search_journeys", new() { ["query"] = "MCP" }))
                .GetProperty("items")
                .EnumerateArray()
        );
        Assert.Equal(
            2,
            (await Call(client, "get_journey_timeline", new() { ["journeyId"] = jid }))
                .GetProperty("items")
                .GetArrayLength()
        );
        await Call(client, "get_journey_evidence", new() { ["nodeId"] = jid + ":a" });
        Assert.Single(
            (await Call(client, "search_decisions", new() { ["query"] = "Decision" }))
                .GetProperty("items")
                .EnumerateArray()
        );
        var entry = await Call(
            client,
            "propose_journey_entry",
            new()
            {
                ["data"] = new
                {
                    journeyId = jid,
                    title = "Proposed entry",
                    body = "<script>unsafe</script>",
                    kind = "note",
                },
            }
        );
        var milestone = await Call(
            client,
            "propose_milestone",
            new() { ["journeyId"] = jid, ["data"] = new { title = "Proposed milestone" } }
        );
        var annotation = await Call(
            client,
            "propose_annotation",
            new() { ["nodeId"] = jid + ":a", ["text"] = "Proposed annotation" }
        );
        var relationship = await Call(
            client,
            "suggest_relationship",
            new() { ["sourceId"] = jid + ":a", ["targetId"] = jid + ":b" }
        );
        Assert.True(entry.GetProperty("requiresUserApproval").GetBoolean());
        var after = await Call(client, "get_journey", new() { ["journeyId"] = jid });
        Assert.Equal(before.GetRawText(), after.GetRawText());
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDb>();
            Assert.Equal(
                4,
                await db.Set<JourneyProposal>()
                    .CountAsync(p => p.JourneyId == jid && p.Status == "pending")
            );
            Assert.False(await db.Annotations.AnyAsync(a => a.JourneyId == jid));
        }
        var id = entry.GetProperty("proposalId").GetString();
        int version = entry.GetProperty("baseVersion").GetInt32();
        using var bearer = app.CreateClient(new() { HandleCookies = false });
        bearer.DefaultRequestHeaders.Authorization = new("Bearer", grant.Token);
        bearer.DefaultRequestHeaders.Add("X-Journey", "1");
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (
                await bearer.PostAsJsonAsync(
                    $"/api/integrations/mcp/proposals/{id}/review",
                    new
                    {
                        approve = true,
                        expectedVersion = version,
                        confirm = true,
                    }
                )
            ).StatusCode
        );
        Assert.Equal(
            HttpStatusCode.BadRequest,
            (
                await browser.PostAsJsonAsync(
                    $"/api/integrations/mcp/proposals/{id}/review",
                    new
                    {
                        approve = true,
                        expectedVersion = version,
                        confirm = false,
                    }
                )
            ).StatusCode
        );
        Assert.Equal(
            HttpStatusCode.NoContent,
            (
                await browser.PostAsJsonAsync(
                    $"/api/integrations/mcp/proposals/{id}/review",
                    new
                    {
                        approve = true,
                        expectedVersion = version,
                        confirm = true,
                    }
                )
            ).StatusCode
        );
        Assert.Equal(
            HttpStatusCode.Conflict,
            (
                await browser.PostAsJsonAsync(
                    $"/api/integrations/mcp/proposals/{id}/review",
                    new
                    {
                        approve = true,
                        expectedVersion = version,
                        confirm = true,
                    }
                )
            ).StatusCode
        );
        Assert.Equal(
            HttpStatusCode.Conflict,
            (
                await browser.PostAsJsonAsync(
                    $"/api/integrations/mcp/proposals/{milestone.GetProperty("proposalId").GetString()}/review",
                    new
                    {
                        approve = true,
                        expectedVersion = version + 1,
                        confirm = true,
                    }
                )
            ).StatusCode
        );
        Assert.Equal(
            HttpStatusCode.NoContent,
            (
                await browser.PostAsJsonAsync(
                    $"/api/integrations/mcp/proposals/{annotation.GetProperty("proposalId").GetString()}/review",
                    new
                    {
                        approve = false,
                        expectedVersion = version,
                        confirm = true,
                    }
                )
            ).StatusCode
        );
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDb>();
            var j = await db.Journeys.FindAsync(jid);
            Assert.Equal(version + 1, j!.Version);
            Assert.Contains(
                "&lt;script&gt;",
                JsonDocument
                    .Parse(j.Graph)
                    .RootElement.GetProperty("nodes")[2]
                    .GetProperty("data")
                    .GetProperty("body")
                    .GetString()
            );
            Assert.Equal("Private", j.Visibility);
        }
    }

    [Fact]
    public async Task JourneyScopeReadOnlyRevocationAndCookieIsolationAreEnforced()
    {
        using var root = new Factory();
        using var app = Enabled(root);
        using var browser = await Login(app);
        var jid = await Journey(browser);
        var other = await Journey(browser);
        var grant = await Grant(browser, jid, false);
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (
                await browser.PostAsJsonAsync(
                    "/mcp",
                    new
                    {
                        jsonrpc = "2.0",
                        id = 1,
                        method = "tools/list",
                    }
                )
            ).StatusCode
        );
        await using var client = await Connect(app, grant.Token);
        Assert.True(
            (
                await client.CallToolAsync(
                    "get_journey",
                    new Dictionary<string, object?> { ["journeyId"] = other }
                )
            ).IsError
        );
        Assert.True(
            (
                await client.CallToolAsync(
                    "get_related_conversations",
                    new Dictionary<string, object?> { ["conversationId"] = "not-granted" }
                )
            ).IsError
        );
        Assert.True(
            (
                await client.CallToolAsync(
                    "propose_milestone",
                    new Dictionary<string, object?>
                    {
                        ["journeyId"] = jid,
                        ["data"] = new { title = "Denied" },
                    }
                )
            ).IsError
        );
        var settings = await browser.GetStringAsync("/api/integrations/mcp/w/cloudplay");
        Assert.DoesNotContain(grant.Token, settings);
        Assert.DoesNotContain("tokenHash", settings);
        Assert.DoesNotContain("securityStamp", settings);
        (
            await browser.DeleteAsync("/api/integrations/mcp/connections/" + grant.Id)
        ).EnsureSuccessStatusCode();
        using var http = app.CreateClient(new() { HandleCookies = false });
        http.DefaultRequestHeaders.Authorization = new("Bearer", grant.Token);
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (
                await http.PostAsJsonAsync(
                    "/mcp",
                    new
                    {
                        jsonrpc = "2.0",
                        id = 2,
                        method = "tools/list",
                    }
                )
            ).StatusCode
        );
    }

    [Fact]
    public async Task DisabledEndpointAndUntrustedOriginsAreRejected()
    {
        using var root = new Factory();
        using var plain = root.CreateClient();
        Assert.Equal(HttpStatusCode.NotFound, (await plain.GetAsync("/mcp")).StatusCode);
        using var app = Enabled(root);
        using var http = app.CreateClient();
        http.DefaultRequestHeaders.Add("Origin", "https://evil.example");
        Assert.Equal(HttpStatusCode.Forbidden, (await http.GetAsync("/mcp")).StatusCode);
    }

    [Theory]
    [InlineData("expired")]
    [InlineData("stamp")]
    [InlineData("membership")]
    public async Task ChangedAccessInvalidatesExistingTokens(string change)
    {
        using var root = new Factory();
        using var app = Enabled(root);
        using var browser = await Login(app);
        var jid = await Journey(browser);
        var grant = await Grant(browser, jid);
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDb>();
            var g = await db.Set<McpConnection>().FindAsync(grant.Id);
            if (change == "expired")
                g!.ExpiresAt = DateTime.UtcNow.AddSeconds(-1);
            else if (change == "stamp")
                (await db.Users.FindAsync(g!.UserId))!.SecurityStamp = Guid.NewGuid().ToString();
            else
                db.Remove(
                    await db.Memberships.SingleAsync(m =>
                        m.UserId == g!.UserId && m.WorkspaceId == g.WorkspaceId
                    )
                );
            await db.SaveChangesAsync();
        }
        using var http = app.CreateClient(new() { HandleCookies = false });
        http.DefaultRequestHeaders.Authorization = new("Bearer", grant.Token);
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (
                await http.PostAsJsonAsync(
                    "/mcp",
                    new
                    {
                        jsonrpc = "2.0",
                        id = 1,
                        method = "tools/list",
                    }
                )
            ).StatusCode
        );
    }

    [Theory]
    [InlineData("annotation")]
    [InlineData("relationship")]
    [InlineData("milestone")]
    public async Task OtherProposalKindsApplyOnlyAfterApproval(string kind)
    {
        using var root = new Factory();
        using var app = Enabled(root);
        using var browser = await Login(app);
        var jid = await Journey(browser);
        var grant = await Grant(browser, jid);
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDb>();
            var j = await db.Journeys.FindAsync(jid);
            j!.Graph =
                """{"nodes":[{"id":"a","data":{"kind":"note","title":"A"}},{"id":"b","data":{"kind":"note","title":"B"}}],"edges":[],"steps":["a"]}""";
            await db.SaveChangesAsync();
        }
        await using var client = await Connect(app, grant.Token);
        var proposal = kind switch
        {
            "annotation" => await Call(
                client,
                "propose_annotation",
                new() { ["nodeId"] = jid + ":a", ["text"] = "Approved annotation" }
            ),
            "relationship" => await Call(
                client,
                "suggest_relationship",
                new() { ["sourceId"] = jid + ":a", ["targetId"] = jid + ":b" }
            ),
            _ => await Call(
                client,
                "propose_milestone",
                new() { ["journeyId"] = jid, ["data"] = new { title = "Approved milestone" } }
            ),
        };
        var id = proposal.GetProperty("proposalId").GetString();
        (
            await browser.PostAsJsonAsync(
                $"/api/integrations/mcp/proposals/{id}/review",
                new
                {
                    approve = true,
                    expectedVersion = proposal.GetProperty("baseVersion").GetInt32(),
                    confirm = true,
                }
            )
        ).EnsureSuccessStatusCode();
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDb>();
            var j = await db.Journeys.FindAsync(jid);
            var graph = JsonDocument.Parse(j!.Graph).RootElement;
            Assert.Equal(1, j.Version);
            Assert.Equal("Private", j.Visibility);
            if (kind == "annotation")
                Assert.Equal(
                    "Approved annotation",
                    (
                        await db
                            .Annotations.Include(a => a.Comments)
                            .SingleAsync(a => a.JourneyId == jid)
                    )
                        .Comments.Single()
                        .Body
                );
            else if (kind == "relationship")
                Assert.Equal(1, graph.GetProperty("edges").GetArrayLength());
            else
                Assert.Equal(
                    "milestone",
                    graph
                        .GetProperty("nodes")[2]
                        .GetProperty("data")
                        .GetProperty("kind")
                        .GetString()
                );
            Assert.Equal("approved", (await db.Set<JourneyProposal>().FindAsync(id))!.Status);
        }
    }

    [Fact]
    public async Task EvidenceCannotCrossWorkspaceAndChangedRoleOrVisibilityIsApplied()
    {
        using var root = new Factory();
        using var app = Enabled(root);
        using var browser = await Login(app);
        var jid = await Journey(browser);
        var grant = await Grant(browser, jid);
        string first,
            second,
            foreign;
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDb>();
            var conversations = await db
                .Conversations.Where(c => c.WorkspaceId == "cloudplay")
                .Take(2)
                .ToListAsync();
            first = conversations[0].Id;
            second = conversations[1].Id;
            var c = new Conversation
            {
                WorkspaceId = "personal",
                SourceKey = "mcp-test-foreign",
                Title = "FOREIGN_SECRET",
                RawPayload = "RAW_SECRET",
            };
            db.Add(c);
            foreign = c.Id;
            var j = await db.Journeys.FindAsync(jid);
            j!.Graph = JsonSerializer.Serialize(
                new
                {
                    nodes = new[]
                    {
                        new
                        {
                            id = "a",
                            data = new
                            {
                                kind = "conversation",
                                title = "A",
                                conversationId = first,
                            },
                        },
                        new
                        {
                            id = "b",
                            data = new
                            {
                                kind = "conversation",
                                title = "B",
                                conversationId = second,
                            },
                        },
                        new
                        {
                            id = "foreign",
                            data = new
                            {
                                kind = "conversation",
                                title = "FOREIGN_SECRET",
                                conversationId = foreign,
                            },
                        },
                    },
                    edges = new[]
                    {
                        new { source = "a", target = "b" },
                        new { source = "a", target = "foreign" },
                    },
                    steps = new[] { "a", "b", "foreign" },
                }
            );
            await db.SaveChangesAsync();
        }
        await using var client = await Connect(app, grant.Token);
        var journey = await Call(client, "get_journey", new() { ["journeyId"] = jid });
        Assert.DoesNotContain("FOREIGN_SECRET", journey.GetRawText());
        Assert.Equal(2, journey.GetProperty("nodes").GetArrayLength());
        var evidence = await Call(
            client,
            "get_journey_evidence",
            new() { ["nodeId"] = jid + ":a" }
        );
        Assert.DoesNotContain("rawPayload", evidence.GetRawText());
        Assert.DoesNotContain("metadata", evidence.GetRawText());
        Assert.NotEmpty(evidence.GetProperty("source").GetProperty("messages").EnumerateArray());
        var related = await Call(
            client,
            "get_related_conversations",
            new() { ["conversationId"] = first }
        );
        Assert.Equal(
            second,
            Assert
                .Single(related.GetProperty("items").EnumerateArray())
                .GetProperty("id")
                .GetString()
        );
        Assert.True(
            (
                await client.CallToolAsync(
                    "get_journey_evidence",
                    new Dictionary<string, object?> { ["nodeId"] = jid + ":foreign" }
                )
            ).IsError
        );
        Assert.True(
            (
                await client.CallToolAsync(
                    "propose_journey_entry",
                    new Dictionary<string, object?>
                    {
                        ["data"] = new
                        {
                            journeyId = jid,
                            title = "Not allowed",
                            kind = "conversation",
                            conversationId = foreign,
                        },
                    }
                )
            ).IsError
        );
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDb>();
            (
                await db.Memberships.SingleAsync(m =>
                    m.UserId == "alex" && m.WorkspaceId == "cloudplay"
                )
            ).Role = "Viewer";
            await db.SaveChangesAsync();
        }
        await Call(client, "get_journey", new() { ["journeyId"] = jid });
        Assert.True(
            (
                await client.CallToolAsync(
                    "propose_milestone",
                    new Dictionary<string, object?>
                    {
                        ["journeyId"] = jid,
                        ["data"] = new { title = "Denied" },
                    }
                )
            ).IsError
        );
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDb>();
            (await db.Journeys.FindAsync(jid))!.OwnerId = "sam";
            await db.SaveChangesAsync();
        }
        Assert.True(
            (
                await client.CallToolAsync(
                    "get_journey",
                    new Dictionary<string, object?> { ["journeyId"] = jid }
                )
            ).IsError
        );
    }
}
