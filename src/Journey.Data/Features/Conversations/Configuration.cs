using Journey.Domain;
using Microsoft.EntityFrameworkCore;

namespace Journey.Data;

public partial class AppDb
{
    private static void ConfigureConversations(ModelBuilder b)
    {
        b.Entity<Conversation>().HasIndex(x => new { x.WorkspaceId, x.SourceKey }).IsUnique();
        b.Entity<Conversation>().HasIndex(x => new { x.WorkspaceId, x.CreatedAt, x.Id });
        b.Entity<ConversationMessage>().HasIndex(x => new { x.ConversationId, x.Order });
        b.Entity<Conversation>()
            .HasMany(x => x.Messages)
            .WithOne()
            .HasForeignKey(x => x.ConversationId)
            .OnDelete(DeleteBehavior.Cascade);
        b.Entity<ConversationMessage>()
            .HasMany(x => x.Blocks)
            .WithOne()
            .HasForeignKey(x => x.ConversationMessageId)
            .OnDelete(DeleteBehavior.Cascade);
        b.Entity<ConversationBranch>();
        b.Entity<ConversationAttachment>();
    }
}
