using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RentACar.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddCikisSubeFk : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "CikisSubeId",
                table: "Reservations",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CikisSubeId",
                table: "Rentals",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CikisSubeId",
                table: "Quotations",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Reservations_TenantId_CikisSubeId",
                table: "Reservations",
                columns: new[] { "TenantId", "CikisSubeId" });

            migrationBuilder.CreateIndex(
                name: "IX_Rentals_TenantId_CikisSubeId",
                table: "Rentals",
                columns: new[] { "TenantId", "CikisSubeId" });

            migrationBuilder.CreateIndex(
                name: "IX_Quotations_TenantId_CikisSubeId",
                table: "Quotations",
                columns: new[] { "TenantId", "CikisSubeId" });

            migrationBuilder.AddForeignKey(
                name: "FK_Quotations_Branches_TenantId_CikisSubeId",
                table: "Quotations",
                columns: new[] { "TenantId", "CikisSubeId" },
                principalTable: "Branches",
                principalColumns: new[] { "TenantId", "Id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Rentals_Branches_TenantId_CikisSubeId",
                table: "Rentals",
                columns: new[] { "TenantId", "CikisSubeId" },
                principalTable: "Branches",
                principalColumns: new[] { "TenantId", "Id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Reservations_Branches_TenantId_CikisSubeId",
                table: "Reservations",
                columns: new[] { "TenantId", "CikisSubeId" },
                principalTable: "Branches",
                principalColumns: new[] { "TenantId", "Id" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Quotations_Branches_TenantId_CikisSubeId",
                table: "Quotations");

            migrationBuilder.DropForeignKey(
                name: "FK_Rentals_Branches_TenantId_CikisSubeId",
                table: "Rentals");

            migrationBuilder.DropForeignKey(
                name: "FK_Reservations_Branches_TenantId_CikisSubeId",
                table: "Reservations");

            migrationBuilder.DropIndex(
                name: "IX_Reservations_TenantId_CikisSubeId",
                table: "Reservations");

            migrationBuilder.DropIndex(
                name: "IX_Rentals_TenantId_CikisSubeId",
                table: "Rentals");

            migrationBuilder.DropIndex(
                name: "IX_Quotations_TenantId_CikisSubeId",
                table: "Quotations");

            migrationBuilder.DropColumn(
                name: "CikisSubeId",
                table: "Reservations");

            migrationBuilder.DropColumn(
                name: "CikisSubeId",
                table: "Rentals");

            migrationBuilder.DropColumn(
                name: "CikisSubeId",
                table: "Quotations");
        }
    }
}
