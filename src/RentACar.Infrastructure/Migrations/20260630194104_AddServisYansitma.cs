using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RentACar.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddServisYansitma : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "YansitilanCariId",
                table: "ServiceRecords",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "YansitilanTutar",
                table: "ServiceRecords",
                type: "numeric(19,4)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<bool>(
                name: "Yansitildi",
                table: "ServiceRecords",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateIndex(
                name: "IX_AccountLedgerEntries_ServisYansitma_Idem",
                table: "AccountLedgerEntries",
                columns: new[] { "TenantId", "SourceType", "SourceId", "Direction" },
                unique: true,
                filter: "\"SourceType\" = 'ServisYansitma'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AccountLedgerEntries_ServisYansitma_Idem",
                table: "AccountLedgerEntries");

            migrationBuilder.DropColumn(
                name: "YansitilanCariId",
                table: "ServiceRecords");

            migrationBuilder.DropColumn(
                name: "YansitilanTutar",
                table: "ServiceRecords");

            migrationBuilder.DropColumn(
                name: "Yansitildi",
                table: "ServiceRecords");
        }
    }
}
