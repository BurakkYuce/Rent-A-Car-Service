using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RentACar.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddInsurancePolicyZeyil : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "AksesuarDegeri",
                table: "InsurancePolicies",
                type: "numeric(19,4)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "AracDegeri",
                table: "InsurancePolicies",
                type: "numeric(19,4)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "ImmDegeri",
                table: "InsurancePolicies",
                type: "numeric(19,4)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "Kalan",
                table: "InsurancePolicies",
                type: "numeric(19,4)",
                nullable: false,
                defaultValue: 0m);

            // MEVCUT POLİÇELER İÇİN BACKFILL — kolon varsayılanı 0'dır ve 0 "ödenmiş" demektir.
            // Kalan alanı kayıt açılışında = Prim olur, ödeme onu 0'a düşürür; dolayısıyla eski
            // satırları 0 bırakmak ÖDENMEMİŞ poliçeleri "borcu yok" göstermek olurdu.
            // RLS TUZAĞI: racar_owner NOBYPASSRLS'tir → düz UPDATE 0 satır görür. FORCE geçici
            // olarak kaldırılıp geri konur (CLAUDE.md migration reçetesi).
            migrationBuilder.Sql("""
                ALTER TABLE "InsurancePolicies" NO FORCE ROW LEVEL SECURITY;
                UPDATE "InsurancePolicies" SET "Kalan" = CASE WHEN "Odendi" THEN 0 ELSE "Prim" END;
                ALTER TABLE "InsurancePolicies" FORCE ROW LEVEL SECURITY;
                """);

            migrationBuilder.AddUniqueConstraint(
                name: "AK_InsurancePolicies_TenantId_Id",
                table: "InsurancePolicies",
                columns: new[] { "TenantId", "Id" });

            migrationBuilder.CreateTable(
                name: "InsurancePolicyZeyilleri",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    PolicyId = table.Column<Guid>(type: "uuid", nullable: false),
                    ZeyilNo = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Tarih = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Tanzim = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Deger = table.Column<decimal>(type: "numeric(19,4)", nullable: false),
                    Brut = table.Column<decimal>(type: "numeric(19,4)", nullable: false),
                    Net = table.Column<decimal>(type: "numeric(19,4)", nullable: false),
                    FonVergi = table.Column<decimal>(type: "numeric(19,4)", nullable: false),
                    Tipi = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    Neden = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InsurancePolicyZeyilleri", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InsurancePolicyZeyilleri_InsurancePolicies_TenantId_PolicyId",
                        columns: x => new { x.TenantId, x.PolicyId },
                        principalTable: "InsurancePolicies",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_InsurancePolicyZeyilleri_TenantId_PolicyId_ZeyilNo",
                table: "InsurancePolicyZeyilleri",
                columns: new[] { "TenantId", "PolicyId", "ZeyilNo" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_InsurancePolicyZeyilleri_TenantId_Tarih",
                table: "InsurancePolicyZeyilleri",
                columns: new[] { "TenantId", "Tarih" });

            // ---- RLS bloğu ELLE eklendi (EF üretmez) — CLAUDE.md §5 adım 4 ----
            // Zeyil MALİ BELGE DEĞİLDİR: hiçbir AccountLedgerEntry üretmez, tutarları yalnız
            // bilgi/geçmiştir (docs/roadmap/KARARLAR.md "yeni tutar alanları deftere yazmaz").
            // → rc_prevent_mutation() değişmezlik trigger'ı GEREKMEZ; yanlış girilen zeyil
            //   düzeltilebilmeli/silinebilmeli, bu yüzden TAM CRUD grant verilir.
            migrationBuilder.Sql("""
                ALTER TABLE "InsurancePolicyZeyilleri" ENABLE ROW LEVEL SECURITY;
                ALTER TABLE "InsurancePolicyZeyilleri" FORCE ROW LEVEL SECURITY;
                CREATE POLICY tenant_isolation ON "InsurancePolicyZeyilleri"
                    USING ("TenantId" = NULLIF(current_setting('app.tenant_id', true), '')::uuid)
                    WITH CHECK ("TenantId" = NULLIF(current_setting('app.tenant_id', true), '')::uuid);
                GRANT SELECT, INSERT, UPDATE, DELETE ON "InsurancePolicyZeyilleri" TO racar_app;
                """);

            // İkinci savunma: Değer bir TEMİNAT tabanıdır, negatif olamaz (uygulama zaten reddediyor).
            // Brüt/Net/Fon-Vergi'ye CHECK KONMAZ: "tenzil (iade) zeyli" bunları negatif yazar ve
            // bilgi alanı oldukları için işaret serbesttir.
            migrationBuilder.Sql("""
                ALTER TABLE "InsurancePolicyZeyilleri"
                  ADD CONSTRAINT "CK_InsurancePolicyZeyilleri_DegerNegatifDegil" CHECK ("Deger" >= 0);
                """);

            // ---- Kalan backfill (mevcut poliçeler) ----
            // DİKKAT — DÜZ `UPDATE` BURADA SESSİZCE 0 SATIR ETKİLER: "InsurancePolicies" FORCE ROW
            // LEVEL SECURITY taşıyor ve migration'ı çalıştıran racar_owner'ın BYPASSRLS yetkisi YOK
            // (bilinçli). app.tenant_id GUC'u set edilmeden policy hiçbir satırı eşleştirmez ve
            // UPDATE hata VERMEDEN hiçbir şey yapmaz (YakitNullable migration'ında yaşanan tuzak).
            // Çözüm: tenant döngüsü + İŞLEM-YEREL set_config.
            // Ödenmiş poliçe → 0 (varsayılan zaten 0), ödenmemiş → Prim.
            migrationBuilder.Sql("""
                DO $$
                DECLARE t uuid;
                BEGIN
                    FOR t IN SELECT "Id" FROM "Tenants" LOOP
                        PERFORM set_config('app.tenant_id', t::text, true);
                        UPDATE "InsurancePolicies" SET "Kalan" = "Prim" WHERE "Odendi" = false;
                    END LOOP;
                END $$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "InsurancePolicyZeyilleri");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_InsurancePolicies_TenantId_Id",
                table: "InsurancePolicies");

            migrationBuilder.DropColumn(
                name: "AksesuarDegeri",
                table: "InsurancePolicies");

            migrationBuilder.DropColumn(
                name: "AracDegeri",
                table: "InsurancePolicies");

            migrationBuilder.DropColumn(
                name: "ImmDegeri",
                table: "InsurancePolicies");

            migrationBuilder.DropColumn(
                name: "Kalan",
                table: "InsurancePolicies");
        }
    }
}
