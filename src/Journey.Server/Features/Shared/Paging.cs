using System.Buffers.Text;
using System.Globalization;
using System.Text;
using Journey.Domain;

namespace Journey.Server.Features.Shared;

public record Page<T>(IReadOnlyList<T> Items, string? NextCursor);

/// <summary>Newest-first keyset paging over (CreatedAt, Id), matching the composite list indexes.</summary>
public static class Paging
{
    public const int DefaultLimit = 50;
    public const int MaxLimit = 200;

    public static int Limit(int? requested, int fallback = DefaultLimit, int max = MaxLimit) =>
        Math.Clamp(requested ?? fallback, 1, max);

    public static IQueryable<T> Newest<T>(this IQueryable<T> query, string? cursor)
        where T : Record
    {
        if (Decode(cursor) is { } after)
        {
            var (at, id) = after;
            query = query.Where(x => x.CreatedAt < at || (x.CreatedAt == at && string.Compare(x.Id, id) < 0));
        }
        return query.OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id);
    }

    /// <summary>Builds a page from <paramref name="limit"/> + 1 rows fetched with <see cref="Newest"/>.</summary>
    public static Page<T> Of<T>(List<T> rows, int limit, Func<T, (DateTime CreatedAt, string Id)> key)
    {
        if (rows.Count <= limit)
            return new(rows, null);
        rows.RemoveRange(limit, rows.Count - limit);
        return new(rows, Encode(key(rows[^1])));
    }

    static string Encode((DateTime CreatedAt, string Id) key) =>
        Base64Url.EncodeToString(
            Encoding.UTF8.GetBytes(key.CreatedAt.Ticks.ToString(CultureInfo.InvariantCulture) + ":" + key.Id)
        );

    static (DateTime, string)? Decode(string? cursor)
    {
        if (string.IsNullOrEmpty(cursor))
            return null;
        try
        {
            var text = Encoding.UTF8.GetString(Base64Url.DecodeFromChars(cursor));
            var split = text.IndexOf(':');
            if (split > 0 && long.TryParse(text[..split], CultureInfo.InvariantCulture, out var ticks))
                return (new DateTime(ticks, DateTimeKind.Utc), text[(split + 1)..]);
        }
        catch (FormatException) { }
        catch (ArgumentOutOfRangeException) { }
        throw new InvalidDataException("Invalid cursor.");
    }
}
