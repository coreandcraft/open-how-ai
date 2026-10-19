using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Journey.Data.Migrations
{
    /// <inheritdoc />
    public partial class StandaloneSharingAndDecisions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "JourneyId",
                table: "Publications",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "TEXT"
            );

            migrationBuilder.AddColumn<string>(
                name: "ConversationId",
                table: "Publications",
                type: "TEXT",
                nullable: true
            );

            migrationBuilder.AlterColumn<string>(
                name: "JourneyId",
                table: "Comparisons",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "TEXT"
            );

            migrationBuilder.AddColumn<string>(
                name: "Outcome",
                table: "Comparisons",
                type: "TEXT",
                nullable: false,
                defaultValue: ""
            );

            migrationBuilder.AddColumn<string>(
                name: "Question",
                table: "Comparisons",
                type: "TEXT",
                nullable: false,
                defaultValue: ""
            );

            migrationBuilder.AddColumn<string>(
                name: "Ranges",
                table: "Comparisons",
                type: "TEXT",
                nullable: false,
                defaultValue: ""
            );

            migrationBuilder.AddColumn<string>(
                name: "Rationale",
                table: "Comparisons",
                type: "TEXT",
                nullable: false,
                defaultValue: ""
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "ConversationId", table: "Publications");

            migrationBuilder.DropColumn(name: "Outcome", table: "Comparisons");

            migrationBuilder.DropColumn(name: "Question", table: "Comparisons");

            migrationBuilder.DropColumn(name: "Ranges", table: "Comparisons");

            migrationBuilder.DropColumn(name: "Rationale", table: "Comparisons");

            migrationBuilder.AlterColumn<string>(
                name: "JourneyId",
                table: "Publications",
                type: "TEXT",
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "TEXT",
                oldNullable: true
            );

            migrationBuilder.AlterColumn<string>(
                name: "JourneyId",
                table: "Comparisons",
                type: "TEXT",
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "TEXT",
                oldNullable: true
            );
        }
    }
}
