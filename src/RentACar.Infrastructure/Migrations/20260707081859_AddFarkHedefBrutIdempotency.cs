using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RentACar.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddFarkHedefBrutIdempotency : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Invoices_TenantId_KaynakKiraId",
                table: "Invoices");

            migrationBuilder.AddColumn<decimal>(
                name: "KaynakKiraHedefBrut",
                table: "Invoices",
                type: "numeric(19,4)",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Invoices_TenantId_KaynakKiraId_KaynakKiraHedefBrut",
                table: "Invoices",
                columns: new[] { "TenantId", "KaynakKiraId", "KaynakKiraHedefBrut" },
                unique: true,
                filter: "\"KaynakKiraId\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Invoices_TenantId_KaynakKiraId_KaynakKiraHedefBrut",
                table: "Invoices");

            migrationBuilder.DropColumn(
                name: "KaynakKiraHedefBrut",
                table: "Invoices");

            migrationBuilder.CreateIndex(
                name: "IX_Invoices_TenantId_KaynakKiraId",
                table: "Invoices",
                columns: new[] { "TenantId", "KaynakKiraId" },
                filter: "\"KaynakKiraId\" IS NOT NULL");
        }
    }
}
