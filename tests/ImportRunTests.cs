using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Journey.Data;
using Journey.Domain;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Journey.Tests;

public class ImportRunTests
{
    [Fact]
    public async Task LeaseRenewsWhileParserIsIdleAndCancellationStopsItsToken()
    {
        using var factory = new Factory();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDb>();
        var batch = new ImportBatch { WorkspaceId = "cloudplay", Status = "processing", LeaseOwner = "test-worker",
            LeaseExpiresAt = DateTime.UtcNow.AddMinutes(1) };
        db.Add(batch);
        await db.SaveChangesAsync();
        await using var monitor = new ImportRunMonitor(factory.Services.GetRequiredService<IServiceScopeFactory>(),
            batch.Id, "test-worker", NullLogger.Instance, default, TimeSpan.FromMilliseconds(25));
        for (var i = 0; i < 200; i++)
        {
            await db.Entry(batch).ReloadAsync();
            if (batch.LeaseExpiresAt > DateTime.UtcNow.AddMinutes(4)) break;
            await Task.Delay(25);
        }
        Assert.True(batch.LeaseExpiresAt > DateTime.UtcNow.AddMinutes(4));
        Assert.False(monitor.Token.IsCancellationRequested);
        await db.Batches.Where(b => b.Id == batch.Id).ExecuteUpdateAsync(s => s.SetProperty(b => b.Status, "cancelled"));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await Task.Delay(Timeout.Infinite, monitor.Token).WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.True(monitor.CancelledByUser);
        monitor.ThrowIfLeaseLost();
    }

    [Fact]
    public async Task OwnershipLossCancelsWorkAndReportsLeaseLoss()
    {
        using var factory = new Factory();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDb>();
        var batch = new ImportBatch { WorkspaceId = "cloudplay", Status = "processing", LeaseOwner = "test-worker",
            LeaseExpiresAt = DateTime.UtcNow.AddMinutes(5) };
        db.Add(batch);
        await db.SaveChangesAsync();
        await using var monitor = new ImportRunMonitor(factory.Services.GetRequiredService<IServiceScopeFactory>(),
            batch.Id, "test-worker", NullLogger.Instance, default, TimeSpan.FromMilliseconds(25));
        await db.Batches.Where(b => b.Id == batch.Id).ExecuteUpdateAsync(s => s.SetProperty(b => b.LeaseOwner, "other-worker"));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await Task.Delay(Timeout.Infinite, monitor.Token).WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Throws<ImportLeaseLostException>(monitor.ThrowIfLeaseLost);
    }

