using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RentACar.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddMaliyetTeklifi : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "MaliyetTeklifleri",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    KayitNo = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Baslik = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Plaka = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    Tarih = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CariId = table.Column<Guid>(type: "uuid", nullable: true),
                    HazirlayanId = table.Column<Guid>(type: "uuid", nullable: true),
                    Aciklama = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    AlisBedeli = table.Column<decimal>(type: "numeric(19,4)", nullable: false),
                    ResidualYuzde = table.Column<decimal>(type: "numeric(19,4)", nullable: false),
                    SureAy = table.Column<int>(type: "integer", nullable: false),
                    FaizOran = table.Column<decimal>(type: "numeric(19,4)", nullable: false),
                    KkdfOran = table.Column<decimal>(type: "numeric(19,4)", nullable: false),
                    BsmvOran = table.Column<decimal>(type: "numeric(19,4)", nullable: false),
                    DamgaOran = table.Column<decimal>(type: "numeric(19,4)", nullable: false),
                    KarMarji = table.Column<decimal>(type: "numeric(19,4)", nullable: false),
                    KdvOran = table.Column<decimal>(type: "numeric(19,4)", nullable: false),
                    EnflasyonOran = table.Column<decimal>(type: "numeric(19,4)", nullable: false),
                    KrediHesaplamaSekli = table.Column<int>(type: "integer", nullable: false),
                    AracSayisi = table.Column<int>(type: "integer", nullable: false),
                    KaskoYillik = table.Column<decimal>(type: "numeric(19,4)", nullable: false),
                    TrafikSigortasiYillik = table.Column<decimal>(type: "numeric(19,4)", nullable: false),
                    MtvYillik = table.Column<decimal>(type: "numeric(19,4)", nullable: false),
                    BakimYillik = table.Column<decimal>(type: "numeric(19,4)", nullable: false),
                    LastikYillik = table.Column<decimal>(type: "numeric(19,4)", nullable: false),
                    LastikKisYillik = table.Column<decimal>(type: "numeric(19,4)", nullable: false),
                    AracTakipYillik = table.Column<decimal>(type: "numeric(19,4)", nullable: false),
                    TescilPlakaYillik = table.Column<decimal>(type: "numeric(19,4)", nullable: false),
                    MuayeneEmisyonYillik = table.Column<decimal>(type: "numeric(19,4)", nullable: false),
                    YedekAracYillik = table.Column<decimal>(type: "numeric(19,4)", nullable: false),
                    YonetimGideriAylik = table.Column<decimal>(type: "numeric(19,4)", nullable: false),
                    AylikGider = table.Column<decimal>(type: "numeric(19,4)", nullable: false),
                    BankaDosyaDigerMasraf = table.Column<decimal>(type: "numeric(19,4)", nullable: false),
                    ResidualDeger = table.Column<decimal>(type: "numeric(19,4)", nullable: false),
                    NetAmortisman = table.Column<decimal>(type: "numeric(19,4)", nullable: false),
                    FinansmanFaiz = table.Column<decimal>(type: "numeric(19,4)", nullable: false),
                    FinansmanVergi = table.Column<decimal>(type: "numeric(19,4)", nullable: false),
                    Damga = table.Column<decimal>(type: "numeric(19,4)", nullable: false),
                    ToplamGider = table.Column<decimal>(type: "numeric(19,4)", nullable: false),
                    ToplamMaliyet = table.Column<decimal>(type: "numeric(19,4)", nullable: false),
                    BasaBasAylik = table.Column<decimal>(type: "numeric(19,4)", nullable: false),
                    Kar = table.Column<decimal>(type: "numeric(19,4)", nullable: false),
                    TeklifNet = table.Column<decimal>(type: "numeric(19,4)", nullable: false),
                    TeklifAylikNet = table.Column<decimal>(type: "numeric(19,4)", nullable: false),
                    TeklifKdvli = table.Column<decimal>(type: "numeric(19,4)", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MaliyetTeklifleri", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MaliyetTeklifleri_Customers_TenantId_CariId",
                        columns: x => new { x.TenantId, x.CariId },
                        principalTable: "Customers",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MaliyetTeklifleri_Personeller_TenantId_HazirlayanId",
                        columns: x => new { x.TenantId, x.HazirlayanId },
                        principalTable: "Personeller",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MaliyetTeklifleri_TenantId_CariId",
                table: "MaliyetTeklifleri",
                columns: new[] { "TenantId", "CariId" });

            migrationBuilder.CreateIndex(
                name: "IX_MaliyetTeklifleri_TenantId_HazirlayanId",
                table: "MaliyetTeklifleri",
                columns: new[] { "TenantId", "HazirlayanId" });

            migrationBuilder.CreateIndex(
                name: "IX_MaliyetTeklifleri_TenantId_KayitNo",
                table: "MaliyetTeklifleri",
                columns: new[] { "TenantId", "KayitNo" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MaliyetTeklifleri_TenantId_Tarih",
                table: "MaliyetTeklifleri",
                columns: new[] { "TenantId", "Tarih" });

            // RLS — EF ÜRETMEZ, elle (CLAUDE.md §5). MaliyetTeklifi bir PLANLAMA belgesidir:
            // deftere postalamaz, mali belge sayılmaz → değişmezlik trigger'ı YOK, tam CRUD grant
            // (teklif düzeltilebilir/silinebilir olmalı).
            migrationBuilder.Sql("ALTER TABLE \"MaliyetTeklifleri\" ENABLE ROW LEVEL SECURITY;");
            migrationBuilder.Sql("ALTER TABLE \"MaliyetTeklifleri\" FORCE ROW LEVEL SECURITY;");
            migrationBuilder.Sql("DROP POLICY IF EXISTS tenant_isolation ON \"MaliyetTeklifleri\";");
            migrationBuilder.Sql(
                "CREATE POLICY tenant_isolation ON \"MaliyetTeklifleri\" " +
                "USING (\"TenantId\" = NULLIF(current_setting('app.tenant_id', true), '')::uuid) " +
                "WITH CHECK (\"TenantId\" = NULLIF(current_setting('app.tenant_id', true), '')::uuid);");
            migrationBuilder.Sql("GRANT SELECT, INSERT, UPDATE, DELETE ON \"MaliyetTeklifleri\" TO racar_app;");

            // Uygulama zaten reddediyor; DB de kendi başına savunsun (MaliyetHesapService guard'ları
            // ile AYNI sınırlar — ikisi ayrışırsa test ampirik olarak yakalar).
            migrationBuilder.Sql(
                "ALTER TABLE \"MaliyetTeklifleri\" ADD CONSTRAINT \"CK_MaliyetTeklifleri_AlisPozitif\" " +
                "CHECK (\"AlisBedeli\" > 0);");
            migrationBuilder.Sql(
                "ALTER TABLE \"MaliyetTeklifleri\" ADD CONSTRAINT \"CK_MaliyetTeklifleri_SureAy\" " +
                "CHECK (\"SureAy\" BETWEEN 1 AND 120);");
            migrationBuilder.Sql(
                "ALTER TABLE \"MaliyetTeklifleri\" ADD CONSTRAINT \"CK_MaliyetTeklifleri_AracSayisi\" " +
                "CHECK (\"AracSayisi\" BETWEEN 1 AND 1000);");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MaliyetTeklifleri");
        }
    }
}
