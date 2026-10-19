using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Journey.Domain;

namespace Journey.Service.Features.Imports;

public abstract class SessionImporter : Importer
{
    protected static List<JsonElement> Lines(SourceInput source, out List<string> warnings) =>
        ParsedImportInput.For(source).Lines(out warnings);

    protected static void Parents(Conversation c)
    {
        var ids = c
            .Messages.Where(m => m.ProviderId != null)
            .GroupBy(m => m.ProviderId!)
            .ToDictionary(g => g.Key, g => g.First().Id);
        foreach (var m in c.Messages)
            m.ParentId =
                m.ExternalParentId == null ? null : ids.GetValueOrDefault(m.ExternalParentId);
    }

    protected static ConversationMessage Message(
        JsonElement payload,
        string? id,
        string? parent,
        string role,
        JsonElement content,
        int order,
        JsonElement raw
    ) =>
        new()
        {
            ProviderId = id,
            ExternalParentId = parent,
            Role = role,
            Order = order,
            Model = payload.Get("model").Str(),
            Timestamp = (
                raw.Get("timestamp").ValueKind != JsonValueKind.Undefined
                    ? raw.Get("timestamp")
                    : payload.Get("timestamp")
            ).Date(),
            Blocks = Blocks(content),
            Metadata = raw.GetRawText(),
        };
}

public class ClaudeCodeImporter : SessionImporter
{
    public override string Provider => "Claude Code";

    public override bool CanImport(SourceInput source) =>
        HasExtension(source, ".jsonl", ".ndjson")
        && Lines(source, out _)
            .Any(r =>
                r.Get("sessionId").Str() != null
                && (
                    r.Get("message").ValueKind == JsonValueKind.Object
                    || r.Get("type").Str() == "summary"
                )
            );

    protected override List<Conversation> Parse(SourceInput source)
    {
        var rows = Lines(source, out var warnings);
        return rows.Where(r => r.Get("sessionId").Str() != null)
            .GroupBy(r => (Session: r.Get("sessionId").Str()!, Agent: r.Get("agentId").Str()))
            .Select(group =>
            {
                var first = group.First();
                var id =
                    group.Key.Agent == null
                        ? group.Key.Session
                        : group.Key.Session + ":agent:" + group.Key.Agent;
                var c = new Conversation
                {
                    Provider = Provider,
                    ProviderId = id,
                    Title =
                        group.Key.Agent == null
                            ? "Claude Code session " + group.Key.Session
                            : "Subagent " + group.Key.Agent,
                    ParentConversationExternalId =
                        group.Key.Agent != null
                            ? group.Key.Session
                            : first.Get("parentSessionId").Str(),
                    SourceFormat = "claude-code-session-jsonl-v1",
                    SourcePath =
                        first.Get("cwd").Str()
                        ?? rows.FirstOrDefault(r =>
                                r.Get("sessionId").Str() == group.Key.Session
                                && r.Get("cwd").Str() != null
                            )
                            .Get("cwd")
                            .Str(),
                    RawPayload = Json.Write(group),
                    Metadata = Json.Write(group),
                    Diagnostics = Json.Write(warnings),
                };
                foreach (var row in group)
                {
                    var payload = row.Get("message");
                    if (payload.ValueKind == JsonValueKind.Object)
                        c.Messages.Add(
                            Message(
                                payload,
                                row.Get("uuid").Str() ?? payload.Get("id").Str(),
                                row.Get("parentUuid").Str(),
                                payload.Get("role").Str() ?? row.Get("type").Str() ?? "unknown",
                                payload.Get("content"),
                                c.Messages.Count,
                                row
                            )
                        );
                    else
                        c.Messages.Add(
                            Message(
                                row,
                                row.Get("uuid").Str(),
                                row.Get("parentUuid").Str(),
                                "event",
                                JsonSerializer.SerializeToElement(
                                    new[]
                                    {
                                        new
                                        {
                                            type = row.Get("type").Str() ?? "unknown",
                                            text = row.Get("summary").Str() ?? row.GetRawText(),
                                        },
                                    }
                                ),
                                c.Messages.Count,
                                row
                            )
                        );
                }
                Parents(c);
                c.Model = c.Messages.LastOrDefault(m => m.Model != null)?.Model;
                return c;
            })
            .ToList();
    }
}

