using System.IO.Compression;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Journey.Data;
using Journey.Domain;
using Journey.Service.Features.Imports;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Journey.Tests;

public class SanitizationTests : IClassFixture<Factory>
{
    readonly Factory factory;

    public SanitizationTests(Factory factory) => this.factory = factory;

    [Fact]
    public void ReplacementsAreLiteralLongestFirstAndDoNotCascade()
    {
        Assert.Equal(
            "B C .* ABC",
            ImportSanitization.Replace(
                "A AB .* ABC",
                [new("A", "B"), new("AB", "C"), new("ABC", "ABC")]
            )
        );
        Assert.Equal("safe", ImportSanitization.Replace("a.b", [new("a.b", "safe")]));
        Assert.Equal("B", ImportSanitization.Replace("A", [new("A", "B"), new("B", "C")]));
        var publication = new Journey.Service.Features.Publications.PublishRequest(
            [],
            ["secret"],
            "Public",
            false,
            ReplacementRules: [new("PrivateCorp", "ExampleCorp")]
        );
        Assert.Equal(
            "ExampleCorp [REDACTED]",
            Journey.Service.Features.Publications.Publishing.Redact(
                "PrivateCorp secret",
                publication
            )
        );
        Assert.Throws<InvalidDataException>(() => ImportSanitization.Validate([new("", "x")]));
        Assert.Throws<InvalidDataException>(() =>
            ImportSanitization.Validate([new("a", "x"), new("a", "y")])
        );
    }

