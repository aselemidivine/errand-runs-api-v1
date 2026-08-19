using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ErrandRuns.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class SupportPayoutReversals : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_RunnerLedger_PayoutId",
                schema: "payments",
                table: "RunnerLedger");

            migrationBuilder.CreateIndex(
                name: "IX_RunnerLedger_PayoutId_Type",
                schema: "payments",
                table: "RunnerLedger",
                columns: new[] { "PayoutId", "Type" },
                unique: true,
                filter: "[PayoutId] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_RunnerLedger_PayoutId_Type",
                schema: "payments",
                table: "RunnerLedger");

            migrationBuilder.CreateIndex(
                name: "IX_RunnerLedger_PayoutId",
                schema: "payments",
                table: "RunnerLedger",
                column: "PayoutId",
                unique: true,
                filter: "[PayoutId] IS NOT NULL");
        }
    }
}
