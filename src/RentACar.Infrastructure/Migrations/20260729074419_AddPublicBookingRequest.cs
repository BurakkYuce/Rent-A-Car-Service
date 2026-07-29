using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RentACar.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPublicBookingRequest : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SiteTalepleri",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    AdSoyad = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    Telefon = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Email = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: true),
                    AracGrupKod = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    BasTar = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    BitTar = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Sube = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    Not = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    GosterilenGunlukUcretKdvDahil = table.Column<decimal>(type: "numeric(19,4)", nullable: true),
                    Durum = table.Column<int>(type: "integer", nullable: false),
                    DonusenReservationId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SiteTalepleri", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SiteTalepleri_TenantId_Durum_CreatedAtUtc",
                table: "SiteTalepleri",
                columns: new[] { "TenantId", "Durum", "CreatedAtUtc" });

            // ---- RLS (EF ÜRETMEZ — elle; CLAUDE.md §5 reçetesi). Mali belge DEĞİL → tam CRUD grant.
            // NOT: bu tablo repo'daki İLK anonim-YAZMA yüzeyidir (PublicSite'tan guard'sız insert) —
            // RLS tenant sınırı bu yüzden burada ekstra kritik. ----
            migrationBuilder.Sql("ALTER TABLE \"SiteTalepleri\" ENABLE ROW LEVEL SECURITY;");
            migrationBuilder.Sql("ALTER TABLE \"SiteTalepleri\" FORCE ROW LEVEL SECURITY;");
            migrationBuilder.Sql("DROP POLICY IF EXISTS tenant_isolation ON \"SiteTalepleri\";");
            migrationBuilder.Sql(
                "CREATE POLICY tenant_isolation ON \"SiteTalepleri\" " +
                "USING (\"TenantId\" = NULLIF(current_setting('app.tenant_id', true), '')::uuid) " +
                "WITH CHECK (\"TenantId\" = NULLIF(current_setting('app.tenant_id', true), '')::uuid);");
            migrationBuilder.Sql("GRANT SELECT, INSERT, UPDATE, DELETE ON \"SiteTalepleri\" TO racar_app;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SiteTalepleri");
        }
    }
}
