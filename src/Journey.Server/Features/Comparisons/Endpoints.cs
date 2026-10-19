using System.Text.Json;
using Journey.Domain;
using Journey.Data;
using Microsoft.EntityFrameworkCore;

namespace Journey.Server.Features.Comparisons;

public static class ComparisonsEndpoints
{
    public static void MapComparisons(this WebApplication app, RouteGroupBuilder api)
    {
        api.MapGet(
            "/w/{wid}/comparisons",
            async (string wid, string? cursor, int? limit, AppDb db, Access access) =>
            {
                await access.Require(wid);
                var uid = access.UserId;
                var take = Paging.Limit(limit);
                var rows = await db
                    .Comparisons.Where(x =>
                        x.WorkspaceId == wid
                        && (
                            x.JourneyId == null
                            || db.Journeys.Any(j =>
                                j.Id == x.JourneyId && (j.Visibility != "Private" || j.OwnerId == uid)
                            )
                        )
                    )
                    .Newest(cursor)
                    .Take(take + 1)
                    .Select(x => new ComparisonSummary(x.Id, x.Title, x.CreatedAt))
                    .ToListAsync();
                return Paging.Of(rows, take, r => (r.CreatedAt, r.Id));
            }
        );
        api.MapGet(
            "/comparisons/{id}",
            async (string id, AppDb db, Access access) =>
            {
                var saved =
                    await db.Comparisons.AsNoTracking().SingleOrDefaultAsync(c => c.Id == id)
                    ?? throw new KeyNotFoundException();
                await access.Require(saved.WorkspaceId);
                if (saved.JourneyId != null)
                    await access.Journey(saved.JourneyId);
                return saved;
            }
        );
        api.MapPost(
            "/w/{wid}/comparisons",
            async (string wid, ComparisonSession input, AppDb db, Access access) =>
            {
                await Validate(wid, input, db, access);
                input.Id = Guid.NewGuid().ToString("N");
                input.CreatedAt = DateTime.UtcNow;
                input.WorkspaceId = wid;
                db.Comparisons.Add(input);
                await db.SaveChangesAsync();
                return input;
            }
        );
        api.MapPut(
            "/comparisons/{id}",
            async (string id, ComparisonSession input, AppDb db, Access access) =>
            {
                var saved = await db.Comparisons.FindAsync(id) ?? throw new KeyNotFoundException();
                await access.Require(saved.WorkspaceId, "edit");
                if (saved.JourneyId != null)
                    await access.Journey(saved.JourneyId);
                await Validate(saved.WorkspaceId, input, db, access);
                saved.Title = input.Title;
                saved.Question = input.Question;
                saved.Outcome = input.Outcome;
                saved.Rationale = input.Rationale;
                saved.LeftId = input.LeftId;
                saved.RightId = input.RightId;
                saved.JourneyId = input.JourneyId;
                saved.Links = input.Links;
                saved.Ranges = input.Ranges;
                await db.SaveChangesAsync();
                return saved;
            }
        );
    }

    record ComparisonSummary(string Id, string Title, DateTime CreatedAt);

    static async Task Validate(string wid, ComparisonSession input, AppDb db, Access access)
    {
        await access.Require(wid, "edit");
        input.JourneyId = string.IsNullOrWhiteSpace(input.JourneyId) ? null : input.JourneyId;
        if (input.JourneyId != null && (await access.Journey(input.JourneyId)).WorkspaceId != wid)
            throw new InvalidDataException("Workspace mismatch");
        if (
            await access.RequireConversation(input.LeftId) != wid
            || await access.RequireConversation(input.RightId) != wid
        )
            throw new InvalidDataException("Workspace mismatch");
        if (input.LeftId == input.RightId)
            throw new InvalidDataException("Choose two different conversations.");
        if (string.IsNullOrWhiteSpace(input.Title))
            throw new InvalidDataException("Give the comparison a title.");
        if (input.Outcome is not ("Undecided" or "Left" or "Right" or "Combine" or "Neither"))
            throw new InvalidDataException("Invalid decision.");
        if (input.Outcome != "Undecided" && string.IsNullOrWhiteSpace(input.Rationale))
            throw new InvalidDataException("Explain the reason for this decision.");
        var ranges = Json.Read<int[][]>(input.Ranges);
        if (ranges.Length != 2 || ranges.Any(r => r.Length != 2 || r[0] < 1 || r[1] < r[0]))
            throw new InvalidDataException("Invalid message ranges.");
        var links = JsonDocument.Parse(input.Links).RootElement;
        if (links.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("Evidence must be a list.");
        var lefts = links.Items().Select(l => l.Get("left").Str()).ToList();
        var rights = links.Items().Select(l => l.Get("right").Str()).ToList();
        if (
            !await AllBelong(db, input.LeftId, lefts)
            || !await AllBelong(db, input.RightId, rights)
        )
            throw new InvalidDataException(
                "Linked messages must belong to the selected conversations."
            );
    }

    static async Task<bool> AllBelong(AppDb db, string conversationId, List<string?> messageIds)
    {
        if (messageIds.Count == 0)
            return true;
        if (messageIds.Any(id => id == null))
            return false;
        var ids = messageIds.Distinct().ToArray();
        var found = await db
            .Messages.Where(m => m.ConversationId == conversationId && ids.Contains(m.Id))
            .CountAsync();
        return found == ids.Length;
    }
}
