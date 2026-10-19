using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Journey.Domain;

namespace Journey.Service.Features.Imports;

public class DeveloperHistoryImporter : SessionImporter, IConversationImporter
{
    public override string Provider => "Developer history";
    ImportCapability IConversationImporter.Capability =>
        new(
            "developer-history-v1",
            Provider,
            "experimental",
            "1",
            [".json", ".md", ".history"],
            "Narrow fixture shapes: VS Code requests, Continue history.message, OpenCode info+parts, Cline/Roo API history arrays, Aider textual logs. Other variants remain unsupported."
        );

    public override bool CanImport(SourceInput source)
    {
        if (Path.GetFileName(source.Name).StartsWith(".aider.", StringComparison.OrdinalIgnoreCase))
            return true;
        if (!HasExtension(source, ".json", ".history"))
            return false;
        try
        {
            var root = Root(source);
            return root.Get("requests").ValueKind == JsonValueKind.Array
                || root.Get("history").Items().Any(h => h.Get("message").Get("role").Str() != null)
                || root.Get("info").Get("id").Str() != null
                    && root.Get("messages")
                        .Items()
                        .Any(m => m.Get("parts").ValueKind == JsonValueKind.Array)
                || Path.GetFileName(source.Name) == "api_conversation_history.json"
                    && root.Items().Any(m => m.Get("role").Str() != null);
        }
        catch
        {
            return false;
        }
    }

    protected override List<Conversation> Parse(SourceInput source)
    {
        if (Path.GetFileName(source.Name).StartsWith(".aider.", StringComparison.OrdinalIgnoreCase))
        {
            var text = Encoding.UTF8.GetString(source.Bytes);
            return
            [
                new()
                {
                    Provider = "Aider",
                    Title = Path.GetFileName(source.Name),
                    SourceFormat = "aider-text-v1",
                    RawPayload = text,
                    Messages = [new() { Role = "document", Blocks = [new() { Text = text }] }],
                    Diagnostics = Json.Write(
                        new[]
                        {
                            "Text log preserved; prompt boundaries, commits and tool outcomes are not inferred.",
                        }
                    ),
                },
            ];
        }
        var root = Root(source);
        var c = New(root, "developer-history-v1");
        if (root.Get("requests").ValueKind == JsonValueKind.Array)
        {
            c.Provider = "VS Code Chat";
            c.ProviderId = root.Get("sessionId").Str();
            foreach (var request in root.Get("requests").Items())
            {
                var id = request.Get("requestId").Str();
                var user = Message(
                    request,
                    id,
                    null,
                    "user",
                    request.Get("message").Get("text"),
                    c.Messages.Count,
                    request
                );
                c.Messages.Add(user);
                foreach (var response in request.Get("response").Items())
                    c.Messages.Add(
                        Message(
                            response,
                            null,
                            id,
                            "assistant",
                            response.Get("value").ValueKind != JsonValueKind.Undefined
                                ? response.Get("value")
                                : response,
                            c.Messages.Count,
                            response
                        )
                    );
            }
        }
        else if (root.Get("history").ValueKind == JsonValueKind.Array)
        {
            c.Provider = "Continue";
            c.ProviderId = root.Get("sessionId").Str();
            c.SourcePath = root.Get("workspaceDirectory").Str();
            foreach (var row in root.Get("history").Items())
            {
                var m = row.Get("message");
                c.Messages.Add(
                    Message(
                        m,
                        m.Get("id").Str(),
                        m.Get("parentId").Str(),
                        m.Get("role").Str() ?? "unknown",
                        m.Get("content"),
                        c.Messages.Count,
                        row
                    )
                );
            }
        }
        else if (root.Get("info").ValueKind == JsonValueKind.Object)
        {
            var info = root.Get("info");
            c.Provider = "OpenCode";
            c.ProviderId = info.Get("id").Str();
            c.ExternalProjectId = info.Get("projectID").Str();
            c.SourcePath = info.Get("directory").Str();
            c.ParentConversationExternalId = info.Get("parentID").Str();
            c.Title = info.Get("title").Str() ?? c.Title;
            foreach (var row in root.Get("messages").Items())
            {
                var m = row.Get("info");
                c.Messages.Add(
                    Message(
                        m,
                        m.Get("id").Str(),
                        m.Get("parentID").Str(),
                        m.Get("role").Str() ?? "unknown",
                        row.Get("parts"),
                        c.Messages.Count,
                        row
                    )
                );
            }
        }
        else
        {
            c.Provider = "Cline / Roo Code";
            c.Title = source.Name;
            c.ProviderId = null;
            foreach (var m in root.Items())
                c.Messages.Add(
                    Message(
                        m,
                        m.Get("id").Str(),
                        null,
                        m.Get("role").Str() ?? "unknown",
                        m.Get("content"),
                        c.Messages.Count,
                        m
                    )
                );
        }
        c.Diagnostics = Json.Write(
            new[]
            {
                "Experimental format variant. Unknown fields are preserved in original records; absence of explicit IDs prevents session merging.",
            }
        );
        Parents(c);
        return [c];
    }
}

