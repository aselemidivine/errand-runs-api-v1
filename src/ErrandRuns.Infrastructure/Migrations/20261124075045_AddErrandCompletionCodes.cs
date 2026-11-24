using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ErrandRuns.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddErrandCompletionCodes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "CompletionCodeFailedAttempts",
                schema: "app",
                table: "Errands",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "CompletionCodeHash",
                schema: "app",
                table: "Errands",
                type: "nvarchar(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "CompletionCodeIssuedAt",
                schema: "app",
                table: "Errands",
                type: "datetimeoffset",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CompletionCodeFailedAttempts",
                schema: "app",
                table: "Errands");

            migrationBuilder.DropColumn(
                name: "CompletionCodeHash",
                schema: "app",
                table: "Errands");

            migrationBuilder.DropColumn(
                name: "CompletionCodeIssuedAt",
                schema: "app",
                table: "Errands");
        }
    }
}
