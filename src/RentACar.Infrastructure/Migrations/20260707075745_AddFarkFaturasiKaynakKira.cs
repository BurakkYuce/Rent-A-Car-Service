using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RentACar.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddFarkFaturasiKaynakKira : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "KaynakKiraId",
                table: "Invoices",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Invoices_TenantId_KaynakKiraId",
                table: "Invoices",
                columns: new[] { "TenantId", "KaynakKiraId" },
                filter: "\"KaynakKiraId\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Invoices_TenantId_KaynakKiraId",
                table: "Invoices");

            migrationBuilder.DropColumn(
                name: "KaynakKiraId",
                table: "Invoices");
        }
    }
}
