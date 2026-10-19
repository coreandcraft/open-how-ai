using System.Diagnostics;
using System.Text;
using Journey.Service.Features.Imports;

namespace Journey.Tests;

public class TranscriptReadTests
{
    [Fact]
    public async Task CursorTranscriptKeepsPromptTextAndToolRecords()
    {
        var source = new SourceInput(
            "cursor-agent.jsonl",
            await File.ReadAllBytesAsync(
                Path.Combine(AppContext.BaseDirectory, "fixtures", "cursor-agent.jsonl")
            )
        );
        var result = await new ImportRegistry().Detect(source).PreviewAsync(source, default);
        var conversation = Assert.Single(result.Conversations);
        Assert.Equal("Cursor", conversation.Provider);
        Assert.Equal(3, conversation.Messages.Count);
        Assert.Equal("Review the scaling notes", conversation.Messages[0].Blocks[0].Text);
        Assert.Contains(
            conversation.Messages[1].Blocks,
            b => b.Kind == "tool_use" && b.Metadata.Contains("scaling-strategy.md")
        );
        Assert.Equal("turn_ended: success", conversation.Messages[2].Blocks[0].Text);
        Assert.Equal(conversation.Messages[0].Id, conversation.Messages[1].ParentId);
    }

    [Fact]
    public async Task CursorTranscriptLiftsTheTimestampOutOfThePrompt()
    {
        var line =
            """
            {"role":"user","message":{"content":[{"type":"text","text":"<timestamp>Friday, Oct 9, 2026, 5:02 PM (UTC-4)</timestamp>\n<user_query>\nIn docs there is a scaling strategy.\n</user_query>"}]}}
            {"role":"user","message":{"content":[{"type":"text","text":"<timestamp>Saturday, Oct 10, 2026, 1:58 AM (UTC-4)</timestamp>"}]}}
            """;
        var source = new SourceInput("session.jsonl", Encoding.UTF8.GetBytes(line));
        var result = await new ImportRegistry().Detect(source).PreviewAsync(source, default);
        var messages = Assert.Single(result.Conversations).Messages;
        Assert.Equal("In docs there is a scaling strategy.", messages[0].Blocks[0].Text);
        Assert.Equal(new DateTime(2026, 10, 9, 21, 2, 0, DateTimeKind.Utc), messages[0].Timestamp);
        Assert.Equal("", messages[1].Blocks[0].Text);
        Assert.Equal(new DateTime(2026, 10, 10, 5, 58, 0, DateTimeKind.Utc), messages[1].Timestamp);
        CursorTranscript.Lift(messages[0]);
        Assert.Equal("In docs there is a scaling strategy.", messages[0].Blocks[0].Text);
    }

    [Fact]
    public async Task LargeMarkdownTranscriptIsRecognizedQuickly()
    {
        var body = string.Join('\n', Enumerable.Repeat("This is prose, not JSON.", 20_000));
        var source = new SourceInput("segment.md", Encoding.UTF8.GetBytes(body));
        var clock = Stopwatch.StartNew();
        var adapter = new ImportRegistry().Detect(source);
        var preview = await adapter.PreviewAsync(source, default);
        clock.Stop();
        Assert.Equal("Generic", adapter.Provider);
        Assert.Equal(body, Assert.Single(Assert.Single(preview.Conversations).Messages).Blocks[0].Text);
        Assert.True(clock.ElapsedMilliseconds < 500, "elapsed " + clock.ElapsedMilliseconds);
    }
}
