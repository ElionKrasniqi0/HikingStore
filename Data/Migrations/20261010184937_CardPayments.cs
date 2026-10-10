using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HikingStore.Data.Migrations
{
    /// <inheritdoc />
    public partial class CardPayments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "PaidAmount",
                table: "Orders",
                type: "decimal(18,2)",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "PaidAt",
                table: "Orders",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PaymentAttempts",
                table: "Orders",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "PaymentExpiresAt",
                table: "Orders",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PaymentFailureReason",
                table: "Orders",
                type: "nvarchar(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PaymentProvider",
                table: "Orders",
                type: "nvarchar(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PaymentSessionId",
                table: "Orders",
                type: "nvarchar(255)",
                maxLength: 255,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PaymentTransactionId",
                table: "Orders",
                type: "nvarchar(255)",
                maxLength: 255,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "PaymentEvents",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Provider = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    EventId = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    EventType = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    OrderId = table.Column<int>(type: "int", nullable: true),
                    ReceivedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Result = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PaymentEvents", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Orders_PaymentSessionId",
                table: "Orders",
                column: "PaymentSessionId",
                filter: "[PaymentSessionId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Orders_PaymentStatus_PaymentExpiresAt",
                table: "Orders",
                columns: new[] { "PaymentStatus", "PaymentExpiresAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Orders_PaymentTransactionId",
                table: "Orders",
                column: "PaymentTransactionId",
                filter: "[PaymentTransactionId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentEvents_OrderId",
                table: "PaymentEvents",
                column: "OrderId");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentEvents_Provider_EventId",
                table: "PaymentEvents",
                columns: new[] { "Provider", "EventId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PaymentEvents");

            migrationBuilder.DropIndex(
                name: "IX_Orders_PaymentSessionId",
                table: "Orders");

            migrationBuilder.DropIndex(
                name: "IX_Orders_PaymentStatus_PaymentExpiresAt",
                table: "Orders");

            migrationBuilder.DropIndex(
                name: "IX_Orders_PaymentTransactionId",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "PaidAmount",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "PaidAt",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "PaymentAttempts",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "PaymentExpiresAt",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "PaymentFailureReason",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "PaymentProvider",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "PaymentSessionId",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "PaymentTransactionId",
                table: "Orders");
        }
    }
}
