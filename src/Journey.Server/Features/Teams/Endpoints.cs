using System.IO.Compression;
using System.Security.Claims;
using System.Text.Json;
using Journey.Domain;
using Journey.Data;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Journey.Server.Features.Teams;

public static class TeamsEndpoints
{
    public static void MapTeams(this WebApplication app, RouteGroupBuilder api)
    {
        api.MapGet(
            "/w/{wid}/members",
            async (string wid, AppDb db, Access a) =>
            {
                await a.Require(wid);
                return await (
                    from m in db.Memberships
                    join u in db.Users on m.UserId equals u.Id
                    where m.WorkspaceId == wid
                    select new
                    {
                        u.Id,
                        u.Name,
                        u.Email,
                        m.Role,
                    }
                ).ToListAsync();
            }
        );
        api.MapPost(
            "/w/{wid}/invitations",
            async (string wid, InviteInput input, AppDb db, Access a) =>
            {
                var role = await a.Require(wid, "manage");
                if ((await db.Workspaces.FindAsync(wid))?.TeamId == null)
                    throw new InvalidDataException(
                        "Personal workspaces are private. Create a team workspace to invite people."
                    );
                if (
                    input.Role is not ("Admin" or "Editor" or "Commenter" or "Viewer")
                    || (input.Role == "Admin" && role != "Owner")
                )
                    throw new InvalidDataException("Invalid invitation role");
                var invitation = new Invitation
                {
                    WorkspaceId = wid,
                    Email = input.Email.ToLowerInvariant().Trim(),
                    Role = input.Role,
                };
                db.Invitations.Add(invitation);
                await db.SaveChangesAsync();
                return invitation;
            }
        );
        api.MapPost(
            "/invitations/{token}/accept",
            async (string token, AppDb db, Access a) =>
            {
                var inv =
                    await db.Invitations.SingleOrDefaultAsync(x => x.Token == token && !x.Accepted)
                    ?? throw new KeyNotFoundException();
                if ((await db.Workspaces.FindAsync(inv.WorkspaceId))?.TeamId == null)
                    throw new InvalidDataException("Personal workspaces do not accept members.");
                var u = await db.Users.FindAsync(a.UserId);
                if (u!.Email != inv.Email || inv.CreatedAt < DateTime.UtcNow.AddDays(-7))
                    throw new UnauthorizedAccessException();
                if (
                    !await db.Memberships.AnyAsync(x =>
                        x.WorkspaceId == inv.WorkspaceId && x.UserId == a.UserId
                    )
                )
                    db.Memberships.Add(
                        new()
                        {
                            WorkspaceId = inv.WorkspaceId,
                            UserId = a.UserId,
                            Role = inv.Role,
                        }
                    );
                inv.Accepted = true;
                await db.SaveChangesAsync();
                return Results.Ok();
            }
        );
        api.MapPut(
            "/w/{wid}/members/{uid}",
            async (string wid, string uid, RoleInput input, AppDb db, Access a) =>
            {
                var role = await a.Require(wid, "manage");
                var m =
                    await db.Memberships.SingleOrDefaultAsync(x =>
                        x.WorkspaceId == wid && x.UserId == uid
                    ) ?? throw new KeyNotFoundException();
                if (
                    m.Role == "Owner"
                    || (role != "Owner" && (m.Role == "Admin" || input.Role == "Admin"))
                )
                    throw new UnauthorizedAccessException();
                if (input.Role == "Remove")
                    db.Memberships.Remove(m);
                else if (input.Role is "Admin" or "Editor" or "Commenter" or "Viewer")
                    m.Role = input.Role;
                else
                    throw new InvalidDataException("Invalid role");
                await db.SaveChangesAsync();
                return Results.Ok();
            }
        );
        api.MapPut(
            "/w/{wid}",
            async (string wid, NameInput input, AppDb db, Access a) =>
            {
                await a.Require(wid, "manage");
                if (string.IsNullOrWhiteSpace(input.Name) || input.Name.Trim().Length > 100)
                    throw new InvalidDataException("A workspace name is required.");
                var w = await db.Workspaces.FindAsync(wid) ?? throw new KeyNotFoundException();
                w.Name = input.Name.Trim();
                if (w.TeamId != null)
                {
                    var t = await db.Teams.FindAsync(w.TeamId);
                    if (t != null)
                        t.Name = input.Name.Trim();
                }
                await db.SaveChangesAsync();
                return w;
            }
        );
        api.MapPost(
            "/w/{wid}/leave",
            async (string wid, AppDb db, Access a) =>
            {
                var role = await a.Require(wid);
                if (role == "Owner")
                    throw new InvalidDataException("The owner cannot leave their workspace.");
                var m = await db.Memberships.SingleAsync(x =>
                    x.WorkspaceId == wid && x.UserId == a.UserId
                );
                db.Memberships.Remove(m);
                await db.SaveChangesAsync();
                return Results.Ok();
            }
        );
        api.MapGet(
            "/w/{wid}/activity",
            async (string wid, AppDb db, Access a) =>
            {
                await a.Require(wid);
                return await db
                    .Activities.Where(x => x.WorkspaceId == wid)
                    .OrderByDescending(x => x.CreatedAt)
                    .Take(50)
                    .ToListAsync();
            }
        );
    }
}
