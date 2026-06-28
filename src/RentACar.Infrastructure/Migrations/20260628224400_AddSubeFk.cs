using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RentACar.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddSubeFk : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "SubeId",
                table: "Vehicles",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "AtanmisSubeId",
                table: "Users",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "SubeId",
                table: "TarifeMatris",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "SubeId",
                table: "Personeller",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "SubeId",
                table: "Locations",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "SubeId",
                table: "KiralamaKurallari",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "SubeId",
                table: "Expenses",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddUniqueConstraint(
                name: "AK_Branches_TenantId_Id",
                table: "Branches",
                columns: new[] { "TenantId", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_Vehicles_SubeId",
                table: "Vehicles",
                column: "SubeId");

            migrationBuilder.CreateIndex(
                name: "IX_Vehicles_TenantId_SubeId",
                table: "Vehicles",
                columns: new[] { "TenantId", "SubeId" });

            migrationBuilder.CreateIndex(
                name: "IX_Users_AtanmisSubeId",
                table: "Users",
                column: "AtanmisSubeId");

            migrationBuilder.CreateIndex(
                name: "IX_Users_TenantId_AtanmisSubeId",
                table: "Users",
                columns: new[] { "TenantId", "AtanmisSubeId" });

            migrationBuilder.CreateIndex(
                name: "IX_TarifeMatris_SubeId",
                table: "TarifeMatris",
                column: "SubeId");

            migrationBuilder.CreateIndex(
                name: "IX_TarifeMatris_TenantId_SubeId",
                table: "TarifeMatris",
                columns: new[] { "TenantId", "SubeId" });

            migrationBuilder.CreateIndex(
                name: "IX_Personeller_SubeId",
                table: "Personeller",
                column: "SubeId");

            migrationBuilder.CreateIndex(
                name: "IX_Personeller_TenantId_SubeId",
                table: "Personeller",
                columns: new[] { "TenantId", "SubeId" });

            migrationBuilder.CreateIndex(
                name: "IX_Locations_SubeId",
                table: "Locations",
                column: "SubeId");

            migrationBuilder.CreateIndex(
                name: "IX_Locations_TenantId_SubeId",
                table: "Locations",
                columns: new[] { "TenantId", "SubeId" });

            migrationBuilder.CreateIndex(
                name: "IX_KiralamaKurallari_SubeId",
                table: "KiralamaKurallari",
                column: "SubeId");

            migrationBuilder.CreateIndex(
                name: "IX_KiralamaKurallari_TenantId_SubeId",
                table: "KiralamaKurallari",
                columns: new[] { "TenantId", "SubeId" });

            migrationBuilder.CreateIndex(
                name: "IX_Expenses_SubeId",
                table: "Expenses",
                column: "SubeId");

            migrationBuilder.CreateIndex(
                name: "IX_Expenses_TenantId_SubeId",
                table: "Expenses",
                columns: new[] { "TenantId", "SubeId" });

            migrationBuilder.AddForeignKey(
                name: "FK_Expenses_Branches_TenantId_SubeId",
                table: "Expenses",
                columns: new[] { "TenantId", "SubeId" },
                principalTable: "Branches",
                principalColumns: new[] { "TenantId", "Id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_KiralamaKurallari_Branches_TenantId_SubeId",
                table: "KiralamaKurallari",
                columns: new[] { "TenantId", "SubeId" },
                principalTable: "Branches",
                principalColumns: new[] { "TenantId", "Id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Locations_Branches_TenantId_SubeId",
                table: "Locations",
                columns: new[] { "TenantId", "SubeId" },
                principalTable: "Branches",
                principalColumns: new[] { "TenantId", "Id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Personeller_Branches_TenantId_SubeId",
                table: "Personeller",
                columns: new[] { "TenantId", "SubeId" },
                principalTable: "Branches",
                principalColumns: new[] { "TenantId", "Id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_TarifeMatris_Branches_TenantId_SubeId",
                table: "TarifeMatris",
                columns: new[] { "TenantId", "SubeId" },
                principalTable: "Branches",
                principalColumns: new[] { "TenantId", "Id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Users_Branches_TenantId_AtanmisSubeId",
                table: "Users",
                columns: new[] { "TenantId", "AtanmisSubeId" },
                principalTable: "Branches",
                principalColumns: new[] { "TenantId", "Id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Vehicles_Branches_TenantId_SubeId",
                table: "Vehicles",
                columns: new[] { "TenantId", "SubeId" },
                principalTable: "Branches",
                principalColumns: new[] { "TenantId", "Id" },
                onDelete: ReferentialAction.Restrict);

            // ---- ELLE veri backfill (roadmap F1) ----
            // Serbest-metin şube → AYNI TENANT Branch.Ad (exact, case-insensitive) eşleşmesiyle FK doldur.
            // Eşleşmeyen null kalır (metin yedek korunur; veri kaybı yok). Composite FK (TenantId,SubeId) zaten
            // çapraz-tenant atamayı imkansız kılar; bu UPDATE de WHERE b.TenantId=t.TenantId ile tenant-scoped.
            // İdempotent (yeniden çalışsa aynı sonuç). owner çalıştırır (RLS bypass) ama WHERE tenant'ı zorlar.
            foreach (var (tablo, metin, fk) in new[]
            {
                ("Vehicles", "Sube", "SubeId"),
                ("Expenses", "Sube", "SubeId"),
                ("Personeller", "Sube", "SubeId"),
                ("Locations", "Sube", "SubeId"),
                ("KiralamaKurallari", "Sube", "SubeId"),
                ("TarifeMatris", "Sube", "SubeId"),
                ("Users", "AtanmisSube", "AtanmisSubeId"),
            })
            {
                migrationBuilder.Sql(
                    $"UPDATE \"{tablo}\" t SET \"{fk}\" = b.\"Id\" " +
                    "FROM \"Branches\" b " +
                    $"WHERE b.\"TenantId\" = t.\"TenantId\" AND lower(b.\"Ad\") = lower(t.\"{metin}\") " +
                    $"AND t.\"{metin}\" IS NOT NULL AND t.\"{metin}\" <> '';");
            }
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Expenses_Branches_TenantId_SubeId",
                table: "Expenses");

            migrationBuilder.DropForeignKey(
                name: "FK_KiralamaKurallari_Branches_TenantId_SubeId",
                table: "KiralamaKurallari");

            migrationBuilder.DropForeignKey(
                name: "FK_Locations_Branches_TenantId_SubeId",
                table: "Locations");

            migrationBuilder.DropForeignKey(
                name: "FK_Personeller_Branches_TenantId_SubeId",
                table: "Personeller");

            migrationBuilder.DropForeignKey(
                name: "FK_TarifeMatris_Branches_TenantId_SubeId",
                table: "TarifeMatris");

            migrationBuilder.DropForeignKey(
                name: "FK_Users_Branches_TenantId_AtanmisSubeId",
                table: "Users");

            migrationBuilder.DropForeignKey(
                name: "FK_Vehicles_Branches_TenantId_SubeId",
                table: "Vehicles");

            migrationBuilder.DropIndex(
                name: "IX_Vehicles_SubeId",
                table: "Vehicles");

            migrationBuilder.DropIndex(
                name: "IX_Vehicles_TenantId_SubeId",
                table: "Vehicles");

            migrationBuilder.DropIndex(
                name: "IX_Users_AtanmisSubeId",
                table: "Users");

            migrationBuilder.DropIndex(
                name: "IX_Users_TenantId_AtanmisSubeId",
                table: "Users");

            migrationBuilder.DropIndex(
                name: "IX_TarifeMatris_SubeId",
                table: "TarifeMatris");

            migrationBuilder.DropIndex(
                name: "IX_TarifeMatris_TenantId_SubeId",
                table: "TarifeMatris");

            migrationBuilder.DropIndex(
                name: "IX_Personeller_SubeId",
                table: "Personeller");

            migrationBuilder.DropIndex(
                name: "IX_Personeller_TenantId_SubeId",
                table: "Personeller");

            migrationBuilder.DropIndex(
                name: "IX_Locations_SubeId",
                table: "Locations");

            migrationBuilder.DropIndex(
                name: "IX_Locations_TenantId_SubeId",
                table: "Locations");

            migrationBuilder.DropIndex(
                name: "IX_KiralamaKurallari_SubeId",
                table: "KiralamaKurallari");

            migrationBuilder.DropIndex(
                name: "IX_KiralamaKurallari_TenantId_SubeId",
                table: "KiralamaKurallari");

            migrationBuilder.DropIndex(
                name: "IX_Expenses_SubeId",
                table: "Expenses");

            migrationBuilder.DropIndex(
                name: "IX_Expenses_TenantId_SubeId",
                table: "Expenses");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_Branches_TenantId_Id",
                table: "Branches");

            migrationBuilder.DropColumn(
                name: "SubeId",
                table: "Vehicles");

            migrationBuilder.DropColumn(
                name: "AtanmisSubeId",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "SubeId",
                table: "TarifeMatris");

            migrationBuilder.DropColumn(
                name: "SubeId",
                table: "Personeller");

            migrationBuilder.DropColumn(
                name: "SubeId",
                table: "Locations");

            migrationBuilder.DropColumn(
                name: "SubeId",
                table: "KiralamaKurallari");

            migrationBuilder.DropColumn(
                name: "SubeId",
                table: "Expenses");
        }
    }
}
