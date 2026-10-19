using System.Text.Json;

namespace Journey.Domain;

public class Journey : Record
{
    public string WorkspaceId { get; set; } = "";
    public string OwnerId { get; set; } = "";
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public string Visibility { get; set; } = "Private";
    public int Version { get; set; }
    public string Graph { get; set; } = "{\"nodes\":[],\"edges\":[],\"steps\":[]}";

    /// <summary>Small graph excerpt for list cards; derived from <see cref="Graph"/> on save.</summary>
    public string Preview { get; set; } = "";
    public int NodeCount { get; set; }
}

// Graph value objects are persisted atomically in Journey.Graph with an optimistic version token.
public record JourneyNode(string Id, string Type, JsonElement Data, JsonElement Position);

public record JourneyEdge(string Id, string Source, string Target, string? Label);

public record JourneyReadingStep(string NodeId, int Order);

