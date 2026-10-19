using System.Text.Json;

namespace Journey.Domain;

public class Annotation : Record
{
    public string WorkspaceId { get; set; } = "";
    public string? ConversationId { get; set; }
    public string? MessageId { get; set; }
    public string? JourneyId { get; set; }
    public string? NodeId { get; set; }
    public string AuthorId { get; set; } = "";
    public string Kind { get; set; } = "comment";
    public string Anchor { get; set; } = "{}";
    public string? ComparisonAnchor { get; set; }
    public bool Resolved { get; set; }
    public bool Orphaned { get; set; }
    public List<Comment> Comments { get; set; } = [];
    public List<AnnotationArtifact> Artifacts { get; set; } = [];
}

public class AnnotationArtifact : Record
{
    public string AnnotationId { get; set; } = "";
    public string Name { get; set; } = "";
    public string ContentType { get; set; } = "";
    public long Size { get; set; }
}

public class ArtifactBlob
{
    public string Id { get; set; } = "";
    public byte[] Data { get; set; } = [];
    public string? StorageKey { get; set; }
}

public record AnnotationAnchor(
    string BlockId,
    int Start,
    int End,
    string Quote,
    string Prefix,
    string Suffix,
    string Fingerprint
);

public record CommentThread(string AnnotationId);

public class Comment : Record
{
    public string AnnotationId { get; set; } = "";
    public string AuthorId { get; set; } = "";
    public string Body { get; set; } = "";
    public DateTime? UpdatedAt { get; set; }
}

public class Mention : Record
{
    public string CommentId { get; set; } = "";
    public string UserId { get; set; } = "";
}