public class HtmlTranscriptImporter : Importer, IConversationImporter
{
    public override string Provider => "HTML transcript";
    ImportCapability IConversationImporter.Capability =>
        new(
            "html-document-v1",
            Provider,
            "implemented",
            "1",
            [".html", ".htm"],
            "Document-only reconstruction. No scripts execute; roles and branches are not guessed."
        );

    public override bool CanImport(SourceInput source) =>
        Path.GetExtension(source.Name).ToLowerInvariant() is ".html" or ".htm"
        && Encoding.UTF8.GetString(source.Bytes).Contains('<');

    protected override List<Conversation> Parse(SourceInput source)
    {
        var raw = Encoding.UTF8.GetString(source.Bytes);
        var text = Regex.Replace(
            raw,
            @"<(script|style)\b[^>]*>[\s\S]*?</\1>",
            "",
            RegexOptions.IgnoreCase
        );
        text = Regex.Replace(
            text,
            @"</(p|div|tr|li|h[1-6])>|<br\s*/?>",
            "\n",
            RegexOptions.IgnoreCase
        );
        text = System.Net.WebUtility.HtmlDecode(Regex.Replace(text, "<[^>]*>", ""));
        return
        [
            new()
            {
                Provider = Provider,
                Title = Path.GetFileNameWithoutExtension(source.Name),
                SourceFormat = "html-document-v1",
                RawPayload = raw,
                Messages = [new() { Role = "document", Blocks = [new() { Text = text }] }],
            },
        ];
    }
}

public class TraceImporter : SessionImporter, IConversationImporter
{
    public override string Provider => "OpenTelemetry";
    ImportCapability IConversationImporter.Capability =>
        new(
            "otel-otlp-json-v1",
            Provider,
            "experimental",
            "1",
            [".json"],
            "OTLP resourceSpans/scopeSpans JSON only. Spans are retained as events; arbitrary traces are not interpreted as user/assistant messages."
        );

    public override bool CanImport(SourceInput source)
    {
        if (!HasExtension(source, ".json"))
            return false;
        try
        {
            return Root(source).Get("resourceSpans").ValueKind == JsonValueKind.Array;
        }
        catch
        {
            return false;
        }
    }

    protected override List<Conversation> Parse(SourceInput source)
    {
        var root = Root(source);
        return root.Get("resourceSpans")
            .Items()
            .SelectMany(r => r.Get("scopeSpans").Items())
            .SelectMany(s => s.Get("spans").Items())
            .GroupBy(s => s.Get("traceId").Str())
            .Select(g =>
            {
                var c = new Conversation
                {
                    Provider = Provider,
                    ProviderId = g.Key,
                    Title = "Agent trace " + g.Key,
                    SourceFormat = "otel-otlp-json-v1",
                    RawPayload = Json.Write(g),
                };
                foreach (var span in g)
                    c.Messages.Add(
                        Message(
                            span,
                            span.Get("spanId").Str(),
                            span.Get("parentSpanId").Str(),
                            "event",
                            JsonSerializer.SerializeToElement(
                                new[] { new { type = "trace", text = span.GetRawText() } }
                            ),
                            c.Messages.Count,
                            span
                        )
                    );
                Parents(c);
                return c;
            })
            .ToList();
    }
}