    [Fact]
    public async Task CancelledArrayStopsBeforeParsingRemainingRecordsAndWorkerRemainsAvailable()
    {
        using var factory = new Factory();
        var importer = new CancellingImporter();
        using var app = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.AddSingleton<IConversationImporter>(sp =>
            {
                importer.Scopes = sp.GetRequiredService<IServiceScopeFactory>();
                return importer;
            })));
        using var client = app.CreateClient();
        client.DefaultRequestHeaders.Add("X-Journey", "1");
        (await client.PostAsJsonAsync("/api/auth/login", new { email = "alex@journey.local", password = "JourneyDemo!2026" }))
            .EnsureSuccessStatusCode();
        var json = JsonSerializer.Serialize(Enumerable.Range(0, 30).Select(i => new
        {
            id = "cancel-" + i, messages = new[] { new { role = "user", content = "synthetic " + i } },
        }));
        using var form = new MultipartFormDataContent();
        form.Add(new StringContent(json), "files", "cancel-array.json");
        form.Add(new StringContent("cancelling"), "adapterId");
        var preview = await (await client.PostAsync("/api/w/cloudplay/imports/preview", form))
            .Content.ReadFromJsonAsync<JsonElement>();
        var batchId = preview.GetProperty("batchId").GetString()!;
        importer.BatchId = batchId;
        (await client.PostAsJsonAsync($"/api/imports/{batchId}/commit", new
        {
            keys = preview.GetProperty("conversations").EnumerateArray().Select(c => c.GetProperty("key").GetString()).ToArray(),
        })).EnsureSuccessStatusCode();
        for (var i = 0; i < 200; i++)
        {
            var status = await client.GetFromJsonAsync<JsonElement>($"/api/imports/{batchId}");
            if (status.GetProperty("fileProgress").GetProperty("stage").GetString() == "cancelled") break;
            await Task.Delay(25);
        }
        Assert.Equal(1, importer.Imports);
        // A later committed batch must still run after user cancellation.
        using var nextForm = new MultipartFormDataContent();
        nextForm.Add(new StringContent("Next independent import"), "files", "next.txt");
        var next = await (await client.PostAsync("/api/w/cloudplay/imports/preview", nextForm))
            .Content.ReadFromJsonAsync<JsonElement>();
        var nextId = next.GetProperty("batchId").GetString();
        (await client.PostAsJsonAsync($"/api/imports/{nextId}/commit", new
        {
            keys = next.GetProperty("conversations").EnumerateArray().Select(c => c.GetProperty("key").GetString()).ToArray(),
        })).EnsureSuccessStatusCode();
        JsonElement finished = default;
        for (var i = 0; i < 200; i++)
        {
            finished = await client.GetFromJsonAsync<JsonElement>($"/api/imports/{nextId}");
            if (finished.GetProperty("status").GetString() == "completed") break;
            await Task.Delay(25);
        }
        Assert.Equal("completed", finished.GetProperty("status").GetString());
    }

    [Fact]
    public async Task DeferredRecoveryUsesIncomingAssetsAfterCheckpointReloadForExistingConversations()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        using var db = new AppDb(new DbContextOptionsBuilder<AppDb>().UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync();
        var oldSource = new ImportSource { WorkspaceId = "w", BatchId = "old-batch", Name = "old/transcript.json" };
        var batch = new ImportBatch { WorkspaceId = "w" };
        var incoming = new ImportSource { WorkspaceId = "w", BatchId = batch.Id, Name = "new/transcript.json" };
        var asset = new ImportSource { WorkspaceId = "w", BatchId = batch.Id, AdapterId = "private-asset", Name = "new/picture.png" };
        var c = new Conversation { WorkspaceId = "w", SourceId = oldSource.Id, SourceKey = "unchanged", Messages =
            [new ConversationMessage { Role = "user", Blocks = [new MessageContentBlock { Metadata = "{\"path\":\"picture.png\"}" }] }] };
        db.AddRange(batch, oldSource, incoming, asset, c);
        await db.SaveChangesAsync();
        await IncrementalImport.Save(db, batch, incoming, new Conversation { SourceKey = "unchanged" }, default, recover: false);
        Assert.Empty(await db.Set<ImportedAttachment>().ToListAsync());
        db.Add(new ImportProcessedKey { SourceId = incoming.Id, Key = "unchanged" });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        await SourceRecovery.Batch(db, batch, default);
        var attachment = Assert.Single(await db.Set<ImportedAttachment>().ToListAsync());
        Assert.Equal(asset.Id, attachment.SourceId);
        Assert.Equal(oldSource.Id, (await db.Conversations.SingleAsync()).SourceId);
    }

    [Fact]
    public async Task LargeAttachmentsAreIdentifiedFromBoundedSignatureReads()
    {
        using var stream = new LargeImageStream();
        var source = new ImportSource { Name = "large.png", Size = stream.Length, Hash = "original-hash" };
        var inputs = new List<SourceInput>();
        await foreach (var input in StreamingImportInputs.Read(source, stream, default)) inputs.Add(input);
        var result = Assert.Single(inputs);
        Assert.Equal(8192, stream.BytesRead);
        Assert.Equal("original-hash", result.OriginalHash);
        Assert.IsType<PrivateAssetImporter>(new ImportRegistry().Detect(result));
    }

    sealed class CancellingImporter : IConversationImporter
    {
        public IServiceScopeFactory Scopes = null!;
        public string BatchId = "";
        public int Imports;
        public string Provider => "Cancelling";
        public bool CanImport(SourceInput s) => s.Name == "cancel-array.json";
        public Task<ImportPreview> PreviewAsync(SourceInput s, CancellationToken ct) => new GenericImporter().PreviewAsync(s, ct);
        public async Task<ImportResult> ImportAsync(SourceInput s, CancellationToken ct)
        {
            Imports++;
            using var scope = Scopes.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDb>();
            await db.Batches.Where(b => b.Id == BatchId).ExecuteUpdateAsync(set => set.SetProperty(b => b.Status, "cancelled"), ct);
            return await new GenericImporter().ImportAsync(s, ct);
        }
    }
    sealed class LargeImageStream : Stream
    {
        public int BytesRead;
        public override long Length => 500_000_000;
        public override long Position { get; set; }
        public override bool CanRead => true;
        public override bool CanSeek => true;
        public override bool CanWrite => false;
        public override int Read(byte[] buffer, int offset, int count)
        {
            var read = (int)Math.Min(count, Length - Position);
            Array.Clear(buffer, offset, read);
            if (Position == 0) new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }.CopyTo(buffer, offset);
            Position += read;
            BytesRead += read;
            return read;
        }
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
        {
            var bytes = new byte[buffer.Length];
            var count = Read(bytes, 0, bytes.Length);
            bytes.AsMemory(0, count).CopyTo(buffer);
            return ValueTask.FromResult(count);
        }
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
