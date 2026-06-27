using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RentACar.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddHgsLedgerIdempotency : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_AccountLedgerEntries_TenantId_SourceType_SourceId_Direction",
                table: "AccountLedgerEntries",
                columns: new[] { "TenantId", "SourceType", "SourceId", "Direction" },
                unique: true,
                filter: "\"SourceType\" = 'Hgs'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AccountLedgerEntries_TenantId_SourceType_SourceId_Direction",
                table: "AccountLedgerEntries");
        }
    }
}
