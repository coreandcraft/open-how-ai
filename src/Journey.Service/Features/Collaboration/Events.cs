using System.Security.Claims;
using Journey.Domain;
using Journey.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace Journey.Service.Features.Collaboration;

public class Events(AppDb db, IHubContext<CollaborationHub> hub)
{
    public async Task Send(string wid, string uid, string text)
    {
        db.Activities.Add(
            new()
            {
                WorkspaceId = wid,
                ActorId = uid,
                Text = text,
            }
        );
        await db.SaveChangesAsync();
        await hub.Clients.Group(wid).SendAsync("changed", new { text });
    }
}

