using System.Collections;
using System.Data.Common;
using Journey.Data;
using Journey.Domain;
using Journey.Service.Features.Imports;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Journey.Tests;

public class ImportRecoveryQueryTests
{
    [Theory]
    [InlineData("batch")]
    [InlineData("duplicate")]
    [InlineData("update")]
    public async Task RecoveryDoesNotTransferTheSourcePayloadForEveryMessageBlock(string mode)
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        var counter = new PayloadCounter();
        using var db = new AppDb(
            new DbContextOptionsBuilder<AppDb>()
                .UseSqlite(connection)
                .AddInterceptors(counter)
                .Options
        );
        db.Database.EnsureCreated();
        var batch = new ImportBatch { WorkspaceId = "w", Status = "processing" };
        var source = new ImportSource { WorkspaceId = "w", BatchId = batch.Id };
        var raw = new string('x', 64 * 1024);
        var conversation = new Conversation
        {
            WorkspaceId = "w",
            SourceId = source.Id,
            Provider = "Codex",
            ProviderId = "session",
            SourceKey = "original",
            RawPayload = raw,
            Messages = Enumerable
                .Range(0, 24)
                .Select(i => new ConversationMessage
                {
                    Order = i,
                    Role = "user",
                    ProviderId = "message-" + i,
                    Blocks =
                    [
                        new() { Text = "first", Metadata = "{}" },
                        new() { Text = "second", Metadata = "{}" },
                    ],
                })
                .ToList(),
        };
        db.AddRange(batch, source, conversation);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        counter.Enabled = true;
        if (mode == "batch")
        {
            var checkpoints = 0;
            await SourceRecovery.Batch(
                db,
                batch,
                default,
                () =>
                {
                    checkpoints++;
                    return Task.CompletedTask;
                }
            );
            Assert.Equal(1, checkpoints);
            Assert.Empty(db.ChangeTracker.Entries<ConversationMessage>());
            Assert.Empty(db.ChangeTracker.Entries<MessageContentBlock>());
        }
        else
        {
            await IncrementalImport.Save(
                db,
                batch,
                source,
                new Conversation
                {
                    Provider = "Codex",
                    ProviderId = "session",
                    SourceKey = mode == "duplicate" ? "original" : "changed",
                },
                default
            );
            Assert.Equal(mode == "duplicate" ? 1 : 0, batch.Duplicates);
            Assert.Equal(mode == "update" ? 1 : 0, batch.Updated);
        }
        var payloadBytes = counter.PayloadBytes;
        // Source size must not be multiplied by the number of messages or content blocks.
        Assert.Equal(raw.Length, payloadBytes);
        counter.Enabled = false;
        db.ChangeTracker.Clear();
        Assert.Equal(raw, await db.Conversations.Select(c => c.RawPayload).SingleAsync());
        Assert.Equal(24, await db.Messages.CountAsync());
        Assert.Equal(48, await db.Set<MessageContentBlock>().CountAsync());
    }

    [Fact]
    public async Task RecoveryObservesCancellationAtTheNextCheckpoint()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        using var db = new AppDb(
            new DbContextOptionsBuilder<AppDb>().UseSqlite(connection).Options
        );
        db.Database.EnsureCreated();
        var batch = new ImportBatch { WorkspaceId = "w", Status = "processing" };
        var source = new ImportSource { WorkspaceId = "w", BatchId = batch.Id };
        db.AddRange(
            batch,
            source,
            new Conversation
            {
                WorkspaceId = "w",
                SourceId = source.Id,
                SourcePath = "/source/path",
                Provider = "Codex",
            }
        );
        await db.SaveChangesAsync();
        var checkpoints = 0;
        await SourceRecovery.Batch(
            db,
            batch,
            default,
            () =>
            {
                checkpoints++;
                batch.Status = "cancelled";
                return Task.CompletedTask;
            }
        );
        Assert.Equal(1, checkpoints);
        Assert.Empty(await db.Set<SourceWorkspace>().ToListAsync());
    }

    sealed class PayloadCounter : DbCommandInterceptor
    {
        public bool Enabled { get; set; }
        public long PayloadBytes { get; set; }

        public override ValueTask<DbDataReader> ReaderExecutedAsync(
            DbCommand command,
            CommandExecutedEventData eventData,
            DbDataReader result,
            CancellationToken cancellationToken = default
        ) => ValueTask.FromResult<DbDataReader>(Enabled ? new PayloadReader(result, this) : result);
    }

    sealed class PayloadReader(DbDataReader inner, PayloadCounter counter) : DbDataReader
    {
        void CountRow()
        {
            for (var i = 0; i < inner.FieldCount; i++)
                if (inner.GetName(i) == "RawPayload" && !inner.IsDBNull(i))
                    counter.PayloadBytes += inner.GetString(i).Length;
        }

        public override bool Read()
        {
            var read = inner.Read();
            if (read)
                CountRow();
            return read;
        }

        public override async Task<bool> ReadAsync(CancellationToken ct)
        {
            var read = await inner.ReadAsync(ct);
            if (read)
                CountRow();
            return read;
        }

        public override bool NextResult() => inner.NextResult();

        public override Task<bool> NextResultAsync(CancellationToken ct) =>
            inner.NextResultAsync(ct);

        public override object this[int ordinal] => inner[ordinal];
        public override object this[string name] => inner[name];
        public override int Depth => inner.Depth;
        public override int FieldCount => inner.FieldCount;
        public override bool HasRows => inner.HasRows;
        public override bool IsClosed => inner.IsClosed;
        public override int RecordsAffected => inner.RecordsAffected;

        public override bool GetBoolean(int i) => inner.GetBoolean(i);

        public override byte GetByte(int i) => inner.GetByte(i);

        public override long GetBytes(
            int i,
            long offset,
            byte[]? buffer,
            int bufferOffset,
            int length
        ) => inner.GetBytes(i, offset, buffer, bufferOffset, length);

        public override char GetChar(int i) => inner.GetChar(i);

        public override long GetChars(
            int i,
            long offset,
            char[]? buffer,
            int bufferOffset,
            int length
        ) => inner.GetChars(i, offset, buffer, bufferOffset, length);

        public override string GetDataTypeName(int i) => inner.GetDataTypeName(i);

        public override DateTime GetDateTime(int i) => inner.GetDateTime(i);

        public override decimal GetDecimal(int i) => inner.GetDecimal(i);

        public override double GetDouble(int i) => inner.GetDouble(i);

        public override Type GetFieldType(int i) => inner.GetFieldType(i);

        public override float GetFloat(int i) => inner.GetFloat(i);

        public override Guid GetGuid(int i) => inner.GetGuid(i);

        public override short GetInt16(int i) => inner.GetInt16(i);

        public override int GetInt32(int i) => inner.GetInt32(i);

        public override long GetInt64(int i) => inner.GetInt64(i);

        public override string GetName(int i) => inner.GetName(i);

        public override int GetOrdinal(string name) => inner.GetOrdinal(name);

        public override string GetString(int i) => inner.GetString(i);

        public override object GetValue(int i) => inner.GetValue(i);

        public override int GetValues(object[] values) => inner.GetValues(values);

        public override bool IsDBNull(int i) => inner.IsDBNull(i);

        public override IEnumerator GetEnumerator() => ((IEnumerable)inner).GetEnumerator();

        public override T GetFieldValue<T>(int i) => inner.GetFieldValue<T>(i);

        public override void Close() => inner.Close();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                inner.Dispose();
            base.Dispose(disposing);
        }

        public override ValueTask DisposeAsync() => inner.DisposeAsync();
    }
}
