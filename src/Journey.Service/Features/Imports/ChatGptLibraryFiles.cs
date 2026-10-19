using System.Text.Json;

namespace Journey.Service.Features.Imports;

// ChatGPT exports store attachment bytes as file_<id>.dat. library_files.json
// records the same id, the original file name, and the real extension.
public static class ChatGptLibraryFiles
{
    public readonly record struct Entry(string FileId, string FileName, string Extension)
    {
        public string? Leaf
        {
            get
            {
                var extension = Extension.Trim().TrimStart('.').ToLowerInvariant();
                if (!SafeExtension(extension))
                    return null;
                var name = FileName.Replace('\\', '/').Trim();
                var suffix = "." + extension;
                if (
                    name.Length is > 0 and <= 180
                    && name.IndexOf('/') < 0
                    && !name.Contains(':')
                    && name is not ("." or "..")
                    && !name.Any(char.IsControl)
                    && name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)
                )
                    return name;
                return FileId + suffix;
            }
        }
    }

    public static bool SafeExtension(string extension)
    {
        if (extension.Length is < 1 or > 10)
            return false;
        foreach (var c in extension)
            if (!char.IsAsciiLetterOrDigit(c))
                return false;
        return true;
    }

    public static bool IsPayload(string path)
    {
        var file = Path.GetFileName(path.Replace('\\', '/'));
        return file.EndsWith(".dat", StringComparison.OrdinalIgnoreCase)
            && IsFileId(file[..^4]);
    }

    public static bool IsIndex(string path) =>
        Path.GetFileName(path).Equals("library_files.json", StringComparison.OrdinalIgnoreCase);

    public static bool IsFileId(string value)
    {
        if (value.Length is < 21 or > 80)
            return false;
        if (
            !value.StartsWith("file_", StringComparison.OrdinalIgnoreCase)
            && !value.StartsWith("file-", StringComparison.OrdinalIgnoreCase)
        )
            return false;
        for (var i = 5; i < value.Length; i++)
            if (!char.IsAsciiLetterOrDigit(value[i]))
                return false;
        return true;
    }

    public static void Add(
        Dictionary<string, Entry> index,
        HashSet<string> ambiguous,
        byte[] json
    )
    {
        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("Expected a ChatGPT library_files.json array.");
        foreach (var item in document.RootElement.EnumerateArray())
        {
            var id = item.Get("file_id").Str();
            var extension = item.Get("file_extension").Str();
            if (id == null || extension == null || !IsFileId(id) || ambiguous.Contains(id))
                continue;
            var entry = new Entry(id, item.Get("file_name").Str() ?? "", extension);
            if (entry.Leaf == null)
                continue;
            if (index.TryGetValue(id, out var existing) && existing.Leaf != entry.Leaf)
            {
                index.Remove(id);
                ambiguous.Add(id);
                continue;
            }
            index[id] = entry;
        }
    }

    public static string? RenamedPath(string sourceName, IReadOnlyDictionary<string, Entry> index)
    {
        var normalized = sourceName.Replace('\\', '/');
        var slash = normalized.LastIndexOf('/');
        var file = slash < 0 ? normalized : normalized[(slash + 1)..];
        if (!file.EndsWith(".dat", StringComparison.OrdinalIgnoreCase))
            return null;
        var id = file[..^4];
        if (!index.TryGetValue(id, out var entry) || entry.Leaf == null)
            return null;
        var dir = slash < 0 ? "" : normalized[..slash];
        var renamed = (dir.Length == 0 ? "" : dir + "/") + id + "/" + entry.Leaf;
        if (renamed.Split('/').Any(p => p is "" or "." or "..") || renamed.Contains(':'))
            return null;
        return renamed;
    }

    public static bool NamesFile(string storedName, string id)
    {
        foreach (var segment in SourceRecovery.Normalize(storedName).Split('/'))
        {
            if (segment.Equals(id, StringComparison.OrdinalIgnoreCase))
                return true;
            var dot = segment.LastIndexOf('.');
            if (dot > 0 && segment[..dot].Equals(id, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }
}
