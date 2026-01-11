using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HikingStore.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddShippingOptionToShoppingCart : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ShippingOption",
                table: "ShoppingCarts",
                type: "nvarchar(max)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ShippingOption",
                table: "ShoppingCarts");
        }
    }
}
