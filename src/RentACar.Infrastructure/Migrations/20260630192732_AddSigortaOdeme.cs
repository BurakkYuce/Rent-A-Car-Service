using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RentACar.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddSigortaOdeme : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "Odendi",
                table: "InsurancePolicies",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<decimal>(
                name: "ZeyilPrim",
                table: "InsurancePolicies",
                type: "numeric(19,4)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.CreateIndex(
                name: "IX_AccountLedgerEntries_SigortaOdeme_Idem",
                table: "AccountLedgerEntries",
                columns: new[] { "TenantId", "SourceType", "SourceId", "Direction" },
                unique: true,
                filter: "\"SourceType\" = 'SigortaOdeme'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AccountLedgerEntries_SigortaOdeme_Idem",
                table: "AccountLedgerEntries");

            migrationBuilder.DropColumn(
                name: "Odendi",
                table: "InsurancePolicies");

            migrationBuilder.DropColumn(
                name: "ZeyilPrim",
                table: "InsurancePolicies");
        }
    }
}
