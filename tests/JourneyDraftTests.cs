using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Journey.Data;
using Journey.Domain;
using Journey.Server.Features.Journeys;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Thread = Journey.Server.Features.Journeys.JourneyDraftBuilder.Thread;
using Execution = Journey.Server.Features.Journeys.JourneyDraftBuilder.Execution;

namespace Journey.Tests;

public class JourneyDraftTests : IClassFixture<Factory>
{
    readonly Factory factory;
    public JourneyDraftTests(Factory factory) => this.factory = factory;
    static readonly DateTime T = new(2026, 10, 1, 12, 0, 0, DateTimeKind.Utc);
    static Thread Conversation(string id, DateTime? start, string? parent = null, string provider = "Codex", string identity = "default") =>
        new(id, id, provider, "model", id, identity, parent, start, start?.AddMinutes(10), 2);
    static JsonElement Graph(Thread[] threads, Execution[]? calls = null, SourceRelationship[]? relations = null) =>
        JsonDocument.Parse(JourneyDraftBuilder.Build(threads, calls ?? [], relations ?? [])).RootElement.Clone();
    static JsonElement Node(JsonElement graph, string id) => graph.GetProperty("nodes").EnumerateArray().Single(n => n.GetProperty("id").GetString() == id);
    static int X(JsonElement n) => n.GetProperty("position").GetProperty("x").GetInt32();
    static int Y(JsonElement n) => n.GetProperty("position").GetProperty("y").GetInt32();

    [Fact]
    public void TimelineAlignsParallelThreadsAndKeepsUnknownTimesHonest()
    {
        var g = Graph([Conversation("late", T.AddHours(1)), Conversation("unknown", null), Conversation("a", T), Conversation("b", T)]);
        Assert.Equal(X(Node(g, "conversation-a")), X(Node(g, "conversation-b")));
        Assert.NotEqual(Y(Node(g, "conversation-a")), Y(Node(g, "conversation-b")));
        Assert.True(X(Node(g, "conversation-late")) > X(Node(g, "conversation-a")));
        Assert.True(X(Node(g, "conversation-unknown")) > X(Node(g, "conversation-late")));
        Assert.Equal(JsonValueKind.Null, Node(g, "conversation-unknown").GetProperty("data").GetProperty("timelineStart").ValueKind);
        Assert.Empty(g.GetProperty("edges").EnumerateArray());
        Assert.Equal(4, g.GetProperty("steps").GetArrayLength());
    }

    [Fact]
    public void ToolIntervalsSplitOverlapsAndReuseOnlyProvenSequentialLanes()
    {
        Execution Call(string id, DateTime? start, DateTime? end) => new(id, "a", "spawn_agent", "call", "result", "call-and-result-recorded", start, end, "other-model");
        var g = Graph([Conversation("a", T)], [Call("a", T, T.AddMinutes(2)), Call("b", T, T.AddMinutes(3)),
            Call("c", T.AddMinutes(4), T.AddMinutes(5)), Call("d", null, null)]);
        Assert.NotEqual(Y(Node(g, "execution-a")), Y(Node(g, "execution-b")));
        Assert.Equal(Y(Node(g, "execution-a")), Y(Node(g, "execution-c")));
        Assert.NotEqual(Y(Node(g, "execution-c")), Y(Node(g, "execution-d")));
        Assert.Equal(4, g.GetProperty("edges").GetArrayLength());
        Assert.Equal("other-model", Node(g, "execution-a").GetProperty("data").GetProperty("model").GetString());
    }

    [Fact]
    public void ParentReferencesAreScopedAndAmbiguityNeverCreatesAnEdge()
    {
        var child = Conversation("child", T, "parent");
        Assert.Empty(Graph([child, Conversation("parent", T, provider: "Claude")]).GetProperty("edges").EnumerateArray());
        Assert.Empty(Graph([child, Conversation("parent", T, identity: "other-account")]).GetProperty("edges").EnumerateArray());
        Assert.Empty(Graph([child, Conversation("parent", T), Conversation("duplicate", T) with { ProviderId = "parent" }]).GetProperty("edges").EnumerateArray());
        var edge = Assert.Single(Graph([child, Conversation("parent", T)]).GetProperty("edges").EnumerateArray());
        Assert.Equal("conversation-parent", edge.GetProperty("source").GetString());
        Assert.Equal("conversation-child", edge.GetProperty("target").GetString());
    }

    [Fact]
    public void RecordedRelationshipsKeepDirectionAndOmitUnselectedTargets()
    {
        var g = Graph([Conversation("child", T), Conversation("parent", T)], relations: [
            new() { ConversationId = "child", TargetConversationId = "parent", Status = "confirmed" },
            new() { ConversationId = "child", TargetConversationId = "outside", Status = "confirmed" },
            new() { ConversationId = "child", TargetConversationId = "parent", Kind = "related", Status = "unresolved" }]);
        var edge = Assert.Single(g.GetProperty("edges").EnumerateArray());
        Assert.Equal("conversation-parent", edge.GetProperty("source").GetString());
        Assert.Equal(2, g.GetProperty("timeline").GetProperty("omittedRelationshipCount").GetInt32());
    }

