using Journey.Data;
using Journey.Domain;
using Microsoft.EntityFrameworkCore;

namespace Journey.Server.Features.Journeys;

public record BuildJourneyInput(string? Name, string[]? ConversationIds);

/// <summary>Builds an editable graph from source evidence, without inferring causality from time.</summary>
public static class JourneyDraftBuilder
{
    public const int MaxConversations = 100;
    public const int MaxExecutions = 500;
    public record Thread(string Id, string Title, string Provider, string? Model, string? ProviderId,
        string SourceIdentity, string? ParentId, DateTime? Start, DateTime? End, int MessageCount);
    public record Execution(string Id, string ConversationId, string? ToolName, string? CallBlockId,
        string? ResultBlockId, string Status, DateTime? Start, DateTime? End, string? Model);
    record Stamp(string Id, DateTime? Time, string? Model);
    record Lane(string Id, string Label, string ConversationId, string? ToolName, DateTime? End);

    public static async Task<string> Generate(AppDb db, string wid, string[] ids, CancellationToken ct)
    {
        var threads = await db.Conversations.AsNoTracking()
            .Where(c => c.WorkspaceId == wid && ids.Contains(c.Id))
            .Select(c => new Thread(c.Id, c.Title, c.Provider, c.Model, c.ProviderId, c.SourceIdentity,
                c.ParentConversationExternalId,
                c.Messages.Min(m => m.Timestamp) ?? c.SourceCreatedAt,
                c.Messages.Max(m => m.Timestamp) ?? c.SourceUpdatedAt, c.Messages.Count))
            .ToListAsync(ct);
        if (threads.Count != ids.Length)
            throw new InvalidDataException("Choose conversations from this workspace. A selected conversation may have been deleted.");
        // Read only execution references; tool arguments/results can be very large and are not needed here.
        var calls = await db.Set<AgentExecution>().AsNoTracking()
            .Where(e => e.WorkspaceId == wid && ids.Contains(e.ConversationId))
            .Select(e => new { e.Id, e.ConversationId, e.ToolName, e.CallBlockId, e.ResultBlockId, e.Status })
            .Take(MaxExecutions + 1).ToListAsync(ct);
        if (calls.Count > MaxExecutions)
            throw new InvalidDataException("The selection contains more than 500 tool executions. Select fewer conversations to build a readable draft.");
        var blockIds = calls.SelectMany(e => new[] { e.CallBlockId, e.ResultBlockId }).OfType<string>().Distinct().ToArray();
        var stamps = await db.Set<MessageContentBlock>().AsNoTracking()
            .Where(b => blockIds.Contains(b.Id))
            .Join(db.Messages.Where(m => ids.Contains(m.ConversationId)), b => b.ConversationMessageId, m => m.Id,
                (b, m) => new Stamp(b.Id, m.Timestamp, m.Model)).ToDictionaryAsync(s => s.Id, ct);
        var executions = calls.Select(e => new Execution(e.Id, e.ConversationId, e.ToolName,
            e.CallBlockId, e.ResultBlockId, e.Status,
            stamps.GetValueOrDefault(e.CallBlockId ?? "")?.Time,
            stamps.GetValueOrDefault(e.ResultBlockId ?? "")?.Time,
            stamps.GetValueOrDefault(e.CallBlockId ?? "")?.Model)).ToList();
        var relationships = await db.Set<SourceRelationship>().AsNoTracking()
            .Where(r => r.WorkspaceId == wid && ids.Contains(r.ConversationId))
            .Select(r => new SourceRelationship { Id = r.Id, ConversationId = r.ConversationId,
                TargetConversationId = r.TargetConversationId, ExternalTargetId = r.ExternalTargetId,
                Kind = r.Kind, Status = r.Status }).ToListAsync(ct);
        return Build(threads, executions, relationships);
    }

