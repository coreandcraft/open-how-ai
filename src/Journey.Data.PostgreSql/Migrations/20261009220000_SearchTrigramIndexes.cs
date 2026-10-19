using Journey.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Journey.Data.PostgreSql.Migrations
{
    /// <summary>
    /// Trigram indexes for the library's substring search. Raw SQL because the model is shared with SQLite.
    /// Azure Database for PostgreSQL must allow-list the extension (server parameter azure.extensions = PG_TRGM).
    /// On a large existing table, create the indexes with CREATE INDEX CONCURRENTLY before deploying;
    /// IF NOT EXISTS then makes this migration a no-op.
    /// </summary>
    [DbContext(typeof(AppDb))]
    [Migration("20261009220000_SearchTrigramIndexes")]
    public partial class SearchTrigramIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("CREATE EXTENSION IF NOT EXISTS pg_trgm;");
            migrationBuilder.Sql(
                """CREATE INDEX IF NOT EXISTS "IX_MessageContentBlock_Text_trgm" ON "MessageContentBlock" USING gin ("Text" gin_trgm_ops);"""
            );
            migrationBuilder.Sql(
                """CREATE INDEX IF NOT EXISTS "IX_Comments_Body_trgm" ON "Comments" USING gin ("Body" gin_trgm_ops);"""
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""DROP INDEX IF EXISTS "IX_Comments_Body_trgm";""");
            migrationBuilder.Sql("""DROP INDEX IF EXISTS "IX_MessageContentBlock_Text_trgm";""");
        }
    }
}
