using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using Journey.Data;
using Journey.Domain;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using ModelContextProtocol.Client;

namespace Journey.Tests;

public class McpOAuthTests
{
    const string Issuer = "https://identity.example",
        Resource = "https://openhow.example/mcp";
    static readonly SymmetricSecurityKey SigningKey = new(
        Encoding.UTF8.GetBytes("test-only-signing-key-must-be-at-least-32-bytes-long")
    );

    [Fact]
    public async Task RemoteTokensRequireIssuerAudienceSubjectGrantAndScope()
    {
        using var root = new Factory();
        using var app = root.WithWebHostBuilder(b =>
            b.UseSetting("Mcp:Enabled", "true")
                .UseSetting("Mcp:Authority", Issuer)
                .UseSetting("Mcp:Resource", Resource)
                .ConfigureAppConfiguration(
                    (_, c) =>
                        c.AddInMemoryCollection(
                            new Dictionary<string, string?>
                            {
                                ["Mcp:Enabled"] = "true",
                                ["Mcp:Authority"] = Issuer,
                                ["Mcp:Resource"] = Resource,
                                ["Mcp:SubjectBindings:0:Subject"] = "external-alex",
                                ["Mcp:SubjectBindings:0:UserId"] = "alex",
                            }
                        )
                )
                .ConfigureServices(services =>
                    services.PostConfigure<JwtBearerOptions>(
                        "openhow-mcp-oauth",
                        o =>
                        {
                            var discovery = new OpenIdConnectConfiguration { Issuer = Issuer };
                            discovery.SigningKeys.Add(SigningKey);
                            o.ConfigurationManager =
                                new StaticConfigurationManager<OpenIdConnectConfiguration>(
                                    discovery
                                );
                        }
                    )
                )
        );
        using var browser = app.CreateClient(new() { BaseAddress = new Uri("https://localhost") });
        browser.DefaultRequestHeaders.Add("X-Journey", "1");
        (
            await browser.PostAsJsonAsync(
                "/api/auth/login",
                new { email = "alex@journey.local", password = "JourneyDemo!2026" }
            )
        ).EnsureSuccessStatusCode();
        string jid,
            gid;
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDb>();
            var user = await db.Users.FindAsync("alex");
            var j = new Journey.Domain.Journey
            {
                WorkspaceId = "cloudplay",
                OwnerId = "alex",
                Title = "OAuth journey",
            };
            db.Add(j);
            jid = j.Id;
            var g = new McpConnection
            {
                WorkspaceId = "cloudplay",
                UserId = "alex",
                JourneyIds = System.Text.Json.JsonSerializer.Serialize(new[] { jid }),
                Mode = "oauth",
                SecurityStamp = user!.SecurityStamp!,
                ExpiresAt = DateTime.UtcNow.AddHours(1),
                AllowProposals = true,
            };
            db.Add(g);
            gid = g.Id;
            await db.SaveChangesAsync();
        }
        string Token(
            string issuer = Issuer,
            string audience = Resource,
            string subject = "external-alex",
            string scope = "mcp:read",
            string? grant = null,
            bool expired = false,
            SecurityKey? key = null
        ) =>
            new JwtSecurityTokenHandler().WriteToken(
                new JwtSecurityToken(
                    issuer,
                    audience,
                    new[]
                    {
                        new Claim("sub", subject),
                        new Claim("scope", scope),
                        new Claim("openhow_connection_id", grant ?? gid),
                    },
                    DateTime.UtcNow.AddMinutes(-10),
                    expired ? DateTime.UtcNow.AddMinutes(-2) : DateTime.UtcNow.AddMinutes(10),
                    new SigningCredentials(key ?? SigningKey, SecurityAlgorithms.HmacSha256)
                )
            );
        using var client = app.CreateClient(
            new() { BaseAddress = new Uri("https://localhost"), HandleCookies = false }
        );
        async Task<HttpStatusCode> Probe(string? token)
        {
            client.DefaultRequestHeaders.Authorization =
                token == null ? null : new("Bearer", token);
            return (
                await client.PostAsJsonAsync(
                    "/mcp",
                    new
                    {
                        jsonrpc = "2.0",
                        id = 1,
                        method = "tools/list",
                    }
                )
            ).StatusCode;
        }
        Assert.Equal(HttpStatusCode.Unauthorized, await Probe(null));
        var challenge = await client.PostAsJsonAsync(
            "/mcp",
            new
            {
                jsonrpc = "2.0",
                id = 1,
                method = "tools/list",
            }
        );
        Assert.Contains("resource_metadata", challenge.Headers.WwwAuthenticate.ToString());
        var metadata = await client.GetStringAsync("/.well-known/oauth-protected-resource/mcp");
        Assert.Contains(Resource, metadata);
        Assert.Contains(Issuer, metadata);
        foreach (
            var token in new[]
            {
                Token(issuer: "https://evil.example"),
                Token(audience: "another-api"),
                Token(subject: "unknown"),
                Token(scope: "mcp:propose"),
                Token(grant: "other-grant"),
                Token(expired: true),
                Token(
                    key: new SymmetricSecurityKey(
                        Encoding.UTF8.GetBytes("another-untrusted-test-key-with-32-bytes")
                    )
                ),
            }
        )
            Assert.Equal(HttpStatusCode.Unauthorized, await Probe(token));
        client.DefaultRequestHeaders.Authorization = new("Bearer", Token());
        await using var mcp = await McpClient.CreateAsync(
            new HttpClientTransport(
                new()
                {
                    Endpoint = new Uri(client.BaseAddress!, "/mcp"),
                    TransportMode = HttpTransportMode.StreamableHttp,
                    EnableStandaloneGetStream = false,
                },
                client
            )
        );
        var read = await mcp.CallToolAsync(
            "get_journey",
            new Dictionary<string, object?> { ["journeyId"] = jid }
        );
        Assert.NotEqual(true, read.IsError);
        var denied = await mcp.CallToolAsync(
            "propose_milestone",
            new Dictionary<string, object?>
            {
                ["journeyId"] = jid,
                ["data"] = new { title = "No proposal scope" },
            }
        );
        Assert.True(denied.IsError);
        client.DefaultRequestHeaders.Authorization = new(
            "Bearer",
            Token(scope: "mcp:read mcp:propose")
        );
        var allowed = await mcp.CallToolAsync(
            "propose_milestone",
            new Dictionary<string, object?>
            {
                ["journeyId"] = jid,
                ["data"] = new { title = "Scoped proposal" },
            }
        );
        Assert.NotEqual(true, allowed.IsError);
    }
}
