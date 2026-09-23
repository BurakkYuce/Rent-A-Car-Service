using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RentACar.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddRentalAddOnIslemAnahtari : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "IslemAnahtari",
                table: "RentalAddOns",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_RentalAddOns_TenantId_IslemAnahtari",
                table: "RentalAddOns",
                columns: new[] { "TenantId", "IslemAnahtari" },
                unique: true,
                filter: "\"IslemAnahtari\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_RentalAddOns_TenantId_IslemAnahtari",
                table: "RentalAddOns");

            migrationBuilder.DropColumn(
                name: "IslemAnahtari",
                table: "RentalAddOns");
        }
    }
}
