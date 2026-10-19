using System.Net;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Threading.RateLimiting;
using Journey.Data;
using Journey.Domain;
using Journey.Service.Features.Integrations;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using ModelContextProtocol.AspNetCore;
using ModelContextProtocol.AspNetCore.Authentication;

namespace Journey.Server.Features.Integrations;

public static class McpSetup
{
    public const string Scheme = "openhow-mcp",
        LocalScheme = "openhow-mcp-local",
        RemoteScheme = "openhow-mcp-oauth";

    public static bool Enabled(IConfiguration c) => c.GetValue<bool>("Mcp:Enabled");

    public static bool LocalEnvironment(IHostEnvironment e) =>
        e.IsDevelopment() || e.IsEnvironment("Testing");

    public static bool Loopback(HttpContext c, IHostEnvironment e) =>
        LocalEnvironment(e)
        && (
            c.Connection.RemoteIpAddress is { } ip
                ? IPAddress.IsLoopback(ip)
                : e.IsEnvironment("Testing")
        );

    public static bool RemoteConfigured(IConfiguration c) =>
        !string.IsNullOrWhiteSpace(c["Mcp:Authority"])
        && !string.IsNullOrWhiteSpace(c["Mcp:Resource"]);

    public static string Hash(string token) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    public static async Task<bool> ValidGrant(AppDb db, McpConnection? g, CancellationToken ct)
    {
        if (g == null || g.Revoked || g.ExpiresAt <= DateTime.UtcNow)
            return false;
        var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(u => u.Id == g.UserId, ct);
        return user != null
            && user.SecurityStamp == g.SecurityStamp
            && !(user.LockoutEnabled && user.LockoutEnd > DateTimeOffset.UtcNow)
            && await db.Memberships.AnyAsync(
                m => m.UserId == g.UserId && m.WorkspaceId == g.WorkspaceId,
                ct
            );
    }

    public static void AddExperimentalMcp(this WebApplicationBuilder builder)
    {
        builder.Services.AddScoped<McpJourneyService>();
        if (!Enabled(builder.Configuration))
            return;
        var config = builder.Configuration;
        bool remote = RemoteConfigured(config);
        if (!LocalEnvironment(builder.Environment) && !remote)
            throw new InvalidOperationException(
                "Remote MCP requires Mcp:Authority and Mcp:Resource HTTPS URLs."
            );
        if (remote)
        {
            foreach (var key in new[] { "Authority", "Resource" })
                if (
                    !Uri.TryCreate(config["Mcp:" + key], UriKind.Absolute, out var url)
                    || url.Scheme != "https"
                    || !string.IsNullOrEmpty(url.Query)
                    || !string.IsNullOrEmpty(url.Fragment)
                )
                    throw new InvalidOperationException(
                        "Mcp:" + key + " must be a fixed HTTPS URL."
                    );
        }
        var auth = builder.Services.AddAuthentication();
        auth.AddScheme<AuthenticationSchemeOptions, LocalMcpAuthentication>(LocalScheme, _ => { });
        if (remote)
        {
            auth.AddJwtBearer(
                RemoteScheme,
                o =>
                {
                    o.Authority = config["Mcp:Authority"];
                    o.RequireHttpsMetadata = true;
                    o.MapInboundClaims = false;
                    o.TokenValidationParameters = new TokenValidationParameters
                    {
                        ValidateIssuer = true,
                        ValidIssuer = config["Mcp:Authority"],
                        ValidateAudience = true,
                        ValidAudience = config["Mcp:Resource"],
                        ValidateLifetime = true,
                        ValidateIssuerSigningKey = true,
                        RequireSignedTokens = true,
                        RequireExpirationTime = true,
                        ClockSkew = TimeSpan.FromSeconds(30),
                    };
                    o.Events = new JwtBearerEvents
                    {
                        OnTokenValidated = async context =>
                        {
                            var principal = context.Principal!;
                            var subject = principal.FindFirstValue("sub");
                            var userId = config
                                .GetSection("Mcp:SubjectBindings")
                                .GetChildren()
                                .FirstOrDefault(b => b["Subject"] == subject)
                                ?["UserId"];
                            var connectionId = principal.FindFirstValue("openhow_connection_id");
                            var scopes = (principal.FindFirstValue("scope") ?? "").Split(
                                ' ',
                                StringSplitOptions.RemoveEmptyEntries
                            );
                            var db =
                                context.HttpContext.RequestServices.GetRequiredService<AppDb>();
                            var grant = await db.Set<McpConnection>()
                                .AsNoTracking()
                                .SingleOrDefaultAsync(
                                    g => g.Id == connectionId,
                                    context.HttpContext.RequestAborted
                                );
                            if (
                                string.IsNullOrEmpty(subject)
                                || string.IsNullOrEmpty(userId)
                                || grant?.UserId != userId
                                || grant.Mode != "oauth"
                                || !scopes.Contains("mcp:read")
                                || !await ValidGrant(db, grant, context.HttpContext.RequestAborted)
                            )
                            {
                                context.Fail("MCP grant is unavailable.");
                                return;
                            }
                            // Replace application identity claims; never trust token-supplied local IDs.
                            var identity = (ClaimsIdentity)principal.Identity!;
                            foreach (
                                var claim in identity
                                    .Claims.Where(c =>
                                        c.Type == ClaimTypes.NameIdentifier
                                        || c.Type == McpJourneyService.ConnectionClaim
                                        || c.Type == "openhow_propose"
                                    )
                                    .ToArray()
                            )
                                identity.RemoveClaim(claim);
                            identity.AddClaim(new(ClaimTypes.NameIdentifier, userId));
                            identity.AddClaim(new(McpJourneyService.ConnectionClaim, grant.Id));
                            if (grant.AllowProposals && scopes.Contains("mcp:propose"))
                                identity.AddClaim(new("openhow_propose", "true"));
                            await db.Set<McpConnection>()
                                .Where(g => g.Id == grant.Id)
                                .ExecuteUpdateAsync(s =>
                                    s.SetProperty(g => g.LastUsedAt, DateTime.UtcNow)
                                );
                        },
                    };
                }
            );
            auth.AddMcp(o =>
            {
                o.ResourceMetadata = new()
                {
                    Resource = config["Mcp:Resource"]!,
                    AuthorizationServers = { config["Mcp:Authority"]! },
                    ScopesSupported = ["mcp:read", "mcp:propose"],
                };
            });
        }
        auth.AddPolicyScheme(
            Scheme,
            Scheme,
            o =>
            {
                o.ForwardDefaultSelector = c =>
                    c.Request.Headers.Authorization.ToString()
                        .StartsWith("Bearer ohm_", StringComparison.Ordinal)
                    && Loopback(c, builder.Environment)
                        ? LocalScheme
                    : remote ? RemoteScheme
                    : LocalScheme;
                o.ForwardChallenge = remote
                    ? McpAuthenticationDefaults.AuthenticationScheme
                    : LocalScheme;
            }
        );
        builder.Services.AddAuthorization(o =>
            o.AddPolicy(
                "mcp",
                p =>
                    p.AddAuthenticationSchemes(Scheme)
                        .RequireAuthenticatedUser()
                        .RequireClaim(McpJourneyService.ConnectionClaim)
            )
        );
        builder.Services.AddRateLimiter(o =>
            o.AddPolicy(
                "mcp",
                c =>
                    RateLimitPartition.GetFixedWindowLimiter(
                        c.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                        _ => new FixedWindowRateLimiterOptions
                        {
                            PermitLimit = LocalEnvironment(builder.Environment) ? 600 : 120,
                            Window = TimeSpan.FromMinutes(1),
                            QueueLimit = 0,
                        }
                    )
            )
        );
        builder
            .Services.AddMcpServer()
            .WithHttpTransport(o => o.SessionMode = HttpServerSessionMode.Stateless)
            .WithTools<JourneyMcpTools>();
    }

