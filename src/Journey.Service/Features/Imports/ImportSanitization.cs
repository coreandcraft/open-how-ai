using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Journey.Domain;

namespace Journey.Service.Features.Imports;

public record ReplacementRule(string Find, string Replace);

public record SanitizationOptions(
    bool Enabled = false,
    bool ScanPatterns = false,
    ReplacementRule[]? Rules = null,
    bool AllowOriginalFiles = false
);

public sealed class OriginalFileConsentRequiredException : Exception
{
    public OriginalFileConsentRequiredException()
        : base("Some files cannot be sanitized. Review the warning before keeping originals.") { }
}

public record SanitizedSource(byte[] Bytes, bool OriginalRetained);

public static class ImportSanitization
{
    // Bound decoded text and JSON DOM allocations independently of the account upload quota.
    public const long MaxSourceBytes = 50_000_000;

    public static async Task<byte[]> Source(
        Stream input,
        string name,
        SanitizationOptions options,
        CancellationToken ct
    ) => (await PrepareSource(input, name, options, ct)).Bytes;

    public static async Task<SanitizedSource> PrepareSource(
        Stream input,
        string name,
        SanitizationOptions options,
        CancellationToken ct
    )
    {
        var bytes = await ArchiveInspector.ReadBounded(input, MaxSourceBytes, ct);
        SanitizedSource Original() =>
            options.AllowOriginalFiles
                ? new(bytes, true)
                : throw new OriginalFileConsentRequiredException();
        string text;
        try
        {
            text = new UTF8Encoding(false, true).GetString(bytes).TrimStart('\uFEFF');
        }
        catch (DecoderFallbackException)
        {
            return Original();
        }
        if (
            text.Any(c => char.IsControl(c) && c is not ('\r' or '\n' or '\t'))
            || PrivateAssetImporter.Recognized(bytes)
        )
            return Original();
        try
        {
            var extension = Path.GetExtension(name).ToLowerInvariant();
            if (extension == ".json")
            {
                // Fail closed rather than treating malformed/escaped JSON as plain text.
                using var document = System.Text.Json.JsonDocument.Parse(text);
                text = Payload(text, options, ct);
            }
            else if (extension is ".jsonl" or ".ndjson")
            {
                var lines = text.Split('\n');
                for (var i = 0; i < lines.Length; i++)
                {
                    ct.ThrowIfCancellationRequested();
                    if (!string.IsNullOrWhiteSpace(lines[i]))
                    {
                        using var document = System.Text.Json.JsonDocument.Parse(lines[i]);
                        lines[i] = Payload(lines[i], options, ct);
                    }
                }
                text = string.Join('\n', lines);
            }
            else if (extension is ".txt" or ".md" or ".markdown" or ".csv" or ".log")
                text = Text(text, options, ct);
            else
                return Original();
            return new(Encoding.UTF8.GetBytes(text), false);
        }
        catch (Exception ex)
            when (ex
                    is System.Text.Json.JsonException
                        or InvalidDataException
                        or RegexMatchTimeoutException
            )
        {
            return Original();
        }
    }

    public static ReplacementRule[] Validate(ReplacementRule[]? rules)
    {
        rules ??= [];
        if (
            rules.Length > 100
            || rules.Any(r =>
                r == null
                || string.IsNullOrWhiteSpace(r.Find)
                || r.Find.Length > 1000
                || r.Replace == null
                || r.Replace.Length > 1000
            )
        )
            throw new InvalidDataException(
                "Use at most 100 replacement rules with nonblank search text and values of at most 1000 characters."
            );
        if (rules.Select(r => r.Find).Distinct(StringComparer.Ordinal).Count() != rules.Length)
            throw new InvalidDataException("Each search phrase must be unique.");
        return rules;
    }

    public static string Replace(string text, ReplacementRule[]? rules)
    {
        var valid = Validate(rules);
        if (valid.Length == 0 || text.Length == 0)
            return text;
        var values = valid.ToDictionary(r => r.Find, r => r.Replace, StringComparer.Ordinal);
        // A single pass prevents replacement text from being replaced again by another rule.
        return Regex.Replace(
            text,
            string.Join(
                "|",
                valid.OrderByDescending(r => r.Find.Length).Select(r => Regex.Escape(r.Find))
            ),
            m => values[m.Value],
            RegexOptions.None,
            TimeSpan.FromSeconds(5)
        );
    }

    public static string Text(string text, SanitizationOptions options, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (!options.Enabled)
            return text;
        text = Replace(text, options.Rules);
        return options.ScanPatterns
            ? Publishing.RedactPatterns(text, ct)
            : text;
    }

    public static string Payload(string text, SanitizationOptions options, CancellationToken ct = default)
    {
        if (!options.Enabled || string.IsNullOrEmpty(text))
            return text;
        JsonNode? root;
        try
        {
            root = JsonNode.Parse(text);
        }
        catch (System.Text.Json.JsonException)
        {
            return Text(text, options, ct);
        }
        JsonNode? Walk(JsonNode? node)
        {
            ct.ThrowIfCancellationRequested();
            if (node is JsonValue v && v.TryGetValue<string>(out var s))
                return JsonValue.Create(Text(s, options, ct));
            if (node is JsonObject obj)
            {
                // Detach once instead of DeepClone-ing every subtree at every nesting level.
                var properties = obj.ToArray();
                obj.Clear();
                foreach (var property in properties)
                {
                    var key = Text(property.Key, options, ct);
                    if (obj.ContainsKey(key))
                        throw new InvalidDataException(
                            "Sanitization would create duplicate JSON field names. Adjust the replacement rules."
                        );
                    obj.Add(key, Walk(property.Value));
                }
                return obj;
            }
            if (node is JsonArray array)
                for (int i = 0; i < array.Count; i++)
                {
                    var item = array[i];
                    array[i] = null;
                    array[i] = Walk(item);
                }
            return node;
        }
        return Walk(root)?.ToJsonString() ?? "null";
    }

    public static void Apply(Conversation c, SanitizationOptions options)
    {
        if (!options.Enabled)
            return;
        c.Title = Text(c.Title, options);
        c.SourceProjectName =
            c.SourceProjectName == null ? null : Text(c.SourceProjectName, options);
        c.SourcePath = c.SourcePath == null ? null : Text(c.SourcePath, options);
        c.Metadata = Payload(c.Metadata, options);
        c.RawPayload = Payload(c.RawPayload, options);
        c.Provenance = Payload(c.Provenance, options);
        c.Diagnostics = Payload(c.Diagnostics, options);
        foreach (var m in c.Messages)
        {
            m.Metadata = Payload(m.Metadata, options);
            foreach (var b in m.Blocks)
            {
                b.Text = Text(b.Text, options);
                b.Metadata = Payload(b.Metadata, options);
            }
            m.ContentHash = ImportedMessageFingerprint.Compute(m);
        }
    }
}
