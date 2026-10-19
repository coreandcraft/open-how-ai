using System.Diagnostics;
using OpenTelemetry;

namespace Journey.Server;

/// <summary>
/// Drops health-check traces and removes query strings and invitation tokens before export.
/// </summary>
sealed class RequestTelemetryProcessor : BaseProcessor<Activity>
{
    static readonly string[] UrlTags = ["url.full", "url.path", "http.url", "http.target"];

    public override void OnEnd(Activity activity)
    {
        var drop = false;
        foreach (var key in UrlTags)
        {
            if (activity.GetTagItem(key) is not string value)
                continue;
            drop |= RequestTelemetry.IsHealthCheck(value);
            var redacted = RequestTelemetry.Redact(value);
            if (!string.Equals(redacted, value, StringComparison.Ordinal))
                activity.SetTag(key, redacted);
        }

        if (activity.GetTagItem("url.query") is not null)
            activity.SetTag("url.query", null);

        if (!drop)
            return;
        activity.ActivityTraceFlags &= ~ActivityTraceFlags.Recorded;
        activity.IsAllDataRequested = false;
    }
}

public static class RequestTelemetry
{
    public static bool IsHealthCheck(string? value)
    {
        var path = TrimQuery(value);
        return path.Equals("/api/health", StringComparison.OrdinalIgnoreCase);
    }

    public static string Redact(string value)
    {
        value = TrimQuery(value);
        const string marker = "/invitations/";
        var at = value.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (at < 0)
            return value;
        var start = at + marker.Length;
        var end = value.IndexOf('/', start);
        if (end < 0)
            end = value.Length;
        if (end == start)
            return value;
        return string.Concat(value.AsSpan(0, start), "{redacted}", value.AsSpan(end));
    }

    static string TrimQuery(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return "";
        var cut = value.IndexOfAny(['?', '#']);
        return cut >= 0 ? value[..cut] : value;
    }
}
