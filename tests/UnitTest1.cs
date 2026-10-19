using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Journey.Domain;
using Journey.Data;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Journey.Server.Features.Accounts;

namespace Journey.Tests;

public class ParsingTests
{
    static SourceInput Source(string name) =>
        new(name, File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "fixtures", name)));

    [Fact]
    public async Task ChatGptPreservesBranchesAndFidelity()
    {
        var p = await new ChatGptImporter().PreviewAsync(Source("chatgpt.json"), default);
        var c = Assert.Single(p.Conversations);
        Assert.Equal(3, c.Messages.Count);
        Assert.Equal(c.Messages[0].Id, c.Messages[1].ParentId);
        Assert.Equal(c.Messages[0].Id, c.Messages[2].ParentId);
        Assert.Equal("Keep  two spaces.\nNext line.", c.Messages[0].Blocks[0].Text);
        Assert.Contains("unknown_flag", c.Messages[0].Metadata);
        Assert.Equal("fixture-model", c.Model);
        Assert.Null(c.Messages[0].ParentId);
    }

    [Fact]
    public async Task ChatGptKeepsThoughtsAndReasoningRecaps()
    {
        var json = """
            [{"id":"t","title":"Thoughts","mapping":{"root":{"parent":null,"message":null},"u":{"parent":"root","message":{"id":"u","author":{"role":"user"},"content":{"content_type":"text","parts":["Hi"]}}},"a":{"parent":"u","message":{"id":"a","author":{"role":"assistant"},"content":{"content_type":"thoughts","thoughts":[{"summary":"Plan","finished":true}]}}},"b":{"parent":"a","message":{"id":"b","author":{"role":"assistant"},"content":{"content_type":"reasoning_recaps","content":"worked for em 22's"}}}}}]
            """;
        var p = await new ChatGptImporter().PreviewAsync(
            new SourceInput("thoughts.json", Encoding.UTF8.GetBytes(json)),
            default
        );
        var c = Assert.Single(p.Conversations);
        Assert.Equal("thoughts", c.Messages[1].Blocks[0].Kind);
        Assert.Contains("Plan", c.Messages[1].Blocks[0].Metadata);
        Assert.Equal("reasoning_recaps", c.Messages[2].Blocks[0].Kind);
        Assert.Equal("worked for em 22's", c.Messages[2].Blocks[0].Text);
        Assert.Contains("reasoning_recaps", c.Messages[2].Blocks[0].Metadata);
    }

    [Fact]
    public async Task ClaudeRetainsToolBlocksAndUnknownFields()
    {
        var c = Assert.Single(
            (await new ClaudeImporter().PreviewAsync(Source("claude.json"), default)).Conversations
        );
        Assert.Equal("user", c.Messages[0].Role);
        Assert.Contains("attachments", c.Messages[0].Metadata);
        Assert.Equal("tool_use", c.Messages[1].Blocks[1].Kind);
        Assert.Contains("retain me", c.Messages[1].Metadata);
        Assert.Null(c.Model);
    }

    [Theory]
    [InlineData("generic.json", 2)]
    [InlineData("conversation.jsonl", 2)]
    [InlineData("conversation.md", 1)]
    public async Task GenericVariants(string file, int count)
    {
        var c = Assert.Single(
            (await new GenericImporter().ImportAsync(Source(file), default)).Conversations
        );
        Assert.Equal(count, c.Messages.Count);
        Assert.NotEmpty(c.RawPayload);
    }

    [Theory]
    [InlineData("unsupported-ocf.json")]
    [InlineData("unsupported-codex.jsonl")]
    public async Task UnsupportedVariantsFailHonestly(string file)
    {
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            new GenericImporter().ImportAsync(Source(file), default)
        );
    }

    [Fact]
    public async Task DuplicateFingerprintIsStable()
    {
        var importer = new ChatGptImporter();
        var a = await importer.ImportAsync(Source("chatgpt.json"), default);
        var b = await importer.ImportAsync(Source("chatgpt.json"), default);
        Assert.Equal(a.Conversations[0].SourceKey, b.Conversations[0].SourceKey);
        Assert.NotEqual(a.Conversations[0].Id, b.Conversations[0].Id);
    }

    [Fact]
    public void AnchorsRecoverAndOrphanAmbiguousQuotes()
    {
        var a = Anchoring.Create("b", "before unique after", 7, 13);
        Assert.Equal("unique", a.Quote);
        Assert.Equal(11, Anchoring.Recover(a, "new before unique after")!.Start);
        Assert.Null(Anchoring.Recover(a, "removed"));
        var ambiguous = a with { Prefix = "", Suffix = "" };
        Assert.Null(Anchoring.Recover(ambiguous, "unique and unique"));
    }

    [Theory]
    [InlineData("Viewer", "edit", false)]
    [InlineData("Viewer", "comment", false)]
    [InlineData("Commenter", "comment", true)]
    [InlineData("Editor", "publish", false)]
    [InlineData("Admin", "manage", true)]
    public void RoleMatrix(string role, string action, bool expected) =>
        Assert.Equal(expected, Access.Allows(role, action));

    [Fact]
    public void PublicationHasAnAllowlistAndServerRedaction()
    {
        var c = new Conversation
        {
            Id = "c",
            RawPayload = "RAW_SECRET",
            Metadata = "HIDDEN_META",
            Messages =
            [
                new()
                {
                    Id = "m",
                    Role = "assistant",
                    Blocks =
                    [
                        new() { Kind = "text", Text = "visible sk-testsecret123456" },
                        new() { Kind = "image", Text = "PRIVATE_ATTACHMENT" },
                    ],
                },
                new()
                {
                    Id = "hidden",
                    Role = "user",
                    Blocks = [new() { Text = "HIDDEN_MESSAGE" }],
                },
            ],
        };
        var j = new Journey.Domain.Journey
        {
            Title = "Test",
            Graph = Json.Write(
                new
                {
                    nodes = new[]
                    {
                        new
                        {
                            id = "n",
                            data = new
                            {
                                conversationId = "c",
                                title = "Test",
                                body = "PRIVATE_CURATOR",
                            },
                        },
                    },
                    steps = new[] { "n" },
                }
            ),
        };
        var result = Json.Write(
            Publishing.Snapshot(j, [c], new(["m"], ["sk-testsecret123456"], "Unlisted", true))
        );
        Assert.Contains("[REDACTED]", result);
        foreach (
            var s in new[]
            {
                "RAW_SECRET",
                "HIDDEN_META",
                "PRIVATE_ATTACHMENT",
                "HIDDEN_MESSAGE",
                "PRIVATE_CURATOR",
                "sk-testsecret123456",
            }
        )
            Assert.DoesNotContain(s, result);
    }
}

