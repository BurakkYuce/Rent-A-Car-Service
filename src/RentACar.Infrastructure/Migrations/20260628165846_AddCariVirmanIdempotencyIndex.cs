using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RentACar.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddCariVirmanIdempotencyIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_AccountLedgerEntries_CariVirman_Idem",
                table: "AccountLedgerEntries",
                columns: new[] { "TenantId", "SourceType", "SourceId", "AccountRef" },
                unique: true,
                filter: "\"SourceType\" = 'CariVirman'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AccountLedgerEntries_CariVirman_Idem",
                table: "AccountLedgerEntries");
        }
    }
}
