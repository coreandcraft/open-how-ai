using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Nodes;
using Journey.Data;
using Journey.Domain;
using Microsoft.EntityFrameworkCore;

namespace Journey.Service.Features.Integrations;

public sealed class McpJourneyService(AppDb db, Access access, IHttpContextAccessor http)
{
    public const string ConnectionClaim = "openhow_connection";

    public async Task<McpConnection> Connection(bool write, CancellationToken ct)
    {
        var id = http.HttpContext?.User.FindFirstValue(ConnectionClaim);
        var grant = await db.Set<McpConnection>()
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == id, ct);
        if (
            grant == null
            || grant.UserId != access.UserId
            || grant.Revoked
            || grant.ExpiresAt <= DateTime.UtcNow
        )
            throw new UnauthorizedAccessException("Connection is unavailable.");
        var user = await db
            .Users.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == grant.UserId, ct);
        if (
            user == null
            || user.SecurityStamp != grant.SecurityStamp
            || (user.LockoutEnabled && user.LockoutEnd > DateTimeOffset.UtcNow)
        )
            throw new UnauthorizedAccessException("Connection is unavailable.");
        await access.Require(grant.WorkspaceId, write ? "edit" : "read");
        if (
            write
            && (
                !grant.AllowProposals || !http.HttpContext!.User.HasClaim("openhow_propose", "true")
            )
        )
            throw new UnauthorizedAccessException("This connection permits reading only.");
        return grant;
    }

    public async Task<List<Journey.Domain.Journey>> Journeys(bool write, CancellationToken ct)
    {
        var grant = await Connection(write, ct);
        var ids = JsonSerializer.Deserialize<string[]>(grant.JourneyIds) ?? [];
        return await db
            .Journeys.Where(j =>
                j.WorkspaceId == grant.WorkspaceId
                && ids.Contains(j.Id)
                && (j.Visibility != "Private" || j.OwnerId == grant.UserId)
            )
            .OrderBy(j => j.Id)
            .ToListAsync(ct);
    }

    public async Task<Journey.Domain.Journey> Journey(
        string id,
        bool write,
        CancellationToken ct
    )
    {
        var grant = await Connection(write, ct);
        var ids = JsonSerializer.Deserialize<string[]>(grant.JourneyIds) ?? [];
        return (ids.Contains(id)
                ? await db.Journeys.SingleOrDefaultAsync(
                    j =>
                        j.Id == id
                        && j.WorkspaceId == grant.WorkspaceId
                        && (j.Visibility != "Private" || j.OwnerId == grant.UserId),
                    ct
                )
                : null)
            ?? throw new UnauthorizedAccessException("Journey is unavailable to this connection.");
    }

    public static JsonObject Graph(Journey.Domain.Journey j) =>
        JsonNode.Parse(j.Graph)?.AsObject() ?? new();

    public static IEnumerable<JsonObject> Nodes(JsonObject graph) =>
        (graph["nodes"] as JsonArray ?? []).OfType<JsonObject>();

    public static string Text(JsonNode? node, string key) => node?[key]?.GetValue<string>() ?? "";

    public static string Clip(string value, int max = 8000) =>
        value.Length <= max ? value : value[..max];

    public static string Plain(string value) =>
        System.Net.WebUtility.HtmlDecode(
            System.Text.RegularExpressions.Regex.Replace(value, "<[^>]+>", "")
        );

    public static void ValidateText(string value, int maximum, bool required = false)
    {
        if (
            (required && string.IsNullOrWhiteSpace(value))
            || value == null
            || value.Length > maximum
        )
            throw new InvalidDataException(
                $"Text must be {(required ? "nonempty and " : "")}at most {maximum} characters."
            );
    }

    private async Task<List<JsonObject>> SafeNodes(Journey.Domain.Journey j, CancellationToken ct) =>
        (await SafeNodes([j], ct))[0].Nodes;

    /// <summary>Nodes whose linked conversation is still in the journey's workspace, checked in one query.</summary>
    private async Task<List<(Journey.Domain.Journey Journey, List<JsonObject> Nodes)>> SafeNodes(
        List<Journey.Domain.Journey> journeys,
        CancellationToken ct
    )
    {
        var graphs = journeys.Select(j => (Journey: j, Nodes: Nodes(Graph(j)).ToList())).ToList();
        var ids = graphs
            .SelectMany(g => g.Nodes)
            .Select(n => Text(n["data"], "conversationId"))
            .Where(id => id != "")
            .Distinct()
            .ToArray();
        var workspaces = journeys.Select(j => j.WorkspaceId).Distinct().ToArray();
        var permitted = ids.Length == 0
            ? []
            : (
                await db
                    .Conversations.Where(c => ids.Contains(c.Id) && workspaces.Contains(c.WorkspaceId))
                    .Select(c => new { c.Id, c.WorkspaceId })
                    .ToListAsync(ct)
            )
                .Select(c => (c.Id, c.WorkspaceId))
                .ToHashSet();
        return graphs
            .Select(g =>
                (
                    g.Journey,
                    g.Nodes.Where(n =>
                            Text(n["data"], "conversationId") is var cid
                            && (cid == "" || permitted.Contains((cid, g.Journey.WorkspaceId)))
                        )
                        .ToList()
                )
            )
            .ToList();
    }

    private static object NodeView(Journey.Domain.Journey j, JsonObject n) =>
        new
        {
            nodeId = j.Id + ":" + Text(n, "id"),
            localNodeId = Text(n, "id"),
            journeyId = j.Id,
            kind = Text(n["data"], "kind"),
            title = Clip(Text(n["data"], "title"), 200),
            body = Clip(Plain(Text(n["data"], "body"))),
            conversationId = Text(n["data"], "conversationId"),
        };

    public async Task<object> SearchJourneys(string query, CancellationToken ct)
    {
        ValidateText(query, 200);
        var hits = (await Journeys(false, ct))
            .Where(j =>
                (j.Title + " " + j.Description).Contains(query, StringComparison.OrdinalIgnoreCase)
            )
            .ToList();
        return new
        {
            items = hits.Take(50)
                .Select(j => new
                {
                    j.Id,
                    j.Title,
                    description = Clip(j.Description),
                    j.Version,
                }),
            truncated = hits.Count > 50,
        };
    }

    public async Task<object> GetJourney(string journeyId, CancellationToken ct)
    {
        var j = await Journey(journeyId, false, ct);
        var nodes = await SafeNodes(j, ct);
        var shown = nodes.Take(200).ToList();
        var ids = shown.Select(n => Text(n, "id")).ToHashSet();
        return new
        {
            j.Id,
            j.Title,
            description = Clip(j.Description),
            j.Version,
            nodes = shown.Select(n => NodeView(j, n)),
            truncated = nodes.Count > 200,
            relationships = (Graph(j)["edges"] as JsonArray ?? [])
                .OfType<JsonObject>()
                .Where(e => ids.Contains(Text(e, "source")) && ids.Contains(Text(e, "target")))
                .Take(400)
                .Select(e => new
                {
                    sourceId = j.Id + ":" + Text(e, "source"),
                    targetId = j.Id + ":" + Text(e, "target"),
                    label = Clip(Text(e, "label"), 200),
                }),
        };
    }

    public async Task<object> Timeline(string journeyId, CancellationToken ct)
    {
        var j = await Journey(journeyId, false, ct);
        var nodes = await SafeNodes(j, ct);
        var steps = (Graph(j)["steps"] as JsonArray ?? [])
            .Select(s => s?.GetValue<string>())
            .ToArray();
        var ordered = steps
            .Select(id => nodes.FirstOrDefault(n => Text(n, "id") == id))
            .Where(n => n != null)
            .ToList();
        return new
        {
            journeyId,
            ordering = "Curated reading order, not an execution timeline.",
            truncated = ordered.Count > 200,
            items = ordered
                .Take(200)
                .Select((n, index) => new { order = index + 1, entry = NodeView(j, n!) }),
        };
    }

    public async Task<(Journey.Domain.Journey Journey, JsonObject Node)> FindNode(
        string id,
        bool write,
        CancellationToken ct
    )
    {
        var matches = new List<(Journey.Domain.Journey, JsonObject)>();
        foreach (var (j, nodes) in await SafeNodes(await Journeys(write, ct), ct))
        foreach (var n in nodes)
            if (id == Text(n, "id") || id == j.Id + ":" + Text(n, "id"))
                matches.Add((j, n));
        if (matches.Count != 1)
            throw new InvalidDataException(
                "Node is unavailable or ambiguous. Use the qualified nodeId returned by get_journey."
            );
        return matches[0];
    }

    public async Task<object> Evidence(string nodeId, CancellationToken ct)
    {
        var (j, n) = await FindNode(nodeId, false, ct);
        var cid = Text(n["data"], "conversationId");
        if (cid == "")
            return new { entry = NodeView(j, n), source = (object?)null };
        if (await access.RequireConversation(cid) != j.WorkspaceId)
            throw new UnauthorizedAccessException();
        var c = await db
            .Conversations.Where(x => x.Id == cid)
            .Select(x => new { x.Id, x.Title, x.Provider })
            .SingleAsync(ct);
        var messages = await db
            .Messages.Where(m => m.ConversationId == cid)
            .OrderBy(m => m.Order)
            .Take(101)
            .Select(m => new
            {
                m.Id,
                m.Role,
                Texts = m
                    .Blocks.Where(b => b.Kind == "text" || b.Kind == "code")
                    .OrderBy(b => b.Order)
                    .Select(b => b.Text)
                    .ToList(),
            })
            .AsSplitQuery()
            .ToListAsync(ct);
        return new
        {
            entry = NodeView(j, n),
            source = (object)
                new
                {
                    c.Id,
                    c.Title,
                    c.Provider,
                    messages = messages
                        .Take(100)
                        .Select(m => new
                        {
                            m.Id,
                            m.Role,
                            content = Clip(string.Join("\n", m.Texts)),
                        }),
                    truncated = messages.Count > 100,
                },
        };
    }

    public async Task<object> Related(string conversationId, CancellationToken ct)
    {
        var found = false;
        var related = new HashSet<string>();
        foreach (var (j, nodes) in await SafeNodes(await Journeys(false, ct), ct))
        {
            var source = nodes
                .Where(n => Text(n["data"], "conversationId") == conversationId)
                .Select(n => Text(n, "id"))
                .ToHashSet();
            if (source.Count == 0)
                continue;
            found = true;
            foreach (var e in (Graph(j)["edges"] as JsonArray ?? []).OfType<JsonObject>())
            {
                var a = Text(e, "source");
                var b = Text(e, "target");
                var target =
                    source.Contains(a) ? b
                    : source.Contains(b) ? a
                    : null;
                var node = nodes.FirstOrDefault(n => Text(n, "id") == target);
                if (node != null && Text(node["data"], "conversationId") != "")
                    related.Add(Text(node["data"], "conversationId"));
            }
        }
        if (!found)
            throw new UnauthorizedAccessException("Conversation is not in an authorized journey.");
        var grant = await Connection(false, ct);
        return new
        {
            conversationId,
            relationshipBasis = "Explicit journey edges",
            items = await db
                .Conversations.Where(c =>
                    c.WorkspaceId == grant.WorkspaceId
                    && related.Contains(c.Id)
                    && c.Id != conversationId
                )
                .Select(c => new
                {
                    c.Id,
                    c.Title,
                    c.Provider,
                })
                .Take(50)
                .ToListAsync(ct),
            truncated = related.Count > 50,
        };
    }

    public async Task<object> Decisions(string query, CancellationToken ct)
    {
        ValidateText(query, 200);
        var result = new List<object>();
        foreach (var (j, nodes) in await SafeNodes(await Journeys(false, ct), ct))
        foreach (var n in nodes)
            if (
                Text(n["data"], "kind") == "decision"
                && (Text(n["data"], "title") + " " + Plain(Text(n["data"], "body"))).Contains(
                    query,
                    StringComparison.OrdinalIgnoreCase
                )
            )
                result.Add(NodeView(j, n));
        return new { items = result.Take(50), truncated = result.Count > 50 };
    }

    public async Task<object> Propose(
        string journeyId,
        string kind,
        JsonObject payload,
        CancellationToken ct
    )
    {
        var grant = await Connection(true, ct);
        var j = await Journey(journeyId, true, ct);
        await ValidatePayload(j, kind, payload, ct);
        var p = new JourneyProposal
        {
            WorkspaceId = j.WorkspaceId,
            JourneyId = j.Id,
            ConnectionId = grant.Id,
            ProposedBy = access.UserId,
            Kind = kind,
            Payload = payload.ToJsonString(),
            BaseVersion = j.Version,
        };
        db.Add(p);
        await db.SaveChangesAsync(ct);
        return new
        {
            proposalId = p.Id,
            status = p.Status,
            requiresUserApproval = true,
            journeyId = j.Id,
            baseVersion = p.BaseVersion,
        };
    }

    public async Task ValidatePayload(
        Journey.Domain.Journey j,
        string kind,
        JsonObject data,
        CancellationToken ct
    )
    {
        if (data.ToJsonString().Length > 24000)
            throw new InvalidDataException("Proposal is too large.");
        var nodes = Nodes(Graph(j)).ToList();
        bool Has(string key) => nodes.Any(n => Text(n, "id") == Text(data, key));
        if (kind is "entry" or "milestone")
        {
            ValidateText(Text(data, "title"), 200, true);
            ValidateText(Text(data, "body"), 12000);
            var entryKind = Text(data, "kind");
            if (kind == "entry" && entryKind is not ("note" or "decision" or "conversation"))
                throw new InvalidDataException(
                    "Entry kind must be note, decision, or conversation."
                );
            var cid = Text(data, "conversationId");
            if (entryKind == "conversation" && cid == "")
                throw new InvalidDataException("Choose a conversation.");
            if (
                cid != ""
                && !await db.Conversations.AnyAsync(
                    c => c.Id == cid && c.WorkspaceId == j.WorkspaceId,
                    ct
                )
            )
                throw new InvalidDataException("Conversation is outside the journey workspace.");
        }
        else if (kind == "annotation")
        {
            if (!Has("nodeId"))
                throw new InvalidDataException("Node no longer exists.");
            ValidateText(Text(data, "text"), 8000, true);
        }
        else if (kind == "relationship")
        {
            if (
                !Has("sourceId")
                || !Has("targetId")
                || Text(data, "sourceId") == Text(data, "targetId")
            )
                throw new InvalidDataException("Choose two different nodes in the same journey.");
        }
        else
            throw new InvalidDataException("Unknown proposal kind.");
    }

    public async Task Review(string id, bool approve, int expectedVersion, CancellationToken ct)
    {
        if (http.HttpContext!.User.HasClaim(c => c.Type == ConnectionClaim))
            throw new UnauthorizedAccessException();
        var p =
            await db.Set<JourneyProposal>().SingleOrDefaultAsync(p => p.Id == id, ct)
            ?? throw new KeyNotFoundException();
        var j = await access.Journey(p.JourneyId, "edit");
        if (p.Status != "pending")
            throw new DbUpdateConcurrencyException("Proposal was already reviewed.");
        if (approve)
        {
            if (j.Version != expectedVersion || p.BaseVersion != j.Version)
                throw new DbUpdateConcurrencyException(
                    "Journey changed since this proposal. Reject it and request a fresh proposal."
                );
            var grant = await db.Set<McpConnection>()
                .AsNoTracking()
                .SingleOrDefaultAsync(g => g.Id == p.ConnectionId, ct);
            var proposer = await db
                .Users.AsNoTracking()
                .SingleOrDefaultAsync(u => u.Id == p.ProposedBy, ct);
            var member = await db
                .Memberships.AsNoTracking()
                .SingleOrDefaultAsync(
                    m => m.UserId == p.ProposedBy && m.WorkspaceId == j.WorkspaceId,
                    ct
                );
            if (
                grant == null
                || grant.UserId != p.ProposedBy
                || grant.WorkspaceId != j.WorkspaceId
                || grant.Revoked
                || !grant.AllowProposals
                || grant.ExpiresAt <= DateTime.UtcNow
                || proposer == null
                || proposer.SecurityStamp != grant.SecurityStamp
                || (proposer.LockoutEnabled && proposer.LockoutEnd > DateTimeOffset.UtcNow)
                || member == null
                || !Access.Allows(member.Role, "edit")
                || (j.Visibility == "Private" && j.OwnerId != p.ProposedBy)
                || !JsonSerializer.Deserialize<string[]>(grant.JourneyIds)!.Contains(j.Id)
            )
                throw new UnauthorizedAccessException(
                    "The proposing connection no longer has access."
                );
            var payload = JsonNode.Parse(p.Payload)!.AsObject();
            await ValidatePayload(j, p.Kind, payload, ct);
            var graph = Graph(j);
            var nodes = graph["nodes"]!.AsArray();
            if (p.Kind is "entry" or "milestone")
            {
                var nodeId = Guid.NewGuid().ToString();
                var data = new JsonObject
                {
                    ["kind"] = p.Kind == "milestone" ? "milestone" : Text(payload, "kind"),
                    ["title"] = Text(payload, "title"),
                    ["body"] = System.Net.WebUtility.HtmlEncode(Text(payload, "body")),
                    ["conversationId"] = Text(payload, "conversationId") is ""
                        ? null
                        : Text(payload, "conversationId"),
                };
                nodes.Add(
                    new JsonObject
                    {
                        ["id"] = nodeId,
                        ["type"] = "journey",
                        ["data"] = data,
                        ["position"] = new JsonObject
                        {
                            ["x"] = 150 + (nodes.Count % 4) * 300,
                            ["y"] = 150 + (nodes.Count / 4) * 180,
                        },
                    }
                );
                (graph["steps"] as JsonArray ?? (JsonArray)(graph["steps"] = new JsonArray())!).Add(
                    nodeId
                );
            }
            else if (p.Kind == "relationship")
            {
                var edges = graph["edges"]!.AsArray();
                if (
                    edges
                        .OfType<JsonObject>()
                        .Any(e =>
                            Text(e, "source") == Text(payload, "sourceId")
                            && Text(e, "target") == Text(payload, "targetId")
                        )
                )
                    throw new InvalidDataException("Relationship already exists.");
                edges.Add(
                    new JsonObject
                    {
                        ["id"] = Guid.NewGuid().ToString(),
                        ["source"] = Text(payload, "sourceId"),
                        ["target"] = Text(payload, "targetId"),
                        ["label"] = "Related to",
                        ["type"] = "smoothstep",
                    }
                );
            }
            else
            {
                var annotation = new Annotation
                {
                    WorkspaceId = j.WorkspaceId,
                    JourneyId = j.Id,
                    NodeId = Text(payload, "nodeId"),
                    AuthorId = access.UserId,
                };
                annotation.Comments.Add(
                    new Comment
                    {
                        AnnotationId = annotation.Id,
                        AuthorId = access.UserId,
                        Body = Text(payload, "text"),
                    }
                );
                db.Add(annotation);
            }
            j.Graph = graph.ToJsonString();
            j.Version++;
        }
        p.Status = approve ? "approved" : "rejected";
        p.ReviewedBy = access.UserId;
        p.ReviewedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
    }
}
