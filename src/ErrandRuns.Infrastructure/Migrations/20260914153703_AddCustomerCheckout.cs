using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ErrandRuns.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddCustomerCheckout : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "ProviderReference",
                schema: "payments",
                table: "Payments",
                type: "nvarchar(160)",
                maxLength: 160,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(450)");

            migrationBuilder.AlterColumn<string>(
                name: "IdempotencyKey",
                schema: "payments",
                table: "Payments",
                type: "nvarchar(120)",
                maxLength: 120,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(450)");

            migrationBuilder.AddColumn<string>(
                name: "AccessCode",
                schema: "payments",
                table: "Payments",
                type: "nvarchar(160)",
                maxLength: 160,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CheckoutUrl",
                schema: "payments",
                table: "Payments",
                type: "nvarchar(2048)",
                maxLength: 2048,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "CreatedAt",
                schema: "payments",
                table: "Payments",
                type: "datetimeoffset",
                nullable: false,
                defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)));

            migrationBuilder.AddColumn<string>(
                name: "PaymentMethod",
                schema: "payments",
                table: "Payments",
                type: "nvarchar(40)",
                maxLength: 40,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Provider",
                schema: "payments",
                table: "Payments",
                type: "nvarchar(40)",
                maxLength: 40,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "UpdatedAt",
                schema: "payments",
                table: "Payments",
                type: "datetimeoffset",
                nullable: false,
                defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)));

            migrationBuilder.CreateIndex(
                name: "IX_Payments_ErrandId",
                schema: "payments",
                table: "Payments",
                column: "ErrandId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Payments_ErrandId",
                schema: "payments",
                table: "Payments");

            migrationBuilder.DropColumn(
                name: "AccessCode",
                schema: "payments",
                table: "Payments");

            migrationBuilder.DropColumn(
                name: "CheckoutUrl",
                schema: "payments",
                table: "Payments");

            migrationBuilder.DropColumn(
                name: "CreatedAt",
                schema: "payments",
                table: "Payments");

            migrationBuilder.DropColumn(
                name: "PaymentMethod",
                schema: "payments",
                table: "Payments");

            migrationBuilder.DropColumn(
                name: "Provider",
                schema: "payments",
                table: "Payments");

            migrationBuilder.DropColumn(
                name: "UpdatedAt",
                schema: "payments",
                table: "Payments");

            migrationBuilder.AlterColumn<string>(
                name: "ProviderReference",
                schema: "payments",
                table: "Payments",
                type: "nvarchar(450)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(160)",
                oldMaxLength: 160);

            migrationBuilder.AlterColumn<string>(
                name: "IdempotencyKey",
                schema: "payments",
                table: "Payments",
                type: "nvarchar(450)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(120)",
                oldMaxLength: 120);
        }
    }
}
