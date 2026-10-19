using System.IO.Compression;
using System.Security.Claims;
using System.Text.Json;
using Journey.Domain;
using Journey.Data;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Journey.Server.Features.Notifications;

public static class NotificationsEndpoints
{
    public static void MapNotifications(this WebApplication app, RouteGroupBuilder api)
    {
        api.MapGet(
            "/notifications",
            async (AppDb db, Access a) =>
                await db
                    .Notifications.Where(x =>
                        x.UserId == a.UserId
                        && db.Memberships.Any(m =>
                            m.UserId == a.UserId && m.WorkspaceId == x.WorkspaceId
                        )
                    )
                    .OrderByDescending(x => x.CreatedAt)
                    .ToListAsync()
        );
        api.MapPatch(
            "/notifications/{id}",
            async (string id, AppDb db, Access a) =>
            {
                var n =
                    await db.Notifications.SingleOrDefaultAsync(x =>
                        x.Id == id && x.UserId == a.UserId
                    ) ?? throw new KeyNotFoundException();
                n.Read = !n.Read;
                await db.SaveChangesAsync();
                return n;
            }
        );
    }
}
