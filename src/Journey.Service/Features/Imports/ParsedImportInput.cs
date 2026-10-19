using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;

namespace Journey.Service.Features.Imports;

// A record's detectors and parser share decoded JSON. Weak keys bound the cache to input lifetime,
// and keying by bytes also keeps SourceInput 'with { Bytes = ... }' safe.
internal sealed class ParsedImportInput
{
    static readonly ConditionalWeakTable<byte[], ParsedImportInput> cache = new();
    readonly Lazy<JsonElement> root;
    readonly Lazy<(List<JsonElement> Rows, List<string> Warnings)> lines;

    ParsedImportInput(byte[] bytes)
    {
        root = new(() =>
        {
            using var document = JsonDocument.Parse(bytes);
            return document.RootElement.Clone();
        });
        lines = new(() =>
        {
            var rows = new List<JsonElement>();
            var warnings = new List<string>();
            using var reader = new StreamReader(new MemoryStream(bytes, false), Encoding.UTF8,
                detectEncodingFromByteOrderMarks: true);
            string? line;
            int number = 0;
            while ((line = reader.ReadLine()) != null)
            {
                number++;
                if (string.IsNullOrWhiteSpace(line))
                    continue;
                try
                {
                    using var document = JsonDocument.Parse(line);
                    rows.Add(document.RootElement.Clone());
                }
                catch (JsonException)
                {
                    warnings.Add($"Malformed JSON at line {number}; preserved in original source.");
                }
            }
            return (rows, warnings);
        });
    }

    internal static ParsedImportInput For(SourceInput source) =>
        cache.GetValue(source.Bytes, bytes => new(bytes));
    internal JsonElement Root => root.Value;
    internal List<JsonElement> Lines(out List<string> warnings)
    {
        var parsed = lines.Value;
        // Warnings can be augmented by the selected parser without changing later detection.
        warnings = new(parsed.Warnings);
        return parsed.Rows;
    }
}
