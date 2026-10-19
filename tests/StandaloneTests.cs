using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Journey.Domain;
using Journey.Data;

namespace Journey.Tests;

public class StandaloneTests : IClassFixture<Factory>
{
    readonly Factory factory;

    public StandaloneTests(Factory f) => factory = f;

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
    public async Task SharesConversationWithoutCreatingJourneyAndOnlySelectedRedactedMessages()
    {
        using var c = await Login("alex");
        var before = await c.GetFromJsonAsync<JsonElement>("/api/w/cloudplay/summary");
        var request = new
        {
            messageIds = new[] { "demo-m9-0" },
            redactions = new[] { "cloud gaming" },
            visibility = "Unlisted",
            confirm = true,
        };
        var preview = await c.PostAsJsonAsync(
            "/api/conversations/demo-c9/publication-preview",
            request
        );
        preview.EnsureSuccessStatusCode();
        var published = await c.PostAsJsonAsync("/api/conversations/demo-c9/publish", request);
        published.EnsureSuccessStatusCode();
        var slug = (await published.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("slug")
            .GetString();
        using var anon = factory.CreateClient();
        var text = await anon.GetStringAsync("/api/public/" + slug);
        var data = JsonDocument.Parse(text).RootElement;
        Assert.Equal("conversation", data.GetProperty("kind").GetString());
        Assert.Single(data.GetProperty("messages").EnumerateArray());
        Assert.Contains("[REDACTED]", text);
        Assert.DoesNotContain("cloud gaming", text);
        Assert.DoesNotContain("rawPayload", text);
        Assert.DoesNotContain("metadata", text);
        Assert.DoesNotContain("demo-m9-1", text);
        Assert.Equal(
            before.GetProperty("journeys").GetInt32(),
            (await c.GetFromJsonAsync<JsonElement>("/api/w/cloudplay/summary"))
                .GetProperty("journeys")
                .GetInt32()
        );
    }

    [Fact]
    public async Task ShareRequiresPublishPermissionConfirmationAndValidMessageSelection()
    {
        using var viewer = await Login("riley");
        using var editor = await Login("sam");
        using var owner = await Login("alex");
        var input = new
        {
            messageIds = new[] { "demo-m0-0" },
            redactions = Array.Empty<string>(),
            visibility = "Unlisted",
            confirm = true,
        };
        foreach (var client in new[] { viewer, editor })
            Assert.Equal(
                HttpStatusCode.Forbidden,
                (
                    await client.PostAsJsonAsync("/api/conversations/demo-c0/publish", input)
                ).StatusCode
            );
        Assert.Equal(
            HttpStatusCode.BadRequest,
            (
                await owner.PostAsJsonAsync(
                    "/api/conversations/demo-c0/publish",
                    new
                    {
                        messageIds = new[] { "demo-m0-0" },
                        redactions = Array.Empty<string>(),
                        visibility = "Unlisted",
                        confirm = false,
                    }
                )
            ).StatusCode
        );
        Assert.Equal(
            HttpStatusCode.BadRequest,
            (await owner.PostAsJsonAsync("/api/conversations/demo-c1/publish", input)).StatusCode
        );
        Assert.Equal(
            HttpStatusCode.BadRequest,
            (
                await owner.PostAsJsonAsync(
                    "/api/conversations/demo-c0/publish",
                    new
                    {
                        messageIds = Array.Empty<string>(),
                        redactions = Array.Empty<string>(),
                        visibility = "Unlisted",
                        confirm = true,
                    }
                )
            ).StatusCode
        );
    }

    [Fact]
    public async Task SavesAndUpdatesAStandaloneComparisonWithEvidenceAndDecision()
    {
        using var c = await Login("alex");
        var input = new ComparisonSession
        {
            Title = "Which runtime?",
            Question = "Which runtime?",
            LeftId = "demo-c1",
            RightId = "demo-c2",
            Outcome = "Right",
            Rationale = "Prefer compatibility",
            Links =
                "[{\"left\":\"demo-m1-1\",\"right\":\"demo-m2-1\",\"criterion\":\"Tradeoff\",\"note\":\"Compatibility versus latency\"}]",
        };
        var response = await c.PostAsJsonAsync("/api/w/cloudplay/comparisons", input);
        response.EnsureSuccessStatusCode();
        var saved = await response.Content.ReadFromJsonAsync<ComparisonSession>();
        Assert.Null(saved!.JourneyId);
        var list = await c.GetFromJsonAsync<JsonElement>("/api/w/cloudplay/comparisons");
        var summary = Assert.Single(
            list.GetProperty("items").EnumerateArray(),
            x => x.GetProperty("id").GetString() == saved.Id
        );
        Assert.False(summary.TryGetProperty("links", out _));
        Assert.Equal(
            "Prefer compatibility",
            (await c.GetFromJsonAsync<ComparisonSession>("/api/comparisons/" + saved.Id))!.Rationale
        );
        input.Rationale = "Revised after testing";
        (await c.PutAsJsonAsync("/api/comparisons/" + saved.Id, input)).EnsureSuccessStatusCode();
        var updated = await c.GetFromJsonAsync<JsonElement>("/api/w/cloudplay/comparisons");
        Assert.Single(
            updated.GetProperty("items").EnumerateArray(),
            x => x.GetProperty("id").GetString() == saved.Id
        );
        Assert.Equal(
            "Revised after testing",
            (await c.GetFromJsonAsync<ComparisonSession>("/api/comparisons/" + saved.Id))!.Rationale
        );
    }

    [Fact]
    public async Task ComparisonRejectsForeignEvidenceAndViewerWrites()
    {
        using var owner = await Login("alex");
        using var viewer = await Login("riley");
        var input = new ComparisonSession
        {
            Title = "Invalid evidence",
            LeftId = "demo-c0",
            RightId = "demo-c1",
            Links = "[{\"left\":\"demo-m9-0\",\"right\":\"demo-m1-0\"}]",
        };
        Assert.Equal(
            HttpStatusCode.BadRequest,
            (await owner.PostAsJsonAsync("/api/w/cloudplay/comparisons", input)).StatusCode
        );
        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await viewer.PostAsJsonAsync("/api/w/cloudplay/comparisons", input)).StatusCode
        );
        input.Links = "[]";
        Assert.Equal(
            HttpStatusCode.BadRequest,
            (await owner.PostAsJsonAsync("/api/w/personal/comparisons", input)).StatusCode
        );
        input.Outcome = "Right";
        Assert.Equal(
            HttpStatusCode.BadRequest,
            (await owner.PostAsJsonAsync("/api/w/cloudplay/comparisons", input)).StatusCode
        );
    }
}
