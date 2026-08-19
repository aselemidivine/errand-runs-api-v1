using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ErrandRuns.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ExpandCustomerErrands : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Currency",
                schema: "app",
                table: "Errands",
                type: "nvarchar(3)",
                maxLength: 3,
                nullable: false,
                defaultValue: "NGN");

            migrationBuilder.AddColumn<decimal>(
                name: "MerchandiseEstimate",
                schema: "app",
                table: "Errands",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "PreferredProvider",
                schema: "app",
                table: "Errands",
                type: "nvarchar(160)",
                maxLength: 160,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "ServiceFee",
                schema: "app",
                table: "Errands",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "SpecialInstructions",
                schema: "app",
                table: "Errands",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ErrandItems",
                schema: "app",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                    Quantity = table.Column<int>(type: "int", nullable: false),
                    Unit = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    EstimatedUnitPrice = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    ErrandId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ErrandItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ErrandItems_Errands_ErrandId",
                        column: x => x.ErrandId,
                        principalSchema: "app",
                        principalTable: "Errands",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ErrandItems_ErrandId",
                schema: "app",
                table: "ErrandItems",
                column: "ErrandId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ErrandItems",
                schema: "app");

            migrationBuilder.DropColumn(
                name: "Currency",
                schema: "app",
                table: "Errands");

            migrationBuilder.DropColumn(
                name: "MerchandiseEstimate",
                schema: "app",
                table: "Errands");

            migrationBuilder.DropColumn(
                name: "PreferredProvider",
                schema: "app",
                table: "Errands");

            migrationBuilder.DropColumn(
                name: "ServiceFee",
                schema: "app",
                table: "Errands");

            migrationBuilder.DropColumn(
                name: "SpecialInstructions",
                schema: "app",
                table: "Errands");
        }
    }
}
