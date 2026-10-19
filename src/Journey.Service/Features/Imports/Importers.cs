using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Journey.Data;
using Journey.Domain;

namespace Journey.Service.Features.Imports;

public record SourceInput(
    string Name,
    byte[] Bytes,
    string SourceIdentity = "default",
    string Mapping = "{}",
    string? OriginalHash = null,
    string RecordLocation = "$"
);

public record ImportCapability(
    string Id,
    string Provider,
    string Status,
    string Version,
    string[] Extensions,
    string Limitations
)
{
    public string RequiredPermission => "workspace:edit";
    public string Incremental =>
        "Stable source identity and external IDs; changed messages require review";
    public string Relationships => "Explicit IDs only; missing targets remain unresolved";
    public string Attachments =>
        "Exact supplied local references recovered; ambiguous or absent files remain unresolved";
}

public record DetectionCandidate(
    string AdapterId,
    string Provider,
    double Confidence,
    string Status
);

public record ImportPreview(
    string Provider,
    List<Conversation> Conversations,
    List<string> Warnings,
    List<SourceProject>? Projects = null
);

public record ImportResult(List<Conversation> Conversations, List<SourceProject>? Projects = null);

public interface IConversationImporter
{
    string Provider { get; }
    ImportCapability Capability =>
        new(
            Provider.ToLowerInvariant().Replace(" ", "-"),
            Provider,
            "implemented",
            "1",
            Provider == "Generic" ? [".json", ".jsonl", ".ndjson", ".md", ".markdown", ".txt"]
                : Provider is "Codex" or "Claude Code" ? [".jsonl"]
                : [".json"],
            "Only fixture-validated shapes are supported; projects, branches and tools are recovered where explicit in source."
        );
    double Confidence(SourceInput source) => CanImport(source) ? 0.9 : 0;
    bool CanImport(SourceInput source);
    Task<ImportPreview> InspectAsync(SourceInput source, CancellationToken ct) =>
        PreviewAsync(source, ct);
    async Task<string[]> ValidateAsync(SourceInput source, CancellationToken ct)
    {
        try
        {
            var preview = await PreviewAsync(source, ct);
            return preview.Warnings.ToArray();
        }
        catch (Exception ex) when (ex is InvalidDataException or JsonException or ArgumentException)
        {
            return [ex.Message];
        }
    }
    Task<ImportPreview> PreviewAsync(SourceInput source, CancellationToken ct);
    Task<ImportResult> ImportAsync(SourceInput source, CancellationToken ct);
}

public static class Value
{
    public static JsonElement Get(this JsonElement e, string key) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(key, out var v) ? v : default;

    public static string? Str(this JsonElement e) =>
        e.ValueKind == JsonValueKind.String ? e.GetString()
        : e.ValueKind == JsonValueKind.Number ? e.GetRawText()
        : null;

    public static IEnumerable<JsonElement> Items(this JsonElement e) =>
        e.ValueKind == JsonValueKind.Array ? e.EnumerateArray() : [];

    public static DateTime? Date(this JsonElement e)
    {
        if (e.ValueKind == JsonValueKind.Number && e.TryGetDouble(out var n))
        {
            try
            {
                return DateTime.UnixEpoch.AddSeconds(n);
            }
            catch
            {
                return null;
            }
        }
        return DateTime.TryParse(e.Str(), out var d) ? d.ToUniversalTime() : null;
    }

    public static string Hash(byte[] b) => Convert.ToHexString(SHA256.HashData(b));

    public static string Hash(string s) => Hash(Encoding.UTF8.GetBytes(s));
}

public abstract class Importer : IConversationImporter
{
    public abstract string Provider { get; }
    public abstract bool CanImport(SourceInput s);
    protected abstract List<Conversation> Parse(SourceInput s);

    public static bool HasExtension(SourceInput source, params string[] extensions) =>
        extensions.Contains(Path.GetExtension(source.Name).ToLowerInvariant());

