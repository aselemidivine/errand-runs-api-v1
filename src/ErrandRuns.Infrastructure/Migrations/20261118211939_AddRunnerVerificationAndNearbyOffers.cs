using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ErrandRuns.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddRunnerVerificationAndNearbyOffers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "RunnerVerifications",
                schema: "runners",
                columns: table => new
                {
                    RunnerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    GovernmentIdType = table.Column<int>(type: "int", nullable: true),
                    IdentityDocumentKey = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    VehicleType = table.Column<int>(type: "int", nullable: true),
                    VehiclePlateNumber = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    VehicleMakeModel = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: true),
                    OwnershipDocumentKey = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    SubmittedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ReviewedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    RejectionReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RunnerVerifications", x => x.RunnerId);
                    table.ForeignKey(
                        name: "FK_RunnerVerifications_RunnerProfiles_RunnerId",
                        column: x => x.RunnerId,
                        principalSchema: "runners",
                        principalTable: "RunnerProfiles",
                        principalColumn: "UserId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RunnerVerifications_Status_SubmittedAt",
                schema: "runners",
                table: "RunnerVerifications",
                columns: new[] { "Status", "SubmittedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RunnerVerifications",
                schema: "runners");
        }
    }
}
