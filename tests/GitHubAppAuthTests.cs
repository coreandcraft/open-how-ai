using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Journey.Server.Features.Integrations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace Journey.Tests;

public class GitHubAppAuthTests
{
    sealed class Remote : HttpMessageHandler, IHttpClientFactory, IDisposable
    {
        public readonly RSA Key = RSA.Create(2048);
        public int TokenRequests,
            InstallationRequests,
            ContentRequests;
        public bool Private,
            Suspended;
        public string Permission = "read",
            Slug = "openhow-test";
        public HttpStatusCode? Failure;
        public DateTimeOffset Expires = DateTimeOffset.UtcNow.AddHours(1);
        public bool RevokeContents;

        public GitHubAppAuth Auth(IDictionary<string, string?>? overrides = null)
        {
            var settings = new Dictionary<string, string?>
            {
                ["GitHub:AuthenticationMode"] = "App",
                ["GitHub:App:ClientId"] = "Iv1.test",
                ["GitHub:App:Slug"] = "openhow-test",
                ["GitHub:App:PrivateKeyPem"] = Key.ExportRSAPrivateKeyPem(),
            };
            if (overrides != null)
                foreach (var pair in overrides)
                    settings[pair.Key] = pair.Value;
            return new(
                new ConfigurationBuilder().AddInMemoryCollection(settings).Build(),
                this,
                NullLogger<GitHubAppAuth>.Instance
            );
        }

        public HttpClient CreateClient(string name) => new(this, false);

        public GitHubClient Client(GitHubAppAuth auth) =>
            new(CreateClient("repo"), auth, NullLogger<GitHubClient>.Instance);

        static byte[] Decode(string value) =>
            Convert.FromBase64String(
                value.Replace('-', '+').Replace('_', '/').PadRight((value.Length + 3) / 4 * 4, '=')
            );

        void CheckJwt(HttpRequestMessage request)
        {
            Assert.Equal("Bearer", request.Headers.Authorization?.Scheme);
            var parts = request.Headers.Authorization!.Parameter!.Split('.');
            Assert.Equal(3, parts.Length);
            Assert.True(
                Key.VerifyData(
                    Encoding.ASCII.GetBytes(parts[0] + "." + parts[1]),
                    Decode(parts[2]),
                    HashAlgorithmName.SHA256,
                    RSASignaturePadding.Pkcs1
                )
            );
            using var header = JsonDocument.Parse(Decode(parts[0]));
            using var payload = JsonDocument.Parse(Decode(parts[1]));
            Assert.Equal("RS256", header.RootElement.GetProperty("alg").GetString());
            Assert.Equal("Iv1.test", payload.RootElement.GetProperty("iss").GetString());
            var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            Assert.InRange(payload.RootElement.GetProperty("iat").GetInt64(), now - 90, now - 50);
            Assert.InRange(payload.RootElement.GetProperty("exp").GetInt64(), now + 400, now + 600);
        }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken ct
        )
        {
            Assert.Equal("https", request.RequestUri!.Scheme);
            Assert.Equal("api.github.com", request.RequestUri.Host);
            var path = request.RequestUri.AbsolutePath;
            if (path == "/app" || path.EndsWith("/installation") || path.EndsWith("/access_tokens"))
            {
                CheckJwt(request);
                if (Failure.HasValue)
                    return new(Failure.Value);
                if (path == "/app")
                    return Ok(new { slug = Slug });
                if (path.EndsWith("/installation"))
                {
                    InstallationRequests++;
                    return Ok(
                        new
                        {
                            id = 81,
                            suspended_at = Suspended ? "2026-01-01T00:00:00Z" : null,
                            permissions = new { contents = Permission },
                        }
                    );
                }
                Assert.Equal("/app/installations/81/access_tokens", path);
                Assert.Equal(HttpMethod.Post, request.Method);
                var body = await request.Content!.ReadFromJsonAsync<JsonElement>(ct);
                Assert.Equal(
                    new[] { "app" },
                    body.GetProperty("repositories").EnumerateArray().Select(x => x.GetString())
                );
                Assert.Equal(
                    "read",
                    body.GetProperty("permissions").GetProperty("contents").GetString()
                );
                TokenRequests++;
                return Ok(
                    new { token = "installation-token-" + TokenRequests, expires_at = Expires }
                );
            }
            Assert.Equal("Bearer", request.Headers.Authorization?.Scheme);
            Assert.StartsWith("installation-token-", request.Headers.Authorization!.Parameter);
            if (path.Contains("/contents/"))
            {
                ContentRequests++;
                if (RevokeContents)
                    return new(HttpStatusCode.Unauthorized);
                return Ok(
                    new
                    {
                        type = "file",
                        encoding = "base64",
                        size = 6,
                        content = Convert.ToBase64String(Encoding.UTF8.GetBytes("source")),
                    }
                );
            }
            if (path.Contains("/branches/"))
                return Ok(new { name = "main" });
            return Ok(
                new
                {
                    @private = Private,
                    html_url = "https://github.com/acme/app",
                    default_branch = "main",
                }
            );
        }

