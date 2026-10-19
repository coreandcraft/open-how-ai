using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Caching.Memory;

namespace Journey.Server.Features.Integrations;

/// <summary>Server-side installation authentication. No credentials or tokens are returned to browsers.</summary>
public sealed class GitHubAppAuth : IDisposable
{
    readonly IHttpClientFactory clients;
    readonly ILogger<GitHubAppAuth> logger;
    readonly string clientId;
    readonly string privateKey;
    readonly string slug;
    readonly MemoryCache tokens = new(new MemoryCacheOptions { SizeLimit = 256 });
    readonly SemaphoreSlim[] gates = Enumerable
        .Range(0, 16)
        .Select(_ => new SemaphoreSlim(1, 1))
        .ToArray();
    public string Mode { get; }
    public bool UsesApp => Mode == "App";
    public bool Configured => ConfigurationError == null;
    public string? ConfigurationError { get; private set; }
    public string? InstallationUrl =>
        UsesApp && Configured ? $"https://github.com/apps/{slug}/installations/new" : null;

    public GitHubAppAuth(
        IConfiguration config,
        IHttpClientFactory clients,
        ILogger<GitHubAppAuth> logger
    )
    {
        this.clients = clients;
        this.logger = logger;
        Mode = config["GitHub:AuthenticationMode"]?.Trim() ?? "Public";
        Mode =
            Mode.Equals("App", StringComparison.OrdinalIgnoreCase) ? "App"
            : Mode.Equals("Public", StringComparison.OrdinalIgnoreCase) ? "Public"
            : Mode;
        clientId = config["GitHub:App:ClientId"]?.Trim() ?? "";
        slug = config["GitHub:App:Slug"]?.Trim() ?? "";
        privateKey = (config["GitHub:App:PrivateKeyPem"] ?? "").Replace("\\n", "\n");
        if (Mode is not ("Public" or "App"))
            ConfigurationError = "Choose Public or App for GitHub authentication mode.";
        else if (UsesApp)
        {
            if (
                !Regex.IsMatch(clientId, @"^[A-Za-z0-9_.-]{1,100}$")
                || !Regex.IsMatch(slug, @"^[a-z0-9]+(?:-[a-z0-9]+)*$")
                || slug.Length > 100
                || string.IsNullOrWhiteSpace(privateKey)
            )
                ConfigurationError =
                    "GitHub App setup is incomplete. Configure its Client ID, slug, and private key on the server.";
            else
            {
                try
                {
                    using var rsa = RSA.Create();
                    rsa.ImportFromPem(privateKey);
                    _ = rsa.ExportParameters(true);
                }
                catch (Exception ex) when (ex is CryptographicException or ArgumentException)
                {
                    ConfigurationError =
                        "The GitHub App private key is invalid. Configure a PEM private key on the server.";
                }
            }
        }
    }

    public void RequireConfigured()
    {
        if (ConfigurationError != null)
            throw new InvalidDataException(ConfigurationError);
    }

    string Jwt()
    {
        RequireConfigured();
        static string Encode(byte[] bytes) =>
            Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var now = DateTimeOffset.UtcNow;
        var header = Encode(Encoding.UTF8.GetBytes("""{"alg":"RS256","typ":"JWT"}"""));
        var payload = Encode(
            JsonSerializer.SerializeToUtf8Bytes(
                new
                {
                    iat = now.AddSeconds(-60).ToUnixTimeSeconds(),
                    exp = now.AddMinutes(8).ToUnixTimeSeconds(),
                    iss = clientId,
                }
            )
        );
        using var rsa = RSA.Create();
        rsa.ImportFromPem(privateKey);
        var input = $"{header}.{payload}";
        var signature = rsa.SignData(
            Encoding.ASCII.GetBytes(input),
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1
        );
        return input + "." + Encode(signature);
    }

    public async Task<object> Check(CancellationToken ct)
    {
        RequireConfigured();
        if (!UsesApp)
            return new
            {
                mode = Mode,
                verified = true,
                installationUrl = (string?)null,
            };
        using var data = await Send(HttpMethod.Get, "app", null, ct);
        if (
            data.RootElement.ValueKind != JsonValueKind.Object
            || !data.RootElement.TryGetProperty("slug", out var remoteSlug)
            || remoteSlug.ValueKind != JsonValueKind.String
            || remoteSlug.GetString() != slug
        )
            throw new InvalidDataException(
                "The configured GitHub App slug does not match its credentials."
            );
        return new
        {
            mode = Mode,
            verified = true,
            installationUrl = InstallationUrl,
        };
    }

    public void Invalidate(string repository) =>
        tokens.Remove(GitHubClient.Repository(repository).ToLowerInvariant());

