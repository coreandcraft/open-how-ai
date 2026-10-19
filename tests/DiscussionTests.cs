using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Journey.Tests;

public class DiscussionTests(Factory factory) : IClassFixture<Factory>
{
    async Task<HttpClient> Login(string name)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Journey", "1");
        (await client.PostAsJsonAsync("/api/auth/login", new { email = name + "@journey.local", password = "JourneyDemo!2026" })).EnsureSuccessStatusCode();
        return client;
    }
    async Task<string> Publish(HttpClient client, string source, bool enabled = false)
    {
        var response = await client.PostAsJsonAsync("/api/" + source + "/publish", new {
            messageIds = new[] { "demo-m9-0" }, redactions = Array.Empty<string>(), visibility = "Unlisted", confirm = true, discussionEnabled = enabled
        });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("slug").GetString()!;
    }

    [Theory]
    [InlineData("journeys/cloud-gaming")]
    [InlineData("conversations/demo-c9")]
    public async Task DiscussionRequiresOptInAndAccountAndNeverChangesSnapshot(string source)
    {
        using var owner = await Login("alex");
        using var viewer = await Login("riley");
        using var anonymous = factory.CreateClient();
        anonymous.DefaultRequestHeaders.Add("X-Journey", "1");
        var slug = await Publish(owner, source);
        var endpoint = "/api/public/" + slug + "/discussion";
        var snapshot = await anonymous.GetStringAsync("/api/public/" + slug);
        Assert.False((await anonymous.GetFromJsonAsync<JsonElement>(endpoint)).GetProperty("enabled").GetBoolean());
        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.PostAsJsonAsync(endpoint, new { body = "Hello" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.PatchAsJsonAsync(endpoint, new { enabled = true })).StatusCode);
        (await owner.PatchAsJsonAsync(endpoint, new { enabled = true })).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PostAsJsonAsync(endpoint, new { body = "Hello" })).StatusCode);
        // A registered visitor outside the source workspace can participate on the shared link.
        using var outsider = factory.CreateClient();
        outsider.DefaultRequestHeaders.Add("X-Journey", "1");
        var guestEmail = Guid.NewGuid()+"@example.test";
        (await outsider.PostAsJsonAsync("/api/auth/register", new { email = guestEmail, password = "DiscussionTest!2026", name = "Guest member" })).EnsureSuccessStatusCode();
        await AccountTestHelper.ConfirmAndLogin(factory, outsider, guestEmail, "DiscussionTest!2026");
        var post = await outsider.PostAsJsonAsync(endpoint, new { body = "Why WebRTC?", kind = "Question" });
        post.EnsureSuccessStatusCode();
        var id = (await post.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetString();
        (await owner.PostAsJsonAsync(endpoint, new { body = "Low latency.", parentId = id })).EnsureSuccessStatusCode();
        var result = await anonymous.GetFromJsonAsync<JsonElement>(endpoint);
        Assert.Equal(2, result.GetProperty("entries").GetArrayLength());
        Assert.Contains(result.GetProperty("entries").EnumerateArray(), row => row.GetProperty("isSharer").GetBoolean());
        Assert.DoesNotContain("@example.test", result.ToString());
        Assert.Equal(snapshot, await anonymous.GetStringAsync("/api/public/" + slug));
        Assert.Equal(HttpStatusCode.BadRequest, (await viewer.PostAsJsonAsync(endpoint, new { body = " " })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await viewer.PostAsJsonAsync(endpoint, new { body = new string('x', 4001) })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await viewer.PostAsJsonAsync(endpoint, new { body = "Hi", kind = "Admin" })).StatusCode);
        var other = await Publish(owner, source, true);
        Assert.Equal(HttpStatusCode.BadRequest, (await viewer.PostAsJsonAsync("/api/public/" + other + "/discussion", new { body = "Wrong link", parentId = id })).StatusCode);
        (await owner.PatchAsJsonAsync(endpoint, new { enabled = false })).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Forbidden, (await outsider.PostAsJsonAsync(endpoint, new { body = "Stale browser" })).StatusCode);
        Assert.Equal(0, (await anonymous.GetFromJsonAsync<JsonElement>(endpoint)).GetProperty("entries").GetArrayLength());
        (await owner.PatchAsJsonAsync(endpoint, new { enabled = true })).EnsureSuccessStatusCode();
        Assert.Equal(2, (await anonymous.GetFromJsonAsync<JsonElement>(endpoint)).GetProperty("entries").GetArrayLength());
        var history = await owner.GetFromJsonAsync<JsonElement>("/api/" + source + "/publications");
        var publicationId = history.EnumerateArray().Single(p => p.GetProperty("slug").GetString() == slug).GetProperty("id").GetString();
        (await owner.DeleteAsync("/api/" + source + "/publications/" + publicationId)).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.NotFound, (await anonymous.GetAsync(endpoint)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await outsider.PostAsJsonAsync(endpoint, new { body = "Revoked" })).StatusCode);
    }
}
