using Journey.Data;
using Journey.Domain;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Journey.Tests;

public class IdentityMigrationTests
{
    [Fact]
    public async Task UpgradePreservesLegacyUserPasswordAndWorkspaceReferences()
    {
        var path = Path.Combine(Path.GetTempPath(), "journey-migration-" + Guid.NewGuid() + ".db");
        await using var db = new AppDb(new DbContextOptionsBuilder<AppDb>().UseSqlite("Data Source=" + path).Options);
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync("20261009040127_PublicationDiscussions");
        var legacy = new User { Id = "legacy", Email = "legacy@example.test" };
        var hash = new PasswordHasher<User>().HashPassword(legacy, "LegacyPassword!2026");
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO Users (Id, CreatedAt, Name, Email, PasswordHash) VALUES ('legacy', {DateTime.UtcNow}, 'Legacy user', {legacy.Email}, {hash})");
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO Workspaces (Id, CreatedAt, Name, OwnerId, TeamId) VALUES ('legacy-workspace', {DateTime.UtcNow}, 'Saved work', 'legacy', NULL)");
        await migrator.MigrateAsync();
        var migrated = await db.Users.SingleAsync(u => u.Id == "legacy");
        Assert.Equal(hash, migrated.PasswordHash);
        Assert.NotEqual(PasswordVerificationResult.Failed, new PasswordHasher<User>().VerifyHashedPassword(migrated, migrated.PasswordHash!, "LegacyPassword!2026"));
        Assert.Equal("LEGACY@EXAMPLE.TEST", migrated.NormalizedEmail);
        Assert.False(migrated.EmailConfirmed);
        Assert.True(migrated.LockoutEnabled);
        Assert.False(string.IsNullOrEmpty(migrated.SecurityStamp));
        Assert.Equal("legacy", (await db.Workspaces.SingleAsync()).OwnerId);
    }
}
