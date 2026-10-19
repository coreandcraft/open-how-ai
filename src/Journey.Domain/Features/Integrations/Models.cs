namespace Journey.Domain;

public class McpConnection : Record
{
    public string UserId { get; set; } = "";
    public string WorkspaceId { get; set; } = "";
    public string Name { get; set; } = "";
    public string JourneyIds { get; set; } = "[]";
    public bool AllowProposals { get; set; }
    public string Mode { get; set; } = "local";
    public string TokenHash { get; set; } = "";
    public string SecurityStamp { get; set; } = "";
    public DateTime ExpiresAt { get; set; }
    public DateTime? LastUsedAt { get; set; }
    public bool Revoked { get; set; }
}

public class JourneyProposal : Record
{
    public string WorkspaceId { get; set; } = "";
    public string JourneyId { get; set; } = "";
    public string ConnectionId { get; set; } = "";
    public string ProposedBy { get; set; } = "";
    public string Kind { get; set; } = "";
    public string Payload { get; set; } = "{}";
    public int BaseVersion { get; set; }
    public string Status { get; set; } = "pending";
    public string? ReviewedBy { get; set; }
    public DateTime? ReviewedAt { get; set; }
}
