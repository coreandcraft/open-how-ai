using System.Text;
using System.Text.Json;
using Journey.Data;
using Journey.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Journey.ScaleBench;

/// <summary>
/// Pilot corpus: zipf-like conversation sizes, mixed-entropy text, realistic raw payloads,
/// a 1,000-node journey, and a second small workspace so load is skewed.
/// </summary>
public static class Corpus
{
    public const string LargeJourney = "scale-j-large";
    public const int PilotConversations = 1500;
    public const int PilotComparisons = 150;
    public const int PilotSmallJourneys = 15;

    static readonly string[] Words =
        "the of and to in is you that it he was for on are as with his they I at be this have from or one had by word but not what all were we when your can said there use an each which she do how their if will up other about out many then them these so some her would make like him into time has look two more write go see number no way could people my than first water been call who oil its now find long down day did get come made may part evidence decision latency compatibility streaming emulator browser region player session prototype tradeoff risk cost".Split(
            ' '
        );

    public static async Task SeedBaseline(BenchFactory factory, int previous, int size)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDb>();
        var owner = (await db.Users.SingleAsync(u => u.Email == "alex@journey.local")).Id;
        for (var i = previous; i < size; i++)
        {
            var c = Conversation($"scale-c{i}", "cloudplay", 20, Repeat('t', 1000), Repeat('x', 2048));
            db.Conversations.Add(c);
            db.Comparisons.Add(
                new ComparisonSession
                {
                    Id = $"scale-compare{i}",
                    WorkspaceId = "cloudplay",
                    Title = $"Comparison {i}",
                    LeftId = "scale-c0",
                    RightId = "scale-long",
                    Rationale = Repeat('r', 2048),
                }
            );
            if (i % 10 == 0)
                db.Journeys.Add(Journey($"scale-j{i}", owner, "cloudplay", 100, c.Id));
            if (i % 50 == 49)
            {
                await db.SaveChangesAsync();
                db.ChangeTracker.Clear();
            }
        }
        if (previous == 0)
        {
            db.Conversations.Add(Conversation("scale-long", "cloudplay", 1000, Repeat('t', 1000), Repeat('x', 65536)));
            db.Publications.Add(Publication());
        }
        await db.SaveChangesAsync();
    }

    public static async Task<(int Conversations, int Messages, int Journeys)> SeedPilot(BenchFactory factory)
    {
        using var scope = factory.Services.CreateScope();
        return await SeedPilot(scope.ServiceProvider.GetRequiredService<AppDb>());
    }

    public static async Task<(int Conversations, int Messages, int Journeys)> SeedPilot(AppDb db)
    {
        var owner = (await db.Users.SingleAsync(u => u.Email == "alex@journey.local")).Id;
        var messages = 0;
        for (var i = 0; i < PilotConversations; i++)
        {
            var count = MessageCount(i);
            var raw = i % 20 == 0 ? 64 * 1024 : 4 * 1024;
            var c = Conversation($"scale-c{i}", "cloudplay", count, Phrase($"scale-c{i}", count, 180), Phrase($"raw-{i}", 1, raw));
            c.RawPayload = c.Messages[0].Blocks[0].Text.Length >= raw ? c.Messages[0].Blocks[0].Text[..raw] : Phrase($"raw-{i}", 1, raw);
            messages += count;
            db.Conversations.Add(c);
            if (i < PilotComparisons)
                db.Comparisons.Add(
                    new ComparisonSession
                    {
                        Id = $"scale-compare{i}",
                        WorkspaceId = "cloudplay",
                        Title = $"Comparison {i}",
                        Question = Phrase($"q-{i}", 1, 80),
                        LeftId = "scale-c0",
                        RightId = "scale-long",
                        Rationale = Phrase($"rationale-{i}", 1, 400),
                        Links = """[{"left":"scale-c0-m0","right":"scale-long-m0","criterion":"Tradeoff","note":"pilot"}]""",
                    }
                );
            if (i < PilotSmallJourneys)
                db.Journeys.Add(Journey($"scale-j{i}", owner, "cloudplay", 40, c.Id));
            if (i % 40 == 39)
            {
                await db.SaveChangesAsync();
                db.ChangeTracker.Clear();
            }
        }
        db.Conversations.Add(
            Conversation("scale-long", "cloudplay", 1000, Phrase("scale-long", 1000, 220), Phrase("raw-long", 1, 256 * 1024))
        );
        messages += 1000;
        db.Journeys.Add(Journey(LargeJourney, owner, "cloudplay", 1000, "scale-long"));
        db.Publications.Add(Publication());
        for (var i = 0; i < 40; i++)
            db.Conversations.Add(
                Conversation($"scale-personal-{i}", "personal", 6, Phrase($"p-{i}", 6, 120), Phrase($"p-raw-{i}", 1, 1024))
            );
        await db.SaveChangesAsync();
        return (PilotConversations + 1 + 40, messages + 240, PilotSmallJourneys + 1);
    }

    public static Conversation Conversation(string id, string workspace, int count, string text, string raw) =>
        new()
        {
            Id = id,
            WorkspaceId = workspace,
            SourceKey = id,
            Title = "Synthetic " + id,
            Provider = "synthetic",
            RawPayload = raw,
            Messages = Enumerable
                .Range(0, count)
                .Select(i => new ConversationMessage
                {
                    Id = id + "-m" + i,
                    ConversationId = id,
                    Order = i,
                    Role = i % 2 == 0 ? "user" : "assistant",
                    Blocks =
                    [
                        new MessageContentBlock
                        {
                            Id = id + "-b" + i,
                            ConversationMessageId = id + "-m" + i,
                            Kind = "text",
                            Text = text.Contains("benchmark", StringComparison.Ordinal)
                                ? text
                                : "benchmark evidence " + Phrase(id, i, 160),
                        },
                    ],
                })
                .ToList(),
        };

    static Journey.Domain.Journey Journey(string id, string owner, string workspace, int nodes, string conversationId)
    {
        var graph = new
        {
            nodes = Enumerable
                .Range(0, nodes)
                .Select(n => new
                {
                    id = "node" + n,
                    type = "journey",
                    position = new { x = (n % 40) * 80, y = (n / 40) * 70 },
                    data = new
                    {
                        kind = n % 17 == 0 ? "decision" : "conversation",
                        title = "Decision " + n + " " + Phrase(id, n, 40),
                        conversationId = n % 3 == 0 ? conversationId : "",
                    },
                }),
            edges = Enumerable
                .Range(0, Math.Max(0, nodes - 1))
                .Select(n => new { source = "node" + n, target = "node" + (n + 1) }),
            steps = Enumerable.Range(0, Math.Min(nodes, 200)).Select(n => "node" + n),
        };
        return new Journey.Domain.Journey
        {
            Id = id,
            WorkspaceId = workspace,
            OwnerId = owner,
            Title = "Journey " + id,
            Description = Phrase(id, 0, 120),
            Visibility = "Team",
            Graph = JsonSerializer.Serialize(graph),
        };
    }

    static JourneyPublication Publication() =>
        new()
        {
            Id = "scale-publication",
            WorkspaceId = "cloudplay",
            Slug = "scale-public",
            Visibility = "Unlisted",
            Snapshot = JsonSerializer.Serialize(new { title = "Synthetic snapshot", body = Repeat('s', 2 * 1024 * 1024) }),
        };

    static int MessageCount(int index)
    {
        var rank = (index + 1) / (double)PilotConversations;
        if (rank <= 0.01)
            return 200;
        if (rank <= 0.05)
            return 80;
        if (rank <= 0.20)
            return 30;
        return 8;
    }

    static string Phrase(string seed, int salt, int length)
    {
        var text = new StringBuilder(length + 16);
        var state = HashCode.Combine(seed, salt);
        while (text.Length < length)
        {
            state = unchecked(state * 1103515245 + 12345);
            text.Append(Words[(state & 0x7fffffff) % Words.Length]).Append(' ');
        }
        return text.ToString();
    }

    static string Repeat(char c, int count) => new(c, count);
}
