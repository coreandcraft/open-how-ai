using Journey.Data;
using Journey.Domain;
using Microsoft.EntityFrameworkCore;

namespace Journey.Server.Features.Imports;

// Keep only bounded excerpts: retaining Conversation here would keep the full parsed source alive.
public sealed class ConversationPreview(Conversation c, ImportCapability adapter, bool originalRetained)
{
    public string Key { get; } = c.SourceKey;
    public bool OriginalRetained { get; } = originalRetained;
    public string Title { get; } = c.Title;
    public string Provider { get; } = c.Provider;
    public string? Model { get; } = c.Model;
    public string? ProviderId { get; } = c.ProviderId;
    public string? ExternalProjectId { get; } = c.ExternalProjectId;
    public string? SourceProjectName { get; } = c.SourceProjectName;
    public string? SourcePath { get; } = c.SourcePath;
    public string? ParentConversationExternalId { get; } = c.ParentConversationExternalId;
    public string Diagnostics { get; } = c.Diagnostics;
    public string Provenance { get; } = c.Provenance;
    public ImportCapability Adapter { get; } = adapter;
    public int MessageCount { get; } = c.Messages.Count;
    public object[] Messages { get; } = c.Messages.Take(3).Select(m => (object)new
    {
        m.Id, m.Role, m.Model,
        blocks = m.Blocks.Take(8).Select(b => new
        {
            b.Kind,
            text = b.Text.Length > 4000
                ? b.Text[..4000] + "… [preview truncated; full source retained]" : b.Text,
        }).ToArray(),
    }).ToArray();
    public bool Duplicate { get; set; }
    public bool Update { get; set; }

    public static async Task FindDuplicates(AppDb db, string workspace, string identity,
        IEnumerable<ConversationPreview> previews, ImportProgress progress, CancellationToken ct)
    {
        using var timing = progress.Measure("duplicateLookup");
        foreach (var chunk in previews.Chunk(500))
        {
            ct.ThrowIfCancellationRequested();
            var keys = chunk.Select(p => p.Key).Distinct().ToArray();
            progress.Count("duplicateQueries");
            var duplicates = (await db.Conversations.AsNoTracking()
                .Where(c => c.WorkspaceId == workspace && keys.Contains(c.SourceKey))
                .Select(c => c.SourceKey).ToListAsync(ct)).ToHashSet();
            var ids = chunk.Select(p => p.ProviderId).OfType<string>().Distinct().ToArray();
            var providers = chunk.Select(p => p.Provider).Distinct().ToArray();
            var updates = new HashSet<(string, string?)>();
            if (ids.Length > 0)
            {
                progress.Count("duplicateQueries");
                var matches = await db.Conversations.AsNoTracking()
                    .Where(c => c.WorkspaceId == workspace && c.SourceIdentity == identity
                        && providers.Contains(c.Provider) && ids.Contains(c.ProviderId!))
                    .Select(c => new { c.Provider, c.ProviderId }).ToListAsync(ct);
                updates.UnionWith(matches.Select(c => (c.Provider, c.ProviderId)));
            }
            foreach (var preview in chunk)
            {
                preview.Duplicate = duplicates.Contains(preview.Key);
                preview.Update = preview.ProviderId != null && updates.Contains((preview.Provider, preview.ProviderId));
            }
        }
    }
}
