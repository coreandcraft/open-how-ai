using Journey.Domain;
using Microsoft.EntityFrameworkCore;

namespace Journey.Data;

public partial class AppDb
{
    private static void ConfigurePublications(ModelBuilder b)
    {
        b.Entity<PublicationComment>().HasOne(c => c.Publication).WithMany().HasForeignKey(c => c.PublicationId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<PublicationComment>().HasOne(c => c.Author).WithMany().HasForeignKey(c => c.AuthorId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<PublicationComment>().HasIndex(c => new { c.PublicationId, c.ParentId, c.CreatedAt });
        b.Entity<PublicationComment>().Property(c => c.Body).HasMaxLength(4000);
        b.Entity<JourneyPublication>().HasIndex(x => x.Slug).IsUnique();
    }
}

