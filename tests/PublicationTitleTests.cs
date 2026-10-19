using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Journey.Domain;
using Journey.Data;

namespace Journey.Tests;

public class PublicationTitleTests : IClassFixture<Factory>
{
    readonly Factory factory;
    public PublicationTitleTests(Factory factory) => this.factory = factory;

    async Task<HttpClient> Login()
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Journey", "1");
        (await client.PostAsJsonAsync("/api/auth/login", new
        {
            email = "alex@journey.local",
            password = "JourneyDemo!2026",
        })).EnsureSuccessStatusCode();
        return client;
    }

    [Fact]
    public async Task SharedTitleIsScannedRedactedAndStoredWithoutRenamingTheSource()
    {
        using var client = await Login();
        const string path = "/api/conversations/demo-c9";
        var source = await client.GetFromJsonAsync<JsonElement>(path);
        var input = new PublishRequest(["demo-m9-0"], [], "Unlisted", true,
            Title: "  Scaling review for private@example.test  ");
        var scan = await client.PostAsJsonAsync(path + "/publication-preview", input);
        scan.EnsureSuccessStatusCode();
        var scanned = await scan.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Contains(scanned.GetProperty("findings").EnumerateArray(),
            finding => finding.GetString() == "private@example.test");

        input = input with { Redactions = ["private@example.test"] };
        var preview = await client.PostAsJsonAsync(path + "/publication-preview", input);
        preview.EnsureSuccessStatusCode();
        var reviewed = await preview.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Scaling review for [REDACTED]",
            reviewed.GetProperty("snapshot").GetProperty("title").GetString());

        var result = await client.PostAsJsonAsync(path + "/publish", input);
        result.EnsureSuccessStatusCode();
        var slug = (await result.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("slug").GetString();
        using var visitor = factory.CreateClient();
        var published = await visitor.GetFromJsonAsync<JsonElement>("/api/public/" + slug);
        Assert.Equal("Scaling review for [REDACTED]", published.GetProperty("title").GetString());
        Assert.DoesNotContain("private@example.test", published.GetRawText());
        var unchanged = await client.GetFromJsonAsync<JsonElement>(path);
        Assert.Equal(source.GetProperty("title").GetString(), unchanged.GetProperty("title").GetString());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void MissingDisplayTitlePreservesTheOriginalTitle(string? title)
    {
        var source = new Conversation
        {
            Title = "Original private title",
            Messages = [new ConversationMessage { Id = "m", Role = "user" }],
        };
        var request = new PublishRequest(["m"], ["private"], "Unlisted", true, Title: title);
        using var snapshot = JsonDocument.Parse(Json.Write(Publishing.ConversationSnapshot(source, request)));
        Assert.Equal("Original [REDACTED] title", snapshot.RootElement.GetProperty("title").GetString());
    }

    [Theory]
    [InlineData("publication-preview")]
    [InlineData("publish")]
    public async Task RejectsTitlesOverTheLimit(string action)
    {
        using var client = await Login();
        var input = new PublishRequest(["demo-m9-0"], [], "Unlisted", true, Title: new string('x', 201));
        var response = await client.PostAsJsonAsync("/api/conversations/demo-c9/" + action, input);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