    public static string Build(IReadOnlyList<Thread> threads, IReadOnlyList<Execution> executions,
        IReadOnlyList<SourceRelationship> relationships)
    {
        var ordered = threads.OrderBy(c => c.Start == null).ThenBy(c => c.Start).ThenBy(c => c.Id, StringComparer.Ordinal).ToList();
        var byId = threads.ToDictionary(c => c.Id);
        var times = threads.Select(c => c.Start).Concat(executions.Select(e => e.Start))
            .OfType<DateTime>().Distinct().Order().Select((time, index) => (time, index))
            .ToDictionary(x => x.time, x => x.index);
        // Equal starts align. Spacing is ordinal so long idle periods do not create enormous gaps.
        int X(DateTime? time) => 340 + (time.HasValue ? times[time.Value] : times.Count) * 320;
        DateTime? End(DateTime? start, DateTime? end) => start.HasValue && end >= start ? end : null;
        var nodes = new List<object>();
        var edges = new List<object>();
        var steps = new List<(string Id, DateTime? Start)>();
        var lanes = new List<Lane>();
        var omitted = 0;
        var linked = new HashSet<(string, string, string)>();
        void Link(string source, string target, string label, string evidenceId, string status)
        {
            if (source == target || !linked.Add((source, target, label))) return;
            edges.Add(new { id = "edge-" + edges.Count, source, target, label, type = "smoothstep",
                markerEnd = new { type = "arrowclosed" },
                data = new { recorded = true, evidenceId, status } });
        }
        int AddLane(string label, string conversationId, string? toolName, DateTime? end)
        {
            var index = lanes.Count;
            var id = "lane-" + index;
            lanes.Add(new Lane(id, label, conversationId, toolName, end));
            nodes.Add(new { id, type = "journey", position = new { x = 0, y = index * 210 },
                data = new { kind = "group", title = label, body = "", timelineLaneHeader = true,
                    status = toolName == null ? "Conversation thread" : "Tool execution lane" } });
            return index;
        }
        foreach (var c in ordered)
        {
            var lane = AddLane(c.Title, c.Id, null, c.End);
            var id = "conversation-" + c.Id;
            nodes.Add(new { id, type = "journey", position = new { x = X(c.Start), y = lane * 210 },
                data = new { kind = "conversation", title = c.Title, conversationId = c.Id,
                    provider = c.Provider, model = c.Model, messageCount = c.MessageCount, body = "",
                    groupId = lanes[lane].Id, laneLabel = lanes[lane].Label, timelineStart = c.Start,
                    timelineEnd = End(c.Start, c.End), timeline = true } });
            steps.Add((id, c.Start));
            foreach (var e in executions.Where(e => e.ConversationId == c.Id)
                .OrderBy(e => e.Start == null).ThenBy(e => e.Start).ThenBy(e => e.Id, StringComparer.Ordinal))
            {
                var tool = string.IsNullOrWhiteSpace(e.ToolName) ? "Tool execution" : e.ToolName;
                // Reuse a tool lane only when the recorded interval proves executions do not overlap.
                var toolLane = e.Start.HasValue ? lanes.FindIndex(l => l.ConversationId == c.Id &&
                    l.ToolName == tool && l.End.HasValue && l.End < e.Start) : -1;
                if (toolLane < 0) toolLane = AddLane(tool, c.Id, tool, End(e.Start, e.End));
                else lanes[toolLane] = lanes[toolLane] with { End = End(e.Start, e.End) };
                var executionId = "execution-" + e.Id;
                nodes.Add(new { id = executionId, type = "journey", position = new { x = X(e.Start), y = toolLane * 210 },
                    data = new { kind = "note", title = tool, body = "", status = e.Status,
                        provider = c.Provider, model = e.Model ?? c.Model, sourceConversationId = c.Id,
                        recordedExecution = true, executionId = e.Id, callBlockId = e.CallBlockId,
                        resultBlockId = e.ResultBlockId, groupId = lanes[toolLane].Id,
                        laneLabel = lanes[toolLane].Label, timelineStart = e.Start,
                        timelineEnd = End(e.Start, e.End), timeline = true } });
                steps.Add((executionId, e.Start));
                Link(id, executionId, "Recorded tool execution", e.Id, e.Status);
            }
        }
        foreach (var r in relationships.OrderBy(r => r.Id, StringComparer.Ordinal))
        {
            if (!byId.ContainsKey(r.ConversationId)) continue;
            if (r.Status is not ("confirmed" or "user-associated") ||
                r.TargetConversationId == null || !byId.ContainsKey(r.TargetConversationId))
            { omitted++; continue; }
            if (r.Kind == "parent-session")
                Link("conversation-" + r.TargetConversationId, "conversation-" + r.ConversationId,
                    "Parent session", r.Id, r.Status);
            else
                Link("conversation-" + r.ConversationId, "conversation-" + r.TargetConversationId,
                    "Recorded relationship: " + r.Kind, r.Id, r.Status);
        }
        foreach (var c in threads.Where(c => !string.IsNullOrWhiteSpace(c.ParentId)))
        {
            // Legacy imports may carry an explicit parent ID without a SourceRelationship row.
            if (relationships.Any(r => r.ConversationId == c.Id && r.Kind == "parent-session")) continue;
            var candidates = threads.Where(p => p.Id != c.Id && p.Provider == c.Provider &&
                p.SourceIdentity == c.SourceIdentity && p.ProviderId == c.ParentId).ToList();
            if (candidates.Count == 1)
                Link("conversation-" + candidates[0].Id, "conversation-" + c.Id, "Parent session", c.Id, "recorded-parent-id");
            else omitted++;
        }
        return Json.Write(new { nodes, edges, steps = steps.OrderBy(s => s.Start == null).ThenBy(s => s.Start)
                .ThenBy(s => s.Id, StringComparer.Ordinal).Select(s => s.Id),
            timeline = new { version = 1, unknownTimeCount = steps.Count(s => s.Start == null),
                omittedRelationshipCount = omitted, spacing = "chronological" } });
    }
}
