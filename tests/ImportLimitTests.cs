using System.IO.Compression;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Journey.Data;
using Journey.Domain;
using Journey.Server.Features.Review;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Journey.Tests;

public class ImportLimitTests : IClassFixture<Factory>
{
    private readonly Factory factory;

    public ImportLimitTests(Factory factory) => this.factory = factory;

    [Fact]
    public void TierRequiresAnExplicitUserGrantAndSupportsLongBoundaries()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["Imports:LargeImportUserIds:0"] = "enabled-user",
                }
            )
            .Build();
        var limits = new ImportLimits(config);
        Assert.Equal(15_000_000_000L, limits.ForUser("owner"));
        Assert.Equal(20_000_000_000L, limits.ForUser("enabled-user"));
        Assert.Equal(15_000_000_000L, limits.ForUser("ENABLED-USER"));
        foreach (long max in new[] { ImportLimits.DefaultBytes, ImportLimits.ExtendedBytes })
        {
            ImportLimits.Check(max - 1, 1, max);
            Assert.Throws<ImportSizeException>(() => ImportLimits.Check(max, 1, max));
            Assert.Throws<ImportSizeException>(() => ImportLimits.Check(1, long.MaxValue, max));
            Assert.Throws<ImportSizeException>(() => ImportLimits.Check(0, -1, max));
        }
        config["Imports:LargeImportUserIds:0"] = null;
        Assert.Equal(ImportLimits.DefaultBytes, limits.ForUser("enabled-user"));
        Assert.Equal(50 * 1024 * 1024, AnnotationArtifacts.MaxSize);
    }

    [Fact]
    public async Task LimitsAreAuthenticatedPerUserAndOwnersDoNotGetAutomaticGrants()
    {
        using var app = factory.WithWebHostBuilder(builder =>
            builder.ConfigureAppConfiguration(
                (_, c) =>
                    c.AddInMemoryCollection(
                        new Dictionary<string, string?> { ["Imports:LargeImportUserIds:0"] = "sam" }
                    )
            )
        );
        using var anonymous = app.CreateClient();
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (await anonymous.GetAsync("/api/imports/limits")).StatusCode
        );
        foreach (
            var (user, expected) in new[]
            {
                ("alex", ImportLimits.DefaultBytes),
                ("sam", ImportLimits.ExtendedBytes),
                ("riley", ImportLimits.DefaultBytes),
            }
        )
        {
            using var client = app.CreateClient(
                new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
                {
                    AllowAutoRedirect = false,
                }
            );
            client.DefaultRequestHeaders.Add("X-Journey", "1");
            (
                await client.PostAsJsonAsync(
                    "/api/auth/login",
                    new { email = user + "@journey.local", password = "JourneyDemo!2026" }
                )
            ).EnsureSuccessStatusCode();
            var response = await client.GetFromJsonAsync<JsonElement>(
                "/api/imports/limits?largeImports=true&userId=sam"
            );
            Assert.Equal(expected, response.GetProperty("maxBytes").GetInt64());
            using var body = new DeclaredLengthContent(
                expected + ImportLimits.MultipartOverheadBytes + 1
            );
            var rejected = await client.PostAsync("/api/w/cloudplay/imports/preview", body);
            Assert.Equal(
                user == "riley" ? HttpStatusCode.Forbidden : HttpStatusCode.RequestEntityTooLarge,
                rejected.StatusCode
            );
        }
    }

    [Fact]
    public async Task UploadAboveOld50MbLimitPreservesRawFileOutsideDatabase()
    {
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Journey", "1");
        (
            await client.PostAsJsonAsync(
                "/api/auth/login",
                new { email = "alex@journey.local", password = "JourneyDemo!2026" }
            )
        ).EnsureSuccessStatusCode();
        var fixture = File.ReadAllBytes(
            Path.Combine(AppContext.BaseDirectory, "fixtures", "chatgpt.json")
        );
        var bytes = new byte[53_000_000];
        Array.Fill(bytes, (byte)' ');
        fixture.CopyTo(bytes, 0);
        using var form = new MultipartFormDataContent();
        form.Add(new ByteArrayContent(bytes), "files", "large.json");
        var response = await client.PostAsync("/api/w/personal/imports/preview", form);
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(result.GetProperty("conversations").GetArrayLength() > 0);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDb>();
        var batch = result.GetProperty("batchId").GetString();
        var source = db.Sources.Single(x => x.BatchId == batch);
        Assert.True(source.StoredExternally);
        Assert.Empty(source.Bytes);
        Assert.Equal(bytes.LongLength, source.Size);
        Assert.Equal(Value.Hash(bytes), source.Hash);
        Assert.Equal(bytes, await client.GetByteArrayAsync("/api/sources/" + source.Id));
    }

    [Fact]
    public async Task ArchivesAllowEntriesAbove20MbButEnforceExpandedAggregate()
    {
        using var memory = new MemoryStream();
        using (var zip = new ZipArchive(memory, ZipArchiveMode.Create, true))
        {
            using var entry = zip.CreateEntry("conversation.txt").Open();
            await entry.WriteAsync(new byte[21_000_000]);
        }
        var input = new SourceInput("export.zip", memory.ToArray());
        var files = await ArchiveInspector.Expand(input, default);
        Assert.Equal(21_000_000, files.Single().Bytes.Length);
        await Assert.ThrowsAsync<ImportSizeException>(() =>
            ArchiveInspector.Expand(input, default, 20_000_000)
        );
    }

    [Fact]
    public async Task StreamStorageChecksActualBytesAndCleansUpPartialFiles()
    {
        using var scope = factory.Services.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<ImportSourceStore>();
        var source = new ImportSource();
        await Assert.ThrowsAsync<ImportSizeException>(() =>
            store.Save(source, new MemoryStream(new byte[101]), 100, 100, default)
        );
        // A retry with the same identifier proves the partial file was removed.
        await store.Save(source, new MemoryStream(new byte[100]), 100, 100, default);
        Assert.Equal(100, source.Size);
        Assert.Equal(new byte[100], await store.Read(source, default));
        store.Delete(source);
        Assert.Equal(
            new byte[] { 1, 2 },
            await store.Read(new ImportSource { Bytes = [1, 2] }, default)
        );
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ArchivesWithMoreThan500EntriesAreAccepted(bool tarGzip)
    {
        using var memory = new MemoryStream();
        if (tarGzip)
        {
            using var gzip = new GZipStream(memory, CompressionMode.Compress, true);
            using var writer = new System.Formats.Tar.TarWriter(gzip, leaveOpen: true);
            for (var i = 0; i < 501; i++)
                writer.WriteEntry(
                    new System.Formats.Tar.PaxTarEntry(
                        System.Formats.Tar.TarEntryType.RegularFile,
                        $"conversation-{i}.txt"
                    )
                    {
                        DataStream = new MemoryStream("hello"u8.ToArray()),
                    }
                );
        }
        else
        {
            using var zip = new ZipArchive(memory, ZipArchiveMode.Create, true);
            for (var i = 0; i < 501; i++)
            {
                using var entry = zip.CreateEntry($"conversation-{i}.txt").Open();
                entry.Write("hello"u8);
            }
        }
        memory.Position = 0;
        var count = 0;
        await foreach (var entry in ArchiveInspector.Entries(memory, default))
        {
            await entry.Content.CopyToAsync(Stream.Null);
            count++;
        }
        Assert.Equal(501, count);
    }

    [Fact]
    public async Task ArchiveEntryLimitStillBoundsExtraction()
    {
        using var memory = new MemoryStream();
        using (var zip = new ZipArchive(memory, ZipArchiveMode.Create, true))
            for (var i = 0; i <= ImportLimits.MaxArchiveEntries; i++)
                zip.CreateEntry($"{i}.txt");
        memory.Position = 0;
        var error = await Assert.ThrowsAsync<InvalidDataException>(async () =>
        {
            await foreach (var entry in ArchiveInspector.Entries(memory, default))
                await entry.Content.CopyToAsync(Stream.Null);
        });
        Assert.Equal("Archive exceeds 50,000 entries.", error.Message);
    }

    private sealed class DeclaredLengthContent(long length) : HttpContent
    {
        protected override bool TryComputeLength(out long value)
        {
            value = length;
            return true;
        }

        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) =>
            Task.CompletedTask;
    }
}
