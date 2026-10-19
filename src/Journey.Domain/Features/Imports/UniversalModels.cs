namespace Journey.Domain;

public class ApplicationProject : Record
{
    public string WorkspaceId { get; set; } = "";
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public string? GitHubRepositoryUrl { get; set; }
    public string? GitHubBranch { get; set; }
    public string? GitHubLocalRoot { get; set; }
}

public class SourceProject : Record
{
    public string WorkspaceId { get; set; } = "";
    public string Provider { get; set; } = "";
    public string SourceIdentity { get; set; } = "default";
    public string ExternalId { get; set; } = "";
    public string Name { get; set; } = "";
    public string Metadata { get; set; } = "{}";
    public string SourceId { get; set; } = "";
}

public class ImportConflict : Record
{
    public string BaseHash { get; set; } = "";
    public string WorkspaceId { get; set; } = "";
    public string BatchId { get; set; } = "";
    public string ConversationId { get; set; } = "";
    public string MessageId { get; set; } = "";
    public string Incoming { get; set; } = "{}";
    public string Status { get; set; } = "unresolved";
}

public class SourceRelationship : Record
{
    public string WorkspaceId { get; set; } = "";
    public string ConversationId { get; set; } = "";
    public string? TargetConversationId { get; set; }
    public string ExternalTargetId { get; set; } = "";
    public string Kind { get; set; } = "parent-session";
    public string Evidence { get; set; } = "";
    public string Status { get; set; } = "unresolved";
}
