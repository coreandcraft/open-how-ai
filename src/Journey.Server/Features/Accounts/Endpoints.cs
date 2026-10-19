using System.IO.Compression;
using System.Security.Claims;
using System.Text.Json;
using Journey.Domain;
using Journey.Data;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Journey.Server.Features.Accounts;

public static class AccountsEndpoints
{
    public static void MapAccounts(this WebApplication app, RouteGroupBuilder api)
    {
        app.MapIdentityAccounts();
        api.MapGet("/me/sanitization", async (AppDb db, Access access) =>
        {
            var user = await db.Users.FindAsync(access.UserId) ?? throw new KeyNotFoundException();
            return Json.Read<ReplacementRule[]>(user.SanitizationRules);
        });
        api.MapPut("/me/sanitization", async (ReplacementRule[] rules, AppDb db, Access access) =>
        {
            ImportSanitization.Validate(rules);
            var user = await db.Users.FindAsync(access.UserId) ?? throw new KeyNotFoundException();
            user.SanitizationRules = Json.Write(rules);
            await db.SaveChangesAsync();
            return Results.NoContent();
        });
        api.MapGet(
            "/me",
            async (AppDb db, Access a) =>
            {
                var u = await db.Users.FindAsync(a.UserId);
                return new
                {
                    u!.Id,
                    u.Name,
                    u.Email,
                };
            }
        );
        api.MapGet(
            "/workspaces",
            async (AppDb db, Access a) =>
                await (
                    from w in db.Workspaces
                    join m in db.Memberships on w.Id equals m.WorkspaceId
                    where m.UserId == a.UserId
                    select new
                    {
                        w.Id,
                        w.Name,
                        w.TeamId,
                        m.Role,
                    }
                ).ToListAsync()
        );
        api.MapPost(
            "/workspaces",
            async (CreateWorkspaceInput input, AppDb db, Access access) =>
            {
                var name = input.Name?.Trim();
                if (string.IsNullOrWhiteSpace(name) || name.Length > 100)
                    throw new InvalidDataException(
                        "Use a workspace name between 1 and 100 characters."
                    );
                if (input.Type is not ("Personal" or "Team"))
                    throw new InvalidDataException("Choose a personal or team workspace.");
                Team? team = input.Type == "Team" ? new Team { Name = name } : null;
                var workspace = new Workspace
                {
                    Name = name,
                    OwnerId = access.UserId,
                    TeamId = team?.Id,
                };
                if (team != null)
                    db.Teams.Add(team);
                db.Workspaces.Add(workspace);
                db.Memberships.Add(
                    new()
                    {
                        WorkspaceId = workspace.Id,
                        UserId = access.UserId,
                        Role = "Owner",
                    }
                );
                if (team != null)
                    db.TeamMembers.Add(
                        new()
                        {
                            TeamId = team.Id,
                            UserId = access.UserId,
                            Role = "Owner",
                        }
                    );
                await db.SaveChangesAsync();
                return Results.Created(
                    "/api/w/" + workspace.Id,
                    new
                    {
                        workspace.Id,
                        workspace.Name,
                        workspace.TeamId,
                        Role = "Owner",
                    }
                );
            }
        );
    }
}
