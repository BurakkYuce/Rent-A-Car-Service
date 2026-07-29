using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RentACar.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddIlanSlugVeTalepIlan : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Slug",
                table: "WebIlanlar",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<bool>(
                name: "GosterilenKdvDahil",
                table: "SiteTalepleri",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "IlanBaslik",
                table: "SiteTalepleri",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "IlanId",
                table: "SiteTalepleri",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_WebIlanlar_TenantId_Slug",
                table: "WebIlanlar",
                columns: new[] { "TenantId", "Slug" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_WebIlanlar_TenantId_Slug",
                table: "WebIlanlar");

            migrationBuilder.DropColumn(
                name: "Slug",
                table: "WebIlanlar");

            migrationBuilder.DropColumn(
                name: "GosterilenKdvDahil",
                table: "SiteTalepleri");

            migrationBuilder.DropColumn(
                name: "IlanBaslik",
                table: "SiteTalepleri");

            migrationBuilder.DropColumn(
                name: "IlanId",
                table: "SiteTalepleri");
        }
    }
}
