using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Journey.Domain;
using Journey.Data;

namespace Journey.Service.Features.Review;

public static class Anchoring
{
    public static AnnotationAnchor Create(string blockId, string text, int start, int end)
    {
        if (start < 0 || end > text.Length || end <= start)
            throw new InvalidDataException("Invalid text range.");
        return new(
            blockId,
            start,
            end,
            text[start..end],
            text[Math.Max(0, start - 40)..start],
            text[end..Math.Min(text.Length, end + 40)],
            Value.Hash(text)
        );
    }

    public static AnnotationAnchor? Recover(AnnotationAnchor a, string text)
    {
        if (
            a.Fingerprint == Value.Hash(text)
            && a.Start >= 0
            && a.End > a.Start
            && a.End <= text.Length
            && text[a.Start..a.End] == a.Quote
        )
            return a;
        if (string.IsNullOrEmpty(a.Quote))
            return null;
        var matches = new List<int>();
        for (var i = 0; i <= text.Length - a.Quote.Length; i++)
            if (text.AsSpan(i, a.Quote.Length).SequenceEqual(a.Quote))
                matches.Add(i);
        var contextual = matches
            .Where(i =>
                text[..i].EndsWith(a.Prefix, StringComparison.Ordinal)
                && text[(i + a.Quote.Length)..].StartsWith(a.Suffix, StringComparison.Ordinal)
            )
            .ToList();
        var p =
            contextual.Count == 1 ? contextual[0]
            : matches.Count == 1 ? matches[0]
            : -1;
        return p < 0 ? null : Create(a.BlockId, text, p, p + a.Quote.Length);
    }
}