        static HttpResponseMessage Ok(object body) =>
            new(HttpStatusCode.OK) { Content = JsonContent.Create(body) };

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                Key.Dispose();
            base.Dispose(disposing);
        }
    }

    [Fact]
    public async Task SignsJwtScopesTokenCachesConcurrentRequestsAndChecksApp()
    {
        using var remote = new Remote();
        using var auth = remote.Auth();
        Assert.True(auth.Configured);
        Assert.Equal(
            "https://github.com/apps/openhow-test/installations/new",
            auth.InstallationUrl
        );
        await auth.Check(default);
        var results = await Task.WhenAll(
            Enumerable.Range(0, 8).Select(_ => auth.Token("https://github.com/acme/app", default))
        );
        Assert.All(results, token => Assert.Equal("installation-token-1", token));
        Assert.Equal(1, remote.TokenRequests);
        auth.Invalidate("https://github.com/ACME/app");
        Assert.Equal(
            "installation-token-2",
            await auth.Token("https://github.com/acme/app", default)
        );
        Assert.Equal(2, remote.TokenRequests);
    }

    [Fact]
    public async Task AuthenticatedPreviewRechecksPublicVisibilityAndInvalidatesRejectedTokens()
    {
        using var remote = new Remote();
        using var auth = remote.Auth();
        var client = remote.Client(auth);
        await client.Verify("https://github.com/acme/app", null, default);
        await client.File("https://github.com/acme/app", "main", "source.ts", 1, default);
        Assert.Equal(1, remote.ContentRequests);
        remote.Private = true;
        var ex = await Assert.ThrowsAsync<InvalidDataException>(() =>
            client.File("https://github.com/acme/app", "main", "source.ts", 1, default)
        );
        Assert.Contains("Only public", ex.Message);
        Assert.Equal(1, remote.ContentRequests);
        remote.Private = false;
        remote.RevokeContents = true;
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            client.File("https://github.com/acme/app", "main", "source.ts", 1, default)
        );
        remote.RevokeContents = false;
        await client.File("https://github.com/acme/app", "main", "source.ts", 1, default);
        Assert.Equal(2, remote.TokenRequests);
    }

    [Theory]
    [InlineData(401, "credentials")]
    [InlineData(403, "restricted")]
    [InlineData(404, "Install")]
    [InlineData(422, "selected repositories")]
    [InlineData(429, "rate limited")]
    [InlineData(302, "unavailable")]
    public async Task RemoteFailuresNeverFallBackToAnonymousOrExposeCredentials(
        int code,
        string message
    )
    {
        using var remote = new Remote { Failure = (HttpStatusCode)code };
        using var auth = remote.Auth();
        var ex = await Assert.ThrowsAsync<InvalidDataException>(() =>
            auth.Token("https://github.com/acme/app", default)
        );
        Assert.Contains(message, ex.Message);
        Assert.DoesNotContain("PRIVATE KEY", ex.Message);
        Assert.DoesNotContain("installation-token", ex.Message);
        Assert.Equal(0, remote.TokenRequests);
    }

    [Theory]
    [InlineData("GitHub:App:PrivateKeyPem", null)]
    [InlineData("GitHub:App:PrivateKeyPem", "invalid-private-key")]
    [InlineData("GitHub:App:ClientId", "")]
    [InlineData("GitHub:App:Slug", "../evil")]
    [InlineData("GitHub:AuthenticationMode", "unexpected")]
    public async Task InvalidConfigurationFailsClosed(string key, string? value)
    {
        using var remote = new Remote();
        using var auth = remote.Auth(new Dictionary<string, string?> { [key] = value });
        Assert.False(auth.Configured);
        Assert.Null(auth.InstallationUrl);
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            auth.Token("https://github.com/acme/app", default)
        );
        Assert.Equal(0, remote.InstallationRequests);
    }

    [Fact]
    public async Task PublicModeDoesNotUseAppCredentials()
    {
        using var remote = new Remote();
        using var auth = remote.Auth(
            new Dictionary<string, string?>
            {
                ["GitHub:AuthenticationMode"] = "Public",
                ["GitHub:App:PrivateKeyPem"] = null,
            }
        );
        Assert.True(auth.Configured);
        Assert.Null(await auth.Token("https://github.com/acme/app", default));
        Assert.Null(auth.InstallationUrl);
        Assert.Equal(0, remote.InstallationRequests);
    }

    [Fact]
    public async Task SuspendedInstallationMissingPermissionAndExpiredTokenAreRejected()
    {
        using var remote = new Remote { Suspended = true };
        using var auth = remote.Auth();
        Assert.Contains(
            "suspended",
            (
                await Assert.ThrowsAsync<InvalidDataException>(() =>
                    auth.Token("https://github.com/acme/app", default)
                )
            ).Message
        );
        remote.Suspended = false;
        remote.Permission = "none";
        Assert.Contains(
            "Contents",
            (
                await Assert.ThrowsAsync<InvalidDataException>(() =>
                    auth.Token("https://github.com/acme/app", default)
                )
            ).Message
        );
        remote.Permission = "read";
        remote.Expires = DateTimeOffset.UtcNow.AddMinutes(-1);
        Assert.Contains(
            "invalid installation token",
            (
                await Assert.ThrowsAsync<InvalidDataException>(() =>
                    auth.Token("https://github.com/acme/app", default)
                )
            ).Message
        );
    }

    [Fact]
    public async Task AppCheckDetectsMismatchedSlug()
    {
        using var remote = new Remote { Slug = "different-app" };
        using var auth = remote.Auth();
        Assert.Contains(
            "slug",
            (await Assert.ThrowsAsync<InvalidDataException>(() => auth.Check(default))).Message
        );
    }
}
