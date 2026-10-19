using System.Security.Claims;
using Journey.Domain;
using Journey.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace Journey.Service.Features.Collaboration;

[Authorize]
public class CollaborationHub(AppDb db) : Hub
{
    static readonly System.Collections.Concurrent.ConcurrentDictionary<
        string,
        (string Workspace, string User)
    > Connections = new();

    public async Task Join(string workspaceId)
    {
        var uid = Context.User?.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!await db.Memberships.AnyAsync(x => x.WorkspaceId == workspaceId && x.UserId == uid))
            throw new HubException("Access denied");
        if (Connections.TryGetValue(Context.ConnectionId, out var old))
        {
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, old.Workspace);
        }
        Connections[Context.ConnectionId] = (workspaceId, uid!);
        await Groups.AddToGroupAsync(Context.ConnectionId, workspaceId);
        await Broadcast(workspaceId);
    }

    async Task Broadcast(string workspace) =>
        await Clients
            .Group(workspace)
            .SendAsync(
                "presence",
                Connections
                    .Values.Where(x => x.Workspace == workspace)
                    .Select(x => x.User)
                    .Distinct()
                    .ToArray()
            );

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        if (Connections.TryRemove(Context.ConnectionId, out var old))
            await Broadcast(old.Workspace);
        await base.OnDisconnectedAsync(exception);
    }
}