    public Task<ImportPreview> PreviewAsync(SourceInput s, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var cs = Parse(s);
        if (cs.Count == 0)
            throw new InvalidDataException("No supported conversations found.");
        foreach (var c in cs)
        {
            c.SourceIdentity = s.SourceIdentity;
            c.SourceKey = Value.Hash(c.Provider + "\n" + s.SourceIdentity + "\n" + c.RawPayload);
            c.Provenance = Json.Write(
                new
                {
                    adapter = ((IConversationImporter)this).Capability.Id,
                    version = ((IConversationImporter)this).Capability.Version,
                    source = s.Name,
                    hash = s.OriginalHash ?? Value.Hash(s.Bytes),
                    recordLocation = s.RecordLocation,
                    ordering = "source record order; not inferred execution chronology",
                }
            );
            if (c.RawPayload.TrimStart().StartsWith("{"))
            {
                var metadata = JsonDocument.Parse(c.RawPayload).RootElement;
                c.ExternalProjectId ??=
                    metadata.Get("project_uuid").Str()
                    ?? metadata.Get("project_id").Str()
                    ?? metadata.Get("project").Get("id").Str();
                c.SourceProjectName ??= metadata.Get("project").Get("name").Str();
                c.SourcePath ??= metadata.Get("cwd").Str();
            }
            var diagnostics = Json.Read<List<string>>(c.Diagnostics);
            foreach (var m in c.Messages)
            {
                if (m.ExternalParentId != null && m.ParentId == null)
                    diagnostics.Add("Unresolved parent message: " + m.ExternalParentId);
                m.ConversationId = c.Id;
                m.ContentHash = ImportedMessageFingerprint.Compute(m);
                foreach (var b in m.Blocks)
                {
                    b.ConversationMessageId = m.Id;
                    if (b.Kind is "input_text" or "output_text")
                        b.Kind = "text";
                }
            }
            c.Diagnostics = Json.Write(diagnostics);
        }
        return Task.FromResult(
            new ImportPreview(
                Provider,
                cs,
                cs.SelectMany(c => Json.Read<List<string>>(c.Diagnostics)).Distinct().ToList()
            )
        );
    }

    public async Task<ImportResult> ImportAsync(SourceInput s, CancellationToken ct) =>
        new((await PreviewAsync(s, ct)).Conversations);

    protected static JsonElement Root(SourceInput s) =>
        ParsedImportInput.For(s).Root;

    protected static IEnumerable<JsonElement> Records(JsonElement e) =>
        e.ValueKind == JsonValueKind.Array ? e.EnumerateArray()
        : e.Get("conversations").ValueKind == JsonValueKind.Array
            ? e.Get("conversations").EnumerateArray()
        : [e];

    protected Conversation New(JsonElement e, string format) =>
        new()
        {
            Provider = Provider,
            SourceFormat = format,
            ProviderId = e.Get("id").Str() ?? e.Get("uuid").Str() ?? e.Get("conversation_id").Str(),
            Title = e.Get("title").Str() ?? e.Get("name").Str() ?? "Untitled conversation",
            RawPayload = e.GetRawText(),
            Metadata = e.GetRawText(),
            SourceCreatedAt = (
                e.Get("create_time").ValueKind != JsonValueKind.Undefined
                    ? e.Get("create_time")
                    : e.Get("created_at")
            ).Date(),
            SourceUpdatedAt = (
                e.Get("update_time").ValueKind != JsonValueKind.Undefined
                    ? e.Get("update_time")
                    : e.Get("updated_at")
            ).Date(),
        };

    protected static List<MessageContentBlock> Blocks(JsonElement content)
    {
        if (content.ValueKind == JsonValueKind.String)
        {
            var text = content.GetString() ?? "";
            return string.IsNullOrWhiteSpace(text) ? [] : [new() { Text = text }];
        }
        var array = content.ValueKind == JsonValueKind.Array ? content : content.Get("parts");
        if (array.ValueKind != JsonValueKind.Array)
            return content.ValueKind == JsonValueKind.Undefined
                ? []
                : Keep([Part(content, 0)]);
        return Keep(array.EnumerateArray().Select(Part));
    }

    static List<MessageContentBlock> Keep(IEnumerable<MessageContentBlock> blocks) =>
        blocks
            .Where(block =>
                block.Kind is not ("text" or "markdown") || !string.IsNullOrWhiteSpace(block.Text)
            )
            .ToList();

