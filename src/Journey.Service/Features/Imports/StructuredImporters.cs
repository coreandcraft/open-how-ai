using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Journey.Domain;
using Microsoft.Data.Sqlite;

namespace Journey.Service.Features.Imports;

public static class SqliteInput
{
    public static bool IsDatabase(SourceInput source) =>
        source.Bytes.AsSpan().StartsWith("SQLite format 3\0"u8);

    public static string? UnsupportedReason(SourceInput source)
    {
        if (
            Regex.IsMatch(
                source.Name,
                @"\.(db|sqlite|sqlite3|vscdb)-(wal|shm)$",
                RegexOptions.IgnoreCase
            )
        )
            return "SQLite WAL/SHM sidecar detected. This is not a standalone conversation export. "
                + "JourneyAI does not replay sidecars; upload a consistent database snapshot with a supported message schema, "
                + "or the original JSON/JSONL transcript.";
        if (!IsDatabase(source))
            return null;
        try
        {
            return Read(
                source,
                connection =>
                {
                    using var command = connection.CreateCommand();
                    command.CommandText =
                        "SELECT count(*) FROM sqlite_master WHERE type='table' AND name='local_thread_catalog'";
                    if (Convert.ToInt32(command.ExecuteScalar()) == 0)
                        return null;
                    return "Codex desktop chat catalog detected. Chat titles and IDs are not conversation messages, "
                        + "and this database format is not a supported transcript source. "
                        + "Upload a ChatGPT conversation export containing messages, or a local Codex rollout JSONL from .codex/sessions. "
                        + "WAL/SHM files do not turn the catalog into a conversation export.";
                }
            );
        }
        catch (SqliteException)
        {
            return "SQLite database could not be read. Upload a consistent snapshot or the original JSON/JSONL transcript. "
                + "JourneyAI does not replay WAL/SHM sidecars.";
        }
    }

    public static T Read<T>(SourceInput source, Func<SqliteConnection, T> action)
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            "journey-import-" + Guid.NewGuid().ToString("N") + ".db"
        );
        try
        {
            File.WriteAllBytes(path, source.Bytes);
            using var connection = new SqliteConnection(
                new SqliteConnectionStringBuilder
                {
                    DataSource = path,
                    Mode = SqliteOpenMode.ReadOnly,
                    Pooling = false,
                }.ToString()
            );
            connection.Open();
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "PRAGMA query_only=ON";
                command.ExecuteNonQuery();
            }
            return action(connection);
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }

    public static string Identifier(string value) =>
        Regex.IsMatch(value, "^[A-Za-z_][A-Za-z0-9_]*$")
            ? "\"" + value + "\""
            : throw new InvalidDataException(
                "Mapping identifiers must contain only letters, digits and underscores."
            );
}

public class CursorSqliteImporter : SessionImporter, IConversationImporter
{
    public override string Provider => "Cursor";
    ImportCapability IConversationImporter.Capability =>
        new(
            "cursor-sqlite-v1",
            Provider,
            "experimental",
            "composer-bubbles-v1",
            [".vscdb", ".sqlite", ".db"],
            "Validated only for ItemTable composer.composerData fullConversation and cursorDiskKV composerData records. Upload a consistent SQLite snapshot; WAL sidecars are not replayed."
        );

    public override bool CanImport(SourceInput source)
    {
        if (!SqliteInput.IsDatabase(source))
            return false;
        try
        {
            return SqliteInput.Read(
                source,
                c =>
                {
                    using var cmd = c.CreateCommand();
                    cmd.CommandText =
                        "SELECT name FROM sqlite_master WHERE type='table' AND name IN ('ItemTable','cursorDiskKV')";
                    using var r = cmd.ExecuteReader();
                    return r.Read();
                }
            );
        }
        catch
        {
            return false;
        }
    }

    protected override List<Conversation> Parse(SourceInput source) =>
        SqliteInput.Read(
            source,
            connection =>
            {
                var results = new List<Conversation>();
                foreach (var table in new[] { "ItemTable", "cursorDiskKV" })
                {
                    using var exists = connection.CreateCommand();
                    exists.CommandText =
                        "SELECT count(*) FROM sqlite_master WHERE type='table' AND name=$name";
                    exists.Parameters.AddWithValue("$name", table);
                    if (Convert.ToInt32(exists.ExecuteScalar()) == 0)
                        continue;
                    using var cmd = connection.CreateCommand();
                    cmd.CommandText =
                        $"SELECT key,value FROM {SqliteInput.Identifier(table)} WHERE key = 'composer.composerData' OR key LIKE 'composerData:%' LIMIT 10000";
                    using var reader = cmd.ExecuteReader();
                    while (reader.Read())
                    {
                        var key = reader.GetString(0);
                        var text = reader.GetValue(1) is byte[] bytes
                            ? Encoding.UTF8.GetString(bytes)
                            : reader.GetString(1);
                        if (text.Length > 20_000_000)
                            throw new InvalidDataException("Cursor record exceeds 20 MB.");
                        var root = JsonDocument.Parse(text).RootElement;
                        var records =
                            root.Get("allComposers").ValueKind == JsonValueKind.Array
                                ? root.Get("allComposers").Items()
                                : Records(root);
                        foreach (var record in records)
                        {
                            var messages = record.Get("conversation");
                            if (messages.ValueKind != JsonValueKind.Array)
                                messages = record.Get("fullConversation");
                            if (messages.ValueKind != JsonValueKind.Array)
                                continue;
                            var c = New(record, "cursor-composer-bubbles-v1");
                            c.ProviderId =
                                record.Get("composerId").Str()
                                ?? (key.StartsWith("composerData:") ? key[13..] : null);
                            c.SourcePath = record.Get("workspacePath").Str();
                            foreach (var m in messages.Items())
                            {
                                var role = m.Get("type").Str() switch
                                {
                                    "1" => "user",
                                    "2" => "assistant",
                                    _ => m.Get("role").Str() ?? "unknown",
                                };
                                c.Messages.Add(
                                    Message(
                                        m,
                                        m.Get("bubbleId").Str() ?? m.Get("id").Str(),
                                        m.Get("parentBubbleId").Str(),
                                        role,
                                        m.Get("text").ValueKind != JsonValueKind.Undefined
                                            ? m.Get("text")
                                            : m.Get("content"),
                                        c.Messages.Count,
                                        m
                                    )
                                );
                            }
                            c.Diagnostics = Json.Write(
                                new[]
                                {
                                    "Experimental Cursor schema: unknown records remain in original database; only composer conversation rows are normalized.",
                                }
                            );
                            Parents(c);
                            results.Add(c);
                        }
                    }
                }
                if (results.Count == 0)
                    throw new InvalidDataException(
                        "SQLite detected but no supported Cursor composer payloads. Use manual SQLite mapping or supply a supported snapshot."
                    );
                return results;
            }
        );
}