public class Factory : WebApplicationFactory<Program>
{
    readonly string path = Path.Combine(
        Path.GetTempPath(),
        "journey-tests-" + Guid.NewGuid() + ".db"
    );

    protected override void ConfigureWebHost(IWebHostBuilder builder) =>
        builder
            .UseEnvironment("Testing")
            .UseSetting("Mcp:Enabled", "false")
            .ConfigureServices(services =>
            {
                services.AddSingleton<TestMail>();
                services.AddScoped<AccountEmail, TestAccountEmail>();
            })
            .ConfigureAppConfiguration(
                (_, config) =>
                    config.AddInMemoryCollection(
                        new Dictionary<string, string?>
                        {
                            { "ConnectionStrings:Journey", "Data Source=" + path },
                            { "Imports:StoragePath", path + ".sources" },
                            // Local integration settings and credentials must not affect tests.
                            { "Mcp:Enabled", "false" },
                            { "GitHub:Enabled", "false" },
                            { "GitHub:AuthenticationMode", "Public" },
                        }
                    )
            );
}

public class ApiTests : IClassFixture<Factory>
{
    readonly Factory factory;

    public ApiTests(Factory f) => factory = f;

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
    public async Task AnonymousCanOnlyReadSnapshots()
    {
        using var c = factory.CreateClient();
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (await c.GetAsync("/api/w/cloudplay/library")).StatusCode
        );
        var s = await c.GetStringAsync("/api/public/cloudplay-demo");
        Assert.DoesNotContain("rawPayload", s);
        Assert.DoesNotContain("metadata", s);
        Assert.DoesNotContain("comments", s);
        Assert.Contains("steps", s);
    }

    [Fact]
    public async Task ViewerCannotWriteOrReadOtherWorkspace()
    {
        using var c = await Login("riley");
        Assert.Equal(
            HttpStatusCode.Forbidden,
            (
                await c.PostAsJsonAsync("/api/w/cloudplay/journeys", new { name = "Denied" })
            ).StatusCode
        );
        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await c.GetAsync("/api/w/personal/library")).StatusCode
        );
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await c.GetAsync("/api/journeys/private-research")).StatusCode
        );
        Assert.Equal(
            HttpStatusCode.Forbidden,
            (
                await c.PostAsJsonAsync(
                    "/api/journeys/cloud-gaming/publish",
                    new
                    {
                        messageIds = Array.Empty<string>(),
                        redactions = Array.Empty<string>(),
                        visibility = "Public",
                        confirm = true,
                    }
                )
            ).StatusCode
        );
    }

    [Fact]
    public async Task ImportCommitPreservesRawBytesAndSkipsDuplicates()
    {
        using var c = await Login("alex");
        async Task<JsonElement> Preview()
        {
            using var form = new MultipartFormDataContent();
            form.Add(
                new ByteArrayContent(
                    File.ReadAllBytes(
                        Path.Combine(AppContext.BaseDirectory, "fixtures", "chatgpt.json")
                    )
                ),
                "files",
                "chatgpt.json"
            );
            var r = await c.PostAsync("/api/w/personal/imports/preview", form);
            r.EnsureSuccessStatusCode();
            return (await r.Content.ReadFromJsonAsync<JsonElement>());
        }
        var p = await Preview();
        var batch = p.GetProperty("batchId").GetString();
        var key = p.GetProperty("conversations")[0].GetProperty("key").GetString();
        (
            await c.PostAsJsonAsync(
                "/api/imports/" + batch + "/commit",
                new { keys = new[] { key } }
            )
        ).EnsureSuccessStatusCode();
        JsonElement status = default;
        for (var i = 0; i < 40; i++)
        {
            status = await c.GetFromJsonAsync<JsonElement>("/api/imports/" + batch);
            if (status.GetProperty("status").GetString() == "completed")
                break;
            await Task.Delay(150);
        }
        Assert.Equal(1, status.GetProperty("imported").GetInt32());
        var second = await Preview();
        Assert.True(second.GetProperty("conversations")[0].GetProperty("duplicate").GetBoolean());
        var lib = await c.GetFromJsonAsync<JsonElement>("/api/w/personal/library");
        var source = lib.GetProperty("items")[0].GetProperty("sourceId").GetString();
        Assert.Equal(
            File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "fixtures", "chatgpt.json")),
            await c.GetByteArrayAsync("/api/sources/" + source)
        );
    }

    [Fact]
    public async Task CanvasRejectsStaleVersionAndPublicationNeedsConfirmation()
    {
        using var c = await Login("alex");
        var created = await c.PostAsJsonAsync(
            "/api/w/personal/journeys",
            new { name = "Version test" }
        );
        var j = await created.Content.ReadFromJsonAsync<JsonElement>();
        var id = j.GetProperty("id").GetString();
        var body = new
        {
            title = "Updated",
            description = "",
            visibility = "Private",
            graph = "{\"nodes\":[],\"edges\":[],\"steps\":[]}",
            version = 0,
        };
        Assert.Equal(
            HttpStatusCode.OK,
            (await c.PutAsJsonAsync("/api/journeys/" + id, body)).StatusCode
        );
        Assert.Equal(
            HttpStatusCode.Conflict,
            (await c.PutAsJsonAsync("/api/journeys/" + id, body)).StatusCode
        );
        Assert.Equal(
            HttpStatusCode.BadRequest,
            (
                await c.PostAsJsonAsync(
                    "/api/journeys/" + id + "/publish",
                    new
                    {
                        messageIds = Array.Empty<string>(),
                        redactions = Array.Empty<string>(),
                        visibility = "Unlisted",
                        confirm = false,
                    }
                )
            ).StatusCode
        );
    }
}

