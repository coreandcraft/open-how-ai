using System.Text.Json;

namespace Journey.Domain;

public class Team : Record
{
    public string Name { get; set; } = "";
}

public class Workspace : Record
{
    public string Name { get; set; } = "";
    public string? TeamId { get; set; }
    public string OwnerId { get; set; } = "";
}

public class WorkspaceMembership : Record
{
    public string WorkspaceId { get; set; } = "";
    public string UserId { get; set; } = "";
    public string Role { get; set; } = "Viewer";
}

public class TeamMember : Record
{
    public string TeamId { get; set; } = "";
    public string UserId { get; set; } = "";
    public string Role { get; set; } = "Viewer";
}

public class Invitation : Record
{
    public string WorkspaceId { get; set; } = "";
    public string Email { get; set; } = "";
    public string Role { get; set; } = "Viewer";
    public string Token { get; set; } = Guid.NewGuid().ToString("N");
    public bool Accepted { get; set; }
}

