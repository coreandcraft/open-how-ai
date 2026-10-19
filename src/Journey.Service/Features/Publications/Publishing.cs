using System.Text.Json;
using System.Text.RegularExpressions;
using Journey.Domain;
using Journey.Data;
using Journey.Service.Features.Imports;
using Microsoft.EntityFrameworkCore;

namespace Journey.Service.Features.Publications;

public record PublishRequest(
    string[] MessageIds,
    string[] Redactions,
    string Visibility,
    bool Confirm,
    string[]? CuratorNodeIds = null,
    string[]? AnnotationIds = null,
    bool DiscussionEnabled = false,
    string? Title = null,
    ReplacementRule[]? ReplacementRules = null
);

public static class Publishing
{
    // Long encoded strings must not turn email detection into quadratic backtracking.
    static readonly Regex SensitivePattern = new(
        @"(?:sk-[a-z0-9_-]{12,}|(?:api[_-]?key|password|secret|access[_-]?token)\s*[:=]\s*\S+|[\w.+-]+@[\w.-]+\.[a-z]{2,}|[A-Z]:\\[^\r\n]+|/(?:home|Users)/[^\s]+)",
        RegexOptions.IgnoreCase | RegexOptions.NonBacktracking,
        TimeSpan.FromSeconds(2)
    );

    public static string RedactPatterns(string text, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var result = SensitivePattern.Replace(text, "[REDACTED]");
        ct.ThrowIfCancellationRequested();
        return result;
    }
    public static string Redact(string text, PublishRequest request) =>
        Redact(ImportSanitization.Replace(text, request.ReplacementRules), request.Redactions);
    public static string[] ConversationIds(string graph) =>
        JsonDocument
            .Parse(graph)
            .RootElement.Get("nodes")
            .Items()
            .Select(n => n.Get("data").Get("conversationId").Str())
            .OfType<string>()
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct()
            .ToArray();

    /// <summary>
    /// Loads only what a snapshot can contain: the given conversations' titles and providers, and the
    /// selected messages with their publishable blocks.
    /// </summary>
    public static Task<List<Conversation>> Sources(
        AppDb db,
        string workspaceId,
        string[] conversationIds,
        string[] messageIds
    )
    {
        var ids = conversationIds.Distinct().ToArray();
        var selected = messageIds.Distinct().ToArray();
        return Lift(db
            .Conversations.AsNoTracking()
            .AsSplitQuery()
            .Where(c => c.WorkspaceId == workspaceId && ids.Contains(c.Id))
            .Select(c => new Conversation
            {
                Id = c.Id,
                WorkspaceId = c.WorkspaceId,
                Title = c.Title,
                Provider = c.Provider,
                Messages = c
                    .Messages.Where(m => selected.Contains(m.Id))
                    .OrderBy(m => m.Order)
                    .Select(m => new ConversationMessage
                    {
                        Id = m.Id,
                        ConversationId = m.ConversationId,
                        Order = m.Order,
                        Role = m.Role,
                        Blocks = m
                            .Blocks.Where(b => b.Kind == "text" || b.Kind == "markdown")
                            .OrderBy(b => b.Order)
                            .Select(b => new MessageContentBlock
                            {
                                Kind = b.Kind,
                                Order = b.Order,
                                Text = b.Text,
                            })
                            .ToList(),
                    })
                    .ToList(),
            })
            .ToListAsync());
    }

    static async Task<List<Conversation>> Lift(Task<List<Conversation>> load)
    {
        var conversations = await load;
        foreach (var message in conversations.SelectMany(c => c.Messages))
            CursorTranscript.Lift(message);
        return conversations;
    }

