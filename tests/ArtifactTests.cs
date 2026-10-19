using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Journey.Data;
using Journey.Domain;
using Microsoft.Extensions.DependencyInjection;


namespace Journey.Tests;

public class ArtifactTests : IClassFixture<Factory>
{
    readonly Factory factory;

    public ArtifactTests(Factory factory) => this.factory = factory;

    async Task<HttpClient> Login(string name)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Journey", "1");
        (
            await client.PostAsJsonAsync(
                "/api/auth/login",
                new { email = name + "@journey.local", password = "JourneyDemo!2026" }
            )
        ).EnsureSuccessStatusCode();
        return client;
    }

    static MultipartFormDataContent Upload(byte[] bytes, string name)
    {
        var form = new MultipartFormDataContent();
        form.Add(new ByteArrayContent(bytes), "file", name);
        return form;
    }

    [Fact]
    public async Task PrivateArtifactsPreserveBytesAndEnforceAccessAndFormats()
    {
        using var owner = await Login("alex");
        var response = await owner.PostAsJsonAsync(
            "/api/w/cloudplay/annotations",
            new
            {
                conversationId = "demo-c9",
                messageId = "demo-m9-0",
                body = "Artifact discussion",
            }
        );
        response.EnsureSuccessStatusCode();
        var id = (await response.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("id")
            .GetString();
        byte[] png = Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+jRZkAAAAASUVORK5CYII="
        );
        using var form = Upload(png, "picture.png");
        var upload = await owner.PostAsync($"/api/annotations/{id}/artifacts", form);
        upload.EnsureSuccessStatusCode();
        var artifact = await upload.Content.ReadFromJsonAsync<JsonElement>();
        var fileId = artifact.GetProperty("id").GetString();
        Assert.Equal(png, await owner.GetByteArrayAsync("/api/artifacts/" + fileId));
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDb>();
            var blobRecord = await db.Set<ArtifactBlob>().FindAsync(fileId);
            Assert.NotNull(blobRecord);
            Assert.Empty(blobRecord.Data);
            Assert.Equal("artifacts/" + fileId, blobRecord.StorageKey);

            // Existing database-backed attachments remain readable during a storage cutover.
            blobRecord.Data = png;
            blobRecord.StorageKey = null;
            await db.SaveChangesAsync();
        }
        Assert.Equal(png, await owner.GetByteArrayAsync("/api/artifacts/" + fileId));
        using var anonymous = factory.CreateClient();
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (await anonymous.GetAsync("/api/artifacts/" + fileId)).StatusCode
        );
        using var viewer = await Login("riley");
        using var viewerForm = Upload(png, "pic.png");
        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await viewer.PostAsync($"/api/annotations/{id}/artifacts", viewerForm)).StatusCode
        );
        using var invalid = Upload(
            System.Text.Encoding.UTF8.GetBytes("<html>bad</html>"),
            "picture.png"
        );
        Assert.Equal(
            HttpStatusCode.BadRequest,
            (await owner.PostAsync($"/api/annotations/{id}/artifacts", invalid)).StatusCode
        );
        var preview = await owner.PostAsJsonAsync(
            "/api/conversations/demo-c9/publication-preview",
            new
            {
                messageIds = new[] { "demo-m9-0" },
                annotationIds = new[] { id },
                redactions = Array.Empty<string>(),
                visibility = "Unlisted",
                confirm = false,
            }
        );
        preview.EnsureSuccessStatusCode();
        Assert.DoesNotContain(fileId!, await preview.Content.ReadAsStringAsync());
        var rows = await owner.GetFromJsonAsync<JsonElement>(
            "/api/w/cloudplay/annotations?conversationId=demo-c9"
        );
        var stored = rows.EnumerateArray()
            .Single(a => a.GetProperty("id").GetString() == id)
            .GetProperty("artifacts");
        Assert.Single(stored.EnumerateArray());
        Assert.False(stored[0].TryGetProperty("data", out _));
    }

    [Fact]
    public async Task AcceptsExactly50MiBAndRejectsLargerFile()
    {
        using var owner = await Login("alex");
        var response = await owner.PostAsJsonAsync(
            "/api/w/cloudplay/annotations",
            new
            {
                conversationId = "demo-c9",
                messageId = "demo-m9-0",
                body = "Size test",
            }
        );
        response.EnsureSuccessStatusCode();
        var id = (await response.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("id")
            .GetString();
        var bytes = new byte[AnnotationArtifacts.MaxSize];
        bytes[0] = 80;
        bytes[1] = 75;
        bytes[2] = 5;
        bytes[3] = 6;
        using var exact = Upload(bytes, "max.zip");
        (
            await owner.PostAsync($"/api/annotations/{id}/artifacts", exact)
        ).EnsureSuccessStatusCode();
        using var oversized = Upload(new byte[AnnotationArtifacts.MaxSize + 1], "over.zip");
        Assert.Equal(
            HttpStatusCode.BadRequest,
            (await owner.PostAsync($"/api/annotations/{id}/artifacts", oversized)).StatusCode
        );
    }
}
