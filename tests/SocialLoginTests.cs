using System.Net;
using System.Net.Http.Json;
using System.Text;
using Journey.Domain;
using Microsoft.AspNetCore.Authentication.Facebook;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Journey.Tests;

public class SocialLoginTests(Factory factory) : IClassFixture<Factory>
{
    [Theory]
    [InlineData("Google", "/signin-google")]
    [InlineData("Facebook", "/signin-facebook")]
    public async Task ProviderRegistrationRequiresEmailAndHonorsMfa(string provider, string callback)
    {
        var email = Guid.NewGuid() + "@example.test";
        var providerKey = Guid.NewGuid().ToString();
        using var app = factory.WithWebHostBuilder(b => b.ConfigureAppConfiguration((_, c) => c.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Authentication:Google:ClientId"] = "test-client",
            ["Authentication:Google:ClientSecret"] = "test-secret",
            ["Authentication:Facebook:AppId"] = "test-client",
            ["Authentication:Facebook:AppSecret"] = "test-secret"
        })).ConfigureServices(s =>
        {
            // WebApplicationFactory applies its configuration after top-level service setup.
            s.AddAuthentication().AddGoogle(o =>
            {
                o.ClientId = "test-client"; o.ClientSecret = "test-secret";
                o.SignInScheme = IdentityConstants.ExternalScheme;
                o.Events.OnRemoteFailure = c => { c.HandleResponse(); c.Response.Redirect("/#login?error=external"); return Task.CompletedTask; };
            }).AddFacebook(o =>
            {
                o.AppId = "test-client"; o.AppSecret = "test-secret";
                o.SignInScheme = IdentityConstants.ExternalScheme;
                o.Events.OnRemoteFailure = c => { c.HandleResponse(); c.Response.Redirect("/#login?error=external"); return Task.CompletedTask; };
            });
            s.PostConfigure<GoogleOptions>("Google", o => o.Backchannel = new HttpClient(new ProviderStub(email, providerKey)));
            s.PostConfigure<FacebookOptions>("Facebook", o => o.Backchannel = new HttpClient(new ProviderStub(email, providerKey)));
        }));
        using var client = app.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Add("X-Journey", "1");
        async Task<HttpResponseMessage> External()
        {
            var challenge = await client.GetAsync("/api/auth/external/" + provider + "?returnTo=https://evil.example/");
            Assert.Equal(HttpStatusCode.Redirect, challenge.StatusCode);
            var state = QueryHelpers.ParseQuery(challenge.Headers.Location!.Query)["state"];
            var response = await client.GetAsync(callback + "?code=test-code&state=" + Uri.EscapeDataString(state.ToString()));
            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
            return await client.GetAsync(response.Headers.Location);
        }
        var first = await External();
        Assert.Equal("/#login?message=confirm", first.Headers.Location!.ToString());
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/me")).StatusCode);
        var link = app.Services.GetRequiredService<TestMail>().Links[email];
        var query = QueryHelpers.ParseQuery(new Uri(link).Fragment.Split('?')[1]);
        (await client.PostAsJsonAsync("/api/auth/confirm-email", new { userId = query["userId"].ToString(), code = query["code"].ToString() })).EnsureSuccessStatusCode();
        var signedIn = await External();
        Assert.Equal("/#dashboard", signedIn.Headers.Location!.ToString());
        (await client.GetAsync("/api/me")).EnsureSuccessStatusCode();
        using (var scope = app.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
            var user = (await users.FindByEmailAsync(email))!;
            Assert.True((await users.ResetAuthenticatorKeyAsync(user)).Succeeded);
            Assert.True((await users.SetTwoFactorEnabledAsync(user, true)).Succeeded);
        }
        await client.PostAsJsonAsync("/api/logout", new { });
        var mfa = await External();
        Assert.StartsWith("/#two-factor?", mfa.Headers.Location!.ToString());
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/me")).StatusCode);
        // Missing correlation/state is rejected by the provider middleware.
        var invalid = await client.GetAsync(callback + "?code=forged&state=invalid");
        Assert.Equal("/#login?error=external", invalid.Headers.Location!.ToString());
    }

    private class ProviderStub(string email, string id) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var token = request.RequestUri!.AbsolutePath.Contains("token", StringComparison.OrdinalIgnoreCase);
            var json = token ? "{\"access_token\":\"test-token\",\"token_type\":\"Bearer\",\"expires_in\":3600}" : System.Text.Json.JsonSerializer.Serialize(new { id, sub = id, email, name = "Social tester", verified_email = true });
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") });
        }
    }
}
