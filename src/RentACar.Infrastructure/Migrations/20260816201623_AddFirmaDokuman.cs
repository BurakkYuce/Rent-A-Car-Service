using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RentACar.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddFirmaDokuman : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "FirmaDokumanlari",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Baslik = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Aciklama = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    DosyaAdi = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Bytes = table.Column<byte[]>(type: "bytea", nullable: false),
                    Boyut = table.Column<long>(type: "bigint", nullable: false),
                    ContentType = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Sira = table.Column<int>(type: "integer", nullable: false),
                    YukleyenKullanici = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FirmaDokumanlari", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_FirmaDokumanlari_TenantId_Sira",
                table: "FirmaDokumanlari",
                columns: new[] { "TenantId", "Sira" },
                unique: true);

            // Yuva aralığı: 1..10. Yukarıdaki (TenantId, Sira) unique index ile BİRLİKTE
            // "tenant başına en fazla 10 doküman" sınırını YAPISAL hale getirir — servisteki
            // sayım bir yarışta atlansa bile 11. satır veritabanına giremez.
            migrationBuilder.Sql(
                "ALTER TABLE \"FirmaDokumanlari\" ADD CONSTRAINT ck_firmadokuman_sira " +
                "CHECK (\"Sira\" BETWEEN 1 AND 10);");

            // RLS (tenant izolasyonu). MALİ BELGE DEĞİL → değişmezlik trigger'ı YOK, tam CRUD grant:
            // firma yanlış yüklediği dosyayı silip yenisini koyabilmeli.
            migrationBuilder.Sql("ALTER TABLE \"FirmaDokumanlari\" ENABLE ROW LEVEL SECURITY;");
            migrationBuilder.Sql("ALTER TABLE \"FirmaDokumanlari\" FORCE ROW LEVEL SECURITY;");
            migrationBuilder.Sql("DROP POLICY IF EXISTS tenant_isolation ON \"FirmaDokumanlari\";");
            migrationBuilder.Sql(
                "CREATE POLICY tenant_isolation ON \"FirmaDokumanlari\" " +
                "USING (\"TenantId\" = NULLIF(current_setting('app.tenant_id', true), '')::uuid) " +
                "WITH CHECK (\"TenantId\" = NULLIF(current_setting('app.tenant_id', true), '')::uuid);");
            migrationBuilder.Sql("GRANT SELECT, INSERT, UPDATE, DELETE ON \"FirmaDokumanlari\" TO racar_app;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FirmaDokumanlari");
        }
    }
}
