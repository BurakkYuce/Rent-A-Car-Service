using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RentACar.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddExpenseVadeHesap : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "FinansalHesapId",
                table: "Expenses",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "Vade",
                table: "Expenses",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddUniqueConstraint(
                name: "AK_Hesaplar_TenantId_Id",
                table: "Hesaplar",
                columns: new[] { "TenantId", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_Expenses_TenantId_FinansalHesapId",
                table: "Expenses",
                columns: new[] { "TenantId", "FinansalHesapId" });

            migrationBuilder.AddForeignKey(
                name: "FK_Expenses_Hesaplar_TenantId_FinansalHesapId",
                table: "Expenses",
                columns: new[] { "TenantId", "FinansalHesapId" },
                principalTable: "Hesaplar",
                principalColumns: new[] { "TenantId", "Id" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Expenses_Hesaplar_TenantId_FinansalHesapId",
                table: "Expenses");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_Hesaplar_TenantId_Id",
                table: "Hesaplar");

            migrationBuilder.DropIndex(
                name: "IX_Expenses_TenantId_FinansalHesapId",
                table: "Expenses");

            migrationBuilder.DropColumn(
                name: "FinansalHesapId",
                table: "Expenses");

            migrationBuilder.DropColumn(
                name: "Vade",
                table: "Expenses");
        }
    }
}