    public static void UseMcpBoundary(this WebApplication app)
    {
        app.Use(
            async (ctx, next) =>
            {
                if (!ctx.Request.Path.StartsWithSegments("/mcp"))
                {
                    await next();
                    return;
                }
                ctx.Response.Headers.CacheControl = "no-store";
                ctx.Response.Headers["X-Robots-Tag"] = "noindex, nofollow";
                if (!Enabled(app.Configuration))
                {
                    ctx.Response.StatusCode = 404;
                    return;
                }
                if (!ctx.Request.IsHttps && !Loopback(ctx, app.Environment))
                {
                    ctx.Response.StatusCode = 403;
                    return;
                }
                if (ctx.Request.Headers.Origin.Count > 0)
                {
                    var origin = ctx.Request.Headers.Origin.ToString();
                    var allowed =
                        app.Configuration.GetSection("Mcp:AllowedOrigins").Get<string[]>() ?? [];
                    if (!allowed.Contains(origin, StringComparer.Ordinal))
                    {
                        ctx.Response.StatusCode = 403;
                        return;
                    }
                }
                if (ctx.Request.ContentLength > 65536)
                {
                    ctx.Response.StatusCode = 413;
                    return;
                }
                var size = ctx.Features.Get<IHttpMaxRequestBodySizeFeature>();
                if (size is { IsReadOnly: false })
                    size.MaxRequestBodySize = 65536;
                await next();
            }
        );
    }

    public static void MapExperimentalMcp(this WebApplication app)
    {
        if (Enabled(app.Configuration))
            app.MapMcp("/mcp").RequireAuthorization("mcp").RequireRateLimiting("mcp");
    }
}

public sealed class LocalMcpAuthentication(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    AppDb db,
    IHostEnvironment environment
) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!McpSetup.Loopback(Context, environment))
            return AuthenticateResult.Fail("Local MCP credentials require loopback development.");
        var header = Request.Headers.Authorization.ToString();
        if (!header.StartsWith("Bearer ohm_", StringComparison.Ordinal) || header.Length > 200)
            return AuthenticateResult.NoResult();
        var hash = McpSetup.Hash(header[7..]);
        var grant = await db.Set<McpConnection>()
            .AsNoTracking()
            .SingleOrDefaultAsync(
                g => g.Mode == "local" && g.TokenHash == hash,
                Context.RequestAborted
            );
        if (!await McpSetup.ValidGrant(db, grant, Context.RequestAborted))
            return AuthenticateResult.Fail("Connection is unavailable.");
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, grant!.UserId),
            new(McpJourneyService.ConnectionClaim, grant.Id),
        };
        if (grant.AllowProposals)
            claims.Add(new("openhow_propose", "true"));
        await db.Set<McpConnection>()
            .Where(g => g.Id == grant.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(g => g.LastUsedAt, DateTime.UtcNow));
        return AuthenticateResult.Success(
            new AuthenticationTicket(
                new ClaimsPrincipal(new ClaimsIdentity(claims, Scheme.Name)),
                Scheme.Name
            )
        );
    }

    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = 401;
        Response.Headers.WWWAuthenticate = "Bearer realm=\"openhow-local-mcp\"";
        return Task.CompletedTask;
    }
}
