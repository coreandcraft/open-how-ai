using System.Data.Common;
using System.Diagnostics;
using System.Text.Json;
using Journey.Data;
using Journey.Domain;
using Journey.Server.Features.Imports;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Journey.Tests;

public class ImportPerformanceTests
{
    [Fact]
    public async Task PatternScanningHandlesLongEncodedTextAndManyDistinctSecretsInOnePass()
    {
        var plain = new string('a', 1_000_000);
        var text = plain + "\n" + string.Join('\n', Enumerable.Range(0, 2000).Select(i =>
            $"account{i}@example.test password=private{i} /Users/private{i}/file.txt")); // privacy: synthetic-credential=private{i}
        var watch = Stopwatch.StartNew();
        var redacted = await Task.Run(() => ImportSanitization.Text(text, new(true, true)))
            .WaitAsync(TimeSpan.FromSeconds(5));
        Assert.StartsWith(plain, redacted);
        Assert.DoesNotContain("@example.test", redacted);
        Assert.DoesNotContain("password=", redacted);
        Assert.DoesNotContain("/Users/", redacted);
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(5));
        Assert.Empty(Publishing.Findings(JsonSerializer.Serialize(plain)));
    }

    [Theory]
    [InlineData("Contact Case.User+tag@Example.TEST", "Contact [REDACTED]")]
    [InlineData("password = abc123", "[REDACTED]")]
    [InlineData("sk-abcdefghijklmnop", "[REDACTED]")]
    [InlineData("C:\\Users\\private\\file.txt", "[REDACTED]")]
    public void PatternRedactionPreservesExistingDetection(string text, string expected) =>
        Assert.Equal(expected, ImportSanitization.Text(text, new(true, true)));

    [Fact]
    public async Task DuplicateLookupUsesBoundedQueriesAndKeepsProviderAccountWorkspaceIsolation()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var counter = new QueryCounter();
        using var db = new AppDb(new DbContextOptionsBuilder<AppDb>().UseSqlite(connection)
            .AddInterceptors(counter).Options);
        await db.Database.EnsureCreatedAsync();
        db.Conversations.AddRange(
            new Conversation { WorkspaceId = "w", SourceKey = "key-0", SourceIdentity = "account", Provider = "A", ProviderId = "id-0" },
            new Conversation { WorkspaceId = "other", SourceKey = "key-1", SourceIdentity = "account", Provider = "A", ProviderId = "id-1" },
            new Conversation { WorkspaceId = "w", SourceKey = "different-2", SourceIdentity = "other-account", Provider = "A", ProviderId = "id-2" },
            new Conversation { WorkspaceId = "w", SourceKey = "different-3", SourceIdentity = "account", Provider = "B", ProviderId = "id-3" });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        var capability = ((IConversationImporter)new GenericImporter()).Capability;
        var previews = Enumerable.Range(0, 1201).Select(i => new ConversationPreview(
            new Conversation { SourceKey = "key-" + i, Provider = "A", ProviderId = "id-" + i },
            capability, false)).ToArray();
        counter.Enabled = true;
        var progress = new ImportProgress();
        await ConversationPreview.FindDuplicates(db, "w", "account", previews, progress, default);
        Assert.Equal(6, counter.Queries);
        Assert.True(previews[0].Duplicate);
        Assert.True(previews[0].Update);
        Assert.All(previews.Skip(1), p => { Assert.False(p.Duplicate); Assert.False(p.Update); });
        Assert.Equal(6, progress.Read().Metrics!["duplicateQueries"]);
        Assert.Equal(1, progress.Read().Timings!["duplicateLookup"].Operations);
    }

    [Fact]
    public void DetectionReusesCandidatesRatherThanCallingImportersAgain()
    {
        var importer = new CountingImporter();
        var registry = new ImportRegistry([importer]);
        var source = new SourceInput("probe.txt", "hello"u8.ToArray());
        var candidates = registry.Candidates(source);
        Assert.Same(importer, registry.Detect(source, candidates: candidates));
        Assert.Same(importer, registry.Detect(source, "counted", candidates));
        Assert.Equal(1, importer.Calls);
    }

    [Fact]
    public async Task TimingsReportActiveWorkThenFreezeWithoutExposingInputData()
    {
        var progress = new ImportProgress();
        using (progress.Measure("sanitization"))
        {
            await Task.Delay(15);
            Assert.Equal(1, progress.Read().Timings!["sanitization"].Active);
            Assert.True(progress.Read().Timings!["sanitization"].Milliseconds > 0);
        }
        progress.Finish();
        var snapshot = progress.Read();
        await Task.Delay(10);
        Assert.Equal(snapshot.ElapsedMilliseconds, progress.Read().ElapsedMilliseconds);
        Assert.Equal(1, snapshot.Timings!["sanitization"].Operations);
        Assert.Equal(0, snapshot.Timings["sanitization"].Active);
    }

    sealed class QueryCounter : DbCommandInterceptor
    {
        public bool Enabled;
        public int Queries;
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken ct = default)
        {
            if (Enabled) Queries++;
            return ValueTask.FromResult(result);
        }
    }

    sealed class CountingImporter : IConversationImporter
    {
        public int Calls;
        public string Provider => "Counted";
        public bool CanImport(SourceInput source) { Calls++; return true; }
        public Task<ImportPreview> PreviewAsync(SourceInput s, CancellationToken ct) => throw new NotSupportedException();
        public Task<ImportResult> ImportAsync(SourceInput s, CancellationToken ct) => throw new NotSupportedException();
    }
}
