using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using Journey.Data;
using Journey.Domain;

namespace Journey.Tests;

public class BackgroundImportPreviewTests
{
    sealed class SlowImporter : IConversationImporter
    {
        public string Provider => "Slow";
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool CanImport(SourceInput source) => source.Name == "slow.txt";
        public async Task<ImportPreview> PreviewAsync(SourceInput source, CancellationToken ct)
        {
            Started.TrySetResult();
            await Release.Task.WaitAsync(ct);
            return await new GenericImporter().PreviewAsync(source, ct);
        }
        public Task<ImportResult> ImportAsync(SourceInput source, CancellationToken ct) =>
            new GenericImporter().ImportAsync(source, ct);
    }

    static async Task<HttpClient> Login(Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> app,
        string user = "alex")
    {
        var client = app.CreateClient();
        client.DefaultRequestHeaders.Add("X-Journey", "1");
        (await client.PostAsJsonAsync("/api/auth/login",
            new { email = user + "@journey.local", password = "JourneyDemo!2026" })).EnsureSuccessStatusCode();
        return client;
    }

    static async Task<string> Upload(HttpClient client, string name, string text, string? sanitization = null)
    {
        using var form = new MultipartFormDataContent();
        form.Add(new StringContent(text), "files", name);
        if (sanitization != null)
            form.Add(new StringContent(sanitization), "sanitization");
        if (name == "slow.txt")
            form.Add(new StringContent("slow"), "adapterId");
        using var response = await client.PostAsync("/api/w/cloudplay/imports/preview?background=true", form);
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("processing", result.GetProperty("status").GetString());
        Assert.EndsWith(result.GetProperty("previewId").GetString(), response.Headers.Location!.ToString());
        return response.Headers.Location.ToString();
    }

    static async Task<HttpResponseMessage> Finish(HttpClient client, string location)
    {
        for (var i = 0; i < 200; i++)
        {
            var response = await client.GetAsync(location);
            if (response.StatusCode != HttpStatusCode.Accepted)
                return response;
            response.Dispose();
            await Task.Delay(50);
        }
        throw new TimeoutException("Background preview did not finish.");
    }