public class CodexImporter : SessionImporter
{
    public override string Provider => "Codex";

    public override bool CanImport(SourceInput source) =>
        HasExtension(source, ".jsonl", ".ndjson")
        && Lines(source, out _)
            .Any(r =>
                r.Get("type").Str() == "session_meta" && r.Get("payload").Get("id").Str() != null
            );

    protected override List<Conversation> Parse(SourceInput source)
    {
        var rows = Lines(source, out var warnings);
        var meta = rows.First(r => r.Get("type").Str() == "session_meta").Get("payload");
        var c = new Conversation
        {
            Provider = Provider,
            ProviderId = meta.Get("id").Str(),
            Title = "Codex session " + meta.Get("id").Str(),
            SourceFormat = "codex-rollout-v1",
            SourcePath = meta.Get("cwd").Str(),
            ParentConversationExternalId =
                meta.Get("forked_from_id").Str()
                ?? meta.Get("source")
                    .Get("subagent")
                    .Get("thread_spawn")
                    .Get("parent_thread_id")
                    .Str(),
            SourceCreatedAt = meta.Get("timestamp").Date(),
            RawPayload = Json.Write(rows),
            Metadata = meta.GetRawText(),
            Diagnostics = Json.Write(warnings),
        };
        foreach (var row in rows.Where(r => r.Get("type").Str() != "session_meta"))
        {
            var payload = row.Get("payload");
            var type = payload.Get("type").Str();
            var content =
                type == "message"
                    ? payload.Get("content")
                    : JsonSerializer.SerializeToElement(
                        new[]
                        {
                            new
                            {
                                type = type ?? row.Get("type").Str(),
                                text = payload.Get("output").Str()
                                    ?? payload.Get("arguments").Str()
                                    ?? payload.Get("text").Str()
                                    ?? payload.GetRawText(),
                            },
                        }
                    );
            var message = Message(
                payload,
                payload.Get("id").Str() ?? "record:" + c.Messages.Count,
                payload.Get("parent_id").Str(),
                type == "message" ? payload.Get("role").Str() ?? "unknown" : "event",
                content,
                c.Messages.Count,
                row
            );
            if (type != "message" && message.Blocks.Count > 0)
                message.Blocks[0].Metadata = payload.GetRawText();
            c.Messages.Add(message);
        }
        Parents(c);
        return [c];
    }
}

public class GeminiCliImporter : SessionImporter
{
    public override string Provider => "Gemini CLI";

    public override bool CanImport(SourceInput source)
    {
        if (!HasExtension(source, ".json"))
            return false;
        try
        {
            var r = Root(source);
            return r.Get("sessionId").Str() != null
                && r.Get("projectHash").Str() != null
                && r.Get("messages").ValueKind == JsonValueKind.Array;
        }
        catch
        {
            return false;
        }
    }

    protected override List<Conversation> Parse(SourceInput source)
    {
        var root = Root(source);
        var c = New(root, "gemini-cli-record-v1");
        c.ProviderId = root.Get("sessionId").Str();
        c.ExternalProjectId = root.Get("projectHash").Str();
        c.SourceCreatedAt = root.Get("startTime").Date();
        c.SourceUpdatedAt = root.Get("lastUpdated").Date();
        foreach (var m in root.Get("messages").Items())
        {
            var content = m.Get("content");
            c.Messages.Add(
                Message(
                    m,
                    m.Get("id").Str(),
                    m.Get("parentId").Str(),
                    m.Get("type").Str() == "gemini"
                        ? "assistant"
                        : m.Get("type").Str() ?? "unknown",
                    content,
                    c.Messages.Count,
                    m
                )
            );
            foreach (var tool in m.Get("toolCalls").Items())
                c.Messages.Last()
                    .Blocks.Add(
                        new()
                        {
                            Kind = "tool_call",
                            Text = tool.GetRawText(),
                            Metadata = tool.GetRawText(),
                            Order = c.Messages.Last().Blocks.Count,
                        }
                    );
        }
        Parents(c);
        return [c];
    }
}

