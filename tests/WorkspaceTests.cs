using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Journey.Tests;

public class WorkspaceTests : IClassFixture<Factory>
{
    readonly Factory factory;

    public WorkspaceTests(Factory f) => factory = f;

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
    public async Task CanCreateMultiplePersonalWorkspacesWithoutTeamsOrOtherMembers()
    {
        using var owner = await Login("alex");
        using var other = await Login("sam");
        var ids = new List<string>();
        for (var i = 0; i < 2; i++)
        {
            var result = await owner.PostAsJsonAsync(
                "/api/workspaces",
                new { name = "  Research " + i + "  ", type = "Personal" }
            );
            Assert.Equal(HttpStatusCode.Created, result.StatusCode);
            var w = await result.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal(JsonValueKind.Null, w.GetProperty("teamId").ValueKind);
            Assert.Equal("Research " + i, w.GetProperty("name").GetString());
            Assert.Equal("Owner", w.GetProperty("role").GetString());
            var id = w.GetProperty("id").GetString()!;
            ids.Add(id);
            Assert.Equal(
                HttpStatusCode.Forbidden,
                (await other.GetAsync("/api/w/" + id + "/library")).StatusCode
            );
            Assert.Equal(
                HttpStatusCode.BadRequest,
                (
                    await owner.PostAsJsonAsync(
                        "/api/w/" + id + "/invitations",
                        new { email = "sam@journey.local", role = "Editor" }
                    )
                ).StatusCode
            );
            var members = await owner.GetFromJsonAsync<JsonElement>("/api/w/" + id + "/members");
            Assert.Equal(1, members.GetArrayLength());
        }
        var ownList = await owner.GetFromJsonAsync<JsonElement>("/api/workspaces");
        foreach (var id in ids)
            Assert.Contains(ownList.EnumerateArray(), w => w.GetProperty("id").GetString() == id);
        var others = await other.GetFromJsonAsync<JsonElement>("/api/workspaces");
        Assert.DoesNotContain(
            others.EnumerateArray(),
            w => ids.Contains(w.GetProperty("id").GetString()!)
        );
    }

    [Fact]
    public async Task CanCreateTeamInviteAndRenameWithoutTouchingOtherWorkspaces()
    {
        using var owner = await Login("alex");
        using var other = await Login("sam");
        var result = await owner.PostAsJsonAsync(
            "/api/workspaces",
            new { name = "New team", type = "Team" }
        );
        result.EnsureSuccessStatusCode();
        var w = await result.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(JsonValueKind.String, w.GetProperty("teamId").ValueKind);
        var id = w.GetProperty("id").GetString();
        var invite = await owner.PostAsJsonAsync(
            "/api/w/" + id + "/invitations",
            new { email = "sam@journey.local", role = "Editor" }
        );
        invite.EnsureSuccessStatusCode();
        var token = (await invite.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("token")
            .GetString();
        (
            await other.PostAsJsonAsync("/api/invitations/" + token + "/accept", new { })
        ).EnsureSuccessStatusCode();
        Assert.Equal(
            HttpStatusCode.OK,
            (await other.GetAsync("/api/w/" + id + "/library")).StatusCode
        );
        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await other.PutAsJsonAsync("/api/w/" + id, new { name = "Wrong" })).StatusCode
        );
        (
            await owner.PutAsJsonAsync("/api/w/" + id, new { name = "Renamed team" })
        ).EnsureSuccessStatusCode();
        var list = await owner.GetFromJsonAsync<JsonElement>("/api/workspaces");
        Assert.Contains(
            list.EnumerateArray(),
            w => w.GetProperty("name").GetString() == "Renamed team"
        );
    }

    [Theory]
    [InlineData("", "Personal")]
    [InlineData("  ", "Team")]
    [InlineData("Bad type", "Public")]
    public async Task RejectsInvalidWorkspaceInput(string name, string type)
    {
        using var client = await Login("alex");
        Assert.Equal(
            HttpStatusCode.BadRequest,
            (await client.PostAsJsonAsync("/api/workspaces", new { name, type })).StatusCode
        );
    }

    [Fact]
    public async Task ViewerInOneWorkspaceCanOwnAnother()
    {
        using var viewer = await Login("riley");
        var result = await viewer.PostAsJsonAsync(
            "/api/workspaces",
            new { name = "Riley research", type = "Personal" }
        );
        result.EnsureSuccessStatusCode();
        var w = await result.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Owner", w.GetProperty("role").GetString());
        Assert.Equal(
            HttpStatusCode.Forbidden,
            (
                await viewer.PostAsJsonAsync(
                    "/api/w/cloudplay/journeys",
                    new { name = "Still not allowed" }
                )
            ).StatusCode
        );
    }
}
