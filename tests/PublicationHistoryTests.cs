using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Journey.Tests;

public class PublicationHistoryTests : IClassFixture<Factory>
{
    readonly Factory factory;

    public PublicationHistoryTests(Factory factory) => this.factory = factory;

    [Theory]
    [InlineData("journeys/cloud-gaming")]
    [InlineData("conversations/demo-c9")]
    public async Task ListsOnlySourceLinksAndRevokesOnlyAuthorizedPublication(string source)
    {
        using var owner = factory.CreateClient();
        owner.DefaultRequestHeaders.Add("X-Journey", "1");
        (
            await owner.PostAsJsonAsync(
                "/api/auth/login",
                new { email = "alex@journey.local", password = "JourneyDemo!2026" }
            )
        ).EnsureSuccessStatusCode();
        var published = await owner.PostAsJsonAsync(
            "/api/" + source + "/publish",
            new
            {
                messageIds = new[] { "demo-m9-0" },
                redactions = Array.Empty<string>(),
                visibility = "Public",
                confirm = true,
            }
        );
        published.EnsureSuccessStatusCode();
        var slug = (await published.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("slug")
            .GetString();
        var rows = await owner.GetFromJsonAsync<JsonElement>("/api/" + source + "/publications");
        var row = rows.EnumerateArray().Single(r => r.GetProperty("slug").GetString() == slug);
        Assert.False(row.TryGetProperty("snapshot", out _));
        var id = row.GetProperty("id").GetString();
        using var viewer = factory.CreateClient();
        viewer.DefaultRequestHeaders.Add("X-Journey", "1");
        (
            await viewer.PostAsJsonAsync(
                "/api/auth/login",
                new { email = "riley@journey.local", password = "JourneyDemo!2026" }
            )
        ).EnsureSuccessStatusCode();
        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await viewer.GetAsync("/api/" + source + "/publications")).StatusCode
        );
        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await viewer.DeleteAsync("/api/" + source + "/publications/" + id)).StatusCode
        );
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await owner.DeleteAsync("/api/conversations/demo-c0/publications/" + id)).StatusCode
        );
        using var anonymous = factory.CreateClient();
        Assert.Equal(
            HttpStatusCode.OK,
            (await anonymous.GetAsync("/api/public/" + slug)).StatusCode
        );
        (
            await owner.DeleteAsync("/api/" + source + "/publications/" + id)
        ).EnsureSuccessStatusCode();
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await anonymous.GetAsync("/api/public/" + slug)).StatusCode
        );
        Assert.DoesNotContain(slug!, await anonymous.GetStringAsync("/api/public"));
        Assert.DoesNotContain(
            slug!,
            await owner.GetStringAsync("/api/" + source + "/publications")
        );
        Assert.Equal(HttpStatusCode.OK, (await owner.GetAsync("/api/" + source)).StatusCode);
    }
}
