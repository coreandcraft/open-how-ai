namespace Journey.Domain;

public class ImportedAttachment : Record
{
    public string WorkspaceId { get; set; } = "";
    public string ConversationId { get; set; } = "";
    public string BlockId { get; set; } = "";
    public string Reference { get; set; } = "";
    public string? SourceId { get; set; }
    public string Availability { get; set; } = "missing";
    public string Name { get; set; } = "";
    public string Hash { get; set; } = "";
}

public class SourceWorkspace : Record
{
    public string WorkspaceId { get; set; } = "";
    public string Provider { get; set; } = "";
    public string SourceIdentity { get; set; } = "";
    public string IdentityKey { get; set; } = "";
    public string? ExternalId { get; set; }
    public string? ExternalProjectId { get; set; }
    public string? LocalPath { get; set; }
    public string? RepositoryUrl { get; set; }
    public string? GitBranch { get; set; }
    public string Evidence { get; set; } = "{}";
}

public class AgentExecution : Record
{
    public string WorkspaceId { get; set; } = "";
    public string ConversationId { get; set; } = "";
    public string ExternalCallId { get; set; } = "";
    public string? CallBlockId { get; set; }
    public string? ResultBlockId { get; set; }
    public string? ToolName { get; set; }
    public string Arguments { get; set; } = "";
    public string Result { get; set; } = "";
    public string Status { get; set; } = "unresolved";
}
