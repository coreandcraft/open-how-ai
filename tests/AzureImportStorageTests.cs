using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Azure;
using Azure.Core.Pipeline;
using Azure.Storage.Blobs;
using Journey.Service;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace Journey.Tests;

public class AzureImportStorageTests
{
    static PrivateFileStore Store(BlobTransport transport)
    {
        var options = new BlobClientOptions { Transport = new HttpClientTransport(new HttpClient(transport)) };
        options.Retry.MaxRetries = 0;
        var container = new BlobContainerClient(new Uri("https://example.test/files"), options);
        return new(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            { ["Storage:Provider"] = "AzureBlob" }).Build(), new EnvironmentStub(), container);
    }

    [Fact]
    public async Task AzureWriteUsesSupportedApiAndConditionalCreateWithoutOverwritingExistingBlob()
    {
        var transport = new BlobTransport();
        var store = Store(transport);
        await using (var stream = await store.Create("imports/probe.source", default))
            await stream.WriteAsync("synthetic import"u8.ToArray());
        Assert.Contains(transport.Requests, r => r.Query == "" && r.IfNoneMatch == "*");
        Assert.Contains(transport.Requests, r => r.Query.Contains("comp=blocklist"));
        transport.Exists = true;
        var error = await Assert.ThrowsAsync<RequestFailedException>(() => store.Create("imports/probe.source", default));
        Assert.Equal(412, error.Status);
        Assert.DoesNotContain(transport.Requests, r => r.Method == "DELETE");
    }

    [Fact]
    public async Task PreviewWritesAzureOriginalsButParsesLocallyWithNoBlobGet()
    {
        var transport = new BlobTransport();
        using var factory = new Factory();
        using var app = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.AddSingleton(Store(transport))));
        using var client = app.CreateClient();
        client.DefaultRequestHeaders.Add("X-Journey", "1");
        (await client.PostAsJsonAsync("/api/auth/login",
            new { email = "alex@journey.local", password = "JourneyDemo!2026" })).EnsureSuccessStatusCode();
        using var form = new MultipartFormDataContent();
        form.Add(new StringContent("Synthetic Azure local parsing"), "files", "transcript.txt");
        using var accepted = await client.PostAsync("/api/w/cloudplay/imports/preview?background=true", form);
        Assert.Equal(HttpStatusCode.Accepted, accepted.StatusCode);
        for (var i = 0; i < 200; i++)
        {
            using var response = await client.GetAsync(accepted.Headers.Location);
            if (response.StatusCode == HttpStatusCode.Accepted) { await Task.Delay(25); continue; }
            response.EnsureSuccessStatusCode();
            var preview = await response.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Single(preview.GetProperty("conversations").EnumerateArray());
            Assert.Equal(0, preview.GetProperty("fileProgress").GetProperty("metrics")
                .GetProperty("sourceReadbacks").GetInt64());
            Assert.True(preview.GetProperty("fileProgress").GetProperty("timings").GetProperty("localRead")
                .GetProperty("operations").GetInt32() > 0);
            Assert.DoesNotContain(transport.Requests, r => r.Method == "GET");
            return;
        }
        throw new TimeoutException("Azure-backed preview did not complete");
    }

    sealed record Request(string Method, string Query, string? IfNoneMatch);
    sealed class BlobTransport : HttpMessageHandler
    {
        public readonly List<Request> Requests = [];
        public bool Exists;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var condition = request.Headers.IfNoneMatch.FirstOrDefault()?.ToString();
            Requests.Add(new(request.Method.Method, request.RequestUri!.Query, condition));
            if (request.Content != null) await request.Content.ReadAsByteArrayAsync(ct);
            var response = new HttpResponseMessage(Exists && condition == "*"
                ? HttpStatusCode.PreconditionFailed : HttpStatusCode.Created)
            {
                Content = new ByteArrayContent([]),
            };
            response.Headers.ETag = new EntityTagHeaderValue("\"generated-etag\"");
            response.Headers.Add("x-ms-request-id", "synthetic-request");
            response.Content.Headers.LastModified = DateTimeOffset.UtcNow;
            return response;
        }
    }
    sealed class EnvironmentStub : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Testing";
        public string ApplicationName { get; set; } = "Journey";
        public string ContentRootPath { get; set; } = Path.GetTempPath();
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
