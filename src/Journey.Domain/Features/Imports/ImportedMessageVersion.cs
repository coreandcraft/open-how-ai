namespace Journey.Domain;

public class ImportedMessageVersion : Record
{
    public string WorkspaceId { get; set; } = "";
    public string MessageId { get; set; } = "";
    public string ConflictId { get; set; } = "";
    public string Payload { get; set; } = "{}";
    public string Anchors { get; set; } = "[]";
    public string ActorId { get; set; } = "";
}