    static MessageContentBlock Part(JsonElement part, int order)
    {
        if (part.ValueKind == JsonValueKind.String)
            return new()
            {
                Order = order,
                Kind = "text",
                Text = part.GetString()!,
            };
        return new()
        {
            Order = order,
            Kind =
                part.Get("type").Str()
                ?? part.Get("content_type").Str()
                ?? (part.Get("text").ValueKind == JsonValueKind.String ? "text" : "unknown"),
            Text = part.Get("text").Str() ?? part.Get("content").Str() ?? "",
            Metadata = part.GetRawText(),
        };
    }
}

public class ChatGptImporter : Importer
{
    public override string Provider => "ChatGPT";

    public override bool CanImport(SourceInput s)
    {
        if (!HasExtension(s, ".json"))
            return false;
        try
        {
            return Records(Root(s)).Any(x => x.Get("mapping").ValueKind == JsonValueKind.Object);
        }
        catch
        {
            return false;
        }
    }

    protected override List<Conversation> Parse(SourceInput s) =>
        Records(Root(s))
            .Select(e =>
            {
                if (e.Get("mapping").ValueKind != JsonValueKind.Object)
                    throw new InvalidDataException("Expected ChatGPT mapping export.");
                var c = New(e, "chatgpt-mapping");
                var map = e.Get("mapping").EnumerateObject().ToList();
                var ids = map.ToDictionary(x => x.Name, x => Guid.NewGuid().ToString("N"));
                var parents = map.ToDictionary(x => x.Name, x => x.Value.Get("parent").Str());
                var valid = map.Where(x => x.Value.Get("message").ValueKind == JsonValueKind.Object)
                    .Select(x => x.Name)
                    .ToHashSet();
                foreach (var entry in map)
                {
                    var m = entry.Value.Get("message");
                    if (m.ValueKind != JsonValueKind.Object)
                        continue;
                    var p = parents[entry.Name];
                    var visited = new HashSet<string>();
                    while (p != null && !valid.Contains(p) && visited.Add(p))
                        p = parents.GetValueOrDefault(p);
                    c.Messages.Add(
                        new()
                        {
                            Id = ids[entry.Name],
                            ProviderId = m.Get("id").Str() ?? entry.Name,
                            ExternalParentId = p,
                            ParentId = p != null && valid.Contains(p) ? ids[p] : null,
                            Order = c.Messages.Count,
                            Role = m.Get("author").Get("role").Str() ?? "unknown",
                            Model = m.Get("metadata").Get("model_slug").Str(),
                            Timestamp = m.Get("create_time").Date(),
                            Blocks = Blocks(m.Get("content")),
                            Metadata = m.GetRawText(),
                        }
                    );
                }
                c.Model = c.Messages.LastOrDefault(m => m.Model != null)?.Model;
                return c;
            })
            .ToList();
}

public class ClaudeImporter : Importer
{
    public override string Provider => "Claude";

    public override bool CanImport(SourceInput s)
    {
        if (!HasExtension(s, ".json"))
            return false;
        try
        {
            return Records(Root(s))
                .Any(x => x.Get("chat_messages").ValueKind == JsonValueKind.Array);
        }
        catch
        {
            return false;
        }
    }

    protected override List<Conversation> Parse(SourceInput s) =>
        Records(Root(s))
            .Select(e =>
            {
                if (e.Get("chat_messages").ValueKind != JsonValueKind.Array)
                    throw new InvalidDataException("Expected Claude chat_messages export.");
                var c = New(e, "claude-export");
                var ms = e.Get("chat_messages").Items().ToList();
                var ids = ms.Where(m => m.Get("uuid").Str() != null)
                    .ToDictionary(m => m.Get("uuid").Str()!, _ => Guid.NewGuid().ToString("N"));
                foreach (var m in ms)
                {
                    var p = m.Get("parent_message_uuid").Str();
                    c.Messages.Add(
                        new()
                        {
                            Id =
                                ids.GetValueOrDefault(m.Get("uuid").Str() ?? "")
                                ?? Guid.NewGuid().ToString("N"),
                            ProviderId = m.Get("uuid").Str(),
                            ExternalParentId = p,
                            ParentId = p != null ? ids.GetValueOrDefault(p) : null,
                            Order = c.Messages.Count,
                            Role =
                                m.Get("sender").Str() == "human"
                                    ? "user"
                                    : m.Get("sender").Str() ?? "unknown",
                            Timestamp = m.Get("created_at").Date(),
                            Model = m.Get("model").Str(),
                            Blocks = Blocks(
                                m.Get("content").Items().Any() ? m.Get("content") : m.Get("text")
                            ),
                            Metadata = m.GetRawText(),
                        }
                    );
                }
                c.Model = c.Messages.LastOrDefault(m => m.Model != null)?.Model;
                return c;
            })
            .ToList();
}