    public static object ConversationSnapshot(
        Conversation conversation,
        PublishRequest request,
        List<Annotation>? annotations = null
    )
    {
        var selected = request.MessageIds.ToHashSet();
        if (selected.Count == 0 || selected.Any(id => !conversation.Messages.Any(m => m.Id == id)))
            throw new InvalidDataException("Select messages from this conversation to share.");
        var title = request.Title?.Trim();
        if (title?.Length > 200)
            throw new InvalidDataException("Shared conversation title must be 200 characters or fewer.");
        return new
        {
            annotations = ProjectAnnotations(
                annotations,
                request,
                conversation
                    .Messages.Where(m => selected.Contains(m.Id))
                    .Select(m => m.Id)
                    .ToHashSet(),
                []
            ),
            kind = "conversation",
            title = Redact(string.IsNullOrEmpty(title) ? conversation.Title : title, request),
            provider = Redact(conversation.Provider, request),
            publishedAt = DateTime.UtcNow,
            messages = conversation
                .Messages.Where(m => selected.Contains(m.Id))
                .OrderBy(m => m.Order)
                .Select(m => new
                {
                    id = m.Id,
                    role = m.Role,
                    blocks = m
                        .Blocks.Where(b => b.Kind is "text" or "markdown")
                        .OrderBy(b => b.Order)
                        .Select(b => new
                        {
                            kind = b.Kind,
                            text = Redact(b.Text, request),
                        })
                        .ToArray(),
                })
                .ToArray(),
        };
    }

    private static object[] ProjectAnnotations(
        List<Annotation>? annotations,
        PublishRequest request,
        HashSet<string> messages,
        HashSet<string> nodes
    )
    {
        return (annotations ?? [])
            .Where(a => (request.AnnotationIds ?? []).Contains(a.Id))
            .Select(a =>
            {
                if (
                    a.Orphaned
                    || (
                        a.MessageId != null
                            ? !messages.Contains(a.MessageId)
                            : a.NodeId == null || !nodes.Contains(a.NodeId)
                    )
                )
                    throw new InvalidDataException(
                        "Selected annotations must belong to included messages or nodes and have valid anchors."
                    );
                var anchor = JsonDocument.Parse(a.Anchor).RootElement;
                return (object)
                    new
                    {
                        messageId = a.MessageId,
                        nodeId = a.NodeId,
                        quote = Redact(anchor.Get("quote").Str() ?? "", request),
                        resolved = a.Resolved,
                        comments = a
                            .Comments.OrderBy(c => c.CreatedAt)
                            .Select(c => new { text = Redact(c.Body, request) })
                            .ToArray(),
                    };
            })
            .ToArray();
    }

    public static string Redact(string text, IEnumerable<string> terms)
    {
        foreach (
            var term in terms
                .Where(t => !string.IsNullOrWhiteSpace(t))
                .OrderByDescending(t => t.Length)
        )
            text = text.Replace(term, "[REDACTED]", StringComparison.Ordinal);
        return text;
    }

    public static string[] Findings(string json)
    {
        // Scan decoded string values so redaction suggestions match the exact source, including backslashes.
        using var document = JsonDocument.Parse(json);
        var values = new List<string>();
        void Walk(JsonElement e)
        {
            if (e.ValueKind == JsonValueKind.String)
                values.Add(e.GetString()!);
            else if (e.ValueKind == JsonValueKind.Array)
                foreach (var x in e.EnumerateArray())
                    Walk(x);
            else if (e.ValueKind == JsonValueKind.Object)
                foreach (var x in e.EnumerateObject())
                    Walk(x.Value);
        }
        Walk(document.RootElement);
        return values
            .SelectMany(text =>
                SensitivePattern.Matches(text)
                    .Select(x => x.Value)
            )
            .Distinct()
            .ToArray();
    }

    private static object? SafePosition(JsonElement position)
    {
        var x = position.Get("x");
        var y = position.Get("y");
        if (
            x.ValueKind != JsonValueKind.Number
            || y.ValueKind != JsonValueKind.Number
            || !x.TryGetDouble(out var px)
            || !y.TryGetDouble(out var py)
            || !double.IsFinite(px)
            || !double.IsFinite(py)
        )
            return null;
        return new { x = px, y = py };
    }

