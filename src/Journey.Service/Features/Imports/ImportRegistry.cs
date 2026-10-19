namespace Journey.Service.Features.Imports;

public class ImportRegistry
{
    public IConversationImporter[] Adapters { get; }

    public ImportRegistry()
        : this([
            new PrivateAssetImporter(),
            new ClaudeProjectImporter(),
            new ChatGptImporter(),
            new ClaudeImporter(),
            new ClaudeCodeImporter(),
            new CodexImporter(),
            new GeminiCliImporter(),
            new ApiMessagesImporter(),
            new CursorSqliteImporter(),
            new MappingImporter(),
            new DeveloperHistoryImporter(),
            new HtmlTranscriptImporter(),
            new TraceImporter(),
            new LangChainImporter(),
            new CursorAgentTranscriptImporter(),
            new GenericImporter(),
        ]) { }

    public ImportRegistry(IEnumerable<IConversationImporter> adapters) =>
        Adapters = adapters.ToArray();

    public List<DetectionCandidate> Candidates(SourceInput source) =>
        Adapters
            .Select(a => new DetectionCandidate(
                a.Capability.Id,
                a.Provider,
                a is GenericImporter
                    ? a.CanImport(source)
                        ? 0.2
                        : 0
                    : a.Confidence(source),
                a.Capability.Status
            ))
            .Where(c => c.Confidence > 0)
            .OrderByDescending(c => c.Confidence)
            .ToList();

    public IConversationImporter Detect(SourceInput source, string? adapterId = null,
        IReadOnlyList<DetectionCandidate>? candidates = null)
    {
        var adapter = (
            adapterId == null || adapterId == ""
                ? (candidates ?? Candidates(source))
                    .Select(c => Adapters.First(a => a.Capability.Id == c.AdapterId))
                    .FirstOrDefault()
                : Adapters.FirstOrDefault(a => a.Capability.Id == adapterId
                    && (candidates == null ? a.CanImport(source) : candidates.Any(c => c.AdapterId == adapterId)))
        );
        if (adapter != null)
            return adapter;
        throw new InvalidDataException(
            SqliteInput.UnsupportedReason(source)
                ?? "Unsupported or incompatible input. Choose a validated adapter or configure manual field mapping."
        );
    }
}
