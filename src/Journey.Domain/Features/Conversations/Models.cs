using System.Text.Json;

namespace Journey.Domain;

public class Conversation : Record
{
    public string WorkspaceId { get; set; } = "";
    public string SourceId { get; set; } = "";
    public string SourceIdentity { get; set; } = "default";
    public string? ApplicationProjectId { get; set; }
    public string? ExternalProjectId { get; set; }
    public string? SourceProjectName { get; set; }
    public string? SourcePath { get; set; }
    public string? ParentConversationExternalId { get; set; }
    public string Provenance { get; set; } = "{}";
    public string Diagnostics { get; set; } = "[]";
    public string SourceKey { get; set; } = "";
    public string Provider { get; set; } = "";
    public string SourceFormat { get; set; } = "";
    public string? ProviderId { get; set; }
    public string Title { get; set; } = "";
    public string? Model { get; set; }
    public DateTime? SourceCreatedAt { get; set; }
    public DateTime? SourceUpdatedAt { get; set; }
    public bool Demo { get; set; }
    public string RawPayload { get; set; } = "";
    public string Metadata { get; set; } = "{}";
    public List<ConversationMessage> Messages { get; set; } = [];
}

public class ConversationMessage : Record
{
    public string ConversationId { get; set; } = "";
    public string? ProviderId { get; set; }
    public string? ParentId { get; set; }
    public string? ExternalParentId { get; set; }
    public string ContentHash { get; set; } = "";
    public int Order { get; set; }
    public string Role { get; set; } = "";
    public string? Model { get; set; }
    public DateTime? Timestamp { get; set; }
    public string Metadata { get; set; } = "{}";
    public List<MessageContentBlock> Blocks { get; set; } = [];
}

public class MessageContentBlock : Record
{
    public string ConversationMessageId { get; set; } = "";
    public int Order { get; set; }
    public string Kind { get; set; } = "text";
    public string Text { get; set; } = "";
    public string Metadata { get; set; } = "{}";
}

public class ConversationBranch : Record
{
    public string ConversationId { get; set; } = "";
    public string MessageId { get; set; } = "";
    public string? ParentId { get; set; }
}

public class ConversationAttachment : Record
{
    public string ConversationId { get; set; } = "";
    public string SourceReference { get; set; } = "";
}

