using System.Text.Json;

namespace Journey.Domain;

public class Notification : Record
{
    public string WorkspaceId { get; set; } = "";
    public string UserId { get; set; } = "";
    public string Text { get; set; } = "";
    public string Link { get; set; } = "";
    public bool Read { get; set; }
}

public class ActivityEvent : Record
{
    public string WorkspaceId { get; set; } = "";
    public string ActorId { get; set; } = "";
    public string Text { get; set; } = "";
}

