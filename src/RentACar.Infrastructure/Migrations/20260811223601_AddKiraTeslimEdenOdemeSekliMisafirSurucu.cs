using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RentACar.Infrastructure.Migrations
{
    /// <summary>
    /// FAZ-47 — kira mega-formu derinliği: <c>Rentals</c> tablosuna 6 NULLABLE bilgi kolonu
    /// (çıkışta teslim eden personel, ödeme şekli, misafir 2. sürücü adı/soyadı/telefonu/ehliyet sınıfı).
    ///
    /// <para><b>Hepsi nullable, varsayılan YOK</b> — mevcut satırlara anlam yüklenmez (EF'in non-null
    /// kolona verdiği 0/false, geçmiş kayıtları "ödeme şekli girildi" gibi göstermesin).</para>
    ///
    /// <para><b>RLS bloğu gerekmez:</b> yeni tablo yok; <c>Rentals</c> zaten tenant-owned ve
    /// ENABLE+FORCE ROW LEVEL SECURITY + <c>tenant_isolation</c> politikası altında. Para kolonu
    /// eklenmedi — çok-taraflı bakiye alanları KARARLAR.md gereği AÇILMADI.</para>
    /// </summary>
    public partial class AddKiraTeslimEdenOdemeSekliMisafirSurucu : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "IkinciSurucuSerbestAd",
                table: "Rentals",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "IkinciSurucuSerbestEhliyetSinifi",
                table: "Rentals",
                type: "character varying(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "IkinciSurucuSerbestSoyad",
                table: "Rentals",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "IkinciSurucuSerbestTel",
                table: "Rentals",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OdemeSekli",
                table: "Rentals",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "TeslimEdenPersonelId",
                table: "Rentals",
                type: "uuid",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IkinciSurucuSerbestAd",
                table: "Rentals");

            migrationBuilder.DropColumn(
                name: "IkinciSurucuSerbestEhliyetSinifi",
                table: "Rentals");

            migrationBuilder.DropColumn(
                name: "IkinciSurucuSerbestSoyad",
                table: "Rentals");

            migrationBuilder.DropColumn(
                name: "IkinciSurucuSerbestTel",
                table: "Rentals");

            migrationBuilder.DropColumn(
                name: "OdemeSekli",
                table: "Rentals");

            migrationBuilder.DropColumn(
                name: "TeslimEdenPersonelId",
                table: "Rentals");
        }
    }
}