public class GenericImporter : SessionImporter
{
    public override string Provider => "Generic";

    public override bool CanImport(SourceInput s) =>
        new[] { ".json", ".md", ".markdown", ".txt", ".jsonl", ".ndjson" }.Contains(
            Path.GetExtension(s.Name).ToLowerInvariant()
        );

    protected override List<Conversation> Parse(SourceInput s)
    {
        var ext = Path.GetExtension(s.Name).ToLowerInvariant();
        var text = Encoding.UTF8.GetString(s.Bytes);
        if (ext is ".md" or ".markdown" or ".txt")
            return
            [
                new()
                {
                    Title = Path.GetFileNameWithoutExtension(s.Name),
                    Provider = Provider,
                    SourceFormat = "markdown",
                    RawPayload = text,
                    Messages = [new() { Role = "document", Blocks = [new() { Text = text }] }],
                },
            ];
        var warnings = new List<string>();
        JsonElement root;
        if (ext is ".jsonl" or ".ndjson")
        {
            var lines = Lines(s, out warnings);
            root = lines.All(l => l.Get("role").Str() != null)
                ? JsonSerializer.SerializeToElement(
                    new { title = Path.GetFileNameWithoutExtension(s.Name), messages = lines }
                )
                : JsonSerializer.SerializeToElement(lines);
        }
        else
            root = Root(s);
        if (root.Get("conversations").ValueKind == JsonValueKind.Array)
            root = root.Get("conversations");
        if (
            root.ValueKind == JsonValueKind.Array
            && root.Items().All(m => m.Get("role").Str() != null)
        )
            root = JsonSerializer.SerializeToElement(
                new { title = Path.GetFileNameWithoutExtension(s.Name), messages = root }
            );
        return Records(root)
            .Select(e =>
            {
                if (e.Get("messages").ValueKind != JsonValueKind.Array)
                    throw new InvalidDataException(
                        "Unsupported schema. Choose a compatible adapter or provide a manual field mapping."
                    );
                var c = New(e, "generic-v1");
                c.Diagnostics = Json.Write(warnings);
                c.Provider = e.Get("provider").Str() ?? Provider;
                c.Model = e.Get("model").Str();
                var ms = e.Get("messages").Items().ToList();
                var ids = ms.Where(m => m.Get("id").Str() != null)
                    .ToDictionary(m => m.Get("id").Str()!, _ => Guid.NewGuid().ToString("N"));
                foreach (var m in ms)
                {
                    if (m.Get("role").Str() == null)
                        throw new InvalidDataException("Every message requires a role.");
                    var p = m.Get("parentId").Str();
                    c.Messages.Add(
                        new()
                        {
                            Id =
                                ids.GetValueOrDefault(m.Get("id").Str() ?? "")
                                ?? Guid.NewGuid().ToString("N"),
                            ProviderId = m.Get("id").Str(),
                            ExternalParentId = p,
                            ParentId = p != null ? ids.GetValueOrDefault(p) : null,
                            Order = c.Messages.Count,
                            Role = m.Get("role").Str()!,
                            Model = m.Get("model").Str(),
                            Timestamp = m.Get("timestamp").Date(),
                            Blocks = Blocks(m.Get("content")),
                            Metadata = m.GetRawText(),
                        }
                    );
                }
                return c;
            })
            .ToList();
    }
}
