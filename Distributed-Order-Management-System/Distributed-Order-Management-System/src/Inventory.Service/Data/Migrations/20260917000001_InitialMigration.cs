using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Inventory.Service.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialMigration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Products",
                columns: table => new
                {
                    ProductId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    Description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    Price = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                    InitialStock = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamptz", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Products", x => x.ProductId);
                });

            migrationBuilder.CreateTable(
                name: "ReservationLedgers",
                columns: table => new
                {
                    LedgerId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductId = table.Column<Guid>(type: "uuid", nullable: false),
                    OrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    Quantity = table.Column<int>(type: "integer", nullable: false),
                    Type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    MessageId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamptz", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    ProcessedAt = table.Column<DateTime>(type: "timestamptz", nullable: true),
                    CorrelationId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReservationLedgers", x => x.LedgerId);
                    table.CheckConstraint("chk_ledger_quantity_positive", "[Quantity] > 0");
                    table.ForeignKey(
                        name: "FK_ReservationLedgers_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "ProductId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "idx_product_created_at",
                table: "Products",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "idx_product_name",
                table: "Products",
                column: "Name");

            migrationBuilder.CreateIndex(
                name: "idx_ledger_correlation_id",
                table: "ReservationLedgers",
                column: "CorrelationId");

            migrationBuilder.CreateIndex(
                name: "idx_ledger_created_at",
                table: "ReservationLedgers",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "idx_ledger_message_id_unique",
                table: "ReservationLedgers",
                column: "MessageId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "idx_ledger_order_id",
                table: "ReservationLedgers",
                column: "OrderId");

            migrationBuilder.CreateIndex(
                name: "idx_ledger_product_id",
                table: "ReservationLedgers",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "idx_ledger_product_type",
                table: "ReservationLedgers",
                columns: new[] { "ProductId", "Type" });

            migrationBuilder.CreateIndex(
                name: "idx_ledger_status",
                table: "ReservationLedgers",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "idx_ledger_type",
                table: "ReservationLedgers",
                column: "Type");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ReservationLedgers");

            migrationBuilder.DropTable(
                name: "Products");
        }
    }
}
