using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RentACar.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddVarsayilanGrup : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "VarsayilanGrupId",
                table: "Ayarlar",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Ayarlar_VarsayilanGrupId",
                table: "Ayarlar",
                column: "VarsayilanGrupId");

            migrationBuilder.AddForeignKey(
                name: "FK_Ayarlar_AracGruplari_VarsayilanGrupId",
                table: "Ayarlar",
                column: "VarsayilanGrupId",
                principalTable: "AracGruplari",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Ayarlar_AracGruplari_VarsayilanGrupId",
                table: "Ayarlar");

            migrationBuilder.DropIndex(
                name: "IX_Ayarlar_VarsayilanGrupId",
                table: "Ayarlar");

            migrationBuilder.DropColumn(
                name: "VarsayilanGrupId",
                table: "Ayarlar");
        }
    }
}
