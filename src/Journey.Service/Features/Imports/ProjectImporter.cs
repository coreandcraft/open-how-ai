using System.Text.Json;
using Journey.Domain;

namespace Journey.Service.Features.Imports;

public class ClaudeProjectImporter : IConversationImporter
{
    public string Provider => "Claude";
    public ImportCapability Capability =>
        new(
            "claude-projects-v1",
            Provider,
            "experimental",
            "1",
            [".json"],
            "projects.json records with uuid/name/prompt_template. Knowledge fields remain original metadata; missing conversation associations are not guessed."
        );

    public bool CanImport(SourceInput source)
    {
        if (
            !Importer.HasExtension(source, ".json")
            || !Path.GetFileName(source.Name)
                .Equals("projects.json", StringComparison.OrdinalIgnoreCase)
        )
            return false;
        try
        {
            var root = ParsedImportInput.For(source).Root;
            return root.ValueKind == JsonValueKind.Array
                && root.Items()
                    .Any(r =>
                        r.Get("uuid").Str() != null
                        && r.Get("name").Str() != null
                        && r.Get("prompt_template").ValueKind != JsonValueKind.Undefined
                    );
        }
        catch
        {
            return false;
        }
    }

    public Task<ImportPreview> PreviewAsync(SourceInput source, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var root = JsonDocument.Parse(source.Bytes).RootElement;
        var projects = root.Items()
            .Where(r => r.Get("uuid").Str() != null)
            .Select(r => new SourceProject
            {
                Provider = Provider,
                SourceIdentity = source.SourceIdentity,
                ExternalId = r.Get("uuid").Str()!,
                Name = r.Get("name").Str() ?? r.Get("uuid").Str()!,
                Metadata = r.GetRawText(),
            })
            .ToList();
        return Task.FromResult(new ImportPreview(Provider, [], [], projects));
    }

    public async Task<ImportResult> ImportAsync(SourceInput source, CancellationToken ct)
    {
        var preview = await PreviewAsync(source, ct);
        return new(preview.Conversations, preview.Projects);
    }

    public static string Key(SourceProject p) =>
        Value.Hash(p.Provider + "\n" + p.SourceIdentity + "\n" + p.Metadata);
}
