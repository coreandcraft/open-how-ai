using Journey.Domain;
using Microsoft.EntityFrameworkCore;

namespace Journey.Data;

public partial class AppDb
{
    private static void ConfigureReview(ModelBuilder b)
    {
        b.Entity<Annotation>()
            .HasMany(x => x.Comments)
            .WithOne()
            .HasForeignKey(x => x.AnnotationId)
            .OnDelete(DeleteBehavior.Cascade);
        b.Entity<ArtifactBlob>();
        b.Entity<Annotation>()
            .HasMany(x => x.Artifacts)
            .WithOne()
            .HasForeignKey(x => x.AnnotationId);
        b.Entity<Annotation>().HasIndex(x => new { x.WorkspaceId, x.ConversationId });
        b.Entity<Mention>();
    }
}
