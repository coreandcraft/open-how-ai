using Journey.Data;
using Journey.Domain;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using System.Threading.RateLimiting;

namespace Journey.Server.Features.Accounts;

public static class IdentitySetup
{
    public static void AddJourneyIdentity(this WebApplicationBuilder builder)
    {
        var local = builder.Environment.IsDevelopment() || builder.Environment.IsEnvironment("Testing");
        builder.Services.AddIdentity<User, IdentityRole>(o =>
        {
            o.User.RequireUniqueEmail = true;
            o.Password.RequiredLength = 10;
            o.SignIn.RequireConfirmedEmail = true;
            o.Lockout.MaxFailedAccessAttempts = 5;
            o.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
        }).AddEntityFrameworkStores<AppDb>().AddDefaultTokenProviders();
        builder.Services.Configure<DataProtectionTokenProviderOptions>(o => o.TokenLifespan = TimeSpan.FromHours(2));
        builder.Services.ConfigureApplicationCookie(o =>
        {
            o.Cookie.Name = "journey.identity";
            o.Cookie.HttpOnly = true;
            o.Cookie.SameSite = SameSiteMode.Lax;
            o.Cookie.SecurePolicy = local ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
            o.ExpireTimeSpan = TimeSpan.FromHours(12);
            // Check revocation on every request without renewing the cookie on every
            // response: an in-flight request must not restore a just-cleared session.
            o.Events.OnValidatePrincipal = async context =>
            {
                var signIn = context.HttpContext.RequestServices.GetRequiredService<SignInManager<User>>();
                if (await signIn.ValidateSecurityStampAsync(context.Principal) is null)
                {
                    context.RejectPrincipal();
                    await signIn.SignOutAsync();
                }
            };
            o.Events.OnRedirectToLogin = c => { c.Response.StatusCode = 401; return Task.CompletedTask; };
            o.Events.OnRedirectToAccessDenied = c => { c.Response.StatusCode = 403; return Task.CompletedTask; };
        });
        var auth = builder.Services.AddAuthentication();
        if (!string.IsNullOrWhiteSpace(builder.Configuration["Authentication:Google:ClientId"]) &&
            !string.IsNullOrWhiteSpace(builder.Configuration["Authentication:Google:ClientSecret"]))
            auth.AddGoogle(o =>
            {
                o.ClientId = builder.Configuration["Authentication:Google:ClientId"]!;
                o.ClientSecret = builder.Configuration["Authentication:Google:ClientSecret"]!;
                o.SignInScheme = IdentityConstants.ExternalScheme;
                o.Events.OnRemoteFailure = c => { c.HandleResponse(); c.Response.Redirect("/#login?error=external"); return Task.CompletedTask; };
            });
        if (!string.IsNullOrWhiteSpace(builder.Configuration["Authentication:Facebook:AppId"]) &&
            !string.IsNullOrWhiteSpace(builder.Configuration["Authentication:Facebook:AppSecret"]))
            auth.AddFacebook(o =>
            {
                o.AppId = builder.Configuration["Authentication:Facebook:AppId"]!;
                o.AppSecret = builder.Configuration["Authentication:Facebook:AppSecret"]!;
                o.SignInScheme = IdentityConstants.ExternalScheme;
                o.Events.OnRemoteFailure = c => { c.HandleResponse(); c.Response.Redirect("/#login?error=external"); return Task.CompletedTask; };
            });
        builder.Services.AddScoped<AccountEmail>();
        builder.Services.AddRateLimiter(o =>
        {
            o.RejectionStatusCode = 429;
            o.AddPolicy("accounts", ctx => RateLimitPartition.GetFixedWindowLimiter(
                ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = builder.Environment.IsEnvironment("Testing") ? 10000 : 30,
                    Window = TimeSpan.FromMinutes(1), QueueLimit = 0
                }));
        });
        if (!local)
        {
            var origin = builder.Configuration["Site:PublicUrl"];
            if (!Uri.TryCreate(origin, UriKind.Absolute, out var uri) || uri.Scheme != "https" || uri.AbsolutePath != "/" || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
                throw new InvalidOperationException("Configure Site:PublicUrl as the public HTTPS origin.");
            if (string.IsNullOrWhiteSpace(builder.Configuration["Email:Host"]) || string.IsNullOrWhiteSpace(builder.Configuration["Email:From"]))
                throw new InvalidOperationException("Configure Email:Host and Email:From for account verification and recovery.");
        }
    }
}
