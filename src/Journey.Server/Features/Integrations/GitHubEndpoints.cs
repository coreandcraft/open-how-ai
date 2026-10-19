using System.Text.Json;
using System.Text.RegularExpressions;
using Journey.Data;
using Journey.Domain;
using Microsoft.EntityFrameworkCore;

namespace Journey.Server.Features.Integrations;

public record GitHubConnectionInput(string RepositoryUrl, string? Branch, string? LocalRoot);

public record GitHubSourceContext(
    string RepositoryUrl,
    string Branch,
    string? LocalRoot,
    string? SessionCommit,
    string Provider
);

public static class GitHubEndpoints
{
    public static void AddGitHubIntegration(this WebApplicationBuilder builder)
    {
        static void Configure(HttpClient http)
        {
            http.Timeout = TimeSpan.FromSeconds(12);
            http.DefaultRequestHeaders.UserAgent.ParseAdd("openhow/1.0");
            http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
            http.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");
        }
        builder.Services.AddSingleton<GitHubAppAuth>();
        builder
            .Services.AddHttpClient("GitHubApp", Configure)
            .ConfigurePrimaryHttpMessageHandler(() =>
                new HttpClientHandler { AllowAutoRedirect = false }
            );
        builder
            .Services.AddHttpClient<GitHubClient>(Configure)
            .ConfigurePrimaryHttpMessageHandler(() =>
                new HttpClientHandler { AllowAutoRedirect = false }
            );
    }

    public static void MapGitHubIntegration(this WebApplication app, RouteGroupBuilder api)
    {
        bool Enabled() => app.Configuration.GetValue("GitHub:Enabled", false);
        api.MapGet(
            "/integrations/github/status",
            (GitHubAppAuth auth) =>
                new
                {
                    enabled = Enabled(),
                    mode = auth.Mode,
                    configured = auth.Configured,
                    installationUrl = auth.InstallationUrl,
                    configurationError = auth.ConfigurationError,
                }
        );
        api.MapPost(
            "/w/{wid}/integrations/github/check",
            async (string wid, Access access, GitHubAppAuth auth, CancellationToken ct) =>
            {
                await access.Require(wid, "edit");
                if (!Enabled())
                    return Results.BadRequest(
                        new { error = "The experimental GitHub integration is disabled." }
                    );
                return Results.Ok(await auth.Check(ct));
            }
        );
        api.MapPut(
            "/w/{wid}/projects/{id}/github",
            async (
                string wid,
                string id,
                GitHubConnectionInput input,
                AppDb db,
                Access access,
                GitHubClient github,
                CancellationToken ct
            ) =>
            {
                await access.Require(wid, "edit");
                if (!Enabled())
                    return Results.BadRequest(
                        new { error = "The experimental GitHub integration is disabled." }
                    );
                var project =
                    await db.Set<ApplicationProject>()
                        .SingleOrDefaultAsync(p => p.Id == id && p.WorkspaceId == wid, ct)
                    ?? throw new KeyNotFoundException();
                var root = GitHubClient.LocalRoot(input.LocalRoot);
                var (repository, branch) = await github.Verify(
                    GitHubClient.Repository(input.RepositoryUrl),
                    input.Branch,
                    ct
                );
                project.GitHubRepositoryUrl = repository;
                project.GitHubBranch = branch;
                project.GitHubLocalRoot = root;
                await db.SaveChangesAsync(ct);
                return Results.Ok(project);
            }
        );
        api.MapDelete(
            "/w/{wid}/projects/{id}/github",
            async (string wid, string id, AppDb db, Access access, CancellationToken ct) =>
            {
                await access.Require(wid, "edit");
                var project =
                    await db.Set<ApplicationProject>()
                        .SingleOrDefaultAsync(p => p.Id == id && p.WorkspaceId == wid, ct)
                    ?? throw new KeyNotFoundException();
                project.GitHubRepositoryUrl = project.GitHubBranch = project.GitHubLocalRoot = null;
                await db.SaveChangesAsync(ct);
                return Results.NoContent();
            }
        );
        api.MapGet(
            "/conversations/{id}/github",
            async (string id, AppDb db, Access access, CancellationToken ct) =>
            {
                await access.RequireConversation(id);
                return Results.Ok(new { source = Enabled() ? await Context(db, id, ct) : null });
            }
        );
        api.MapGet(
            "/conversations/{id}/github/file",
            async (
                string id,
                string path,
                int? startLine,
                AppDb db,
                Access access,
                GitHubClient github,
                CancellationToken ct
            ) =>
            {
                await access.RequireConversation(id);
                if (!Enabled())
                    return Results.BadRequest(
                        new { error = "The experimental GitHub integration is disabled." }
                    );
                var context = await Context(db, id, ct) ?? throw new KeyNotFoundException();
                return Results.Ok(
                    await github.File(
                        context.RepositoryUrl,
                        context.SessionCommit ?? context.Branch,
                        path,
                        startLine ?? 1,
                        ct
                    )
                );
            }
        );
    }

    static async Task<GitHubSourceContext?> Context(AppDb db, string id, CancellationToken ct)
    {
        var row = await (
            from c in db.Conversations.AsNoTracking()
            join p in db.Set<ApplicationProject>().AsNoTracking()
                on c.ApplicationProjectId equals p.Id
            where c.Id == id && c.WorkspaceId == p.WorkspaceId && p.GitHubRepositoryUrl != null
            select new
            {
                p.GitHubRepositoryUrl,
                p.GitHubBranch,
                p.GitHubLocalRoot,
                c.Metadata,
                c.Provider,
            }
        ).SingleOrDefaultAsync(ct);
        if (row?.GitHubRepositoryUrl == null || row.GitHubBranch == null)
            return null;
        return new(
            row.GitHubRepositoryUrl,
            row.GitHubBranch,
            row.GitHubLocalRoot,
            SessionCommit(row.Metadata, row.GitHubRepositoryUrl),
            row.Provider
        );
    }

    // Only explicit session Git metadata is a revision. Never infer a commit from arbitrary text or a timestamp.
    public static string? SessionCommit(string metadata, string repository)
    {
        if (metadata.Length > 262144)
            return null;
        try
        {
            using var doc = JsonDocument.Parse(metadata);
            if (
                doc.RootElement.ValueKind != JsonValueKind.Object
                || !doc.RootElement.TryGetProperty("git", out var git)
                || git.ValueKind != JsonValueKind.Object
                || !git.TryGetProperty("commit_hash", out var hash)
            )
                return null;
            var commit = hash.ValueKind == JsonValueKind.String ? hash.GetString() : null;
            if (commit == null || !Regex.IsMatch(commit, "^[a-fA-F0-9]{40}$"))
                return null;
            if (git.TryGetProperty("repository_url", out var url))
            {
                var remote = url.GetString() ?? "";
                if (remote.StartsWith("git@github.com:", StringComparison.OrdinalIgnoreCase))
                    remote = "https://github.com/" + remote[15..];
                if (
                    !GitHubClient
                        .Repository(remote)
                        .Equals(repository, StringComparison.OrdinalIgnoreCase)
                )
                    return null;
            }
            return commit.ToLowerInvariant();
        }
        catch (Exception ex)
            when (ex is JsonException or InvalidOperationException or InvalidDataException)
        {
            return null;
        }
    }
}
