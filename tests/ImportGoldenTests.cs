using System.Text.Json;
using Journey.Domain;
using Journey.Service.Features.Imports;

namespace Journey.Tests;

public class ImportGoldenTests
{
    [Theory]
    [InlineData("claude-code.jsonl", "claude-code.json")]
    [InlineData("codex-rollout.jsonl", "codex-rollout.json")]
    [InlineData("gemini-cli.json", "gemini-cli.json")]
    public async Task MatchesStableNormalizedGraph(string fixture, string golden)
    {
        var root = Path.Combine(AppContext.BaseDirectory, "fixtures");
        var input = new SourceInput(fixture, File.ReadAllBytes(Path.Combine(root, fixture)));
        var preview = await new ImportRegistry().Detect(input).PreviewAsync(input, default);
        var actual = JsonSerializer.SerializeToElement(
            preview.Conversations.Select(c => new
            {
                provider = c.Provider,
                externalId = c.ProviderId,
                parentSession = c.ParentConversationExternalId,
                roles = c.Messages.Select(m => m.Role),
                parents = c.Messages.Select(m => m.ExternalParentId),
                kinds = c.Messages.SelectMany(m => m.Blocks.Select(b => b.Kind)),
            })
        );
        var expected = JsonDocument
            .Parse(File.ReadAllText(Path.Combine(root, "golden", golden)))
            .RootElement;
        Assert.True(
            JsonElement.DeepEquals(expected, actual),
            $"Expected {expected}\nActual {actual}"
        );
    }
}
