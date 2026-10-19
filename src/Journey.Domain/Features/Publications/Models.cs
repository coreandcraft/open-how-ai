using System.Text.Json;

namespace Journey.Domain;

public class JourneyPublication : Record
{
    public string? JourneyId { get; set; }
    public string? ConversationId { get; set; }
    public string WorkspaceId { get; set; } = "";
    public string Slug { get; set; } = Guid.NewGuid().ToString("N");
    public string Visibility { get; set; } = "Unlisted";
    public bool DiscussionEnabled { get; set; }
    public string? CreatedById { get; set; }
    public string Snapshot { get; set; } = "{}";
}

public record PublicationSnapshot(string Title, string Description, object[] Steps);


public class PublicationComment : Record
{
    public string PublicationId { get; set; } = "";
    public JourneyPublication Publication { get; set; } = null!;
    public string AuthorId { get; set; } = "";
    public User Author { get; set; } = null!;
    public string? ParentId { get; set; }
    public string Kind { get; set; } = "Comment";
    public string Body { get; set; } = "";
}
