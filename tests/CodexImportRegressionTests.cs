using System.IO.Compression;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Journey.Data;
using Journey.Service.Features.Imports;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ZstdSharp;

namespace Journey.Tests;

public class CodexImportRegressionTests
{
    [Theory]
    [InlineData(false, false, false, true)]
    [InlineData(false, true, false, true)]
    [InlineData(true, false, false, true)]
    [InlineData(true, true, false, true)]
    [InlineData(true, false, true, true)]
    [InlineData(true, true, true, true)]
    [InlineData(false, false, false, false)]
    [InlineData(true, true, false, false)]
    public async Task BackgroundPreviewAndCommitReadCodexRollouts(bool compressed, bool archive, bool sanitize, bool bom)
    {
        using var factory = new Factory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Journey", "1");
        (await client.PostAsJsonAsync("/api/auth/login",
            new { email = "alex@journey.local", password = "JourneyDemo!2026" })).EnsureSuccessStatusCode();
        // A BOM must not hide session_meta from detection.
        var original = File.ReadAllBytes(
            Path.Combine(AppContext.BaseDirectory, "fixtures", "codex-rollout.jsonl"));
        if (bom)
            original = Encoding.UTF8.GetPreamble().Concat(original).ToArray();
        using var compressor = new Compressor();
        var bytes = compressed ? compressor.Wrap(original).ToArray() : original;
        var name = "sessions/2026/10/08/rollout.jsonl" + (compressed ? ".zst" : "");
        var uploadBytes = bytes;
        if (archive)
        {
            using var output = new MemoryStream();
            using (var zip = new ZipArchive(output, ZipArchiveMode.Create, true))
            using (var entry = zip.CreateEntry(name).Open())
                entry.Write(bytes);
            uploadBytes = output.ToArray();
        }
        using var form = new MultipartFormDataContent();
        form.Add(new ByteArrayContent(uploadBytes), "files", archive ? "sessions.zip" : name);
        form.Add(new StringContent(JsonSerializer.Serialize(new
        {
            enabled = sanitize,
            rules = new[] { new { find = "Test streaming", replace = "Safe transcript" } },
        })), "sanitization");
        using var accepted = await client.PostAsync("/api/w/cloudplay/imports/preview?background=true", form);
        Assert.Equal(HttpStatusCode.Accepted, accepted.StatusCode);
        JsonElement preview = default;
        for (var i = 0; i < 200; i++)
        {
            using var response = await client.GetAsync(accepted.Headers.Location);
            if (response.StatusCode == HttpStatusCode.Accepted)
            {
                await Task.Delay(50);
                continue;
            }
            response.EnsureSuccessStatusCode();
            preview = await response.Content.ReadFromJsonAsync<JsonElement>();
            break;
        }
        Assert.Equal(JsonValueKind.Object, preview.ValueKind);
        Assert.Empty(preview.GetProperty("skippedFiles").EnumerateArray());
        Assert.Empty(preview.GetProperty("errors").EnumerateArray());
        var conversation = Assert.Single(preview.GetProperty("conversations").EnumerateArray());
        Assert.Equal("Codex", conversation.GetProperty("provider").GetString());
        Assert.Equal(4, conversation.GetProperty("messageCount").GetInt32());
        var progress = preview.GetProperty("fileProgress");
        Assert.Equal(1, progress.GetProperty("processed").GetInt32());
        Assert.Equal(1, progress.GetProperty("total").GetInt32());
        var expanded = original.LongLength;
        Assert.Equal(expanded, progress.GetProperty("uncompressedBytes").GetInt64());
        Assert.Equal(uploadBytes.LongLength + (archive ? bytes.LongLength : 0)
            + (compressed ? expanded : 0), progress.GetProperty("usedBytes").GetInt64());
        var bid = preview.GetProperty("batchId").GetString();
        (await client.PostAsJsonAsync($"/api/imports/{bid}/commit",
            new { keys = new[] { conversation.GetProperty("key").GetString() } })).EnsureSuccessStatusCode();
        JsonElement status = default;
        for (var i = 0; i < 200; i++)
        {
            status = await client.GetFromJsonAsync<JsonElement>($"/api/imports/{bid}");
            if (status.GetProperty("status").GetString()!.StartsWith("completed"))
                break;
            await Task.Delay(50);
        }
        Assert.Equal("completed", status.GetProperty("status").GetString());
        Assert.Equal(1, status.GetProperty("imported").GetInt32());
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDb>();
        var sources = await db.Sources.Where(s => s.BatchId == bid).ToListAsync();
        var store = scope.ServiceProvider.GetRequiredService<ImportSourceStore>();
        var transcript = Assert.Single(sources, s => !s.Container);
        var saved = await store.Read(transcript, default);
        if (sanitize)
        {
            Assert.DoesNotContain("Test streaming", Encoding.UTF8.GetString(saved));
            Assert.Contains("Safe transcript", Encoding.UTF8.GetString(saved));
            Assert.DoesNotContain(sources, s => s.Container);
        }
        else
        {
            Assert.Equal(original, saved);
            if (compressed)
                Assert.Equal(bytes, await store.Read(Assert.Single(sources, s => s.Name == name), default));
        }
    }

    [Fact]
    public async Task ExpandedBytesCannotExceedTheRemainingImportQuota()
    {
        using var compressor = new Compressor();
        var bytes = compressor.Wrap(new byte[100_000]).ToArray();
        using var source = new MemoryStream(bytes);
        using var target = new MemoryStream();
        await Assert.ThrowsAsync<ImportSizeException>(() =>
            ZstandardImport.Decode(source, target, 500, 1000, default));
        Assert.True(target.Length <= 500);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DamagedOrTruncatedFramesAreRejected(bool truncated)
    {
        using var compressor = new Compressor();
        var bytes = truncated ? compressor.Wrap(Encoding.UTF8.GetBytes("transcript")).ToArray()[..^1]
            : new byte[] { 0x28, 0xb5, 0x2f, 0xfd, 0xff, 0xff };
        using var source = new MemoryStream(bytes);
        using var target = new MemoryStream();
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            ZstandardImport.Decode(source, target, 0, 1000, default));
    }
}
