using Journey.Domain;
using Microsoft.EntityFrameworkCore;

namespace Journey.Data;

public partial class AppDb
{
    private static void ConfigureJourneys(ModelBuilder b)
    {
        b.Entity<global::Journey.Domain.Journey>().Property(x => x.Version).IsConcurrencyToken();
        b.Entity<global::Journey.Domain.Journey>().HasIndex(x => new { x.WorkspaceId, x.CreatedAt, x.Id });
        b.Entity<ComparisonSession>().HasIndex(x => new { x.WorkspaceId, x.CreatedAt, x.Id });
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        RefreshJourneyPreviews();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(
        bool acceptAllChangesOnSuccess,
        CancellationToken cancellationToken = default
    )
    {
        RefreshJourneyPreviews();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    void RefreshJourneyPreviews()
    {
        foreach (var entry in ChangeTracker.Entries<global::Journey.Domain.Journey>())
            if (
                entry.State == EntityState.Added
                || (entry.State == EntityState.Modified && entry.Property(x => x.Graph).IsModified)
            )
                (entry.Entity.Preview, entry.Entity.NodeCount) = GraphPreview.Build(entry.Entity.Graph);
    }
}
