using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Journey.Domain;
using Journey.Data;


namespace Journey.Tests;

public class BoundaryTests : IClassFixture<Factory>
{
    readonly Factory factory;

    public BoundaryTests(Factory f) => factory = f;

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
    public async Task MentionsArePersistentAndForeignAnchorsAreRejected()
    {
        using var alex = await Login("alex");
        using var sam = await Login("sam");
        var c = await alex.GetFromJsonAsync<Conversation>("/api/conversations/demo-c0");
        var m = c!.Messages.OrderBy(x => x.Order).First();
        var b = m.Blocks[0];
        var anchor = Anchoring.Create(b.Id, b.Text, 0, 10);
        var created = await alex.PostAsJsonAsync(
            "/api/w/cloudplay/annotations",
            new
            {
                conversationId = c.Id,
                messageId = m.Id,
                anchor,
                body = "Please inspect this @Sam Rivera",
            }
        );
        created.EnsureSuccessStatusCode();
        var notification = await sam.GetFromJsonAsync<JsonElement>("/api/notifications");
        Assert.Contains(
            notification.EnumerateArray(),
            n => n.GetProperty("link").GetString()!.Contains(c.Id)
        );
        var bad = await alex.PostAsJsonAsync(
            "/api/w/cloudplay/annotations",
            new
            {
                conversationId = c.Id,
                messageId = m.Id,
                anchor = anchor with
                {
                    BlockId = "demo-b1-0",
                },
                body = "Wrong block",
            }
        );
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
        var cross = await alex.PostAsJsonAsync(
            "/api/w/personal/annotations",
            new
            {
                conversationId = c.Id,
                messageId = m.Id,
                body = "Wrong workspace",
            }
        );
        Assert.Equal(HttpStatusCode.BadRequest, cross.StatusCode);
    }

    [Fact]
    public async Task CannotSmuggleAPrivateJourneyIntoAConversationComment()
    {
        using var c = await Login("sam");
        var response = await c.PostAsJsonAsync(
            "/api/w/cloudplay/annotations",
            new
            {
                conversationId = "demo-c0",
                messageId = "demo-m0-0",
                journeyId = "private-research",
                body = "must be denied",
            }
        );
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task PublicationRemainsUnchangedAfterAuthorEdits()
    {
        using var c = await Login("alex");
        var j = await c.GetFromJsonAsync<JsonElement>("/api/journeys/cloud-gaming");
        var request = new
        {
            messageIds = new[] { "demo-m0-0" },
            redactions = new[] { "cloud gaming" },
            visibility = "Unlisted",
            confirm = true,
        };
        var response = await c.PostAsJsonAsync("/api/journeys/cloud-gaming/publish", request);
        response.EnsureSuccessStatusCode();
        var slug = (await response.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("slug")
            .GetString();
        using var anonymous = factory.CreateClient();
        var before = await anonymous.GetStringAsync("/api/public/" + slug);
        (
            await c.PutAsJsonAsync(
                "/api/journeys/cloud-gaming",
                new
                {
                    title = "Later private edit",
                    description = "not public",
                    visibility = "Team",
                    graph = j.GetProperty("graph").GetString(),
                    version = j.GetProperty("version").GetInt32(),
                }
            )
        ).EnsureSuccessStatusCode();
        Assert.Equal(before, await anonymous.GetStringAsync("/api/public/" + slug));
        Assert.DoesNotContain("not public", before);
        Assert.DoesNotContain("cloud gaming", before);
        Assert.DoesNotContain("rawPayload", before);
    }

    [Fact]
    public async Task MutationRequiresSameOriginHeader()
    {
        using var c = factory.CreateClient();
        var r = await c.PostAsJsonAsync(
            "/api/auth/login",
            new { email = "alex@journey.local", password = "JourneyDemo!2026" }
        );
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
    }

    [Fact]
    public void ScanUsesDecodedPaths()
    {
        var path = @"C:\Users\private\key.txt";
        Assert.Contains(
            path,
            Publishing.Findings(Json.Write(new { text = "Local path: " + path }))
        );
    }

    [Fact]
    public async Task EveryUnsupportedScaffoldRejectsRatherThanFabricates()
    {
        foreach (var adapter in PlannedImporters.Create())
        {
            Assert.False(adapter.CanImport(new("session.json", [])));
            await Assert.ThrowsAsync<InvalidDataException>(() =>
                adapter.ImportAsync(new("session.json", []), default)
            );
        }
    }
}