public class ApiMessagesImporter : SessionImporter
{
    public override string Provider => "API messages";

    public override bool CanImport(SourceInput source)
    {
        if (!HasExtension(source, ".json"))
            return false;
        try
        {
            var r = Root(source);
            return r.Get("choices").ValueKind == JsonValueKind.Array
                || r.Get("type").Str() == "message"
                    && r.Get("role").Str() != null
                    && r.Get("content").ValueKind == JsonValueKind.Array;
        }
        catch
        {
            return false;
        }
    }

    protected override List<Conversation> Parse(SourceInput source)
    {
        var r = Root(source);
        var c = New(r, "api-message-v1");
        var choices = r.Get("choices").Items().ToList();
        if (choices.Count == 0)
            c.Messages.Add(
                Message(
                    r,
                    r.Get("id").Str(),
                    null,
                    r.Get("role").Str() ?? "assistant",
                    r.Get("content"),
                    0,
                    r
                )
            );
        else
            foreach (var choice in choices)
            {
                var m = choice.Get("message");
                c.Messages.Add(
                    Message(
                        m,
                        choice.Get("index").Str(),
                        null,
                        m.Get("role").Str() ?? "assistant",
                        m.Get("content"),
                        c.Messages.Count,
                        m
                    )
                );
                foreach (var call in m.Get("tool_calls").Items())
                    c.Messages.Last()
                        .Blocks.Add(
                            new()
                            {
                                Kind = "tool_call",
                                Text = call.GetRawText(),
                                Metadata = call.GetRawText(),
                                Order = c.Messages.Last().Blocks.Count,
                            }
                        );
            }
        return [c];
    }
}

public class CursorAgentTranscriptImporter : SessionImporter, IConversationImporter
{
    public override string Provider => "Cursor";
    ImportCapability IConversationImporter.Capability =>
        new(
            "cursor-agent-transcript-jsonl-v1",
            Provider,
            "experimental",
            "1",
            [".jsonl", ".ndjson"],
            "Workspace agent transcripts: role plus message.content, with turn status lines kept as events. Tool inputs stay in the original record."
        );

    public override bool CanImport(SourceInput source)
    {
        if (!HasExtension(source, ".jsonl", ".ndjson"))
            return false;
        var rows = Lines(source, out _);
        return rows.Any(IsTranscript)
            && rows.All(r =>
                r.Get("sessionId").Str() == null && r.Get("type").Str() != "session_meta"
            );
    }

    static bool IsTranscript(JsonElement row) =>
        row.Get("role").Str() is "user" or "assistant"
        && row.Get("message").Get("content").ValueKind == JsonValueKind.Array;

    protected override List<Conversation> Parse(SourceInput source)
    {
        var rows = Lines(source, out var warnings);
        var c = new Conversation
        {
            Provider = Provider,
            ProviderId = Path.GetFileNameWithoutExtension(source.Name),
            Title = Path.GetFileNameWithoutExtension(source.Name),
            SourceFormat = "cursor-agent-transcript-jsonl-v1",
            RawPayload = Json.Write(rows),
            Diagnostics = Json.Write(warnings),
        };
        string? parent = null;
        for (var i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            var id = "line:" + (i + 1);
            ConversationMessage message;
            if (IsTranscript(row))
            {
                var payload = row.Get("message");
                message = Message(
                    payload,
                    id,
                    parent,
                    row.Get("role").Str()!,
                    payload.Get("content"),
                    c.Messages.Count,
                    row
                );
                CursorTranscript.Lift(message);
            }
            else if (row.Get("type").Str() != null && row.Get("status").Str() != null)
            {
                message = new()
                {
                    ProviderId = id,
                    ExternalParentId = parent,
                    Role = "event",
                    Order = c.Messages.Count,
                    Blocks =
                    [
                        new()
                        {
                            Kind = "text",
                            Text = row.Get("type").Str() + ": " + row.Get("status").Str(),
                        },
                    ],
                    Metadata = row.GetRawText(),
                };
            }
            else
            {
                warnings.Add("Unrecognized record at line " + (i + 1) + "; preserved as an event.");
                var raw = row.GetRawText();
                message = new()
                {
                    ProviderId = id,
                    ExternalParentId = parent,
                    Role = "event",
                    Order = c.Messages.Count,
                    Blocks =
                    [
                        new()
                        {
                            Kind = "unknown",
                            Text = raw.Length > 500 ? raw[..500] + "…" : raw,
                            Metadata = raw,
                        },
                    ],
                    Metadata = raw,
                };
            }
            c.Messages.Add(message);
            parent = id;
        }
        c.Diagnostics = Json.Write(warnings);
        Parents(c);
        return [c];
    }
}

