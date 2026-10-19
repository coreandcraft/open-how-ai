using System.Text.Encodings.Web;
using System.Text.Json;

namespace Journey.Server.Features.Shared;

public static class SeoEndpoints
{
    internal static string? PublicLink(string? value)
    {
        value = value?.Trim();
        if (string.IsNullOrEmpty(value))
            return null;
        if (
            value.StartsWith("/#public/", StringComparison.Ordinal)
            || value.StartsWith("#public/", StringComparison.Ordinal)
        )
            return System.Text.RegularExpressions.Regex.IsMatch(
                value,
                @"^/?#public/[a-zA-Z0-9_-]+$"
            )
                ? value
                : null;
        return
            Uri.TryCreate(value, UriKind.Absolute, out var url)
            && url.Scheme == Uri.UriSchemeHttps
            && string.IsNullOrEmpty(url.UserInfo)
            ? url.AbsoluteUri
            : null;
    }

    public static void MapSeo(this WebApplication app)
    {
        // Only these public links are exposed; never serialize application settings.
        app.MapGet(
                "/api/site/links",
                () =>
                    Results.Ok(
                        new
                        {
                            journeyUrl = PublicLink(app.Configuration["Site:JourneyUrl"]),
                            repositoryUrl = PublicLink(app.Configuration["Site:RepositoryUrl"]),
                        }
                    )
            )
            .AllowAnonymous();
        var origin = app.Configuration["Site:PublicUrl"]?.TrimEnd('/');
        var indexable =
            !string.IsNullOrEmpty(origin)
            && !app.Environment.IsDevelopment()
            && !app.Environment.IsEnvironment("Testing");
        app.MapGet(
            "/robots.txt",
            () =>
                Results.Text(
                    indexable
                        ? $"User-agent: *\nAllow: /\nDisallow: /api/\nDisallow: /hubs/\nDisallow: /signin-google\nDisallow: /signin-facebook\nSitemap: {origin}/sitemap.xml\n"
                        : "User-agent: *\nDisallow: /\n",
                    "text/plain"
                )
        );
        app.MapGet(
            "/sitemap.xml",
            () =>
                Results.Text(
                    "<?xml version=\"1.0\" encoding=\"UTF-8\"?><urlset xmlns=\"http://www.sitemaps.org/schemas/sitemap/0.9\">"
                        + (
                            indexable
                                ? $"<url><loc>{HtmlEncoder.Default.Encode(origin!)}/</loc></url>"
                                : ""
                        )
                        + "</urlset>",
                    "application/xml"
                )
        );
        app.MapGet(
            "/",
            async (HttpContext ctx) =>
            {
                ctx.Response.Headers.CacheControl = "no-cache";
                if (!indexable)
                    ctx.Response.Headers["X-Robots-Tag"] = "noindex, nofollow";
                var file = Path.Combine(app.Environment.WebRootPath, "index.html");
                if (!File.Exists(file))
                    return Results.Problem("Build the web client before starting the app.");
                var html = await File.ReadAllTextAsync(file);
                if (origin is not null)
                {
                    var safe = HtmlEncoder.Default.Encode(origin);
                    var schema = JsonSerializer.Serialize(
                        new Dictionary<string, string>
                        {
                            ["@context"] = "https://schema.org",
                            ["@type"] = "WebSite",
                            ["name"] = "openhow",
                            ["url"] = origin + "/",
                            ["description"] =
                                "Preserve your AI conversations, connect the decisions, and share the story behind your work.",
                        }
                    );
                    html = html.Replace(
                        "</head>",
                        $"<link rel=\"canonical\" href=\"{safe}/\" /><meta property=\"og:url\" content=\"{safe}/\" /><meta property=\"og:image\" content=\"{safe}/social-card.png\" /><meta name=\"twitter:image\" content=\"{safe}/social-card.png\" /><script type=\"application/ld+json\">{schema}</script></head>"
                    );
                }
                return Results.Content(html, "text/html; charset=utf-8");
            }
        );
    }
}
