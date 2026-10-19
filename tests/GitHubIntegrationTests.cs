using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Journey.Data;
using Journey.Domain;
using Journey.Server.Features.Integrations;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Journey.Tests;

public class GitHubFactory : Factory
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureAppConfiguration(
            (_, config) =>
                config.AddInMemoryCollection(
                    new Dictionary<string, string?> { ["GitHub:Enabled"] = "true" }
                )
        );
        builder.ConfigureServices(services =>
            services
                .AddHttpClient<GitHubClient>()
                .ConfigurePrimaryHttpMessageHandler(() => new FakeGitHub())
        );
    }

    sealed class FakeGitHub : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken ct
        )
        {
            Assert.Equal("api.github.com", request.RequestUri!.Host);
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Null(request.Headers.Authorization);
            var path = request.RequestUri.AbsolutePath;
            if (path.Contains("/missing") || path.Contains("/branches/absent"))
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
            if (path.Contains("/limited"))
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Forbidden));
            object body =
                path.Contains("/contents/")
                    ? new
                    {
                        type = "file",
                        encoding = "base64",
                        size = 18,
                        content = Convert.ToBase64String(
                            Encoding.UTF8.GetBytes("first\nsecond\nthird")
                        ),
                    }
                : path.Contains("/branches/") ? new { name = "main" }
                : new
                {
                    @private = path.Contains("/private"),
                    html_url = "https://github.com/acme/app",
                    default_branch = "main",
                };
            return Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(body) }
            );
        }
    }
}

public class GitHubIntegrationTests(GitHubFactory factory) : IClassFixture<GitHubFactory>
{
    const string Commit = "0123456789abcdef0123456789abcdef01234567";

