using Journey.Domain;
using Microsoft.EntityFrameworkCore;

namespace Journey.Data;

public partial class AppDb
{
    private static void ConfigureIntegrations(ModelBuilder b)
    {
        b.Entity<McpConnection>().HasIndex(x => new { x.UserId, x.WorkspaceId });
        b.Entity<JourneyProposal>().HasIndex(x => new { x.WorkspaceId, x.Status });
        b.Entity<JourneyProposal>().Property(x => x.Status).IsConcurrencyToken();
    }
}
