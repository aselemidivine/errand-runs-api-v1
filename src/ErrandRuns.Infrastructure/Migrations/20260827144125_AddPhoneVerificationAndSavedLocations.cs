using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ErrandRuns.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPhoneVerificationAndSavedLocations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PhoneVerificationChallenges",
                schema: "identity",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PhoneNumber = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    SentAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    FailedAttempts = table.Column<int>(type: "int", nullable: false),
                    ConsumedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PhoneVerificationChallenges", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PhoneVerificationChallenges_Users_UserId",
                        column: x => x.UserId,
                        principalSchema: "identity",
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SavedLocations",
                schema: "identity",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Label = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    Address = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    Latitude = table.Column<decimal>(type: "decimal(9,6)", precision: 9, scale: 6, nullable: false),
                    Longitude = table.Column<decimal>(type: "decimal(9,6)", precision: 9, scale: 6, nullable: false),
                    Landmark = table.Column<string>(type: "nvarchar(240)", maxLength: 240, nullable: true),
                    DeliveryInstructions = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    IsFavorite = table.Column<bool>(type: "bit", nullable: false),
                    IsDefault = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SavedLocations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SavedLocations_Users_UserId",
                        column: x => x.UserId,
                        principalSchema: "identity",
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SavedLocationCategories",
                schema: "identity",
                columns: table => new
                {
                    SavedLocationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Category = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SavedLocationCategories", x => new { x.SavedLocationId, x.Category });
                    table.ForeignKey(
                        name: "FK_SavedLocationCategories_SavedLocations_SavedLocationId",
                        column: x => x.SavedLocationId,
                        principalSchema: "identity",
                        principalTable: "SavedLocations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PhoneVerificationChallenges_UserId_SentAt",
                schema: "identity",
                table: "PhoneVerificationChallenges",
                columns: new[] { "UserId", "SentAt" });

            migrationBuilder.CreateIndex(
                name: "IX_SavedLocations_UserId_IsDefault",
                schema: "identity",
                table: "SavedLocations",
                columns: new[] { "UserId", "IsDefault" },
                unique: true,
                filter: "[IsDefault] = 1");

            migrationBuilder.CreateIndex(
                name: "IX_SavedLocations_UserId_UpdatedAt",
                schema: "identity",
                table: "SavedLocations",
                columns: new[] { "UserId", "UpdatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PhoneVerificationChallenges",
                schema: "identity");

            migrationBuilder.DropTable(
                name: "SavedLocationCategories",
                schema: "identity");

            migrationBuilder.DropTable(
                name: "SavedLocations",
                schema: "identity");
        }
    }
}
