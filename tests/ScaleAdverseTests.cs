using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Journey.Data;
using Journey.Domain;
using Journey.Service.Features.Imports;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Journey.Tests;

public class ScaleAdverseTests : IClassFixture<CountingFactory>
{
    readonly CountingFactory factory;

    public ScaleAdverseTests(CountingFactory f) => factory = f;

    async Task<HttpClient> Login(string who)
    {
        var c = factory.CreateClient();
        c.DefaultRequestHeaders.Add("X-Journey", "1");
        (
            await c.PostAsJsonAsync(
                "/api/auth/login",
                new { email = who + "@journey.local", password = "JourneyDemo!2026" }
            )
        ).EnsureSuccessStatusCode();
        return c;
    }

    [Fact]
    public async Task RevokedMembershipLooksLikeAMissingConversation()
    {
        const string workspace = "revoke-scale";
        const string conversation = "revoke-conversation";
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDb>();
            if (!await db.Workspaces.AnyAsync(w => w.Id == workspace))
            {
                db.Workspaces.Add(new Workspace { Id = workspace, Name = "Revoke", OwnerId = "alex" });
                db.Memberships.Add(new WorkspaceMembership { WorkspaceId = workspace, UserId = "alex", Role = "Owner" });
                db.Memberships.Add(new WorkspaceMembership { WorkspaceId = workspace, UserId = "sam", Role = "Editor" });
                db.Conversations.Add(
                    new Conversation
                    {
                        Id = conversation,
                        WorkspaceId = workspace,
                        SourceKey = conversation,
                        Title = "Soon hidden",
                    }
                );
                await db.SaveChangesAsync();
            }
        }
        using var sam = await Login("sam");
        (await sam.GetAsync("/api/conversations/" + conversation)).EnsureSuccessStatusCode();
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDb>();
            await db.Memberships.Where(m => m.WorkspaceId == workspace && m.UserId == "sam").ExecuteDeleteAsync();
        }
        var hidden = await sam.GetAsync("/api/conversations/" + conversation);
        var missing = await sam.GetAsync("/api/conversations/never-existed");
        Assert.Equal(HttpStatusCode.NotFound, hidden.StatusCode);
        Assert.Equal(missing.StatusCode, hidden.StatusCode);
        Assert.Equal(await missing.Content.ReadAsStringAsync(), await hidden.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task ImportRestartUnionsLegacyJsonAndSkipsRecordedKeys()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDb>();
        var source = new ImportSource
        {
            WorkspaceId = "cloudplay",
            Name = "resume.json",
            ProcessedKeys = """["legacy-key"]""",
        };
        db.Sources.Add(source);
        await db.SaveChangesAsync();
        db.Add(new ImportProcessedKey { SourceId = source.Id, Key = "table-key" });
        await db.SaveChangesAsync();
        var keys = await ImportLease.ProcessedKeys(db, source, default);
        Assert.Contains("legacy-key", keys);
        Assert.Contains("table-key", keys);

        var batch = new ImportBatch { WorkspaceId = "cloudplay", Status = "queued" };
        db.Batches.Add(batch);
        await db.SaveChangesAsync();
        var now = DateTime.UtcNow;
        Assert.True(await ImportLease.TryClaim(db, batch.Id, "worker-crash", now, default));
        await db.Entry(batch).ReloadAsync();
        Assert.Equal("processing", batch.Status);
        await ImportLease.Release(db, batch.Id, "worker-crash");
        Assert.True(await ImportLease.TryClaim(db, batch.Id, "worker-resume", now, default));
        await Assert.ThrowsAsync<ImportLeaseLostException>(() =>
            ImportLease.Checkpoint(db, batch, "worker-crash", default)
        );
    }

    [Fact]
    public async Task DeletedDocumentAndPrivateJourneyAreIndistinguishableFromMissing()
    {
        using var alex = await Login("alex");
        var created = await alex.PostAsJsonAsync("/api/w/cloudplay/journeys", new { name = "Adverse delete" });
        created.EnsureSuccessStatusCode();
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetString();
        (await alex.DeleteAsync("/api/journeys/" + id)).EnsureSuccessStatusCode();
        var deleted = await alex.GetAsync("/api/journeys/" + id);
        var missing = await alex.GetAsync("/api/journeys/never-existed");
        Assert.Equal(HttpStatusCode.NotFound, deleted.StatusCode);
        Assert.Equal(await missing.Content.ReadAsStringAsync(), await deleted.Content.ReadAsStringAsync());

        using var riley = await Login("riley");
        var hidden = await riley.GetAsync("/api/journeys/private-research");
        Assert.Equal(HttpStatusCode.NotFound, hidden.StatusCode);
        Assert.Equal(await missing.Content.ReadAsStringAsync(), await hidden.Content.ReadAsStringAsync());
    }
}
