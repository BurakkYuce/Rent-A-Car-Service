using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RentACar.Infrastructure.Migrations
{
    /// <summary>
    /// FAZ-49 — rezervasyon kaynağı kural matrisi kolonları (mevcut tenant-owned tabloya ekleme).
    ///
    /// <para><b>RLS bloğu YOK ve gerekmiyor:</b> yeni TABLO yok. <c>RezervasyonKaynaklari</c> üzerinde
    /// <c>ENABLE</c>+<c>FORCE ROW LEVEL SECURITY</c> ve <c>tenant_isolation</c> politikası
    /// 20260628095121_AddRezervasyonKaynaklari ile zaten kurulu; politika SATIR düzeyinde çalıştığından
    /// yeni kolonları kendiliğinden kapsar. Grant da tablo düzeyindedir (kolon listesi verilmemiş)
    /// → <c>racar_app</c> yeni kolonlara ek GRANT'siz erişir.</para>
    ///
    /// <para><b>Varsayılan değer tuzağı:</b> bool bayraklar <c>defaultValue: false</c> alır — "işaretli
    /// değil" doğru ifadedir. Buna karşılık <c>KaynakGrubu</c>, <c>MaxGun</c>, tutar ve oran kolonları
    /// NULLABLE bırakılmıştır: 0 yazmak "grup yok / sınır yok / oran yok" değil "OfisSatış / hiç gün
    /// kiralanamaz / sıfır oran" iddiası olurdu (geçmiş kayıtlara sessiz veri uydurma).</para>
    /// </summary>
    public partial class AddReservationSourceKuralMatrisi : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "AcenteFiyatDegistir",
                table: "RezervasyonKaynaklari",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "AyniYonDrop",
                table: "RezervasyonKaynaklari",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<decimal>(
                name: "BebekKoltugu",
                table: "RezervasyonKaynaklari",
                type: "numeric(19,4)",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "CdwDahil",
                table: "RezervasyonKaynaklari",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "DropKaynakNo",
                table: "RezervasyonKaynaklari",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "EkSurucu",
                table: "RezervasyonKaynaklari",
                type: "numeric(19,4)",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "Gizle",
                table: "RezervasyonKaynaklari",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<decimal>(
                name: "IndirimOrani",
                table: "RezervasyonKaynaklari",
                type: "numeric(5,2)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "KaynakGrubu",
                table: "RezervasyonKaynaklari",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "KmSinirsiz",
                table: "RezervasyonKaynaklari",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<decimal>(
                name: "KomisyonOrani",
                table: "RezervasyonKaynaklari",
                type: "numeric(5,2)",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "LcfDahil",
                table: "RezervasyonKaynaklari",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "MailAdres",
                table: "RezervasyonKaynaklari",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "MaliyetYansitma",
                table: "RezervasyonKaynaklari",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "MatrisErken",
                table: "RezervasyonKaynaklari",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "MatrisGecikme",
                table: "RezervasyonKaynaklari",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "MatrisIptal",
                table: "RezervasyonKaynaklari",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "MatrisNoShow",
                table: "RezervasyonKaynaklari",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "MatrisUzatma",
                table: "RezervasyonKaynaklari",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "MaxGun",
                table: "RezervasyonKaynaklari",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MuafiyatSecenek",
                table: "RezervasyonKaynaklari",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "Navigasyon",
                table: "RezervasyonKaynaklari",
                type: "numeric(19,4)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "OnOdemeOrani",
                table: "RezervasyonKaynaklari",
                type: "numeric(5,2)",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "OtomatikMailGitme",
                table: "RezervasyonKaynaklari",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "PaiDahil",
                table: "RezervasyonKaynaklari",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "ProvizyonSecenek",
                table: "RezervasyonKaynaklari",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "ProvizyonYok",
                table: "RezervasyonKaynaklari",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<decimal>(
                name: "PuanOrani",
                table: "RezervasyonKaynaklari",
                type: "numeric(5,2)",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "RezTarihleriDegisemez",
                table: "RezervasyonKaynaklari",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "RiskAnalizYapma",
                table: "RezervasyonKaynaklari",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "SadeceMusteriOdeme",
                table: "RezervasyonKaynaklari",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "ScdwDahil",
                table: "RezervasyonKaynaklari",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "SigortaKaynakNo",
                table: "RezervasyonKaynaklari",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "SubeGor",
                table: "RezervasyonKaynaklari",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "Uzatamaz",
                table: "RezervasyonKaynaklari",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<decimal>(
                name: "Wifi",
                table: "RezervasyonKaynaklari",
                type: "numeric(19,4)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AcenteFiyatDegistir",
                table: "RezervasyonKaynaklari");

            migrationBuilder.DropColumn(
                name: "AyniYonDrop",
                table: "RezervasyonKaynaklari");

            migrationBuilder.DropColumn(
                name: "BebekKoltugu",
                table: "RezervasyonKaynaklari");

            migrationBuilder.DropColumn(
                name: "CdwDahil",
                table: "RezervasyonKaynaklari");

            migrationBuilder.DropColumn(
                name: "DropKaynakNo",
                table: "RezervasyonKaynaklari");

            migrationBuilder.DropColumn(
                name: "EkSurucu",
                table: "RezervasyonKaynaklari");

            migrationBuilder.DropColumn(
                name: "Gizle",
                table: "RezervasyonKaynaklari");

            migrationBuilder.DropColumn(
                name: "IndirimOrani",
                table: "RezervasyonKaynaklari");

            migrationBuilder.DropColumn(
                name: "KaynakGrubu",
                table: "RezervasyonKaynaklari");

            migrationBuilder.DropColumn(
                name: "KmSinirsiz",
                table: "RezervasyonKaynaklari");

            migrationBuilder.DropColumn(
                name: "KomisyonOrani",
                table: "RezervasyonKaynaklari");

            migrationBuilder.DropColumn(
                name: "LcfDahil",
                table: "RezervasyonKaynaklari");

            migrationBuilder.DropColumn(
                name: "MailAdres",
                table: "RezervasyonKaynaklari");

            migrationBuilder.DropColumn(
                name: "MaliyetYansitma",
                table: "RezervasyonKaynaklari");

            migrationBuilder.DropColumn(
                name: "MatrisErken",
                table: "RezervasyonKaynaklari");

            migrationBuilder.DropColumn(
                name: "MatrisGecikme",
                table: "RezervasyonKaynaklari");

            migrationBuilder.DropColumn(
                name: "MatrisIptal",
                table: "RezervasyonKaynaklari");

            migrationBuilder.DropColumn(
                name: "MatrisNoShow",
                table: "RezervasyonKaynaklari");

            migrationBuilder.DropColumn(
                name: "MatrisUzatma",
                table: "RezervasyonKaynaklari");

            migrationBuilder.DropColumn(
                name: "MaxGun",
                table: "RezervasyonKaynaklari");

            migrationBuilder.DropColumn(
                name: "MuafiyatSecenek",
                table: "RezervasyonKaynaklari");

            migrationBuilder.DropColumn(
                name: "Navigasyon",
                table: "RezervasyonKaynaklari");

            migrationBuilder.DropColumn(
                name: "OnOdemeOrani",
                table: "RezervasyonKaynaklari");

            migrationBuilder.DropColumn(
                name: "OtomatikMailGitme",
                table: "RezervasyonKaynaklari");

            migrationBuilder.DropColumn(
                name: "PaiDahil",
                table: "RezervasyonKaynaklari");

            migrationBuilder.DropColumn(
                name: "ProvizyonSecenek",
                table: "RezervasyonKaynaklari");

            migrationBuilder.DropColumn(
                name: "ProvizyonYok",
                table: "RezervasyonKaynaklari");

            migrationBuilder.DropColumn(
                name: "PuanOrani",
                table: "RezervasyonKaynaklari");

            migrationBuilder.DropColumn(
                name: "RezTarihleriDegisemez",
                table: "RezervasyonKaynaklari");

            migrationBuilder.DropColumn(
                name: "RiskAnalizYapma",
                table: "RezervasyonKaynaklari");

            migrationBuilder.DropColumn(
                name: "SadeceMusteriOdeme",
                table: "RezervasyonKaynaklari");

            migrationBuilder.DropColumn(
                name: "ScdwDahil",
                table: "RezervasyonKaynaklari");

            migrationBuilder.DropColumn(
                name: "SigortaKaynakNo",
                table: "RezervasyonKaynaklari");

            migrationBuilder.DropColumn(
                name: "SubeGor",
                table: "RezervasyonKaynaklari");

            migrationBuilder.DropColumn(
                name: "Uzatamaz",
                table: "RezervasyonKaynaklari");

            migrationBuilder.DropColumn(
                name: "Wifi",
                table: "RezervasyonKaynaklari");
        }
    }
}
