using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ErrandRuns.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddGoogleLocationMetadata : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AddressComponentsJson",
                schema: "identity",
                table: "SavedLocations",
                type: "nvarchar(max)",
                maxLength: 16000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "GooglePlaceId",
                schema: "identity",
                table: "SavedLocations",
                type: "nvarchar(255)",
                maxLength: 255,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AddressComponentsJson",
                schema: "identity",
                table: "SavedLocations");

            migrationBuilder.DropColumn(
                name: "GooglePlaceId",
                schema: "identity",
                table: "SavedLocations");
        }
    }
}
