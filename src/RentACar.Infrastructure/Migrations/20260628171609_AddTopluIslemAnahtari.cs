using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RentACar.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddTopluIslemAnahtari : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "IslemAnahtari",
                table: "Expenses",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "IslemAnahtari",
                table: "CashTransactions",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Expenses_TenantId_IslemAnahtari",
                table: "Expenses",
                columns: new[] { "TenantId", "IslemAnahtari" },
                unique: true,
                filter: "\"IslemAnahtari\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_CashTransactions_TenantId_IslemAnahtari",
                table: "CashTransactions",
                columns: new[] { "TenantId", "IslemAnahtari" },
                unique: true,
                filter: "\"IslemAnahtari\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Expenses_TenantId_IslemAnahtari",
                table: "Expenses");

            migrationBuilder.DropIndex(
                name: "IX_CashTransactions_TenantId_IslemAnahtari",
                table: "CashTransactions");

            migrationBuilder.DropColumn(
                name: "IslemAnahtari",
                table: "Expenses");

            migrationBuilder.DropColumn(
                name: "IslemAnahtari",
                table: "CashTransactions");
        }
    }
}
