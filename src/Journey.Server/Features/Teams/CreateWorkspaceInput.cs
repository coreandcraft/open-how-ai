using Journey.Domain;

namespace Journey.Server.Features.Teams;

public record CreateWorkspaceInput(string Name, string Type = "Team");