    public async Task<string?> Token(string repository, CancellationToken ct)
    {
        RequireConfigured();
        if (!UsesApp)
            return null;
        var normalized = GitHubClient.Repository(repository);
        var key = normalized.ToLowerInvariant();
        if (tokens.TryGetValue<string>(key, out var cached))
            return cached;
        var gate = gates[(StringComparer.Ordinal.GetHashCode(key) & int.MaxValue) % gates.Length];
        await gate.WaitAsync(ct);
        try
        {
            if (tokens.TryGetValue<string>(key, out cached))
                return cached;
            var repoPath = normalized["https://github.com/".Length..];
            using var installation = await Send(
                HttpMethod.Get,
                "repos/" + repoPath + "/installation",
                null,
                ct
            );
            var root = installation.RootElement;
            if (
                root.TryGetProperty("suspended_at", out var suspended)
                && suspended.ValueKind != JsonValueKind.Null
            )
                throw new InvalidDataException(
                    "This GitHub App installation is suspended. Ask the repository owner to restore it."
                );
            if (
                !root.TryGetProperty("id", out var identifier)
                || !identifier.TryGetInt64(out var id)
                || id <= 0
            )
                throw new InvalidDataException("GitHub returned an invalid installation response.");
            if (
                !root.TryGetProperty("permissions", out var permissions)
                || !permissions.TryGetProperty("contents", out var contents)
                || contents.GetString() is not ("read" or "write")
            )
                throw new InvalidDataException(
                    "Grant the GitHub App read access to repository Contents, then approve the updated permissions."
                );
            using var response = await Send(
                HttpMethod.Post,
                $"app/installations/{id}/access_tokens",
                new
                {
                    repositories = new[] { repoPath.Split('/')[1] },
                    permissions = new { contents = "read" },
                },
                ct
            );
            var accessToken = response.RootElement.GetProperty("token").GetString();
            var expires = response
                .RootElement.GetProperty("expires_at")
                .GetDateTimeOffset()
                .AddMinutes(-2);
            if (
                string.IsNullOrWhiteSpace(accessToken)
                || accessToken.Any(char.IsControl)
                || expires <= DateTimeOffset.UtcNow
            )
                throw new InvalidDataException("GitHub returned an invalid installation token.");
            tokens.Set(
                key,
                accessToken,
                new MemoryCacheEntryOptions { AbsoluteExpiration = expires, Size = 1 }
            );
            return accessToken;
        }
        catch (Exception ex)
            when (ex
                    is JsonException
                        or InvalidOperationException
                        or KeyNotFoundException
                        or FormatException
            )
        {
            throw new InvalidDataException("GitHub returned an invalid installation response.");
        }
        finally
        {
            gate.Release();
        }
    }

    async Task<JsonDocument> Send(
        HttpMethod method,
        string path,
        object? body,
        CancellationToken ct
    )
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(12));
        try
        {
            using var http = clients.CreateClient("GitHubApp");
            using var request = new HttpRequestMessage(method, "https://api.github.com/" + path);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Jwt());
            if (body != null)
                request.Content = JsonContent.Create(body);
            using var response = await http.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                timeout.Token
            );
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning(
                    "GitHub App request failed with HTTP {StatusCode}.",
                    (int)response.StatusCode
                );
                throw new InvalidDataException(
                    response.StatusCode switch
                    {
                        HttpStatusCode.Unauthorized =>
                            "GitHub rejected the App credentials. Check the Client ID, private key, and server clock.",
                        HttpStatusCode.NotFound =>
                            "Install the GitHub App on this repository, or ask its owner to approve access, then try again.",
                        HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests =>
                            "GitHub App access is restricted or rate limited. Check permissions and retry later.",
                        HttpStatusCode.UnprocessableEntity =>
                            "The GitHub App cannot access this repository. Check its selected repositories and Contents permission.",
                        _ => "GitHub App authentication is unavailable. Try again later.",
                    }
                );
            }
            if (response.Content.Headers.ContentLength > 262144)
                throw new InvalidDataException("GitHub returned an invalid installation response.");
            await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token);
            using var buffer = new MemoryStream();
            var chunk = new byte[8192];
            int read;
            while ((read = await stream.ReadAsync(chunk, timeout.Token)) > 0)
            {
                if (buffer.Length + read > 262144)
                    throw new InvalidDataException(
                        "GitHub returned an invalid installation response."
                    );
                buffer.Write(chunk, 0, read);
            }
            return JsonDocument.Parse(buffer.ToArray());
        }
        catch (HttpRequestException)
        {
            throw new InvalidDataException(
                "Cannot reach GitHub. Check the server connection and try again."
            );
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new InvalidDataException("GitHub timed out. Try again later.");
        }
        catch (JsonException)
        {
            throw new InvalidDataException("GitHub returned an invalid installation response.");
        }
    }

    public void Dispose()
    {
        tokens.Dispose();
        foreach (var gate in gates)
            gate.Dispose();
    }
}
