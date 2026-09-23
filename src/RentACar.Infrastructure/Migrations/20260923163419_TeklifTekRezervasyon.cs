using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RentACar.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class TeklifTekRezervasyon : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "KaynakTeklifId",
                table: "Reservations",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "UX_Reservations_TenantId_KaynakTeklifId",
                table: "Reservations",
                columns: new[] { "TenantId", "KaynakTeklifId" },
                unique: true,
                filter: "\"KaynakTeklifId\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "UX_Reservations_TenantId_KaynakTeklifId",
                table: "Reservations");

            migrationBuilder.DropColumn(
                name: "KaynakTeklifId",
                table: "Reservations");
        }
    }
}
