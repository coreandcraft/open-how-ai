using System.ComponentModel;
using System.Text.Json.Nodes;
using Journey.Service.Features.Integrations;
using ModelContextProtocol.Server;

namespace Journey.Server.Features.Integrations;

public record EntryProposalData(
    string JourneyId,
    string Title,
    string Body = "",
    string Kind = "note",
    string? ConversationId = null
);

public record MilestoneProposalData(string Title, string Body = "");

[McpServerToolType]
public sealed class JourneyMcpTools
{
    [
        McpServerTool(Name = "search_journeys", ReadOnly = true, Idempotent = true),
        Description(
            "Search titles and descriptions of journeys explicitly granted to this connection. Returns at most 50 results."
        )
    ]
    public static Task<object> SearchJourneys(
        McpJourneyService service,
        [Description("Search text, at most 200 characters.")] string query,
        CancellationToken ct
    ) => service.SearchJourneys(query, ct);

    [
        McpServerTool(Name = "get_journey", ReadOnly = true, Idempotent = true),
        Description(
            "Read an authorized journey with bounded entries and relationships. Node IDs are qualified as journeyId:nodeId."
        )
    ]
    public static Task<object> GetJourney(
        McpJourneyService service,
        string journeyId,
        CancellationToken ct
    ) => service.GetJourney(journeyId, ct);

    [
        McpServerTool(Name = "get_journey_timeline", ReadOnly = true, Idempotent = true),
        Description(
            "Read the curated reading order of an authorized journey. This is not an execution timeline."
        )
    ]
    public static Task<object> Timeline(
        McpJourneyService service,
        string journeyId,
        CancellationToken ct
    ) => service.Timeline(journeyId, ct);

    [
        McpServerTool(Name = "get_journey_evidence", ReadOnly = true, Idempotent = true),
        Description(
            "Read a journey entry and bounded text/code source excerpts. Raw files, hidden metadata and attachments are not returned."
        )
    ]
    public static Task<object> Evidence(
        McpJourneyService service,
        [Description("Qualified nodeId returned by get_journey.")] string nodeId,
        CancellationToken ct
    ) => service.Evidence(nodeId, ct);

    [
        McpServerTool(Name = "get_related_conversations", ReadOnly = true, Idempotent = true),
        Description(
            "Find conversations connected by explicit edges inside granted journeys. The input conversation must occur in a granted journey."
        )
    ]
    public static Task<object> Related(
        McpJourneyService service,
        string conversationId,
        CancellationToken ct
    ) => service.Related(conversationId, ct);

    [
        McpServerTool(Name = "search_decisions", ReadOnly = true, Idempotent = true),
        Description(
            "Search decision entries inside authorized journeys; returns at most 50 matches."
        )
    ]
    public static Task<object> Decisions(
        McpJourneyService service,
        string query,
        CancellationToken ct
    ) => service.Decisions(query, ct);

    [
        McpServerTool(
            Name = "propose_journey_entry",
            ReadOnly = false,
            Destructive = false,
            Idempotent = false
        ),
        Description(
            "Create a pending entry proposal. A signed-in user must review and approve it in openhow before the journey changes. Never publishes."
        )
    ]
    public static Task<object> Entry(
        McpJourneyService service,
        EntryProposalData data,
        CancellationToken ct
    ) =>
        service.Propose(
            data.JourneyId,
            "entry",
            new JsonObject
            {
                ["title"] = data.Title,
                ["body"] = data.Body,
                ["kind"] = data.Kind,
                ["conversationId"] = data.ConversationId,
            },
            ct
        );

    [
        McpServerTool(
            Name = "suggest_relationship",
            ReadOnly = false,
            Destructive = false,
            Idempotent = false
        ),
        Description(
            "Propose a relationship between two qualified node IDs in the same authorized journey. Requires user approval; does not modify the graph."
        )
    ]
    public static async Task<object> Relationship(
        McpJourneyService service,
        string sourceId,
        string targetId,
        CancellationToken ct
    )
    {
        var a = await service.FindNode(sourceId, true, ct);
        var b = await service.FindNode(targetId, true, ct);
        if (a.Journey.Id != b.Journey.Id)
            throw new InvalidDataException("Both nodes must be in the same journey.");
        return await service.Propose(
            a.Journey.Id,
            "relationship",
            new JsonObject
            {
                ["sourceId"] = McpJourneyService.Text(a.Node, "id"),
                ["targetId"] = McpJourneyService.Text(b.Node, "id"),
            },
            ct
        );
    }

    [
        McpServerTool(
            Name = "propose_annotation",
            ReadOnly = false,
            Destructive = false,
            Idempotent = false
        ),
        Description(
            "Propose a plain-text annotation on an authorized node. It remains pending until explicitly approved by a user in openhow."
        )
    ]
    public static async Task<object> Annotation(
        McpJourneyService service,
        string nodeId,
        string text,
        CancellationToken ct
    )
    {
        var (j, n) = await service.FindNode(nodeId, true, ct);
        return await service.Propose(
            j.Id,
            "annotation",
            new JsonObject { ["nodeId"] = McpJourneyService.Text(n, "id"), ["text"] = text },
            ct
        );
    }

    [
        McpServerTool(
            Name = "propose_milestone",
            ReadOnly = false,
            Destructive = false,
            Idempotent = false
        ),
        Description(
            "Propose a milestone with a title and plain-text description. Requires user approval and never publishes or silently changes history."
        )
    ]
    public static Task<object> Milestone(
        McpJourneyService service,
        string journeyId,
        MilestoneProposalData data,
        CancellationToken ct
    ) =>
        service.Propose(
            journeyId,
            "milestone",
            new JsonObject { ["title"] = data.Title, ["body"] = data.Body },
            ct
        );
}