    [Fact]
    public async Task AcceptedPreviewOutlivesRequestAndCommitsWithOwnerIsolationAndQueueBound()
    {
        var slow = new SlowImporter();
        using var factory = new Factory();
        using var app = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.AddSingleton<IConversationImporter>(slow)));
        using var client = await Login(app);
        try
        {
            // The upload form/response have been disposed while the parser is still blocked.
            var location = await Upload(client, "slow.txt", "Unique background transcript " + Guid.NewGuid());
            await slow.Started.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal(HttpStatusCode.Accepted, (await client.GetAsync(location)).StatusCode);
            using var otherEditor = await Login(app, "sam");
            Assert.Equal(HttpStatusCode.Gone, (await otherEditor.GetAsync(location)).StatusCode);
            using var viewer = await Login(app, "riley");
            Assert.Equal(HttpStatusCode.Forbidden, (await viewer.GetAsync(location)).StatusCode);
            Assert.Equal(HttpStatusCode.Gone,
                (await client.GetAsync(location.Replace("cloudplay", "personal"))).StatusCode);

            var second = await Upload(client, "second.txt", "Second queued preview");
            using (var form = new MultipartFormDataContent())
            {
                form.Add(new StringContent("third"), "files", "third.txt");
                Assert.Equal(HttpStatusCode.TooManyRequests,
                    (await client.PostAsync("/api/w/cloudplay/imports/preview?background=true", form)).StatusCode);
            }

            slow.Release.TrySetResult();
            using var finished = await Finish(client, location);
            finished.EnsureSuccessStatusCode();
            var preview = await finished.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal("ready", preview.GetProperty("fileProgress").GetProperty("stage").GetString());
            var conversation = Assert.Single(preview.GetProperty("conversations").EnumerateArray());
            var bid = preview.GetProperty("batchId").GetString();
            (await client.PostAsJsonAsync($"/api/imports/{bid}/commit",
                new { keys = new[] { conversation.GetProperty("key").GetString() } })).EnsureSuccessStatusCode();
            JsonElement batch = default;
            for (var i = 0; i < 200; i++)
            {
                batch = await client.GetFromJsonAsync<JsonElement>($"/api/imports/{bid}");
                if (batch.GetProperty("status").GetString()!.StartsWith("completed"))
                    break;
                await Task.Delay(50);
            }
            Assert.Equal("completed", batch.GetProperty("status").GetString());
            Assert.Equal(1, batch.GetProperty("imported").GetInt32());
            using var secondFinished = await Finish(client, second);
            secondFinished.EnsureSuccessStatusCode();
        }
        finally
        {
            slow.Release.TrySetResult();
        }
    }

    [Fact]
    public async Task BackgroundErrorsRetainConsentCodeAndDoNotStopWorker()
    {
        using var factory = new Factory();
        using var client = await Login(factory);
        // Invalid options fail in the worker, with the same client-visible 400 as synchronous preview.
        var invalid = await Upload(client, "bad.txt", "text", "null");
        using var failed = await Finish(client, invalid);
        Assert.Equal(HttpStatusCode.BadRequest, failed.StatusCode);

        var consent = await Upload(client, "binary.bin", "\0\u0001\u0002\u0003",
            "{\"enabled\":true}");
        using var conflict = await Finish(client, consent);
        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
        var error = await conflict.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("original_file_consent_required", error.GetProperty("code").GetString());

        var next = await Upload(client, "sanitized.txt", "private value",
            "{\"enabled\":true,\"rules\":[{\"find\":\"private\",\"replace\":\"safe\"}]}");
        using var ready = await Finish(client, next);
        ready.EnsureSuccessStatusCode();
        var json = await ready.Content.ReadAsStringAsync();
        Assert.Contains("safe value", json);
        Assert.DoesNotContain("private value", json);
    }

    [Fact]
    public async Task CompletedPreviewSurvivesAppRestartAndCacheEviction()
    {
        using var factory = new Factory();
        string location, expected;
        using (var first = factory.WithWebHostBuilder(_ => { }))
        using (var client = await Login(first))
        {
            location = await Upload(client, "sanitized.txt", "private value",
                "{\"enabled\":true,\"rules\":[{\"find\":\"private\",\"replace\":\"safe\"}]}");
            using var finished = await Finish(client, location);
            finished.EnsureSuccessStatusCode();
            expected = await finished.Content.ReadAsStringAsync();
            for (var i = 0; i < 17; i++)
            {
                var other = await Upload(client, "other.txt", "Other preview " + i);
                using var ready = await Finish(client, other);
                ready.EnsureSuccessStatusCode();
            }
            // The first result has left the 16-entry memory cache.
            Assert.Equal(expected, await client.GetStringAsync(location));
        }
        using var restarted = factory.WithWebHostBuilder(_ => { });
        using var restartedClient = await Login(restarted);
        var restored = await restartedClient.GetStringAsync(location);
        Assert.Equal(expected, restored);
        Assert.Contains("safe value", restored);
        Assert.DoesNotContain("private", restored);
        using var otherEditor = await Login(restarted, "sam");
        Assert.Equal(HttpStatusCode.Gone, (await otherEditor.GetAsync(location)).StatusCode);
        Assert.Equal(HttpStatusCode.Gone,
            (await restartedClient.GetAsync(location.Replace("cloudplay", "personal"))).StatusCode);
    }

    [Fact]
    public async Task OtherInstanceSeesPendingStatusThenGracefulInterruption()
    {
        var slow = new SlowImporter();
        using var factory = new Factory();
        var first = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.AddSingleton<IConversationImporter>(slow)));
        try
        {
            using var client = await Login(first);
            var location = await Upload(client, "slow.txt", "Temporary upload");
            await slow.Started.Task.WaitAsync(TimeSpan.FromSeconds(10));
            using var second = factory.WithWebHostBuilder(_ => { });
            using var secondClient = await Login(second);
            using var pending = await secondClient.GetAsync(location);
            Assert.Equal(HttpStatusCode.Accepted, pending.StatusCode);
            Assert.Equal("processing", (await pending.Content.ReadFromJsonAsync<JsonElement>())
                .GetProperty("status").GetString());

            await first.DisposeAsync();
            using var interrupted = await secondClient.GetAsync(location);
            Assert.Equal(HttpStatusCode.Gone, interrupted.StatusCode);
            Assert.Equal("preview_unavailable", (await interrupted.Content.ReadFromJsonAsync<JsonElement>())
                .GetProperty("code").GetString());
        }
        finally { slow.Release.TrySetResult(); await first.DisposeAsync(); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AbandonedOrExpiredPreviewReturnsRecoverableGone(bool expired)
    {
        using var factory = new Factory();
        using var client = await Login(factory);
        var id = Guid.NewGuid().ToString();
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDb>();
            var owner = await db.Users.SingleAsync(u => u.Email == "alex@journey.local");
            db.Add(new ImportPreviewJob
            {
                Id = id, OwnerId = owner.Id, WorkspaceId = "cloudplay",
                ExpiresAt = DateTime.UtcNow.AddHours(expired ? -1 : 1),
                LeaseExpiresAt = DateTime.UtcNow.AddMinutes(-1),
                // An expired completed result must never be returned.
                StatusCode = expired ? 200 : null,
                Response = expired ? "{\"old\":true}" : null,
            });
            await db.SaveChangesAsync();
        }
        using var response = await client.GetAsync($"/api/w/cloudplay/imports/previews/{id}");
        Assert.Equal(HttpStatusCode.Gone, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("preview_unavailable", result.GetProperty("code").GetString());
        Assert.False(result.TryGetProperty("old", out _));
    }
}
