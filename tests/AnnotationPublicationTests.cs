using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Journey.Domain;
using Journey.Data;


namespace Journey.Tests;

public class AnnotationPublicationTests : IClassFixture<Factory>
{
    readonly Factory factory;

    public AnnotationPublicationTests(Factory factory) => this.factory = factory;

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExplicitThreadsAreRedactedInImmutableAnonymousSnapshots(bool journey)
    {
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Journey", "1");
        (
            await client.PostAsJsonAsync(
                "/api/auth/login",
                new { email = "alex@journey.local", password = "JourneyDemo!2026" }
            )
        ).EnsureSuccessStatusCode();
        var created = await client.PostAsJsonAsync(
            "/api/w/cloudplay/annotations",
            new
            {
                conversationId = "demo-c9",
                messageId = "demo-m9-0",
                body = "Private selected secret-value",
            }
        );
        created.EnsureSuccessStatusCode();
        var annotation = await created.Content.ReadFromJsonAsync<JsonElement>();
        var id = annotation.GetProperty("id").GetString()!;
        var path = journey ? "/api/journeys/cloud-gaming" : "/api/conversations/demo-c9";
        var excluded = await client.PostAsJsonAsync(
            path + "/publication-preview",
            new
            {
                messageIds = new[] { "demo-m9-0" },
                redactions = Array.Empty<string>(),
                visibility = "Unlisted",
                confirm = false,
            }
        );
        excluded.EnsureSuccessStatusCode();
        Assert.DoesNotContain("Private selected", await excluded.Content.ReadAsStringAsync());
        var included = await client.PostAsJsonAsync(
            path + "/publish",
            new
            {
                messageIds = new[] { "demo-m9-0" },
                annotationIds = new[] { id },
                redactions = new[] { "secret-value" },
                visibility = "Unlisted",
                confirm = true,
            }
        );
        included.EnsureSuccessStatusCode();
        var slug = (await included.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("slug")
            .GetString();
        using var anonymous = factory.CreateClient();
        var snapshot = await anonymous.GetStringAsync("/api/public/" + slug);
        Assert.Contains("Private selected [REDACTED]", snapshot);
        Assert.DoesNotContain("secret-value", snapshot);
        Assert.DoesNotContain("authorId", snapshot);
        Assert.DoesNotContain("fingerprint", snapshot);
        var hidden = await client.PostAsJsonAsync(
            path + "/publication-preview",
            new
            {
                messageIds = new[] { "demo-m9-1" },
                annotationIds = new[] { id },
                redactions = Array.Empty<string>(),
                visibility = "Unlisted",
                confirm = false,
            }
        );
        Assert.Equal(HttpStatusCode.BadRequest, hidden.StatusCode);
        var foreign = await client.PostAsJsonAsync(
            "/api/conversations/demo-c0/publication-preview",
            new
            {
                messageIds = new[] { "demo-m0-0" },
                annotationIds = new[] { id },
                redactions = Array.Empty<string>(),
                visibility = "Unlisted",
                confirm = false,
            }
        );
        Assert.Equal(HttpStatusCode.BadRequest, foreign.StatusCode);
    }
}
