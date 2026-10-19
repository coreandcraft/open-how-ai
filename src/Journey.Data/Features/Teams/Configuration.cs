using Journey.Domain;
using Microsoft.EntityFrameworkCore;

namespace Journey.Data;

public partial class AppDb
{
    private static void ConfigureTeams(ModelBuilder b)
    {
        b.Entity<WorkspaceMembership>().HasIndex(x => new { x.WorkspaceId, x.UserId }).IsUnique();
    }
}
