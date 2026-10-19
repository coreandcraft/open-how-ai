using Journey.Data;
using Journey.Domain;

namespace Journey.Service.Features.Imports;

public sealed class UnsupportedProviderImporter(string provider, string variant)
    : IConversationImporter
{
    public string Provider => provider;
    public ImportCapability Capability =>
        new(
            provider.ToLowerInvariant().Replace(" ", "-") + "-pending-" + Value.Hash(variant)[..8],
            provider,
            "pending sample validation",
            "unknown",
            [],
            variant
        );

    public bool CanImport(SourceInput source) => false;

    public Task<ImportPreview> PreviewAsync(
        SourceInput source,
        CancellationToken cancellationToken
    ) =>
        throw new InvalidDataException(
            $"{Provider} {variant} is not implemented. Use the field-mapping wizard or supply a validated format; original inputs remain private."
        );

    public Task<ImportResult> ImportAsync(
        SourceInput source,
        CancellationToken cancellationToken
    ) => throw new InvalidDataException($"{Provider} {variant} is not implemented.");
}

public static class PlannedImporters
{
    public static IConversationImporter[] Create() =>
        [
            new UnsupportedProviderImporter("Cursor", "other IDE storage versions"),
            new UnsupportedProviderImporter("Windsurf", "native history"),
            new UnsupportedProviderImporter("Grok / Perplexity / Poe", "native history"),
            new UnsupportedProviderImporter("Gemini", "Takeout"),
            new UnsupportedProviderImporter("OCF", "unversioned dialect"),
            .. new[]
            {
                "Microsoft Copilot",
                "Google AI Studio",
                "LM Studio",
                "Jan",
                "Open WebUI",
                "LibreChat",
                "AnythingLLM",
            }.Select(p => new UnsupportedProviderImporter(
                p,
                "native schema awaiting representative sample validation"
            )),
        ];
}