    [Fact]
    public void SanitizesDecodedMetadataWithoutBreakingJsonOrIdentity()
    {
        var c = new Conversation
        {
            Title = "private",
            SourceKey = "private",
            ProviderId = "private",
            Metadata = "{\"value\":\"private\"}",
            Messages = [new() { Blocks = [new() { Text = "private" }] }],
        };
        var options = new SanitizationOptions(true, false, [new("private", "a\"b\\c")]);
        ImportSanitization.Apply(c, options);
        Assert.Equal(
            "a\"b\\c",
            JsonDocument.Parse(c.Metadata).RootElement.GetProperty("value").GetString()
        );
        Assert.Equal("a\"b\\c", c.Messages[0].Blocks[0].Text);
        Assert.Equal("private", c.SourceKey);
        Assert.Equal("private", c.ProviderId);
        Assert.Equal("[REDACTED]", ImportSanitization.Text("person@example.test", new(true, true)));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task PreviewAndCommittedContentAgreeAndSettingsPersist(bool enabled, bool archive)
    {
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Journey", "1");
        var email = Guid.NewGuid() + "@fixture.test";
        (
            await client.PostAsJsonAsync(
                "/api/auth/register",
                new
                {
                    email,
                    password = "JourneyTest!2026",
                    name = "Sanitization tester",
                }
            )
        ).EnsureSuccessStatusCode();
        await AccountTestHelper.ConfirmAndLogin(factory, client, email, "JourneyTest!2026");
        ReplacementRule[] rules = [new("PrivateCorp", "ExampleCorp")];
        (await client.PutAsJsonAsync("/api/me/sanitization", rules)).EnsureSuccessStatusCode();
        Assert.Equal(
            rules,
            await client.GetFromJsonAsync<ReplacementRule[]>("/api/me/sanitization")
        );
        var wid = (await client.GetFromJsonAsync<JsonElement>("/api/workspaces"))[0]
            .GetProperty("id")
            .GetString();
        using var form = new MultipartFormDataContent();
        form.Add(
            new StringContent(
                JsonSerializer.Serialize(
                    new SanitizationOptions(enabled, true, rules),
                    new JsonSerializerOptions(JsonSerializerDefaults.Web)
                )
            ),
            "sanitization"
        );
        var sourceBytes = Encoding.UTF8.GetBytes(
            "{\"title\":\"PrivateCorp\",\"person@example.test\":\"PrivateCorp\",\"messages\":[{\"role\":\"user\",\"content\":\"PrivateCorp person@example.test\"}]}"
        );
        if (archive)
        {
            using var buffer = new MemoryStream();
            using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, true))
            {
                using var entry = zip.CreateEntry("PrivateCorp.json").Open();
                entry.Write(sourceBytes);
            }
            sourceBytes = buffer.ToArray();
        }
        form.Add(
            new ByteArrayContent(sourceBytes),
            "files",
            archive ? "PrivateCorp.zip" : "PrivateCorp.json"
        );
        var response = await client.PostAsync($"/api/w/{wid}/imports/preview", form);
        response.EnsureSuccessStatusCode();
        var preview = await response.Content.ReadFromJsonAsync<JsonElement>();
        var c = preview.GetProperty("conversations")[0];
        var expected = enabled ? "ExampleCorp [REDACTED]" : "PrivateCorp person@example.test";
        Assert.Equal(
            expected,
            c.GetProperty("messages")[0].GetProperty("blocks")[0].GetProperty("text").GetString()
        );
        var bid = preview.GetProperty("batchId").GetString();
        // Check storage at preview time, before commit or background processing.
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDb>();
            var sources = await db.Sources.Where(s => s.BatchId == bid).ToListAsync();
            var source = Assert.Single(sources);
            Assert.False(source.Container);
            var downloaded = await client.GetStringAsync("/api/sources/" + source.Id);
            if (enabled)
            {
                Assert.DoesNotContain("PrivateCorp", downloaded);
                Assert.DoesNotContain("person@example.test", downloaded);
                Assert.Equal("ExampleCorp.json", source.Name);
                Assert.Equal("{}", (await db.Batches.FindAsync(bid))!.Sanitization);
            }
            else
                Assert.Contains("PrivateCorp", downloaded);
        }
        (
            await client.PostAsJsonAsync(
                $"/api/imports/{bid}/commit",
                new { keys = new[] { c.GetProperty("key").GetString() } }
            )
        ).EnsureSuccessStatusCode();
        JsonElement status = default;
        for (var i = 0; i < 100; i++)
        {
            status = await client.GetFromJsonAsync<JsonElement>($"/api/imports/{bid}");
            if (status.GetProperty("status").GetString()!.StartsWith("completed"))
                break;
            await Task.Delay(100);
        }
        Assert.Equal("completed", status.GetProperty("status").GetString());
        var library = await client.GetFromJsonAsync<JsonElement>($"/api/w/{wid}/library");
        var id = library.GetProperty("items")[0].GetProperty("id").GetString();
        var imported = await client.GetFromJsonAsync<JsonElement>($"/api/conversations/{id}");
        Assert.Equal(
            expected,
            imported
                .GetProperty("messages")[0]
                .GetProperty("blocks")[0]
                .GetProperty("text")
                .GetString()
        );
    }

    [Theory]
    [InlineData("conversation.json")]
    [InlineData("conversation.jsonl")]
    public async Task SanitizesEscapedStringsAndKeysOnce(string name)
    {
        using var source = new MemoryStream(
            Encoding.UTF8.GetBytes("{\"priv\\u0061te\":\"priv\\u0061te\"}")
        );
        var bytes = await ImportSanitization.Source(
            source,
            name,
            new(true, false, [new("private", "safe"), new("safe", "other")]),
            default
        );
        Assert.Equal("{\"safe\":\"safe\"}", Encoding.UTF8.GetString(bytes));
    }

    [Fact]
    public async Task OriginalFilesRequireExplicitConsentForEachUpload()
    {
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Journey", "1");
        var email = Guid.NewGuid() + "@fixture.test";
        (
            await client.PostAsJsonAsync(
                "/api/auth/register",
                new
                {
                    email,
                    password = "JourneyTest!2026",
                    name = "Sanitization tester",
                }
            )
        ).EnsureSuccessStatusCode();
        await AccountTestHelper.ConfirmAndLogin(factory, client, email, "JourneyTest!2026");
        var wid = (await client.GetFromJsonAsync<JsonElement>("/api/workspaces"))[0]
            .GetProperty("id")
            .GetString();
        using var form = new MultipartFormDataContent();
        form.Add(
            new StringContent(
                "{\"enabled\":true,\"rules\":[{\"find\":\"private\",\"replace\":\"safe\"}]}"
            ),
            "sanitization"
        );
        form.Add(
            new ByteArrayContent(
                Encoding.UTF8.GetBytes(
                    "{\"messages\":[{\"role\":\"user\",\"content\":\"private\"}]}"
                )
            ),
            "files",
            "private.json"
        );
        form.Add(
            new ByteArrayContent(Encoding.UTF8.GetBytes("%PDF-private")),
            "files",
            "private.pdf"
        );
        var response = await client.PostAsync($"/api/w/{wid}/imports/preview", form);
        Assert.Equal(System.Net.HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(
            "original_file_consent_required",
            (await response.Content.ReadFromJsonAsync<JsonElement>())
                .GetProperty("code")
                .GetString()
        );
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDb>();
        Assert.False(await db.Sources.AnyAsync(s => s.WorkspaceId == wid));
        Assert.False(await db.Batches.AnyAsync(b => b.WorkspaceId == wid));
        using var allowed = new MultipartFormDataContent();
        allowed.Add(
            new StringContent(
                "{\"enabled\":true,\"allowOriginalFiles\":true,\"rules\":[{\"find\":\"private\",\"replace\":\"safe\"}]}"
            ),
            "sanitization"
        );
        allowed.Add(
            new ByteArrayContent(
                Encoding.UTF8.GetBytes(
                    "{\"messages\":[{\"role\":\"user\",\"content\":\"private\"}]}"
                )
            ),
            "files",
            "private.json"
        );
        allowed.Add(
            new ByteArrayContent(Encoding.UTF8.GetBytes("%PDF-private")),
            "files",
            "private.pdf"
        );
        var accepted = await client.PostAsync($"/api/w/{wid}/imports/preview", allowed);
        accepted.EnsureSuccessStatusCode();
        var preview = await accepted.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(
            "safe.pdf",
            Assert.Single(preview.GetProperty("retainedOriginals").EnumerateArray()).GetString()
        );
        var sources = await db.Sources.Where(s => s.WorkspaceId == wid).ToListAsync();
        Assert.Equal(2, sources.Count);
        var original = sources.Single(s => s.Name == "safe.pdf");
        Assert.Equal("%PDF-private", await client.GetStringAsync("/api/sources/" + original.Id));
        var sanitized = sources.Single(s => s.Name == "safe.json");
        Assert.DoesNotContain(
            "private",
            await client.GetStringAsync("/api/sources/" + sanitized.Id)
        );
        var again = await client.PostAsync($"/api/w/{wid}/imports/preview", form);
        Assert.Equal(System.Net.HttpStatusCode.Conflict, again.StatusCode);
    }

    [Theory]
    [InlineData("file.sqlite", "SQLite format 3\0secret")]
    [InlineData("file.pdf", "%PDF-secret")]
    [InlineData("file.html", "<p>secret</p>")]
    [InlineData("file.json", "{\"text\":\"secret\"")]
    public async Task RetainsUnsupportedFilesOnlyWithConsent(string name, string text)
    {
        using var source = new MemoryStream(Encoding.UTF8.GetBytes(text));
        await Assert.ThrowsAsync<OriginalFileConsentRequiredException>(() =>
            ImportSanitization.Source(source, name, new(true), default)
        );
        source.Position = 0;
        var accepted = await ImportSanitization.PrepareSource(
            source,
            name,
            new(true, AllowOriginalFiles: true),
            default
        );
        Assert.True(accepted.OriginalRetained);
        Assert.Equal(text, Encoding.UTF8.GetString(accepted.Bytes));
    }
}
