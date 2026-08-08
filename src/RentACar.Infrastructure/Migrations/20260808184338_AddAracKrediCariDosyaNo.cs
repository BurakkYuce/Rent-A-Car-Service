using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RentACar.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddAracKrediCariDosyaNo : Migration
    {
        /// <inheritdoc />
        /// <remarks>
        /// FAZ-13 — MEVCUT tabloya iki NULLABLE kolon (CariId, DosyaNo) + composite tenant-FK.
        /// <para><b>Yeni RLS bloğu GEREKMEZ:</b> "AracKredileri" tablosunda ENABLE + FORCE ROW LEVEL
        /// SECURITY ve tenant_isolation policy ilk migration'da kurulmuştu; kolon eklemek policy'yi
        /// etkilemez ve racar_app'in tablo düzeyindeki GRANT'i yeni kolonları da kapsar.</para>
        /// <para><b>BACKFILL YOK — bilinçli:</b> her iki kolon da nullable ve varsayılansız. Eski
        /// kredilerde cari bağı gerçekten YOKTUR; bir değer uydurmak (ör. ilk cari) yanlış anlam
        /// yüklerdi. NULL burada "bilinmiyor/bağlanmamış" demektir ve ekranda "—" görünür.
        /// (Karşı örnek: FAZ-15'te non-null "Kalan" kolonuna 0 default'u "ödenmiş" anlamına gelip
        /// backfill gerektirmişti — burada o sınıf risk yok.)</para>
        /// </remarks>
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "CariId",
                table: "AracKredileri",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DosyaNo",
                table: "AracKredileri",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_AracKredileri_TenantId_CariId",
                table: "AracKredileri",
                columns: new[] { "TenantId", "CariId" });

            migrationBuilder.CreateIndex(
                name: "IX_AracKredileri_TenantId_DosyaNo",
                table: "AracKredileri",
                columns: new[] { "TenantId", "DosyaNo" });

            migrationBuilder.AddForeignKey(
                name: "FK_AracKredileri_Customers_TenantId_CariId",
                table: "AracKredileri",
                columns: new[] { "TenantId", "CariId" },
                principalTable: "Customers",
                principalColumns: new[] { "TenantId", "Id" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_AracKredileri_Customers_TenantId_CariId",
                table: "AracKredileri");

            migrationBuilder.DropIndex(
                name: "IX_AracKredileri_TenantId_CariId",
                table: "AracKredileri");

            migrationBuilder.DropIndex(
                name: "IX_AracKredileri_TenantId_DosyaNo",
                table: "AracKredileri");

            migrationBuilder.DropColumn(
                name: "CariId",
                table: "AracKredileri");

            migrationBuilder.DropColumn(
                name: "DosyaNo",
                table: "AracKredileri");
        }
    }
}
