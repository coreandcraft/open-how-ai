using Journey.Domain;
using Journey.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Journey.Service.Features.Bootstrap;

public static class Seed
{
    public static async Task Run(AppDb db)
    {
        if (await db.Users.AnyAsync())
            return;
        var users = new[]
        {
            new User
            {
                Id = "alex",
                Name = "Alex Morgan",
                Email = "alex@journey.local",
            },
            new User
            {
                Id = "sam",
                Name = "Sam Rivera",
                Email = "sam@journey.local",
            },
            new User
            {
                Id = "riley",
                Name = "Riley Chen",
                Email = "riley@journey.local",
            },
        };
        foreach (var u in users)
        {
            u.UserName = u.Email;
            u.NormalizedUserName = u.Email!.ToUpperInvariant();
            u.NormalizedEmail = u.NormalizedUserName;
            u.EmailConfirmed = true;
            u.LockoutEnabled = true;
            u.PasswordHash = new PasswordHasher<User>().HashPassword(u, "JourneyDemo!2026");
        }
        db.Users.AddRange(users);
        db.Teams.Add(new() { Id = "studio", Name = "Cloudplay Studio" });
        db.Workspaces.AddRange(
            new Workspace
            {
                Id = "personal",
                Name = "Personal library",
                OwnerId = "alex",
            },
            new Workspace
            {
                Id = "cloudplay",
                Name = "Cloudplay Studio",
                OwnerId = "alex",
                TeamId = "studio",
            }
        );
        db.Memberships.AddRange(
            new WorkspaceMembership
            {
                WorkspaceId = "personal",
                UserId = "alex",
                Role = "Owner",
            },
            new WorkspaceMembership
            {
                WorkspaceId = "cloudplay",
                UserId = "alex",
                Role = "Owner",
            },
            new WorkspaceMembership
            {
                WorkspaceId = "cloudplay",
                UserId = "sam",
                Role = "Editor",
            },
            new WorkspaceMembership
            {
                WorkspaceId = "cloudplay",
                UserId = "riley",
                Role = "Viewer",
            }
        );
        var topics = new[]
        {
            (
                "The browser is the console",
                "Initial project concept",
                "Start with one playable session in the browser. Measure time to first frame before expanding the catalog.",
                "A local emulator build does not prove remote play will feel responsive."
            ),
            (
                "Can our emulators run in the browser?",
                "Emulator compatibility research",
                "WebAssembly is viable for lightweight emulators. For demanding systems, run a server process and stream video.",
                "Compatibility, licensing and controller mapping need separate validation."
            ),
            (
                "A stream that feels local",
                "Browser-based streaming",
                "Use WebRTC for media and a data channel for input. Keep signaling separate from the session scheduler.",
                "HTTP chunked video failed our interactive latency target."
            ),
            (
                "One player, one boundary",
                "Container workload isolation",
                "Give every session a dedicated unprivileged workload, read-only image and disposable writable volume.",
                "A container is not a sufficient boundary for hostile workloads; evaluate microVMs before production."
            ),
            (
                "Where did those 180 milliseconds go?",
                "Latency troubleshooting",
                "Instrument capture, encode, transport and decode separately. A three-frame queue dominated our measured delay.",
                "Lowering bitrate alone did not remove buffering. Cap the queue before tuning quality."
            ),
            (
                "Hardware encoding: the tradeoff",
                "GPU encoding options",
                "Compare hardware H.264 with software encoding on the same scene. Track encoder session capacity and p95 frame time.",
                "Shared GPU utilization can look healthy while an individual player misses deadlines."
            ),
            (
                "Designing the Azure control plane",
                "Azure architecture",
                "Use an API for sessions, a queue for placement and workers near players. Keep control-plane state independent of media traffic.",
                "Starting every session on a cold node makes startup unpredictable; a small warm pool costs more but improves experience."
            ),
            (
                "A platform developers can build on",
                "Developer platform considerations",
                "Expose session lifecycle events and a versioned SDK. Keep game images immutable and verify readiness before routing players.",
                "An unversioned callback payload broke our test client during a rollout."
            ),
            (
                "The prototype is playable",
                "Working prototype milestone",
                "A browser connects to an isolated worker, receives WebRTC video and sends controller input. Record traces for reproducibility.",
                "The demo proves one region and two concurrent players, not global scale."
            ),
            (
                "What we would do differently",
                "Results and lessons learned",
                "Keep the direct-browser and server-streaming paths as separate experiments. Publish the measurements alongside the decision.",
                "We chose server streaming for compatibility while retaining WebAssembly for lightweight titles."
            ),
        };
        var cs = new List<Conversation>();
        for (var i = 0; i < topics.Length; i++)
        {
            var t = topics[i];
            var provider = new[] { "ChatGPT", "Claude", "Cursor" }[i % 3];
            var c = new Conversation
            {
                Id = $"demo-c{i}",
                WorkspaceId = "cloudplay",
                SourceId = $"demo-source-{i}",
                SourceKey = $"demo-{i}",
                Title = t.Item1,
                Provider = provider,
                SourceFormat = "demonstration",
                Demo = true,
                Model = "Simulated session",
                SourceCreatedAt = new DateTime(2026, 9, 12, 0, 0, 0, DateTimeKind.Utc).AddDays(i),
                Metadata = "{\"demonstration\":true}",
            };
            var prompts = new[]
            {
                "We are building a browser-based cloud gaming platform. Let's investigate "
                    + t.Item2.ToLowerInvariant()
                    + ".",
                "What is the smallest experiment that can disprove this approach?",
                "What went wrong in our first attempt?",
                "How should we compare the alternatives?",
                "Record the decision and next steps.",
            };
            var answers = new[]
            {
                "## "
                    + t.Item2
                    + "\n\n"
                    + t.Item3
                    + "\n\nThis is simulated demonstration content, not an actual provider response.",
                "Build a repeatable test with a fixed scene and input trace. Capture median and p95 timings.\n\n```typescript\nconst started = performance.now();\nawait session.connect();\nmetrics.record('startup_ms', performance.now() - started);\n```",
                t.Item4
                    + "\n\nWe should retain the failed run in the journey because it explains the change in direction.",
                "| Approach | Benefit | Cost |\n| --- | --- | --- |\n| Direct browser | No streaming hop | Compatibility limits |\n| Server streaming | Existing native workloads | Network and GPU cost |\n\nCompare the same workload and region; these passages are linked by the curator, not by message order.",
                "**Decision:** "
                    + t.Item3
                    + "\n\nNext: document the assumption, link the measurement and revisit if p95 latency regresses. Do not treat this demonstration as a production benchmark.",
            };
            for (var n = 0; n < 10; n++)
            {
                var m = new ConversationMessage
                {
                    Id = $"demo-m{i}-{n}",
                    ConversationId = c.Id,
                    ProviderId = $"simulated-{n}",
                    Order = n,
                    Role = n % 2 == 0 ? "user" : "assistant",
                    ParentId = n > 0 ? $"demo-m{i}-{n - 1}" : null,
                    Timestamp = new DateTime(2026, 9, 12, 0, 0, 0, DateTimeKind.Utc).AddDays(i).AddMinutes(n),
                    Blocks =
                    [
                        new()
                        {
                            Id = $"demo-b{i}-{n}",
                            ConversationMessageId = $"demo-m{i}-{n}",
                            Text = n % 2 == 0 ? prompts[n / 2] : answers[n / 2],
                        },
                    ],
                };
                c.Messages.Add(m);
            }
            c.RawPayload = Json.Write(
                new
                {
                    demonstration = true,
                    c.Title,
                    messages = c.Messages.Select(m => new { m.Role, content = m.Blocks[0].Text }),
                }
            );
            db.Sources.Add(
                new()
                {
                    Id = c.SourceId,
                    WorkspaceId = "cloudplay",
                    Name = $"demo-{i}.json",
                    Bytes = System.Text.Encoding.UTF8.GetBytes(c.RawPayload),
                    Hash = Value.Hash(c.RawPayload),
                }
            );
            cs.Add(c);
        }
        db.Conversations.AddRange(cs);
        var positions = new[]
        {
            (0, 130),
            (310, 0),
            (310, 260),
            (640, 0),
            (640, 260),
            (970, 260),
            (970, 0),
            (1280, 0),
            (1590, 130),
            (1900, 130),
        };
        var nodes = cs.Select(
                (c, i) =>
                    new
                    {
                        id = "node-" + i,
                        type = "journey",
                        position = new { x = positions[i].Item1, y = positions[i].Item2 },
                        data = new
                        {
                            kind = "conversation",
                            title = c.Title,
                            conversationId = c.Id,
                            provider = c.Provider,
                            model = c.Model,
                            messageCount = 10,
                            phase = i < 3 ? "Discover"
                            : i < 7 ? "Experiment"
                            : "Build",
                        },
                    }
            )
            .Cast<object>()
            .ToList();
        nodes.Add(
            new
            {
                id = "decision",
                type = "journey",
                position = new { x = 640, y = 510 },
                data = new
                {
                    kind = "decision",
                    title = "Compatibility over pure browser execution",
                    body = "We chose server streaming; keep WASM as a targeted alternative.",
                },
            }
        );
        nodes.Add(
            new
            {
                id = "milestone",
                type = "journey",
                position = new { x = 1590, y = 380 },
                data = new
                {
                    kind = "milestone",
                    title = "First playable session",
                    body = "Two players, one region. Prototype validated.",
                },
            }
        );
        var connections = new[]
        {
            ("node-0", "node-1", "Alternative approach"),
            ("node-0", "node-2", "Continues from"),
            ("node-1", "node-3", "Depends on"),
            ("node-2", "node-4", "Failed because"),
            ("node-3", "node-6", "Continues from"),
            ("node-4", "node-5", "Validated by"),
            ("node-5", "node-8", "Continues from"),
            ("node-6", "node-7", "Continues from"),
            ("node-7", "node-8", "Validated by"),
            ("node-8", "node-9", "Continues from"),
            ("node-1", "decision", "Replaced by"),
            ("decision", "node-5", "Inspired by"),
            ("node-8", "milestone", "Validated by"),
        };
        var graph = Json.Write(
            new
            {
                nodes,
                edges = connections.Select(
                    (e, i) =>
                        new
                        {
                            id = "e" + i,
                            source = e.Item1,
                            target = e.Item2,
                            label = e.Item3,
                            type = "smoothstep",
                        }
                ),
                steps = Enumerable.Range(0, 10).Select(i => "node-" + i),
            }
        );
        var journey = new global::Journey.Domain.Journey
        {
            Id = "cloud-gaming",
            WorkspaceId = "cloudplay",
            OwnerId = "alex",
            Title = "Building a browser-based cloud gaming platform",
            Description =
                "From an ambitious question to the first playable session. A curated record of the research, detours, and decisions along the way.",
            Visibility = "Team",
            Graph = graph,
        };
        db.Journeys.Add(journey);
        db.Journeys.Add(
            new()
            {
                Id = "private-research",
                WorkspaceId = "cloudplay",
                OwnerId = "alex",
                Title = "The next experiment",
                Description = "Private working notes for the next iteration.",
                Visibility = "Private",
                Graph = graph,
            }
        );
        for (var i = 0; i < 8; i++)
        {
            var c = cs[i];
            var m = c.Messages[5];
            var b = m.Blocks[0];
            var quote = topics[i].Item4;
            var an = new Annotation
            {
                Id = "demo-a" + i,
                WorkspaceId = "cloudplay",
                ConversationId = c.Id,
                MessageId = m.Id,
                AuthorId = i % 2 == 0 ? "alex" : "sam",
                Anchor = Json.Write(Anchoring.Create(b.Id, b.Text, 0, quote.Length)),
                Resolved = i >= 5,
                Comments =
                [
                    new()
                    {
                        AuthorId = "alex",
                        Body =
                            "This is why we preserved the failed approach. @Sam Rivera please connect it to the follow-up experiment.",
                    },
                ],
            };
            if (i < 3)
                an.Comments.Add(
                    new()
                    {
                        AuthorId = "sam",
                        Body =
                            "Linked. The original source stays intact; this explanation belongs to our curation.",
                    }
                );
            db.Annotations.Add(an);
        }
        db.Comparisons.AddRange(
            new ComparisonSession
            {
                Id = "compare-architecture",
                WorkspaceId = "cloudplay",
                JourneyId = journey.Id,
                Title = "Browser versus server execution",
                LeftId = cs[1].Id,
                RightId = cs[2].Id,
                Links = Json.Write(
                    new[]
                    {
                        new
                        {
                            left = cs[1].Messages[7].Id,
                            right = cs[2].Messages[7].Id,
                            note = "Server streaming wins compatibility; direct browser remains useful for lighter workloads.",
                        },
                    }
                ),
            },
            new ComparisonSession
            {
                Id = "compare-latency",
                WorkspaceId = "cloudplay",
                JourneyId = journey.Id,
                Title = "Queue depth versus encoder throughput",
                LeftId = cs[4].Id,
                RightId = cs[5].Id,
            }
        );
        db.Publications.Add(
            new()
            {
                JourneyId = journey.Id,
                WorkspaceId = "cloudplay",
                Slug = "cloudplay-demo",
                Visibility = "Public",
                Snapshot = Json.Write(
                    Publishing.Snapshot(
                        journey,
                        cs,
                        new(
                            cs.SelectMany(c => c.Messages).Select(m => m.Id).ToArray(),
                            [],
                            "Public",
                            true
                        )
                    )
                ),
            }
        );
        db.Notifications.Add(
            new()
            {
                WorkspaceId = "cloudplay",
                UserId = "alex",
                Text = "Sam replied to the latency investigation",
                Link = "explorer/demo-c4?message=demo-m4-5&thread=demo-a4",
            }
        );
        db.Activities.Add(
            new()
            {
                WorkspaceId = "cloudplay",
                ActorId = "sam",
                Text = "Linked the latency experiment to the encoding decision",
            }
        );
        await db.SaveChangesAsync();
    }
}
