using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RentACar.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddInvoiceIade : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "KaynakFaturaId",
                table: "Invoices",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Invoices_TenantId_KaynakFaturaId",
                table: "Invoices",
                columns: new[] { "TenantId", "KaynakFaturaId" },
                unique: true,
                filter: "\"KaynakFaturaId\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Invoices_TenantId_KaynakFaturaId",
                table: "Invoices");

            migrationBuilder.DropColumn(
                name: "KaynakFaturaId",
                table: "Invoices");
        }
    }
}
