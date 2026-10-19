using System.IO.Compression;
using System.Text;
using System.Text.Json;
using Journey.Data;
using Journey.Domain;
using Journey.Service.Features.Imports;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Journey.Tests;

public class UniversalImportTests
{
    static SourceInput Fixture(string name) =>
        new(name, File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "fixtures", name)));

    [Theory]
    [InlineData("claude-code.jsonl", "Claude Code", 2)]
    [InlineData("codex-rollout.jsonl", "Codex", 1)]
    [InlineData("gemini-cli.json", "Gemini CLI", 1)]
    [InlineData("api-message.json", "API messages", 1)]
    [InlineData("vscode-chat.json", "VS Code Chat", 1)]
    [InlineData("continue.json", "Continue", 1)]
    [InlineData("opencode.json", "OpenCode", 1)]
    [InlineData("api_conversation_history.json", "Cline / Roo Code", 1)]
    [InlineData(".aider.chat.history.md", "Aider", 1)]
    [InlineData("otel.json", "OpenTelemetry", 1)]
    [InlineData("transcript.html", "HTML transcript", 1)]
    [InlineData("langchain.json", "LangChain", 1)]
    [InlineData("cursor-agent.jsonl", "Cursor", 1)]
    public async Task DetectsAndPreservesFixture(string file, string provider, int count)
    {
        var source = Fixture(file);
        var adapter = new ImportRegistry().Detect(source);
        var result = await adapter.PreviewAsync(source, default);
        Assert.Equal(count, result.Conversations.Count);
        Assert.All(
            result.Conversations,
            c =>
            {
                Assert.Equal(provider, c.Provider);
                Assert.NotEmpty(c.Messages);
                Assert.NotEmpty(c.RawPayload);
                Assert.Contains("hash", c.Provenance);
            }
        );
    }

    [Fact]
    public async Task ImportsProjectMetadataWithoutInventingConversations()
    {
        var source = Fixture("projects.json");
        var adapter = new ImportRegistry().Detect(source);
        var preview = await adapter.PreviewAsync(source, default);
        Assert.Empty(preview.Conversations);
        Assert.Single(preview.Projects!);
        Assert.Equal("claude-project-a", preview.Projects![0].ExternalId);
        Assert.Contains("prompt_template", preview.Projects[0].Metadata);
    }

    [Fact]
    public async Task PreservesBranchesSubagentsToolsAndPartialErrors()
    {
        var source = Fixture("claude-code.jsonl");
        source = source with
        {
            Bytes = source.Bytes.Concat(Encoding.UTF8.GetBytes("\nnot json\n")).ToArray(),
        };
        var result = await new ClaudeCodeImporter().PreviewAsync(source, default);
        var main = result.Conversations[0];
        Assert.Equal(main.Messages[0].Id, main.Messages[1].ParentId);
        Assert.Contains("tool_use", main.Messages[1].Blocks[0].Kind);
        Assert.Contains("call-1", main.Messages[2].Blocks[0].Metadata);
        Assert.Equal(main.ProviderId, result.Conversations[1].ParentConversationExternalId);
        Assert.Contains("Malformed", main.Diagnostics);
    }

    [Fact]
    public async Task IncrementalPreservesAnchoredContentAndAppendsNewMessages()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        using var db = new AppDb(
            new DbContextOptionsBuilder<AppDb>().UseSqlite(connection).Options
        );
        db.Database.EnsureCreated();
        var batch = new ImportBatch { WorkspaceId = "w" };
        db.Add(batch);
        var source = new ImportSource { WorkspaceId = "w", BatchId = batch.Id };
        db.Add(source);
        await db.SaveChangesAsync();
        var importer = new GenericImporter();
        async Task<Conversation> Parse(string text) =>
            (
                await importer.PreviewAsync(new("x.json", Encoding.UTF8.GetBytes(text)), default)
            ).Conversations.Single();
        var first = await Parse(
            """{"id":"c","messages":[{"id":"a","role":"user","content":"original"}]}"""
        );
        await IncrementalImport.Save(db, batch, source, first, default);
        var messageId = first.Messages[0].Id;
        var blockId = first.Messages[0].Blocks[0].Id;
        db.Add(
            new Annotation
            {
                WorkspaceId = "w",
                ConversationId = first.Id,
                MessageId = messageId,
                Anchor = "{}",
            }
        );
        await db.SaveChangesAsync();
        var next = await Parse(
            """{"id":"c","messages":[{"id":"a","role":"user","content":"changed"},{"id":"b","parentId":"a","role":"assistant","content":"new"}]}"""
        );
        await IncrementalImport.Save(db, batch, source, next, default);
        Assert.Equal(2, first.Messages.Count);
        Assert.Equal("original", first.Messages[0].Blocks[0].Text);
        Assert.Equal(blockId, first.Messages[0].Blocks[0].Id);
        Assert.Equal(messageId, first.Messages[1].ParentId);
        Assert.Single(db.Set<ImportConflict>());
        Assert.Single(db.Annotations);
        await IncrementalImport.Save(db, batch, source, next, default);
        Assert.Single(db.Conversations);
        Assert.Equal(2, first.Messages.Count);
    }

    [Fact]
    public async Task RejectsTraversalAndKeepsArchivesBounded()
    {
        using var bytes = new MemoryStream();
        using (var zip = new ZipArchive(bytes, ZipArchiveMode.Create, true))
        {
            using var writer = new StreamWriter(zip.CreateEntry("../escape.json").Open());
            writer.Write("{}");
        }
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            ArchiveInspector.Expand(new("bad.zip", bytes.ToArray()), default)
        );
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            ArchiveInspector.ReadBounded(new MemoryStream(new byte[101]), 100, default)
        );
    }

    [Fact]
    public async Task CursorReadsOnlySupportedRowsFromReadOnlySnapshot()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".db");
        byte[] bytes;
        try
        {
            using (var connection = new SqliteConnection("Data Source=" + path + ";Pooling=False"))
            {
                connection.Open();
                using var cmd = connection.CreateCommand();
                cmd.CommandText =
                    "CREATE TABLE ItemTable(key TEXT,value TEXT); INSERT INTO ItemTable VALUES ('credentials','PRIVATE'),('composer.composerData',$payload)";
                cmd.Parameters.AddWithValue(
                    "$payload",
                    """{"allComposers":[{"composerId":"cursor-a","name":"Task","fullConversation":[{"bubbleId":"u","type":1,"text":"Prompt"},{"bubbleId":"a","parentBubbleId":"u","type":2,"text":"Answer"}]}]}"""
                );
                cmd.ExecuteNonQuery();
            }
            bytes = File.ReadAllBytes(path);
        }
        finally
        {
            File.Delete(path);
        }
        var source = new SourceInput("state.vscdb", bytes);
        var adapter = new ImportRegistry().Detect(source);
        Assert.Equal("experimental", adapter.Capability.Status);
        var c = (await adapter.PreviewAsync(source, default)).Conversations.Single();
        Assert.Equal(2, c.Messages.Count);
        Assert.Equal(c.Messages[0].Id, c.Messages[1].ParentId);
        Assert.DoesNotContain("PRIVATE", c.RawPayload);
    }

    [Fact]
    public async Task ManualMappingPreservesExplicitFields()
    {
        var source = new SourceInput(
            "custom.json",
            Encoding.UTF8.GetBytes(
                """{"turns":[{"who":"user","body":"Exact  text","key":"u"},{"who":"assistant","body":"Response","key":"a","parent":"u"}]}"""
            ),
            Mapping: Json.Write(
                new FieldMapping(
                    ArrayPath: "turns",
                    Role: "who",
                    Content: "body",
                    Id: "key",
                    Parent: "parent"
                )
            )
        );
        var c = (await new MappingImporter().PreviewAsync(source, default)).Conversations.Single();
        Assert.Equal("Exact  text", c.Messages[0].Blocks[0].Text);
        Assert.Equal(c.Messages[0].Id, c.Messages[1].ParentId);
    }
}
