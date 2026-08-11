using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RentACar.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddCashTransactionKanal : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Kanal",
                table: "CashTransactions",
                type: "character varying(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_CashTransactions_TenantId_Kanal",
                table: "CashTransactions",
                columns: new[] { "TenantId", "Kanal" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_CashTransactions_TenantId_Kanal",
                table: "CashTransactions");

            migrationBuilder.DropColumn(
                name: "Kanal",
                table: "CashTransactions");
        }
    }
}
