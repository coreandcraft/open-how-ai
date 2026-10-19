using System.IO.Compression;
using System.Text;
using Journey.Data;
using Journey.Domain;
using Journey.Service.Features.Imports;
using Journey.Service.Features.Review;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace Journey.Tests;

public class ImportRecoveryTests
{
    [Fact]
    public async Task AcceptedRevisionRetainsHistoryAndRecoversOnlyUnambiguousAnchors()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        using var db = new AppDb(
            new DbContextOptionsBuilder<AppDb>().UseSqlite(connection).Options
        );
        db.Database.EnsureCreated();
        var message = new ConversationMessage
        {
            ProviderId = "m",
            ContentHash = "original",
            Role = "assistant",
            Blocks = [new() { Text = "Keep this sentence. Removed quote." }],
        };
        var c = new Conversation { WorkspaceId = "w", Messages = [message] };
        message.ConversationId = c.Id;
        message.Blocks[0].ConversationMessageId = message.Id;
        db.Add(c);
        var blockId = message.Blocks[0].Id;
        var kept = new Annotation
        {
            WorkspaceId = "w",
            ConversationId = c.Id,
            MessageId = message.Id,
            Anchor = Json.Write(Anchoring.Create(blockId, message.Blocks[0].Text, 0, 19)),
        };
        var missing = new Annotation
        {
            WorkspaceId = "w",
            ConversationId = c.Id,
            MessageId = message.Id,
            Anchor = Json.Write(Anchoring.Create(blockId, message.Blocks[0].Text, 20, 34)),
        };
        var incoming = new ConversationMessage
        {
            Role = "assistant",
            ContentHash = "next",
            Blocks = [new() { Text = "New introduction. Keep this sentence." }],
        };
        var conflict = new ImportConflict
        {
            WorkspaceId = "w",
            ConversationId = c.Id,
            MessageId = message.Id,
            BaseHash = message.ContentHash,
            Incoming = Json.Write(incoming),
        };
        db.AddRange(kept, missing, conflict);
        await db.SaveChangesAsync();
        await ImportConflictReview.Accept(db, conflict, "author", default);
        Assert.False(kept.Orphaned);
        Assert.True(missing.Orphaned);
        Assert.Equal(18, Json.Read<AnnotationAnchor>(kept.Anchor).Start);
        Assert.Equal(blockId, message.Blocks[0].Id);
        Assert.Contains(
            "Removed quote",
            (await db.Set<ImportedMessageVersion>().SingleAsync()).Payload
        );
        Assert.Equal("accepted", conflict.Status);
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            ImportConflictReview.Accept(db, conflict, "author", default)
        );
        var stale = new ImportConflict
        {
            WorkspaceId = "w",
            ConversationId = c.Id,
            MessageId = message.Id,
            BaseHash = "original",
            Incoming = Json.Write(incoming),
        };
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() =>
            ImportConflictReview.Accept(db, stale, "author", default)
        );
    }

    [Fact]
    public void AttachmentMatchingRequiresExactUnambiguousLocalReferences()
    {
        (string Id, string Name)[] files =
        [
            ("a", "account/files/picture.png"),
            ("b", "other/picture.png"),
        ];
        Assert.Equal(
            "a",
            SourceRecovery.Match("files/picture.png", "account/conversations.json", files)
        );
        Assert.Null(SourceRecovery.Match("picture.png", "conversations.json", files));
        Assert.Null(
            SourceRecovery.Match(
                "https://example.com/picture.png",
                "account/conversations.json",
                files
            )
        );
        Assert.Null(
            SourceRecovery.Match("../files/picture.png", "account/conversations.json", files)
        );
        Assert.Null(
            SourceRecovery.Match(
                "account/files/picture.png",
                "conversations.json",
                [.. files, ("c", "account/files/picture.png")]
            )
        );
        Assert.True(
            PrivateAssetImporter.Recognized(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 })
        );
        Assert.False(
            PrivateAssetImporter.Recognized(Encoding.UTF8.GetBytes("<script>no</script>"))
        );
    }

    [Fact]
    public void ChatGptLibraryIndexRenamesDatPayloadsAndMatchesTheirIds()
    {
        const string id = "file_00000000aaaaaaaaaaaaaaaaaaaaaaaa";
        var json = Encoding.UTF8.GetBytes(
            """
            [
              {
                "file_extension": "png",
                "file_id": "file_00000000aaaaaaaaaaaaaaaaaaaaaaaa",
                "file_name": "image(20261011-070924).png"
              },
              {
                "file_extension": "exe",
                "file_id": "file_00000000bbbbbbbbbbbbbbbbbbbbbbbb",
                "file_name": "program.exe"
              },
              {
                "file_extension": "png",
                "file_id": "file_00000000cccccccccccccccccccccccc",
                "file_name": "../escaped.png"
              }
            ]
            """
        );
        var index = new Dictionary<string, ChatGptLibraryFiles.Entry>(
            StringComparer.OrdinalIgnoreCase
        );
        var ambiguous = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        ChatGptLibraryFiles.Add(index, ambiguous, json);
        Assert.Equal(
            "export/" + id + "/image(20261011-070924).png",
            ChatGptLibraryFiles.RenamedPath("export/" + id + ".dat", index)
        );
        Assert.Equal(
            "export/file_00000000bbbbbbbbbbbbbbbbbbbbbbbb/program.exe",
            ChatGptLibraryFiles.RenamedPath(
                "export/file_00000000bbbbbbbbbbbbbbbbbbbbbbbb.dat",
                index
            )
        );
        Assert.Equal(SuppliedFileTypes.Image, SuppliedFileTypes.Kind("export/" + id + "/image.png"));
        Assert.Equal(SuppliedFileTypes.Pdf, SuppliedFileTypes.Kind("notes.pdf"));
        Assert.Equal(SuppliedFileTypes.Video, SuppliedFileTypes.Kind("clip.mp4"));
        Assert.Equal(SuppliedFileTypes.Audio, SuppliedFileTypes.Kind("voice.mp3"));
        Assert.Equal(SuppliedFileTypes.Text, SuppliedFileTypes.Kind("readme.md"));
        Assert.Equal(SuppliedFileTypes.Document, SuppliedFileTypes.Kind("report.docx"));
        Assert.Equal(SuppliedFileTypes.Download, SuppliedFileTypes.Kind("bundle.zip"));
        Assert.Equal(SuppliedFileTypes.Download, SuppliedFileTypes.Kind("program.exe"));
        Assert.Equal(SuppliedFileTypes.Download, SuppliedFileTypes.Kind("page.html"));
        Assert.Contains("attachment", SuppliedFileTypes.ContentDisposition("program.exe", false));
        Assert.Contains("inline", SuppliedFileTypes.ContentDisposition("image.png", false));
        Assert.Equal(
            "export/file_00000000cccccccccccccccccccccccc/file_00000000cccccccccccccccccccccccc.png",
            ChatGptLibraryFiles.RenamedPath(
                "export/file_00000000cccccccccccccccccccccccc.dat",
                index
            )
        );
        var stored = "export/" + id + "/image(20261011-070924).png";
        (string Id, string Name)[] files = [("asset", stored)];
        Assert.Equal(
            "asset",
            SourceRecovery.Match("sediment://" + id, "export/conversations.json", files)
        );
        Assert.Equal(
            "asset",
            SourceRecovery.Match("file-service://" + id, "export/conversations.json", files)
        );
        Assert.Equal(
            "asset",
            SourceRecovery.Match(id + ".dat", "export/conversations.json", files)
        );
        Assert.Equal(
            "asset",
            SourceRecovery.Match(
                "sediment://files/" + id + "/original.png",
                "export/conversations.json",
                files
            )
        );
        Assert.Equal(
            "image/png",
            SuppliedFileTypes.MediaTypeFromSignature(
                new byte[] { 137, 80, 78, 71, 13, 10, 26, 10, 0 }
            )
        );
        Assert.Null(SuppliedFileTypes.MediaTypeFromSignature("MZ"u8.ToArray()));
        Assert.Contains(
            "inline",
            SuppliedFileTypes.ContentDisposition(id + ".dat", false, true)
        );
        Assert.Null(
            SourceRecovery.Match("https://example.com/" + id, "export/conversations.json", files)
        );
        Assert.Null(
            SourceRecovery.Match(
                "sediment://" + id,
                "export/conversations.json",
                [.. files, ("other", "elsewhere/" + id + "/other.png")]
            )
        );
    }

    [Fact]
    public void OfficeDocumentTextIsExtractedAsPlainText()
    {
        using var buffer = new MemoryStream();
        using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, true))
        {
            var entry = zip.CreateEntry("word/document.xml");
            using var writer = new StreamWriter(entry.Open());
            writer.Write(
                """
                <?xml version="1.0" encoding="UTF-8"?>
                <w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
                  <w:body><w:p><w:r><w:t>Hello library</w:t></w:r></w:p></w:body>
                </w:document>
                """
            );
        }
        buffer.Position = 0;
        Assert.Contains("Hello library", SuppliedFileTypes.ReadText(buffer, "report.docx"));
        buffer.Position = 0;
        Assert.Null(SuppliedFileTypes.ReadText(buffer, "program.exe"));
    }

    [Theory]
    [InlineData("image.png")]
    [InlineData("media/preview.mp4")]
    [InlineData("media/preview.webm")]
    public async Task RecoveryPairsOnlyExplicitToolIdsAndPreservesPrivateAttachments(string assetPath)
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        using var db = new AppDb(
            new DbContextOptionsBuilder<AppDb>().UseSqlite(connection).Options
        );
        db.Database.EnsureCreated();
        var source = new ImportSource
        {
            WorkspaceId = "w",
            BatchId = "b",
            Name = "export/conversation.json",
        };
        var asset = new ImportSource
        {
            WorkspaceId = "w",
            BatchId = "b",
            Name = "export/" + assetPath,
            AdapterId = "private-asset",
            Hash = "hash",
        };
        var c = (
            await new ClaudeCodeImporter().PreviewAsync(
                new(
                    "claude-code.jsonl",
                    File.ReadAllBytes(
                        Path.Combine(AppContext.BaseDirectory, "fixtures", "claude-code.jsonl")
                    )
                ),
                default
            )
        ).Conversations[0];
        c.SourceId = source.Id;
        c.WorkspaceId = "w";
        c.Messages[0]
            .Blocks.Add(
                new()
                {
                    ConversationMessageId = c.Messages[0].Id,
                    Metadata = Json.Write(new { type = "file", source = new { url = assetPath } }),
                }
            );
        db.Add(new ImportBatch { Id = "b", WorkspaceId = "w" });
        db.AddRange(source, asset, c);
        await db.SaveChangesAsync();
        await SourceRecovery.Rebuild(db, c, default);
        Assert.Equal(
            "call-and-result-recorded",
            (await db.Set<AgentExecution>().SingleAsync()).Status
        );
        Assert.Equal(
            asset.Id,
            (
                await db.Set<ImportedAttachment>().SingleAsync(a => a.Reference == assetPath)
            ).SourceId
        );
        await SourceRecovery.Rebuild(db, c, default);
        Assert.Single(db.Set<AgentExecution>());
        Assert.Equal(2, db.Set<ImportedAttachment>().Count());
    }

    [Fact]
    public void DiscoveryCannotInspectAnythingWithoutConsentAndOperatorEnablement()
    {
        var discovery = new LocalImportDiscovery(new ConfigurationBuilder().Build());
        Assert.False(discovery.Enabled("user"));
        Assert.Throws<UnauthorizedAccessException>(() =>
            discovery.Scan("user", ["codex"], [], true)
        );
        Assert.Throws<UnauthorizedAccessException>(() => discovery.Open("user", "forged"));
        var enabled = new LocalImportDiscovery(
            new ConfigurationBuilder()
                .AddInMemoryCollection(
                    new Dictionary<string, string?> { ["Imports:DiscoveryUserIds:0"] = "user" }
                )
                .Build()
        );
        Assert.Throws<UnauthorizedAccessException>(() =>
            enabled.Scan("user", ["codex"], [], false)
        );
        Assert.Throws<InvalidDataException>(() =>
            enabled.Scan("user", ["../../arbitrary"], [], true)
        );
    }
}
