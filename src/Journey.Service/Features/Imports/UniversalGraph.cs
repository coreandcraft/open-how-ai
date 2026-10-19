using System.Text.Json;
using Journey.Domain;

namespace Journey.Service.Features.Imports;

// This projection keeps recoverable source links explicit without inferring execution or hidden reasoning.
public static class UniversalGraph
{
    public static object Build(Conversation c)
    {
        var tools = new List<object>();
        var attachments = new List<object>();
        var links = new List<object>();
        foreach (var message in c.Messages)
        {
            if (message.ParentId != null)
                links.Add(
                    new
                    {
                        source = message.ParentId,
                        target = message.Id,
                        kind = "parent-message",
                        confidence = "explicit",
                        evidence = message.ExternalParentId,
                    }
                );
            foreach (var block in message.Blocks)
            {
                JsonElement raw;
                try
                {
                    raw = JsonDocument.Parse(block.Metadata).RootElement.Clone();
                }
                catch
                {
                    continue;
                }
                var type = raw.Get("type").Str() ?? block.Kind;
                var callId =
                    raw.Get("call_id").Str()
                    ?? raw.Get("tool_use_id").Str()
                    ?? raw.Get("callID").Str()
                    ?? raw.Get("id").Str();
                if (
                    type
                    is "tool_use"
                        or "tool_call"
                        or "function_call"
                        or "tool_result"
                        or "function_call_output"
                        or "tool"
                )
                    tools.Add(
                        new
                        {
                            messageId = message.Id,
                            blockId = block.Id,
                            externalCallId = callId,
                            type,
                            name = raw.Get("name").Str() ?? raw.Get("tool").Str(),
                            originalPayload = raw,
                        }
                    );
                var reference =
                    raw.Get("asset_pointer").Str()
                    ?? raw.Get("image_url").Get("url").Str()
                    ?? raw.Get("source").Get("url").Str()
                    ?? raw.Get("file_id").Str();
                if (reference != null)
                    attachments.Add(
                        new
                        {
                            messageId = message.Id,
                            blockId = block.Id,
                            sourceReference = reference,
                            availability = "reference-only",
                            originalPayload = raw,
                        }
                    );
            }
        }
        return new
        {
            conversationId = c.Id,
            c.Provider,
            c.ProviderId,
            c.ExternalProjectId,
            c.SourcePath,
            c.ParentConversationExternalId,
            tools,
            attachments,
            relationships = links,
            provenance = c.Provenance,
        };
    }
}
