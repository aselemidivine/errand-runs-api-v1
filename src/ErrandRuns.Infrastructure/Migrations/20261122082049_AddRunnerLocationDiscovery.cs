using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ErrandRuns.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddRunnerLocationDiscovery : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "CurrentLatitude",
                schema: "runners",
                table: "RunnerProfiles",
                type: "decimal(9,6)",
                precision: 9,
                scale: 6,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "CurrentLongitude",
                schema: "runners",
                table: "RunnerProfiles",
                type: "decimal(9,6)",
                precision: 9,
                scale: 6,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "LocationUpdatedAt",
                schema: "runners",
                table: "RunnerProfiles",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "RunnerDeclinedOffers",
                schema: "runners",
                columns: table => new
                {
                    RunnerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ErrandId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DeclinedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RunnerDeclinedOffers", x => new { x.RunnerId, x.ErrandId });
                    table.ForeignKey(
                        name: "FK_RunnerDeclinedOffers_Errands_ErrandId",
                        column: x => x.ErrandId,
                        principalSchema: "app",
                        principalTable: "Errands",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_RunnerDeclinedOffers_RunnerProfiles_RunnerId",
                        column: x => x.RunnerId,
                        principalSchema: "runners",
                        principalTable: "RunnerProfiles",
                        principalColumn: "UserId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RunnerProfiles_Status_LocationUpdatedAt_CurrentLatitude_CurrentLongitude",
                schema: "runners",
                table: "RunnerProfiles",
                columns: new[] { "Status", "LocationUpdatedAt", "CurrentLatitude", "CurrentLongitude" });

            migrationBuilder.CreateIndex(
                name: "IX_RunnerDeclinedOffers_ErrandId",
                schema: "runners",
                table: "RunnerDeclinedOffers",
                column: "ErrandId");

            migrationBuilder.CreateIndex(
                name: "IX_RunnerDeclinedOffers_RunnerId_DeclinedAt",
                schema: "runners",
                table: "RunnerDeclinedOffers",
                columns: new[] { "RunnerId", "DeclinedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RunnerDeclinedOffers",
                schema: "runners");

            migrationBuilder.DropIndex(
                name: "IX_RunnerProfiles_Status_LocationUpdatedAt_CurrentLatitude_CurrentLongitude",
                schema: "runners",
                table: "RunnerProfiles");

            migrationBuilder.DropColumn(
                name: "CurrentLatitude",
                schema: "runners",
                table: "RunnerProfiles");

            migrationBuilder.DropColumn(
                name: "CurrentLongitude",
                schema: "runners",
                table: "RunnerProfiles");

            migrationBuilder.DropColumn(
                name: "LocationUpdatedAt",
                schema: "runners",
                table: "RunnerProfiles");
        }
    }
}
