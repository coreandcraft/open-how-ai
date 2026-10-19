using Journey.Domain;
using Microsoft.EntityFrameworkCore;

namespace Journey.Data;

public partial class AppDb(DbContextOptions<AppDb> options) : Microsoft.AspNetCore.Identity.EntityFrameworkCore.IdentityDbContext<User>(options)
{
    public DbSet<Team> Teams => Set<Team>();
    public DbSet<Workspace> Workspaces => Set<Workspace>();
    public DbSet<WorkspaceMembership> Memberships => Set<WorkspaceMembership>();
    public DbSet<TeamMember> TeamMembers => Set<TeamMember>();
    public DbSet<Invitation> Invitations => Set<Invitation>();
    public DbSet<ImportBatch> Batches => Set<ImportBatch>();
    public DbSet<ImportSource> Sources => Set<ImportSource>();
    public DbSet<Conversation> Conversations => Set<Conversation>();
    public DbSet<ConversationMessage> Messages => Set<ConversationMessage>();
    public DbSet<Annotation> Annotations => Set<Annotation>();
    public DbSet<Comment> Comments => Set<Comment>();
    public DbSet<global::Journey.Domain.Journey> Journeys => Set<global::Journey.Domain.Journey>();
    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<PublicationComment> PublicationComments => Set<PublicationComment>();
    public DbSet<JourneyPublication> Publications => Set<JourneyPublication>();
    public DbSet<ActivityEvent> Activities => Set<ActivityEvent>();
    public DbSet<ComparisonSession> Comparisons => Set<ComparisonSession>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        base.OnModelCreating(b);
        b.Entity<User>().ToTable("Users");
        ConfigureIntegrations(b);
        ConfigureAccounts(b);
        ConfigureTeams(b);
        ConfigureConversations(b);
        ConfigureImports(b);
        ConfigureJourneys(b);
        ConfigureReview(b);
        ConfigurePublications(b);
        ConfigureNotifications(b);
    }
}
