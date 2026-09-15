using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ErrandRuns.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddProfilePictures : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ProfilePictureContentType",
                schema: "identity",
                table: "Users",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<byte[]>(
                name: "ProfilePictureData",
                schema: "identity",
                table: "Users",
                type: "varbinary(max)",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ProfilePictureUpdatedAt",
                schema: "identity",
                table: "Users",
                type: "datetimeoffset",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ProfilePictureContentType",
                schema: "identity",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "ProfilePictureData",
                schema: "identity",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "ProfilePictureUpdatedAt",
                schema: "identity",
                table: "Users");
        }
    }
}