    async Task<HttpClient> Login(string who = "alex")
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Journey", "1");
        (await client.PostAsJsonAsync("/api/auth/login", new { email = who + "@journey.local", password = "JourneyDemo!2026" })).EnsureSuccessStatusCode();
        return client;
    }

    [Fact]
    public async Task GeneratedDraftIsPrivateEditableAndPreservesSource()
    {
        using var client = await Login();
        string id;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDb>();
            var conversation = new Conversation { WorkspaceId = "cloudplay", Title = "Timeline evidence", SourceKey = "timeline-evidence-test", Provider = "Codex",
                SourceCreatedAt = T, SourceUpdatedAt = T.AddHours(1), RawPayload = "original" };
            var call = new MessageContentBlock { Kind = "tool_call", Text = "original tool" };
            var result = new MessageContentBlock { Kind = "tool_result" };
            conversation.Messages = [new() { Timestamp = T.AddMinutes(2), Blocks = [call] }, new() { Timestamp = T.AddMinutes(3), Blocks = [result] }];
            db.Add(conversation);
            db.Add(new AgentExecution { WorkspaceId = "cloudplay", ConversationId = conversation.Id, ToolName = "Task", CallBlockId = call.Id, ResultBlockId = result.Id, Status = "call-and-result-recorded" });
            await db.SaveChangesAsync();
            id = conversation.Id;
        }
        var response = await client.PostAsJsonAsync("/api/w/cloudplay/journeys/build", new { name = "Generated draft", conversationIds = new[] { id, id } });
        response.EnsureSuccessStatusCode();
        var journey = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Private", journey.GetProperty("visibility").GetString());
        var graph = JsonDocument.Parse(journey.GetProperty("graph").GetString()!).RootElement;
        Assert.Equal(2, graph.GetProperty("steps").GetArrayLength());
        Assert.Equal(T.AddMinutes(2), Node(graph, "conversation-" + id).GetProperty("data").GetProperty("timelineStart").GetDateTime());
        var journeyId = journey.GetProperty("id").GetString();
        var save = await client.PutAsJsonAsync("/api/journeys/" + journeyId, new { title = "Edited draft", description = "", visibility = "Private", graph = graph.GetRawText(), version = 0 });
        save.EnsureSuccessStatusCode();
        var reloaded = await client.GetFromJsonAsync<JsonElement>("/api/journeys/" + journeyId);
        Assert.Equal("Edited draft", reloaded.GetProperty("title").GetString());
        Assert.Equal(graph.GetRawText(), reloaded.GetProperty("graph").GetString());
        using var check = factory.Services.CreateScope();
        var source = await check.ServiceProvider.GetRequiredService<AppDb>().Conversations.FindAsync(id);
        Assert.Equal("original", source!.RawPayload);
    }

    [Fact]
    public async Task OversizedExecutionSelectionDoesNotCreateAPartialJourney()
    {
        using var client = await Login();
        string id;
        int before;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDb>();
            var conversation = new Conversation { WorkspaceId = "cloudplay", Title = "Many tools", SourceKey = "many-tools-test" };
            db.Add(conversation);
            db.AddRange(Enumerable.Range(0, 501).Select(i => new AgentExecution {
                WorkspaceId = "cloudplay", ConversationId = conversation.Id, ExternalCallId = i.ToString() }));
            await db.SaveChangesAsync();
            id = conversation.Id;
            before = await db.Journeys.CountAsync();
        }
        var response = await client.PostAsJsonAsync("/api/w/cloudplay/journeys/build", new { name = "Too large", conversationIds = new[] { id } });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var check = factory.Services.CreateScope();
        Assert.Equal(before, await check.ServiceProvider.GetRequiredService<AppDb>().Journeys.CountAsync());
    }

    [Fact]
    public async Task BuildValidatesSelectionAndWorkspaceAndRole()
    {
        using var client = await Login();
        foreach (var ids in new[] { Array.Empty<string>(), new[] { "missing" }, Enumerable.Repeat("demo-c0", 101).ToArray(), new[] { " " } })
            Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/w/cloudplay/journeys/build", new { name = "Draft", conversationIds = ids })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/w/personal/journeys/build", new { name = "Draft", conversationIds = new[] { "demo-c0" } })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/w/cloudplay/journeys/build", new { name = " ", conversationIds = new[] { "demo-c0" } })).StatusCode);
        using var viewer = await Login("riley");
        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.PostAsJsonAsync("/api/w/cloudplay/journeys/build", new { name = "Denied", conversationIds = new[] { "demo-c0" } })).StatusCode);
    }
}