/// <summary>
/// Cursor wraps each user turn as <c>&lt;timestamp&gt;…&lt;/timestamp&gt;</c> and <c>&lt;user_query&gt;…&lt;/user_query&gt;</c>
/// inside the text block. The clock belongs on the message; the query is the text.
/// </summary>
public static partial class CursorTranscript
{
    static readonly Regex LeadingTimestamp = LeadingTimestampPattern();
    static readonly Regex UserQuery = UserQueryPattern();
    static readonly Regex Offset = OffsetPattern();

    public static void Lift(ConversationMessage message)
    {
        foreach (var block in message.Blocks)
        {
            if (block.Kind is not ("text" or "markdown"))
                continue;
            var match = LeadingTimestamp.Match(block.Text);
            if (!match.Success || !TryParse(match.Groups["when"].Value, out var at))
                continue;
            message.Timestamp ??= at;
            var rest = block.Text[match.Length..];
            var query = UserQuery.Match(rest);
            block.Text = (query.Success ? query.Groups["query"].Value : rest).Trim();
        }
    }

    static bool TryParse(string raw, out DateTime utc)
    {
        utc = default;
        var zone = Offset.Match(raw);
        if (!zone.Success)
            return false;
        var clock = raw[..zone.Index].Trim();
        if (
            !DateTime.TryParseExact(
                clock,
                ["dddd, MMM d, yyyy, h:mm tt", "dddd, MMM dd, yyyy, h:mm tt"],
                CultureInfo.InvariantCulture,
                DateTimeStyles.AllowWhiteSpaces,
                out var local
            )
        )
            return false;
        var off = zone.Groups["off"].Value;
        var offset = TimeSpan.Zero;
        if (off.Length > 0)
        {
            var negative = off[0] == '-';
            var body = off[1..].Split(':');
            if (!int.TryParse(body[0], NumberStyles.None, CultureInfo.InvariantCulture, out var hours))
                return false;
            var minutes = 0;
            if (
                body.Length > 1
                && !int.TryParse(body[1], NumberStyles.None, CultureInfo.InvariantCulture, out minutes)
            )
                return false;
            offset = new TimeSpan(negative ? -hours : hours, negative ? -minutes : minutes, 0);
        }
        utc = new DateTimeOffset(DateTime.SpecifyKind(local, DateTimeKind.Unspecified), offset).UtcDateTime;
        return true;
    }

    [GeneratedRegex(
        @"^\s*<timestamp>\s*(?<when>.*?)\s*</timestamp>\s*",
        RegexOptions.Singleline | RegexOptions.CultureInvariant
    )]
    private static partial Regex LeadingTimestampPattern();

    [GeneratedRegex(@"^<user_query>\s*(?<query>[\s\S]*?)\s*</user_query>\s*$", RegexOptions.CultureInvariant)]
    private static partial Regex UserQueryPattern();

    [GeneratedRegex(@"\(UTC(?<off>[+-]\d{1,2}(?::\d{2})?)?\)\s*$", RegexOptions.CultureInvariant)]
    private static partial Regex OffsetPattern();
}
