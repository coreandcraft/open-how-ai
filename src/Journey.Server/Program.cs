using Journey.Server.Features.Integrations;
using System.IO.Compression;
using System.Security.Claims;
using System.Text.Json;
using Journey.Domain;
using Journey.Service;
using Journey.Data;
using Journey.Data.PostgreSql;
using Journey.Server;

using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);
builder.AddAzureHosting();
builder.Services.AddDbContext<AppDb>(options =>
{
    var connectionString = builder.Configuration.GetConnectionString("Journey");
    var provider = builder.Configuration["Database:Provider"] ?? "Sqlite";
    if (provider.Equals("PostgreSQL", StringComparison.OrdinalIgnoreCase))
        options.UseJourneyPostgreSql(connectionString
            ?? throw new InvalidOperationException("PostgreSQL requires ConnectionStrings:Journey."));
    else if (provider.Equals("Sqlite", StringComparison.OrdinalIgnoreCase))
        options.UseSqlite(connectionString ?? "Data Source=journey.db");
    else
        throw new InvalidOperationException($"Unsupported database provider '{provider}'. Use Sqlite or PostgreSQL.");
});
builder.AddJourneyIdentity();
builder.Services.AddAuthorization();
builder.AddExperimentalMcp();
builder.AddGitHubIntegration();
builder.AddPublicRateLimit();
builder.Services.AddJourneyServices();
builder.Services.AddScoped<ImportPreviewBuilder>();
builder.Services.AddSingleton<ImportPreviewQueue>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<ImportPreviewQueue>());
builder.Services.AddOpenApi();
builder.Services.Configure<Microsoft.AspNetCore.Http.Features.FormOptions>(o =>
    o.MultipartBodyLengthLimit = 52_428_800
);
var app = builder.Build();
app.UseMcpBoundary();
if (!app.Environment.IsDevelopment() && !app.Environment.IsEnvironment("Testing"))
{
    app.UseHsts();
    app.UseHttpsRedirection();
}
app.Use(
    async (ctx, next) =>
    {
        ctx.Response.Headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
        ctx.Response.Headers["X-Content-Type-Options"] = "nosniff";
        if (ctx.Request.Path == "/index.html")
        {
            ctx.Response.Redirect("/", permanent: true);
            return;
        }
        if (ctx.Request.Path.StartsWithSegments("/api"))
        {
            ctx.Response.Headers["X-Robots-Tag"] = "noindex, nofollow";
            ctx.Response.Headers.CacheControl = "no-store";
        }
        try
        {
            if (
                ctx.Request.Path.StartsWithSegments("/api")
                && ctx.Request.Method is not ("GET" or "HEAD" or "OPTIONS")
                && ctx.Request.Headers["X-Journey"] != "1"
            )
            {
                ctx.Response.StatusCode = 400;
                await ctx.Response.WriteAsJsonAsync(
                    new { error = "Missing same-origin request header." }
                );
                return;
            }
            await next();
        }
        catch (ImportSizeException ex)
        {
            ctx.Response.StatusCode = StatusCodes.Status413PayloadTooLarge;
            await ctx.Response.WriteAsJsonAsync(new { error = ex.Message });
        }
        catch (BadHttpRequestException ex) when (ex.StatusCode == StatusCodes.Status413PayloadTooLarge)
        {
            ctx.Response.StatusCode = StatusCodes.Status413PayloadTooLarge;
            await ctx.Response.WriteAsJsonAsync(new { error = "Conversation import request exceeds the allowed size." });
        }
        catch (UnauthorizedAccessException ex)
        {
            ctx.Response.StatusCode = ctx.User.Identity?.IsAuthenticated == true ? 403 : 401;
            await ctx.Response.WriteAsJsonAsync(new { error = ex.Message });
        }
        catch (KeyNotFoundException)
        {
            ctx.Response.StatusCode = 404;
            await ctx.Response.WriteAsJsonAsync(new { error = "Not found" });
        }
        catch (DbUpdateConcurrencyException)
        {
            ctx.Response.StatusCode = 409;
            await ctx.Response.WriteAsJsonAsync(
                new { error = "Another editor saved changes. Reload before saving." }
            );
        }
        catch (Exception ex) when (ex is InvalidDataException or JsonException or ArgumentException)
        {
            ctx.Response.StatusCode = 400;
            await ctx.Response.WriteAsJsonAsync(new { error = ex.Message });
        }
    }
);
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.UseStaticFiles();
app.MapSeo();
app.MapOpenApi();
app.MapHub<CollaborationHub>("/hubs/collaboration");
app.MapGet("/api/health", () => new { status = "ok" });
var api = app.MapGroup("/api").RequireAuthorization();
app.MapAccounts(api);
app.MapMcpManagement(api);
app.MapGitHubIntegration(api);
app.MapExperimentalMcp();
app.MapConversations(api);
app.MapImports(api);
app.MapImportRecovery(api);
app.MapLocalDiscovery(api);
app.MapJourneys(api);
app.MapReview(api);
app.MapTeams(api);
app.MapNotifications(api);
app.MapComparisons(api);
app.MapPublications(api);
app.MapFallback(() => Results.NotFound());
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDb>();
    // Concurrent instances are safe: the PostgreSQL provider takes a table lock around Migrate.
    // Turn this off when migrations are applied out of band from the idempotent script.
    if (app.Configuration.GetValue("Database:MigrateOnStartup", true))
        await db.Database.MigrateAsync();
    else if ((await db.Database.GetPendingMigrationsAsync()).Any())
        throw new InvalidOperationException(
            "Database has pending migrations. Apply the idempotent migration script before starting this version."
        );
    if ((app.Environment.IsDevelopment() || app.Environment.IsEnvironment("Testing")) && app.Configuration.GetValue("SeedDemoData", true))
        await Seed.Run(db);
}
app.Run();

public partial class Program { }