public record FieldMapping(
    string? Table = null,
    string? ArrayPath = null,
    string Role = "role",
    string Content = "content",
    string? Id = "id",
    string? Parent = "parentId",
    string? Conversation = "conversationId",
    string? Project = "projectId",
    string? Timestamp = "timestamp",
    string Provider = "Mapped",
    string? Title = null
);

public class MappingImporter : SessionImporter, IConversationImporter
{
    public override string Provider => "Mapped";
    ImportCapability IConversationImporter.Capability =>
        new(
            "manual-mapping",
            Provider,
            "implemented",
            "1",
            [".json", ".jsonl", ".csv", ".db"],
            "Explicit field mapping only. Dot-separated JSON paths; SQLite table/column identifiers validated. No SQL execution supplied by users."
        );

    public override bool CanImport(SourceInput source) =>
        source.Mapping != "{}" && !string.IsNullOrWhiteSpace(source.Mapping);

    protected override List<Conversation> Parse(SourceInput source)
    {
        var mapping = Json.Read<FieldMapping>(source.Mapping);
        var rows = new List<JsonElement>();
        if (SqliteInput.IsDatabase(source))
        {
            if (mapping.Table == null)
                throw new InvalidDataException("Select a SQLite table.");
            rows = SqliteInput.Read(
                source,
                connection =>
                {
                    using var cmd = connection.CreateCommand();
                    cmd.CommandText =
                        "SELECT * FROM " + SqliteInput.Identifier(mapping.Table) + " LIMIT 100001";
                    using var reader = cmd.ExecuteReader();
                    var result = new List<JsonElement>();
                    while (reader.Read())
                    {
                        if (result.Count == 100000)
                            throw new InvalidDataException(
                                "Mapping exceeds 100,000 rows; split the source."
                            );
                        var row = new Dictionary<string, object?>();
                        for (var i = 0; i < reader.FieldCount; i++)
                            row[reader.GetName(i)] = reader.IsDBNull(i) ? null : reader.GetValue(i);
                        result.Add(JsonSerializer.SerializeToElement(row));
                    }
                    return result;
                }
            );
        }
        else if (Path.GetExtension(source.Name).Equals(".csv", StringComparison.OrdinalIgnoreCase))
        {
            using var parser = new Microsoft.VisualBasic.FileIO.TextFieldParser(
                new StringReader(Encoding.UTF8.GetString(source.Bytes))
            )
            {
                TextFieldType = Microsoft.VisualBasic.FileIO.FieldType.Delimited,
                HasFieldsEnclosedInQuotes = true,
            };
            parser.SetDelimiters(",");
            var headers = parser.ReadFields() ?? [];
            while (!parser.EndOfData)
            {
                var fields = parser.ReadFields() ?? [];
                rows.Add(
                    JsonSerializer.SerializeToElement(
                        headers
                            .Select((h, i) => new { h, v = i < fields.Length ? fields[i] : "" })
                            .ToDictionary(x => x.h, x => x.v)
                    )
                );
            }
        }
        else if (Path.GetExtension(source.Name) is ".jsonl" or ".ndjson")
            rows = Lines(source, out _);
        else
        {
            var root = Root(source);
            var array = At(root, mapping.ArrayPath);
            rows = Records(array).ToList();
        }
        return rows.GroupBy(r => At(r, mapping.Conversation).Str() ?? "document")
            .Select(group =>
            {
                var c = new Conversation
                {
                    Provider = mapping.Provider,
                    ProviderId = group.Key == "document" ? null : group.Key,
                    Title = mapping.Title ?? "Mapped conversation " + group.Key,
                    ExternalProjectId = At(group.First(), mapping.Project).Str(),
                    SourceFormat = "manual-mapping-v1",
                    RawPayload = Json.Write(group),
                    Metadata = source.Mapping,
                };
                foreach (var row in group)
                {
                    var role = At(row, mapping.Role).Str();
                    if (role == null)
                        throw new InvalidDataException("Mapped role field is missing.");
                    c.Messages.Add(
                        Message(
                            row,
                            At(row, mapping.Id).Str(),
                            At(row, mapping.Parent).Str(),
                            role,
                            At(row, mapping.Content),
                            c.Messages.Count,
                            row
                        )
                    );
                    c.Messages.Last().Timestamp = At(row, mapping.Timestamp).Date();
                }
                Parents(c);
                return c;
            })
            .ToList();
    }

    static JsonElement At(JsonElement row, string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return row;
        foreach (var part in path.Split('.'))
            row = row.Get(part);
        return row;
    }
}
