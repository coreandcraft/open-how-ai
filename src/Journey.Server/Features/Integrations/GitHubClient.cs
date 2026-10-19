using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Journey.Server.Features.Integrations;

/// <summary>Read-only public GitHub access. Never fetch a caller-supplied host or download URL.</summary>
public sealed class GitHubClient(HttpClient http, GitHubAppAuth auth, ILogger<GitHubClient> logger)
{
    public static string Repository(string? value)
    {
        var text = value?.Trim().TrimEnd('/') ?? "";
        if (
            !Regex.IsMatch(
                text,
                @"^https://github\.com/[A-Za-z0-9-]+/[A-Za-z0-9_.-]+$",
                RegexOptions.IgnoreCase
            )
        )
            throw new InvalidDataException(
                "Enter a public GitHub repository URL, such as https://github.com/owner/repository."
            );
        var parts = text.Split('/');
        var repo = parts[^1];
        if (repo.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
            repo = repo[..^4];
        if (repo.Length == 0 || repo is "." or ".." || parts[^2].Length > 100 || repo.Length > 100)
            throw new InvalidDataException("Enter a valid GitHub repository URL.");
        return $"https://github.com/{parts[^2]}/{repo}";
    }

    public static string? LocalRoot(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        var root = value.Trim().Replace('\\', '/').TrimEnd('/');
        if (
            root.Length > 1024
            || root.Any(char.IsControl)
            || root.Contains("://")
            || root.StartsWith("//")
            || root.Split('/').Any(x => x is "." or "..")
            || !(root.StartsWith('/') || Regex.IsMatch(root, @"^[A-Za-z]:/"))
        )
            throw new InvalidDataException(
                "Enter the absolute project folder recorded in your tool calls."
            );
        return root;
    }

    public static string RelativePath(string? value)
    {
        var path = value ?? "";
        if (
            path.Length is 0 or > 2048
            || path.StartsWith('/')
            || path.Contains('\\')
            || path.Contains(':')
            || path.Any(char.IsControl)
            || path.Split('/').Any(x => x is "" or "." or ".." or ".git")
        )
            throw new InvalidDataException("Choose a file inside the linked repository.");
        return path;
    }

    public static string Ref(string value)
    {
        if (
            value.Length is 0 or > 250
            || value.Any(char.IsControl)
            || value.Any(char.IsWhiteSpace)
            || value.Contains("..")
            || value.Contains("@{")
            || value.IndexOfAny(['~', '^', ':', '?', '*', '[', '\\']) >= 0
            || value.StartsWith('/')
            || value.EndsWith('/')
            || value.Split('/').Any(x => x is "" or "." or "..")
        )
            throw new InvalidDataException("Enter a valid GitHub branch.");
        return value;
    }

    static string ApiPath(string repository) =>
        "repos/" + Repository(repository)["https://github.com/".Length..];

    public async Task<(string Repository, string Branch)> Verify(
        string repository,
        string? branch,
        CancellationToken ct
    )
    {
        var accessToken = await auth.Token(repository, ct);
        using var info = await Get(repository, ApiPath(repository), accessToken, ct);
        var root = info.RootElement;
        if (
            !root.TryGetProperty("private", out var privacy)
            || privacy.ValueKind != JsonValueKind.False
        )
            throw new InvalidDataException("Only public GitHub repositories can be linked.");
        var canonical = Repository(root.GetProperty("html_url").GetString());
        var selected = Ref(
            string.IsNullOrWhiteSpace(branch)
                ? root.GetProperty("default_branch").GetString() ?? ""
                : branch.Trim()
        );
        using var checkedBranch = await Get(
            canonical,
            ApiPath(canonical) + "/branches/" + Uri.EscapeDataString(selected),
            accessToken,
            ct
        );
        return (canonical, selected);
    }

    public async Task<object> File(
        string repository,
        string reference,
        string path,
        int start,
        CancellationToken ct
    )
    {
        path = RelativePath(path);
        reference = Ref(reference);
        var accessToken = await auth.Token(repository, ct);
        // An installed repository can become private after it was linked. Never expose it through this public-only feature.
        if (auth.UsesApp)
        {
            using var info = await Get(repository, ApiPath(repository), accessToken, ct);
            if (
                !info.RootElement.TryGetProperty("private", out var privacy)
                || privacy.ValueKind != JsonValueKind.False
            )
                throw new InvalidDataException("Only public GitHub repositories can be linked.");
        }
        using var data = await Get(
            repository,
            ApiPath(repository)
                + "/contents/"
                + string.Join('/', path.Split('/').Select(Uri.EscapeDataString))
                + "?ref="
                + Uri.EscapeDataString(reference),
            accessToken,
            ct
        );
        var file = data.RootElement;
        if (
            file.ValueKind != JsonValueKind.Object
            || file.GetProperty("type").GetString() != "file"
            || file.GetProperty("encoding").GetString() != "base64"
            || file.GetProperty("size").GetInt64() > 262144
        )
            throw new InvalidDataException(
                "Preview supports text files up to 256 KB. Open this file on GitHub instead."
            );
        string text;
        try
        {
            var bytes = Convert.FromBase64String(file.GetProperty("content").GetString() ?? "");
            if (bytes.Length > 262144)
                throw new InvalidDataException(
                    "Preview supports text files up to 256 KB. Open this file on GitHub instead."
                );
            text = new UTF8Encoding(false, true).GetString(bytes);
        }
        catch (Exception ex) when (ex is FormatException or DecoderFallbackException)
        {
            throw new InvalidDataException(
                "This file cannot be previewed as text. Open it on GitHub instead."
            );
        }
        if (text.Contains('\0'))
            throw new InvalidDataException(
                "Binary files cannot be previewed. Open this file on GitHub instead."
            );
        var lines = text.Replace("\r\n", "\n").Split('\n');
        start = Math.Clamp(start, 1, lines.Length);
        return new
        {
            path,
            reference,
            startLine = start,
            totalLines = lines.Length,
            lines = lines.Skip(start - 1).Take(160).ToArray(),
        };
    }

    async Task<JsonDocument> Get(
        string repository,
        string path,
        string? accessToken,
        CancellationToken ct
    )
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(12));
        var token = timeout.Token;
        try
        {
            using var request = new HttpRequestMessage(
                HttpMethod.Get,
                new Uri("https://api.github.com/" + path)
            );
            if (accessToken != null)
                request.Headers.Authorization = new AuthenticationHeaderValue(
                    "Bearer",
                    accessToken
                );
            using var response = await http.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                token
            );
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning(
                    "GitHub repository request failed with HTTP {StatusCode}.",
                    (int)response.StatusCode
                );
                if (
                    auth.UsesApp
                    && response.StatusCode
                        is HttpStatusCode.Unauthorized
                            or HttpStatusCode.Forbidden
                            or HttpStatusCode.NotFound
                )
                    auth.Invalidate(repository);
            }
            if (response.StatusCode == HttpStatusCode.Unauthorized)
                throw new InvalidDataException(
                    "GitHub rejected repository access. Check the App installation and try again."
                );
            if (response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests)
                throw new InvalidDataException(
                    "GitHub rate limit or access restriction reached. Try again later."
                );
            if (response.StatusCode == HttpStatusCode.NotFound)
                throw new InvalidDataException(
                    "GitHub could not find this public repository, branch, revision, or file."
                );
            if ((int)response.StatusCode is >= 300 and < 400)
                throw new InvalidDataException(
                    "This repository or file moved. Update the linked GitHub URL."
                );
            if (!response.IsSuccessStatusCode)
                throw new InvalidDataException("GitHub is unavailable. Try again later.");
            if (response.Content.Headers.ContentLength > 1_048_576)
                throw new InvalidDataException("This GitHub response is too large to preview.");
            await using var stream = await response.Content.ReadAsStreamAsync(token);
            using var buffer = new MemoryStream();
            var chunk = new byte[8192];
            int read;
            while ((read = await stream.ReadAsync(chunk, token)) > 0)
            {
                if (buffer.Length + read > 1_048_576)
                    throw new InvalidDataException("This GitHub response is too large to preview.");
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
    }
}
