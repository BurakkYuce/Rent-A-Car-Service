using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RentACar.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddSozlesmePaylasim : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PaylasimLinkler",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    RentalId = table.Column<Guid>(type: "uuid", nullable: false),
                    Token = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SozlesmeNo = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Iptal = table.Column<bool>(type: "boolean", nullable: false),
                    ErisimSayisi = table.Column<int>(type: "integer", nullable: false),
                    SonErisimUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    OlusturmaUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    AnlikGoruntuUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PaylasimLinkler", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PaylasimLinkler_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SozlesmePdfler",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    RentalId = table.Column<Guid>(type: "uuid", nullable: false),
                    Bytes = table.Column<byte[]>(type: "bytea", nullable: false),
                    Boyut = table.Column<long>(type: "bigint", nullable: false),
                    UretimUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SozlesmePdfler", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PaylasimLinkler_Token",
                table: "PaylasimLinkler",
                column: "Token",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_PaylasimLinkler_Aktif",
                table: "PaylasimLinkler",
                columns: new[] { "TenantId", "RentalId" },
                unique: true,
                filter: "NOT \"Iptal\"");

            migrationBuilder.CreateIndex(
                name: "IX_SozlesmePdfler_TenantId_RentalId",
                table: "SozlesmePdfler",
                columns: new[] { "TenantId", "RentalId" },
                unique: true);

            // ================= PR-C: iki tablo, İKİ FARKLI izolasyon rejimi =================
            //
            // 1) "PaylasimLinkler" — PLATFORM tablosu, **RLS YOK** (Tenants/Users/TenantDomains gibi).
            //    Bu bir tercih değil ZORUNLULUK: link ANONİM açılıyor, istekte cookie yok →
            //    app.tenant_id GUC set edilmemiş. FORCE RLS olsaydı token sorgusu SESSİZCE 0 satır
            //    döner ve her paylaşım linki ölürdü (Users.CalendarToken ile birebir aynı sebep).
            //    Bedeli ödenebilir çünkü tabloda KİŞİSEL VERİ YOK: token, tenant, kira, sözleşme no,
            //    iptal bayrağı, sayaç. İzolasyon uygulama katmanında (repo yüklemi TenantId'yi elle
            //    süzer; anonim yolda token TEK kimlik).
            //    Grant: tenant tarafı YAZAR (personel "Paylaş"a basıyor) + anonim uç sayaç günceller
            //    → SELECT/INSERT/UPDATE. DELETE YOK: link satırı asla silinmiyor (iptal = bayrak,
            //    erişim geçmişi kanıt olarak duruyor) — grant'ı vermemek bunu DB seviyesinde kilitler.
            migrationBuilder.Sql("GRANT SELECT, INSERT, UPDATE ON \"PaylasimLinkler\" TO racar_app;");

            // 2) "SozlesmePdfler" — TENANT-OWNED, tam RLS. PDF müşteri adı/adresi/TC'si taşır, yani
            //    kişisel veri; iki savunma katmanı da açık kalmalı (EF global filter + Postgres RLS).
            //    Anonim uç buraya token'dan ÇÖZDÜĞÜ tenant ile GUC açtıktan sonra erişir.
            migrationBuilder.Sql("ALTER TABLE \"SozlesmePdfler\" ENABLE ROW LEVEL SECURITY;");
            migrationBuilder.Sql("ALTER TABLE \"SozlesmePdfler\" FORCE ROW LEVEL SECURITY;");
            migrationBuilder.Sql("DROP POLICY IF EXISTS tenant_isolation ON \"SozlesmePdfler\";");
            migrationBuilder.Sql(
                "CREATE POLICY tenant_isolation ON \"SozlesmePdfler\" " +
                "USING (\"TenantId\" = NULLIF(current_setting('app.tenant_id', true), '')::uuid) " +
                "WITH CHECK (\"TenantId\" = NULLIF(current_setting('app.tenant_id', true), '')::uuid);");
            // DELETE dahil tam CRUD: iptalde anlık görüntü SİLİNİR (mali belge değil, immutability
            // trigger'ı UYGULANMAZ — bu bir yeniden üretilebilir çıktı, defter kaydı değil).
            migrationBuilder.Sql("GRANT SELECT, INSERT, UPDATE, DELETE ON \"SozlesmePdfler\" TO racar_app;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PaylasimLinkler");

            migrationBuilder.DropTable(
                name: "SozlesmePdfler");
        }
    }
}
