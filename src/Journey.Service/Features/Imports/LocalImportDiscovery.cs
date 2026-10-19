using System.Collections.Concurrent;
using Journey.Domain;
using Microsoft.Extensions.Configuration;

namespace Journey.Service.Features.Imports;

public sealed class LocalImportDiscovery(IConfiguration config)
{
    public record CandidateRoot(string Id, string Provider, string Path);

    public record FoundFile(
        string Id,
        string RootId,
        string RelativePath,
        long Size,
        DateTime Modified
    );

    private record Ticket(
        string UserId,
        string Path,
        long Size,
        DateTime Modified,
        DateTime Expires
    );

    private readonly ConcurrentDictionary<string, Ticket> tickets = new();

    public bool Enabled(string userId) =>
        config.GetSection("Imports:DiscoveryUserIds").GetChildren().Any(x => x.Value == userId);

    // Returning candidate locations does not inspect directories or read application data.
    public CandidateRoot[] Roots()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return
        [
            new("claude-code", "Claude Code", Path.Combine(home, ".claude", "projects")),
            new("codex", "Codex", Path.Combine(home, ".codex", "sessions")),
            new("gemini-cli", "Gemini CLI", Path.Combine(home, ".gemini", "tmp")),
        ];
    }

    public static bool SafePath(string path)
    {
        for (
            var item = new DirectoryInfo(Path.GetDirectoryName(path)!);
            item != null;
            item = item.Parent
        )
            if (item.Exists && (item.Attributes & FileAttributes.ReparsePoint) != 0)
                return false;
        return !File.Exists(path) || (File.GetAttributes(path) & FileAttributes.ReparsePoint) == 0;
    }

    public IReadOnlyList<FoundFile> Scan(
        string userId,
        string[] rootIds,
        string[] excludes,
        bool consent
    )
    {
        if (!Enabled(userId) || !consent)
            throw new UnauthorizedAccessException(
                "Explicit local discovery consent and operator enablement are required."
            );
        if (rootIds.Length > 3 || excludes.Length > 100)
            throw new InvalidDataException("Too many discovery selections.");
        foreach (
            var key in tickets.Where(x => x.Value.Expires < DateTime.UtcNow).Select(x => x.Key)
        )
            tickets.TryRemove(key, out _);
        var roots = Roots();
        if (rootIds.Any(id => !roots.Any(r => r.Id == id)))
            throw new InvalidDataException("Unknown discovery root.");
        var results = new List<FoundFile>();
        int visited = 0;
        foreach (var root in roots.Where(r => rootIds.Contains(r.Id)))
        {
            if (!Directory.Exists(root.Path) || !SafePath(Path.Combine(root.Path, "probe")))
                continue;
            var pending = new Stack<(string Path, int Depth)>();
            pending.Push((root.Path, 0));
            while (pending.TryPop(out var directory))
            {
                if (++visited > 10000)
                    throw new InvalidDataException(
                        "Discovery directory limit reached; select a smaller folder manually."
                    );
                try
                {
                    foreach (var entry in Directory.EnumerateFileSystemEntries(directory.Path))
                    {
                        var relative = Path.GetRelativePath(root.Path, entry).Replace('\\', '/');
                        if (
                            excludes.Any(e =>
                                relative.Equals(e.Trim('/'), StringComparison.OrdinalIgnoreCase)
                                || relative.StartsWith(
                                    e.Trim('/') + "/",
                                    StringComparison.OrdinalIgnoreCase
                                )
                            )
                        )
                            continue;
                        var attributes = File.GetAttributes(entry);
                        if ((attributes & FileAttributes.ReparsePoint) != 0)
                            continue;
                        if ((attributes & FileAttributes.Directory) != 0)
                        {
                            if (directory.Depth < 20)
                                pending.Push((entry, directory.Depth + 1));
                            continue;
                        }
                        var extension = Path.GetExtension(entry).ToLowerInvariant();
                        if (
                            root.Id is "codex" or "claude-code"
                                ? extension != ".jsonl"
                                : extension != ".json" || !relative.Contains("/chats/")
                        )
                            continue;
                        if (results.Count >= 500)
                            throw new InvalidDataException(
                                "Discovery found more than 500 files; exclude directories or use manual folder selection."
                            );
                        var info = new FileInfo(entry);
                        var id = Guid.NewGuid().ToString("N");
                        tickets[id] = new(
                            userId,
                            entry,
                            info.Length,
                            info.LastWriteTimeUtc,
                            DateTime.UtcNow.AddMinutes(10)
                        );
                        results.Add(new(id, root.Id, relative, info.Length, info.LastWriteTimeUtc));
                    }
                }
                catch (UnauthorizedAccessException)
                { /* inaccessible directories are not probed further */
                }
            }
        }
        return results;
    }

    public Stream Open(string userId, string id)
    {
        if (
            !Enabled(userId)
            || !tickets.TryGetValue(id, out var ticket)
            || ticket.UserId != userId
            || ticket.Expires < DateTime.UtcNow
        )
            throw new UnauthorizedAccessException(
                "Discovery selection expired; scan again with consent."
            );
        if (!SafePath(ticket.Path))
            throw new InvalidDataException("Source path changed; scan again.");
        var info = new FileInfo(ticket.Path);
        if (!info.Exists || info.Length != ticket.Size || info.LastWriteTimeUtc != ticket.Modified)
            throw new InvalidDataException(
                "Source changed since discovery; scan again before importing."
            );
        return new FileStream(
            ticket.Path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            81920,
            true
        );
    }
}
