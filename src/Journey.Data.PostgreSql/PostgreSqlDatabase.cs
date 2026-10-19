using Microsoft.EntityFrameworkCore;

namespace Journey.Data.PostgreSql;

public static class PostgreSqlDatabase
{
    public static DbContextOptionsBuilder UseJourneyPostgreSql(this DbContextOptionsBuilder options, string connectionString)
        => options.UseNpgsql(connectionString, postgres =>
            postgres.MigrationsAssembly(typeof(PostgreSqlDatabase).Assembly.FullName));
}