    async Task<HttpClient> Login(string who)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Journey", "1");
        (
            await client.PostAsJsonAsync(
                "/api/auth/login",
                new { email = who + "@journey.local", password = "JourneyDemo!2026" }
            )
        ).EnsureSuccessStatusCode();
        return client;
    }

    async Task<string> Project(HttpClient client, string workspace = "cloudplay")
    {
        var response = await client.PostAsJsonAsync(
            $"/api/w/{workspace}/projects",
            new { name = "GitHub " + Guid.NewGuid() }
        );
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("id")
            .GetString()!;
    }

    static object Connection(string url = "https://github.com/acme/app", string? branch = null) =>
        new
        {
            repositoryUrl = url,
            branch,
            localRoot = "C:\\work\\app",
        };

    [Fact]
    public async Task LinksPublicRepositoryResolvesSessionRevisionAndUnlinks()
    {
        using var client = await Login("alex");
        var id = await Project(client);
        var route = $"/api/w/cloudplay/projects/{id}/github";
        var linked = await client.PutAsJsonAsync(route, Connection());
        linked.EnsureSuccessStatusCode();
        var result = await linked.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("main", result.GetProperty("gitHubBranch").GetString());
        Assert.Equal("C:/work/app", result.GetProperty("gitHubLocalRoot").GetString());
        string conversationId;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDb>();
            var conversation = new Conversation
            {
                WorkspaceId = "cloudplay",
                ApplicationProjectId = id,
                Provider = "Codex",
                Metadata = JsonSerializer.Serialize(
                    new
                    {
                        git = new
                        {
                            commit_hash = Commit,
                            repository_url = "git@github.com:acme/app.git",
                        },
                    }
                ),
            };
            db.Add(conversation);
            await db.SaveChangesAsync();
            conversationId = conversation.Id;
        }
        using var viewer = await Login("riley");
        (
            await viewer.GetAsync(
                $"/api/conversations/{conversationId}/github/file?path=src/auth.ts"
            )
        ).EnsureSuccessStatusCode();
        var context = await client.GetFromJsonAsync<JsonElement>(
            $"/api/conversations/{conversationId}/github"
        );
        Assert.Equal(
            Commit,
            context.GetProperty("source").GetProperty("sessionCommit").GetString()
        );
        var preview = await client.GetFromJsonAsync<JsonElement>(
            $"/api/conversations/{conversationId}/github/file?path=src/auth.ts&startLine=2"
        );
        Assert.Equal(Commit, preview.GetProperty("reference").GetString());
        Assert.Equal("second", preview.GetProperty("lines")[0].GetString());
        Assert.Equal(2, preview.GetProperty("startLine").GetInt32());
        Assert.Equal(
            HttpStatusCode.BadRequest,
            (
                await client.GetAsync(
                    $"/api/conversations/{conversationId}/github/file?path=..%2Fsecret.txt"
                )
            ).StatusCode
        );
        (await client.DeleteAsync(route)).EnsureSuccessStatusCode();
        context = await client.GetFromJsonAsync<JsonElement>(
            $"/api/conversations/{conversationId}/github"
        );
        Assert.Equal(JsonValueKind.Null, context.GetProperty("source").ValueKind);
    }

    [Fact]
    public async Task EnforcesWorkspaceBoundariesAndEditorPermissions()
    {
        using var owner = await Login("alex");
        using var viewer = await Login("riley");
        var id = await Project(owner);
        Assert.Equal(
            HttpStatusCode.Forbidden,
            (
                await viewer.PutAsJsonAsync($"/api/w/cloudplay/projects/{id}/github", Connection())
            ).StatusCode
        );
        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await viewer.DeleteAsync($"/api/w/cloudplay/projects/{id}/github")).StatusCode
        );
        var personal = await Project(owner, "personal");
        Assert.Equal(
            HttpStatusCode.NotFound,
            (
                await owner.PutAsJsonAsync(
                    $"/api/w/cloudplay/projects/{personal}/github",
                    Connection()
                )
            ).StatusCode
        );
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDb>();
        var c = new Conversation { WorkspaceId = "personal", ApplicationProjectId = personal };
        db.Add(c);
        await db.SaveChangesAsync();
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await viewer.GetAsync($"/api/conversations/{c.Id}/github")).StatusCode
        );
        Assert.Equal(
            HttpStatusCode.NotFound,
            (
                await viewer.GetAsync($"/api/conversations/{c.Id}/github/file?path=file.ts")
            ).StatusCode
        );
    }

    [Theory]
    [InlineData("https://github.com/acme/private", null)]
    [InlineData("https://github.com/acme/missing", null)]
    [InlineData("https://github.com/acme/limited", null)]
    [InlineData("https://github.com/acme/app", "absent")]
    [InlineData("https://github.com.evil.test/acme/app", null)]
    [InlineData("http://127.0.0.1/secret", null)]
    public async Task InvalidOrUnavailableRepositoryDoesNotPersist(string url, string? branch)
    {
        using var client = await Login("alex");
        var id = await Project(client);
        Assert.Equal(
            HttpStatusCode.BadRequest,
            (
                await client.PutAsJsonAsync(
                    $"/api/w/cloudplay/projects/{id}/github",
                    Connection(url, branch)
                )
            ).StatusCode
        );
        using var scope = factory.Services.CreateScope();
        Assert.Null(
            (
                await scope
                    .ServiceProvider.GetRequiredService<AppDb>()
                    .Set<ApplicationProject>()
                    .FindAsync(id)
            )!.GitHubRepositoryUrl
        );
    }

    [Fact]
    public async Task DisabledIntegrationCannotBeUsedButCanBeUnlinked()
    {
        using var disabled = factory.WithWebHostBuilder(b =>
            b.ConfigureAppConfiguration(
                (_, config) =>
                    config.AddInMemoryCollection(
                        new Dictionary<string, string?> { ["GitHub:Enabled"] = "false" }
                    )
            )
        );
        using var client = disabled.CreateClient();
        client.DefaultRequestHeaders.Add("X-Journey", "1");
        (
            await client.PostAsJsonAsync(
                "/api/auth/login",
                new { email = "alex@journey.local", password = "JourneyDemo!2026" }
            )
        ).EnsureSuccessStatusCode();
        var id = await Project(client);
        Assert.Equal(
            HttpStatusCode.BadRequest,
            (
                await client.PutAsJsonAsync($"/api/w/cloudplay/projects/{id}/github", Connection())
            ).StatusCode
        );
        Assert.Equal(
            HttpStatusCode.NoContent,
            (await client.DeleteAsync($"/api/w/cloudplay/projects/{id}/github")).StatusCode
        );
    }

    [Theory]
    [InlineData("{\"git\":{\"commit_hash\":\"not-a-sha\"}}")]
    [InlineData("{\"commit\":\"0123456789abcdef0123456789abcdef01234567\"}")]
    [InlineData("[]")]
    [InlineData("not-json")]
    public void NeverGuessesHistoricalRevision(string metadata) =>
        Assert.Null(GitHubEndpoints.SessionCommit(metadata, "https://github.com/acme/app"));

    [Fact]
    public void MismatchedRemoteDoesNotPinAnotherRepositorysCommit() =>
        Assert.Null(
            GitHubEndpoints.SessionCommit(
                JsonSerializer.Serialize(
                    new
                    {
                        git = new
                        {
                            commit_hash = Commit,
                            repository_url = "https://github.com/other/app",
                        },
                    }
                ),
                "https://github.com/acme/app"
            )
        );

    [Fact]
    public async Task AppDiagnosticsRequireAnEditorAndStatusNeverReturnsCredentials()
    {
        using var viewer = await Login("riley");
        Assert.Equal(
            HttpStatusCode.Forbidden,
            (
                await viewer.PostAsJsonAsync("/api/w/cloudplay/integrations/github/check", new { })
            ).StatusCode
        );
        using var owner = await Login("alex");
        (
            await owner.PostAsJsonAsync("/api/w/cloudplay/integrations/github/check", new { })
        ).EnsureSuccessStatusCode();
        using var key = System.Security.Cryptography.RSA.Create(2048);
        var pem = key.ExportRSAPrivateKeyPem();
        using var appMode = factory.WithWebHostBuilder(b =>
            b.ConfigureAppConfiguration(
                (_, config) =>
                    config.AddInMemoryCollection(
                        new Dictionary<string, string?>
                        {
                            ["GitHub:AuthenticationMode"] = "App",
                            ["GitHub:App:ClientId"] = "Iv1.example",
                            ["GitHub:App:Slug"] = "openhow-test",
                            ["GitHub:App:PrivateKeyPem"] = pem,
                        }
                    )
            )
        );
        using var client = appMode.CreateClient();
        client.DefaultRequestHeaders.Add("X-Journey", "1");
        (
            await client.PostAsJsonAsync(
                "/api/auth/login",
                new { email = "alex@journey.local", password = "JourneyDemo!2026" }
            )
        ).EnsureSuccessStatusCode();
        var response = await client.GetAsync("/api/integrations/github/status");
        response.EnsureSuccessStatusCode();
        var text = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("PRIVATE KEY", text);
        Assert.DoesNotContain("Iv1.example", text);
        var status = JsonSerializer.Deserialize<JsonElement>(text);
        Assert.Equal("App", status.GetProperty("mode").GetString());
        Assert.True(status.GetProperty("configured").GetBoolean());
        Assert.Equal(
            "https://github.com/apps/openhow-test/installations/new",
            status.GetProperty("installationUrl").GetString()
        );
    }
}
