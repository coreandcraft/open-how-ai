using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Journey.Data.Migrations
{
    /// <inheritdoc />
    public partial class ImportSourceProtection : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "Encrypted",
                table: "Sources",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "PurgedAt",
                table: "Sources",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Encrypted",
                table: "Sources");

            migrationBuilder.DropColumn(
                name: "PurgedAt",
                table: "Sources");
        }
    }
}
