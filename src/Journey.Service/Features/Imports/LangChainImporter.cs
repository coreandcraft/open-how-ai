using System.Text.Json;
using Journey.Domain;

namespace Journey.Service.Features.Imports;

public class LangChainImporter : SessionImporter, IConversationImporter
{
    public override string Provider => "LangChain";
    public ImportCapability Capability =>
        new(
            "langchain-messages-v1",
            Provider,
            "implemented",
            "1",
            [".json"],
            "Serialized message arrays with type and data.content. Application-specific agent state remains unsupported."
        );

    public override bool CanImport(SourceInput source)
    {
        if (!HasExtension(source, ".json"))
            return false;
        try
        {
            var root = Root(source);
            return root.ValueKind == JsonValueKind.Array
                && root.Items()
                    .Any(m =>
                        m.Get("data").Get("content").ValueKind != JsonValueKind.Undefined
                        && m.Get("type").Str() is "human" or "ai" or "system" or "tool"
                    );
        }
        catch
        {
            return false;
        }
    }

    protected override List<Conversation> Parse(SourceInput source)
    {
        var root = Root(source);
        var c = new Conversation
        {
            Provider = Provider,
            Title = Path.GetFileNameWithoutExtension(source.Name),
            SourceFormat = "langchain-messages-v1",
            RawPayload = root.GetRawText(),
        };
        foreach (var row in root.Items())
        {
            var m = row.Get("data");
            var role = row.Get("type").Str() switch
            {
                "human" => "user",
                "ai" => "assistant",
                var other => other ?? "unknown",
            };
            c.Messages.Add(
                Message(
                    m,
                    m.Get("id").Str(),
                    m.Get("parentId").Str(),
                    role,
                    m.Get("content"),
                    c.Messages.Count,
                    row
                )
            );
        }
        Parents(c);
        return [c];
    }
}
