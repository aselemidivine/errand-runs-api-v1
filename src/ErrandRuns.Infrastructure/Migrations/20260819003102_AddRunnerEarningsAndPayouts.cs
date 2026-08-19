using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ErrandRuns.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddRunnerEarningsAndPayouts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "RunnerLedger",
                schema: "payments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RunnerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ErrandId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    PayoutId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Type = table.Column<int>(type: "int", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Currency = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(240)", maxLength: 240, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RunnerLedger", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RunnerPayoutAccounts",
                schema: "payments",
                columns: table => new
                {
                    RunnerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BankCode = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    BankName = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    AccountName = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                    AccountNumberLast4 = table.Column<string>(type: "nvarchar(4)", maxLength: 4, nullable: false),
                    RecipientCode = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    InstantPayout = table.Column<bool>(type: "bit", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RunnerPayoutAccounts", x => x.RunnerId);
                });

            migrationBuilder.CreateTable(
                name: "RunnerPayouts",
                schema: "payments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RunnerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Currency = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    IdempotencyKey = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    ProviderReference = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                    FailureReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    RequestedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ProcessedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RunnerPayouts", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RunnerLedger_ErrandId",
                schema: "payments",
                table: "RunnerLedger",
                column: "ErrandId",
                unique: true,
                filter: "[ErrandId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_RunnerLedger_PayoutId",
                schema: "payments",
                table: "RunnerLedger",
                column: "PayoutId",
                unique: true,
                filter: "[PayoutId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_RunnerLedger_RunnerId",
                schema: "payments",
                table: "RunnerLedger",
                column: "RunnerId");

            migrationBuilder.CreateIndex(
                name: "IX_RunnerPayouts_ProviderReference",
                schema: "payments",
                table: "RunnerPayouts",
                column: "ProviderReference",
                unique: true,
                filter: "[ProviderReference] <> ''");

            migrationBuilder.CreateIndex(
                name: "IX_RunnerPayouts_RunnerId_IdempotencyKey",
                schema: "payments",
                table: "RunnerPayouts",
                columns: new[] { "RunnerId", "IdempotencyKey" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RunnerLedger",
                schema: "payments");

            migrationBuilder.DropTable(
                name: "RunnerPayoutAccounts",
                schema: "payments");

            migrationBuilder.DropTable(
                name: "RunnerPayouts",
                schema: "payments");
        }
    }
}
