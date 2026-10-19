using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Journey.Domain;
using Journey.Server.Features.Accounts;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Journey.Tests;

public class TestMail
{
    public ConcurrentDictionary<string, string> Links { get; } = new();
}
public class TestAccountEmail(IConfiguration configuration, IWebHostEnvironment environment, TestMail mail) : AccountEmail(configuration, environment)
{
    public override Task Send(string to, string subject, string heading, string message, string url, string action)
    {
        mail.Links[to] = url;
        return Task.CompletedTask;
    }
}
public static class AccountTestHelper
{
    public static async Task ConfirmAndLogin(Factory factory, HttpClient client, string email, string password)
    {
        var link = factory.Services.GetRequiredService<TestMail>().Links[email];
        var query = QueryHelpers.ParseQuery(new Uri(link).Fragment.Split('?')[1]);
        (await client.PostAsJsonAsync("/api/auth/confirm-email", new { userId = query["userId"].ToString(), code = query["code"].ToString() })).EnsureSuccessStatusCode();
        (await client.PostAsJsonAsync("/api/auth/login", new { email, password })).EnsureSuccessStatusCode();
    }
}

public class AccountTests(Factory factory) : IClassFixture<Factory>
{
    const string Password = "IdentityTest!2026";
    HttpClient Client()
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Journey", "1");
        return client;
    }
    async Task<string> Register(HttpClient client)
    {
        var email = Guid.NewGuid() + "@example.test";
        (await client.PostAsJsonAsync("/api/auth/register", new { email, password = Password, name = "Account tester" })).EnsureSuccessStatusCode();
        return email;
    }
    [Fact]
    public async Task RegistrationRequiresConfirmationAndResetRevokesSessionsAndTokenReuse()
    {
        using var client = Client();
        var email = await Register(client);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/me")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/auth/login", new { email, password = Password })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/auth/confirm-email", new { userId = "missing", code = "bad" })).StatusCode);
        await AccountTestHelper.ConfirmAndLogin(factory, client, email, Password);
        var session = await client.GetAsync("/api/me");
        Assert.Equal(HttpStatusCode.OK, session.StatusCode);
        Assert.False(session.Headers.Contains("Set-Cookie"));
        Assert.Single((await client.GetFromJsonAsync<JsonElement>("/api/workspaces")).EnumerateArray());
        using var anonymous = Client();
        var known = await anonymous.PostAsJsonAsync("/api/auth/forgot-password", new { email });
        var unknown = await anonymous.PostAsJsonAsync("/api/auth/forgot-password", new { email = "missing@example.test" });
        Assert.Equal(await known.Content.ReadAsStringAsync(), await unknown.Content.ReadAsStringAsync());
        var link = factory.Services.GetRequiredService<TestMail>().Links[email];
        var query = QueryHelpers.ParseQuery(new Uri(link).Fragment.Split('?')[1]);
        var reset = new { email, code = query["code"].ToString(), password = "UpdatedPassword!2026" };
        (await anonymous.PostAsJsonAsync("/api/auth/reset-password", reset)).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/me")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await anonymous.PostAsJsonAsync("/api/auth/reset-password", reset)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PostAsJsonAsync("/api/auth/login", new { email, password = Password })).StatusCode);
        (await anonymous.PostAsJsonAsync("/api/auth/login", new { email = email.ToUpperInvariant(), password = reset.password })).EnsureSuccessStatusCode();
    }
    [Fact]
    public async Task MfaRequiresSecondFactorAndRecoveryCodesAreSingleUse()
    {
        using var client = Client();
        var email = await Register(client);
        await AccountTestHelper.ConfirmAndLogin(factory, client, email, Password);
        var setup = await client.PostAsJsonAsync("/api/auth/mfa/setup", new { password = Password });
        setup.EnsureSuccessStatusCode();
        var key = (await setup.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("sharedKey").GetString()!;
        var enable = await client.PostAsJsonAsync("/api/auth/mfa/enable", new { password = Password, code = Totp(key) });
        enable.EnsureSuccessStatusCode();
        var codes = (await enable.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("recoveryCodes").EnumerateArray().Select(c => c.GetString()!).ToArray();
        Assert.Equal(10, codes.Length);
        using var other = Client();
        var passwordOnly = await other.PostAsJsonAsync("/api/auth/login", new { email, password = Password });
        Assert.True((await passwordOnly.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("requiresTwoFactor").GetBoolean());
        Assert.Equal(HttpStatusCode.Unauthorized, (await other.GetAsync("/api/me")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await other.PostAsJsonAsync("/api/auth/two-factor", new { code = "invalid" })).StatusCode);
        (await other.PostAsJsonAsync("/api/auth/two-factor", new { code = Totp(key) })).EnsureSuccessStatusCode();
        (await other.PostAsJsonAsync("/api/logout", new { })).EnsureSuccessStatusCode();
        (await other.PostAsJsonAsync("/api/auth/login", new { email, password = Password })).EnsureSuccessStatusCode();
        (await other.PostAsJsonAsync("/api/auth/two-factor", new { code = codes[0], recovery = true })).EnsureSuccessStatusCode();
        (await other.PostAsJsonAsync("/api/logout", new { })).EnsureSuccessStatusCode();
        (await other.PostAsJsonAsync("/api/auth/login", new { email, password = Password })).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Unauthorized, (await other.PostAsJsonAsync("/api/auth/two-factor", new { code = codes[0], recovery = true })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/auth/mfa/disable", new { password = Password, code = "wrong" })).StatusCode);
        (await client.PostAsJsonAsync("/api/auth/mfa/disable", new { password = Password, code = Totp(key) })).EnsureSuccessStatusCode();
        Assert.False((await client.GetFromJsonAsync<JsonElement>("/api/auth/mfa")).GetProperty("enabled").GetBoolean());
    }
    [Fact]
    public async Task LoginLocksOutAfterRepeatedFailuresAndPostRequiresHeader()
    {
        using var client = Client();
        var email = await Register(client);
        await AccountTestHelper.ConfirmAndLogin(factory, client, email, Password);
        await client.PostAsJsonAsync("/api/logout", new { });
        for (var i = 0; i < 5; i++)
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/auth/login", new { email, password = "WrongPassword!" })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/auth/login", new { email, password = Password })).StatusCode);
        using var noHeader = factory.CreateClient();
        Assert.Equal(HttpStatusCode.BadRequest, (await noHeader.PostAsJsonAsync("/api/auth/login", new { email, password = Password })).StatusCode);
    }
    [Fact]
    public async Task UnconfiguredProvidersAreHiddenAndUnknownRoutesReturn404()
    {
        using var client = Client();
        Assert.Empty((await client.GetFromJsonAsync<JsonElement>("/api/auth/providers")).GetProperty("providers").EnumerateArray());
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/auth/external/Unknown")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/missing-page")).StatusCode);
        Assert.Contains("Disallow: /", await client.GetStringAsync("/robots.txt"));
        Assert.DoesNotContain("<url>", await client.GetStringAsync("/sitemap.xml"));
    }
    public static string Totp(string key)
    {
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
        var bytes = new List<byte>(); int buffer = 0, bits = 0;
        foreach (var c in key) { buffer = (buffer << 5) | alphabet.IndexOf(c); bits += 5; if (bits >= 8) { bits -= 8; bytes.Add((byte)(buffer >> bits)); } }
        var counter = BitConverter.GetBytes(DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 30);
        if (BitConverter.IsLittleEndian) Array.Reverse(counter);
        var hash = HMACSHA1.HashData(bytes.ToArray(), counter);
        var offset = hash[^1] & 15;
        var value = ((hash[offset] & 127) << 24) | (hash[offset + 1] << 16) | (hash[offset + 2] << 8) | hash[offset + 3];
        return (value % 1000000).ToString("D6");
    }
}
