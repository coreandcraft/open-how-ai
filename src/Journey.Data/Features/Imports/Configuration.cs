using Journey.Domain;
using Microsoft.EntityFrameworkCore;

namespace Journey.Data;

public partial class AppDb
{
    private static void ConfigureImports(ModelBuilder b)
    {
        b.Entity<ImportPreviewJob>().HasIndex(x => x.ExpiresAt);
        b.Entity<ImportedAttachment>().HasIndex(v => new { v.ConversationId, v.BlockId });
        b.Entity<SourceWorkspace>().HasIndex(v => new { v.WorkspaceId, v.IdentityKey }).IsUnique();
        b.Entity<AgentExecution>()
            .HasIndex(v => new { v.ConversationId, v.ExternalCallId })
            .IsUnique();
        b.Entity<ImportedMessageVersion>().HasIndex(v => new { v.WorkspaceId, v.MessageId });
        b.Entity<ApplicationProject>().HasIndex(x => new { x.WorkspaceId, x.Name });
        b.Entity<SourceProject>()
            .HasIndex(x => new
            {
                x.WorkspaceId,
                x.Provider,
                x.SourceIdentity,
                x.ExternalId,
            })
            .IsUnique();
        b.Entity<ImportConflict>().HasIndex(x => new { x.WorkspaceId, x.Status });
        b.Entity<SourceRelationship>()
            .HasIndex(x => new { x.ConversationId, x.ExternalTargetId })
            .IsUnique();
        b.Entity<Conversation>()
            .HasIndex(x => new
            {
                x.WorkspaceId,
                x.Provider,
                x.SourceIdentity,
                x.ProviderId,
            });
        b.Entity<ImportSource>().HasIndex(x => new { x.WorkspaceId, x.Hash });
        b.Entity<ImportProcessedKey>().HasKey(x => new { x.SourceId, x.Key });
        b.Entity<ImportBatch>().HasIndex(x => x.Status);
    }
}
