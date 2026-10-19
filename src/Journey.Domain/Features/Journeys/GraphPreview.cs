using System.Text.Json;
using System.Text.Json.Nodes;

namespace Journey.Domain;

public static class GraphPreview
{
    public const int MaxNodes = 16;
    const int MaxEdges = 32;
    const int MaxTitle = 40;

    /// <summary>Derives the list-card excerpt and node count from a saved graph.</summary>
    public static (string Preview, int NodeCount) Build(string graph)
    {
        JsonObject root;
        try
        {
            root = JsonNode.Parse(graph) as JsonObject ?? [];
        }
        catch (JsonException)
        {
            root = [];
        }
        var nodes = (root["nodes"] as JsonArray ?? []).OfType<JsonObject>().ToList();
        var visible = nodes.Where(n => Text(n["data"], "kind") != "group").ToList();
        var shown = visible.Take(MaxNodes).ToList();
        var ids = shown.Select(n => Text(n, "id")).ToHashSet();
        var preview = new JsonObject
        {
            ["nodes"] = new JsonArray(
                shown
                    .Select(n => (JsonNode)new JsonObject
                    {
                        ["id"] = Text(n, "id"),
                        ["position"] = Position(n["position"]),
                        ["data"] = new JsonObject
                        {
                            ["kind"] = Text(n["data"], "kind"),
                            ["provider"] = Text(n["data"], "provider"),
                            ["title"] = Clip(Text(n["data"], "title")),
                        },
                    })
                    .ToArray()
            ),
            ["edges"] = new JsonArray(
                (root["edges"] as JsonArray ?? [])
                    .OfType<JsonObject>()
                    .Where(e => ids.Contains(Text(e, "source")) && ids.Contains(Text(e, "target")))
                    .Take(MaxEdges)
                    .Select(e => (JsonNode)new JsonObject
                    {
                        ["source"] = Text(e, "source"),
                        ["target"] = Text(e, "target"),
                    })
                    .ToArray()
            ),
            ["truncated"] = visible.Count > shown.Count,
        };
        return (preview.ToJsonString(), visible.Count);
    }

    static string Text(JsonNode? node, string key) =>
        node is JsonObject o && o[key] is JsonValue v && v.TryGetValue<string>(out var s) ? s : "";

    static string Clip(string value) => value.Length <= MaxTitle ? value : value[..MaxTitle];

    static JsonNode? Position(JsonNode? position) =>
        position is JsonObject p
        && p["x"] is JsonValue x
        && p["y"] is JsonValue y
        && x.TryGetValue<double>(out var px)
        && y.TryGetValue<double>(out var py)
        && double.IsFinite(px)
        && double.IsFinite(py)
            ? new JsonObject { ["x"] = px, ["y"] = py }
            : null;
}
