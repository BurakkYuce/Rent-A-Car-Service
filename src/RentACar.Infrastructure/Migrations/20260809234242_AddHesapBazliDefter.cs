using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RentACar.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddHesapBazliDefter : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AccountLedgerEntries_Virman_Idem",
                table: "AccountLedgerEntries");

            migrationBuilder.AddColumn<Guid>(
                name: "HesapId",
                table: "CashTransactions",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "KasaVirmanBilgileri",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    KaynakTur = table.Column<int>(type: "integer", nullable: false),
                    HedefTur = table.Column<int>(type: "integer", nullable: false),
                    KaynakHesapId = table.Column<Guid>(type: "uuid", nullable: true),
                    HedefHesapId = table.Column<Guid>(type: "uuid", nullable: true),
                    Tarih = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    MakbuzNo = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    Sube = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    IslemYapan = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    Aciklama = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_KasaVirmanBilgileri", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CashTransactions_TenantId_HesapId",
                table: "CashTransactions",
                columns: new[] { "TenantId", "HesapId" });

            // Virman idempotency indeksi ELLE kuruluyor: EF'in urettigi CreateIndex(unique: true)
            // NULLS NOT DISTINCT BASMAZ. AccountRef nullable ve hesap secilmeyen (legacy) virmanda
            // iki bacak da NULL olur; PG varsayilaninda NULL'lar farkli sayildigi icin ayni islem
            // anahtariyla ikinci gonderim CAKISMAZ ve mevcut cift-submit korumasi SESSIZCE kaybolurdu.
            // (PG 15+ gerekir; yerel 15.x, CI postgres:16 -- ikisinde de dogrulandi.)
            migrationBuilder.Sql(
                "CREATE UNIQUE INDEX \"IX_AccountLedgerEntries_Virman_Idem\" " +
                "ON \"AccountLedgerEntries\" (\"TenantId\", \"SourceType\", \"SourceId\", \"AccountType\", \"AccountRef\") " +
                "NULLS NOT DISTINCT WHERE \"SourceType\" = 'Virman';");

            migrationBuilder.CreateIndex(
                name: "IX_KasaVirmanBilgileri_TenantId_HedefHesapId",
                table: "KasaVirmanBilgileri",
                columns: new[] { "TenantId", "HedefHesapId" });

            migrationBuilder.CreateIndex(
                name: "IX_KasaVirmanBilgileri_TenantId_KaynakHesapId",
                table: "KasaVirmanBilgileri",
                columns: new[] { "TenantId", "KaynakHesapId" });

            migrationBuilder.CreateIndex(
                name: "IX_KasaVirmanBilgileri_TenantId_Tarih",
                table: "KasaVirmanBilgileri",
                columns: new[] { "TenantId", "Tarih" });

            // RLS (tenant izolasyonu) -- EF uretmez, CLAUDE.md §5 geregi ELLE yazilir.
            // Kunye tablosu PARA TASIMAZ (mali belge degil) -> degismezlik trigger'i YOK, tam CRUD grant.
            migrationBuilder.Sql("ALTER TABLE \"KasaVirmanBilgileri\" ENABLE ROW LEVEL SECURITY;");
            migrationBuilder.Sql("ALTER TABLE \"KasaVirmanBilgileri\" FORCE ROW LEVEL SECURITY;");
            migrationBuilder.Sql("DROP POLICY IF EXISTS tenant_isolation ON \"KasaVirmanBilgileri\";");
            migrationBuilder.Sql(
                "CREATE POLICY tenant_isolation ON \"KasaVirmanBilgileri\" " +
                "USING (\"TenantId\" = NULLIF(current_setting('app.tenant_id', true), '')::uuid) " +
                "WITH CHECK (\"TenantId\" = NULLIF(current_setting('app.tenant_id', true), '')::uuid);");
            migrationBuilder.Sql("GRANT SELECT, INSERT, UPDATE, DELETE ON \"KasaVirmanBilgileri\" TO racar_app;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "KasaVirmanBilgileri");

            migrationBuilder.DropIndex(
                name: "IX_CashTransactions_TenantId_HesapId",
                table: "CashTransactions");

            migrationBuilder.DropIndex(
                name: "IX_AccountLedgerEntries_Virman_Idem",
                table: "AccountLedgerEntries");

            migrationBuilder.DropColumn(
                name: "HesapId",
                table: "CashTransactions");

            // ADVERSARIAL M5 — geri alma, AYNI TÜRDE (Banka→Banka) virman yazılmışsa yapısal olarak
            // MÜMKÜN DEĞİL: eski 4 kolonlu anahtar o iki bacağı ayıramaz ve indeks kurulamaz
            // (23505). Ham CreateIndex çirkin bir DB hatası veriyordu; burada NE OLDUĞUNU söyleyen
            // bir mesajla reddediyoruz — sessiz/kriptik başarısızlık yerine açık teşhis.
            migrationBuilder.Sql(@"
DO $$
DECLARE cakisan int;
BEGIN
    SELECT count(*) INTO cakisan FROM (
        SELECT 1 FROM ""AccountLedgerEntries""
        WHERE ""SourceType"" = 'Virman'
        GROUP BY ""TenantId"", ""SourceType"", ""SourceId"", ""AccountType""
        HAVING count(*) > 1
    ) x;
    IF cakisan > 0 THEN
        RAISE EXCEPTION 'FAZ-50 geri alinamaz: % adet ayni-turde (or. Banka->Banka) virman var; eski benzersizlik anahtari bu kayitlari ayiramaz. Once o virmanlari ters kayitla kapatin.', cakisan;
    END IF;
END $$;");
            migrationBuilder.CreateIndex(
                name: "IX_AccountLedgerEntries_Virman_Idem",
                table: "AccountLedgerEntries",
                columns: new[] { "TenantId", "SourceType", "SourceId", "AccountType" },
                unique: true,
                filter: "\"SourceType\" = 'Virman'");
        }
    }
}
