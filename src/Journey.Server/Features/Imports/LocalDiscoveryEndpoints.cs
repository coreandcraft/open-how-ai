namespace Journey.Server.Features.Imports;

public record DiscoveryRequest(string[] Roots, string[] ExcludeDirectories, bool Consent);

public static class LocalDiscoveryEndpoints
{
    public static void MapLocalDiscovery(this WebApplication app, RouteGroupBuilder api)
    {
        static bool Local(HttpContext ctx) =>
            ctx.Connection.RemoteIpAddress is { } address
            && System.Net.IPAddress.IsLoopback(address);
        api.MapGet(
            "/imports/discovery",
            (Access access, LocalImportDiscovery discovery, HttpContext ctx) =>
            {
                var enabled = Local(ctx) && discovery.Enabled(access.UserId);
                return Results.Ok(
                    new
                    {
                        enabled,
                        roots = enabled ? discovery.Roots() : [],
                        explanation = "Local discovery is optional and restricted to operator-enabled users on this computer. Manual folder import is always available.",
                    }
                );
            }
        );
        api.MapPost(
            "/imports/discovery/scan",
            (
                DiscoveryRequest request,
                Access access,
                LocalImportDiscovery discovery,
                HttpContext ctx
            ) =>
            {
                if (!Local(ctx))
                    throw new UnauthorizedAccessException(
                        "Local discovery requires a loopback connection."
                    );
                return Results.Ok(
                    discovery.Scan(
                        access.UserId,
                        request.Roots,
                        request.ExcludeDirectories,
                        request.Consent
                    )
                );
            }
        );
        api.MapGet(
            "/imports/discovery/files/{id}",
            (string id, Access access, LocalImportDiscovery discovery, HttpContext ctx) =>
            {
                if (!Local(ctx))
                    throw new UnauthorizedAccessException(
                        "Local discovery requires a loopback connection."
                    );
                ctx.Response.Headers.CacheControl = "private, no-store";
                return Results.Stream(
                    discovery.Open(access.UserId, id),
                    "application/octet-stream"
                );
            }
        );
    }
}