    public static object Snapshot(
        global::Journey.Domain.Journey journey,
        List<Conversation> conversations,
        PublishRequest request,
        List<Annotation>? annotations = null
    )
    {
        var graph = JsonDocument.Parse(journey.Graph).RootElement;
        var nodes = graph.Get("nodes").Items().ToList();
        var order = graph
            .Get("steps")
            .Items()
            .Select(x => x.Str())
            .Where(x => x != null)
            .Distinct()
            .ToList();
        if (order.Count == 0)
            order = nodes.Select(x => x.Get("id").Str()).ToList();
        var byId = new Dictionary<string, JsonElement>();
        foreach (var node in nodes)
        {
            var nodeId = node.Get("id").Str();
            if (nodeId != null)
                byId.TryAdd(nodeId, node);
        }
        var steps = new List<object>();
        var included = new HashSet<string>();
        var includedMessages = new HashSet<string>();
        var sharedMessages = new Dictionary<string, PublishedMessage[]>();
        foreach (var id in order)
        {
            if (id == null || !byId.TryGetValue(id, out var n))
                continue;
            var data = n.Get("data");
            var cid = data.Get("conversationId").Str();
            if (string.IsNullOrWhiteSpace(cid))
                cid = null;
            var c = cid == null ? null : conversations.FirstOrDefault(x => x.Id == cid);
            if (cid != null)
            {
                if (!sharedMessages.TryGetValue(cid, out var shared))
                {
                    shared = ProjectMessages(c, request);
                    sharedMessages[cid] = shared;
                }
                if (shared.Length == 0)
                    continue;
                foreach (var message in shared)
                    includedMessages.Add(message.Id);
            }
            included.Add(id);
            steps.Add(
                new
                {
                    id,
                    position = SafePosition(n.Get("position")),
                    kind = data.Get("kind").Str(),
                    title = Redact(data.Get("title").Str() ?? "Untitled", request),
                    provider = c == null ? null : Redact(c.Provider, request),
                    conversationId = cid,
                    commentary = request.CuratorNodeIds?.Contains(id) == true
                        ? Redact(
                            System.Net.WebUtility.HtmlDecode(
                                Regex.Replace(data.Get("body").Str() ?? "", "<[^>]*>", "")
                            ),
                            request
                        )
                        : null,
                }
            );
        }
        var connections = graph
            .Get("edges")
            .Items()
            .Where(e =>
                included.Contains(e.Get("source").Str() ?? "")
                && included.Contains(e.Get("target").Str() ?? "")
            )
            .Select(
                (e, i) =>
                    new
                    {
                        id = "edge-" + i,
                        source = e.Get("source").Str(),
                        target = e.Get("target").Str(),
                        label = Redact(e.Get("label").Str() ?? "Related to", request),
                    }
            )
            .ToArray();
        return new
        {
            annotations = ProjectAnnotations(annotations, request, includedMessages, included),
            title = Redact(journey.Title, request),
            description = Redact(journey.Description, request),
            publishedAt = DateTime.UtcNow,
            steps,
            connections,
            sources = sharedMessages.Select(p => new { id = p.Key, messages = p.Value }).ToArray(),
        };
    }

    /// <summary>Selected messages for one conversation. Stored once on the snapshot, not copied onto every chapter.</summary>
    static PublishedMessage[] ProjectMessages(Conversation? conversation, PublishRequest request)
    {
        if (conversation == null)
            return [];
        var selected = request.MessageIds.ToHashSet();
        return conversation
            .Messages.Where(m => selected.Contains(m.Id))
            .OrderBy(m => m.Order)
            .Select(m => new PublishedMessage(
                m.Id,
                m.Role,
                m.Blocks.Where(b => b.Kind is "text" or "markdown")
                    .OrderBy(b => b.Order)
                    .Select(b => new PublishedBlock(b.Kind, Redact(b.Text, request)))
                    .ToArray()
            ))
            .ToArray();
    }

    record PublishedMessage(string Id, string? Role, PublishedBlock[] Blocks);

    record PublishedBlock(string? Kind, string? Text);
}

