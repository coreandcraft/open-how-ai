using Journey.Domain;

namespace Journey.Service.Features.Imports;

public static class ImportedMessageFingerprint
{
    public static string Compute(ConversationMessage message) =>
        Value.Hash(
            Json.Write(
                new
                {
                    message.Role,
                    message.Model,
                    message.ExternalParentId,
                    message.Timestamp,
                    message.Metadata,
                    blocks = message
                        .Blocks.OrderBy(b => b.Order)
                        .Select(b => new
                        {
                            kind = b.Kind is "input_text" or "output_text" ? "text" : b.Kind,
                            b.Text,
                            b.Metadata,
                        }),
                }
            )
        );
}
