using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RentACar.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddMuayeneOdeme : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "Ceza",
                table: "InspectionRecords",
                type: "numeric(19,4)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<bool>(
                name: "Odendi",
                table: "InspectionRecords",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateIndex(
                name: "IX_AccountLedgerEntries_MuayeneOdeme_Idem",
                table: "AccountLedgerEntries",
                columns: new[] { "TenantId", "SourceType", "SourceId", "Direction" },
                unique: true,
                filter: "\"SourceType\" = 'MuayeneOdeme'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AccountLedgerEntries_MuayeneOdeme_Idem",
                table: "AccountLedgerEntries");

            migrationBuilder.DropColumn(
                name: "Ceza",
                table: "InspectionRecords");

            migrationBuilder.DropColumn(
                name: "Odendi",
                table: "InspectionRecords");
        }
    }
}
