using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Journey.Data.PostgreSql.Migrations
{
    /// <inheritdoc />
    public partial class ImportSanitization : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "SanitizationRules",
                table: "Users",
                type: "text",
                nullable: false,
                defaultValue: "[]");

            migrationBuilder.AddColumn<string>(
                name: "Sanitization",
                table: "Batches",
                type: "text",
                nullable: false,
                defaultValue: "{}");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SanitizationRules",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "Sanitization",
                table: "Batches");
        }
    }
}
