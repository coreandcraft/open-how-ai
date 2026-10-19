using System.Security.Cryptography;
using System.Text.Json;
using Journey.Data;
using Journey.Domain;
using Journey.Service.Features.Integrations;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Journey.Server.Features.Integrations;

public record CreateMcpConnection(
    string Name,
    string[] JourneyIds,
    bool AllowProposals = false,
    string Mode = "local"
);

public record ReviewProposal(bool Approve, int ExpectedVersion, bool Confirm);

public static class McpManagement
{
    public static void MapMcpManagement(this WebApplication app, RouteGroupBuilder api)
    {
        // Explicit cookie-only management: an MCP token can neither grant itself
        // access nor approve a pending proposal through the browser API.
        var routes = api.MapGroup("/integrations/mcp")
            .RequireAuthorization(p =>
                p.AddAuthenticationSchemes(IdentityConstants.ApplicationScheme)
                    .RequireAuthenticatedUser()
            );
        routes.MapGet(
            "/status",
            () =>
                new
                {
                    enabled = McpSetup.Enabled(app.Configuration),
                    experimental = true,
                    localAvailable = McpSetup.LocalEnvironment(app.Environment),
                    remoteConfigured = McpSetup.RemoteConfigured(app.Configuration),
                    endpoint = "/mcp",
                }
        );
        routes.MapGet(
            "/w/{wid}",
            async (string wid, AppDb db, Access access, CancellationToken ct) =>
            {
                await access.Require(wid);
                var uid = access.UserId;
                var visible = await db
                    .Journeys.Where(j =>
                        j.WorkspaceId == wid && (j.Visibility != "Private" || j.OwnerId == uid)
                    )
                    .Select(j => j.Id)
                    .ToListAsync(ct);
                return Results.Ok(
                    new
                    {
                        connections = await db.Set<McpConnection>()
                            .Where(g => g.UserId == uid && g.WorkspaceId == wid)
                            .OrderByDescending(g => g.CreatedAt)
                            .Select(g => new
                            {
                                g.Id,
                                g.Name,
                                g.JourneyIds,
                                g.AllowProposals,
                                g.Mode,
                                g.Revoked,
                                g.ExpiresAt,
                                g.LastUsedAt,
                            })
                            .ToListAsync(ct),
                        proposals = await db.Set<JourneyProposal>()
                            .Where(p => p.WorkspaceId == wid && visible.Contains(p.JourneyId))
                            .OrderBy(p => p.Status != "pending")
                            .ThenBy(p => p.CreatedAt)
                            .Take(100)
                            .ToListAsync(ct),
                    }
                );
            }
        );
        routes.MapPost(
            "/w/{wid}/connections",
            async (
                string wid,
                CreateMcpConnection input,
                HttpContext ctx,
                AppDb db,
                Access access,
                CancellationToken ct
            ) =>
            {
                if (!McpSetup.Enabled(app.Configuration))
                    return Results.NotFound();
                await access.Require(wid, input.AllowProposals ? "edit" : "read");
                McpJourneyService.ValidateText(input.Name, 80, true);
                if (
                    input.JourneyIds is not { Length: > 0 and <= 100 }
                    || input.JourneyIds.Distinct().Count() != input.JourneyIds.Length
                )
                    throw new InvalidDataException("Select between 1 and 100 distinct journeys.");
                foreach (var id in input.JourneyIds)
                {
                    var j = await access.Journey(id);
                    if (j.WorkspaceId != wid)
                        throw new UnauthorizedAccessException();
                }
                if (input.Mode == "local")
                {
                    if (!McpSetup.Loopback(ctx, app.Environment))
                        throw new UnauthorizedAccessException(
                            "Local credentials are available only on a loopback development server."
                        );
                }
                else if (input.Mode != "oauth" || !McpSetup.RemoteConfigured(app.Configuration))
                    throw new InvalidDataException("Remote OAuth is not configured.");
                var user = await db.Users.SingleAsync(u => u.Id == access.UserId, ct);
                string? token =
                    input.Mode == "local"
                        ? "ohm_" + Convert.ToHexString(RandomNumberGenerator.GetBytes(32))
                        : null;
                var g = new McpConnection
                {
                    UserId = access.UserId,
                    WorkspaceId = wid,
                    Name = input.Name.Trim(),
                    JourneyIds = JsonSerializer.Serialize(input.JourneyIds),
                    AllowProposals = input.AllowProposals,
                    Mode = input.Mode,
                    TokenHash = token == null ? "" : McpSetup.Hash(token),
                    SecurityStamp = user.SecurityStamp ?? "",
                    ExpiresAt = DateTime.UtcNow.AddDays(30),
                };
                db.Add(g);
                await db.SaveChangesAsync(ct);
                return Results.Ok(
                    new
                    {
                        g.Id,
                        token,
                        g.ExpiresAt,
                    }
                );
            }
        );
        routes.MapDelete(
            "/connections/{id}",
            async (string id, AppDb db, Access access, CancellationToken ct) =>
            {
                var g =
                    await db.Set<McpConnection>()
                        .SingleOrDefaultAsync(g => g.Id == id && g.UserId == access.UserId, ct)
                    ?? throw new KeyNotFoundException();
                g.Revoked = true;
                await db.SaveChangesAsync(ct);
                return Results.NoContent();
            }
        );
        routes.MapPost(
            "/proposals/{id}/review",
            async (
                string id,
                ReviewProposal input,
                McpJourneyService service,
                CancellationToken ct
            ) =>
            {
                if (!McpSetup.Enabled(app.Configuration))
                    return Results.NotFound();
                if (!input.Confirm)
                    throw new InvalidDataException("Explicit confirmation is required.");
                await service.Review(id, input.Approve, input.ExpectedVersion, ct);
                return Results.NoContent();
            }
        );
    }
}
