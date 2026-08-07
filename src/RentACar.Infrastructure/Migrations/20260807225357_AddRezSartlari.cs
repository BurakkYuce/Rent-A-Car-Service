using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RentACar.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddRezSartlari : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "RezSartlari",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    MusteriId = table.Column<Guid>(type: "uuid", nullable: false),
                    Grup = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    Sart = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    BasTar = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    BitTar = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    TalepTarihi = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    KarsilamaTarihi = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    TeslimEden = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    ReservationId = table.Column<Guid>(type: "uuid", nullable: true),
                    QuotationId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RezSartlari", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RezSartlari_TenantId_KarsilamaTarihi",
                table: "RezSartlari",
                columns: new[] { "TenantId", "KarsilamaTarihi" });

            migrationBuilder.CreateIndex(
                name: "IX_RezSartlari_TenantId_MusteriId",
                table: "RezSartlari",
                columns: new[] { "TenantId", "MusteriId" });

            migrationBuilder.CreateIndex(
                name: "IX_RezSartlari_TenantId_TalepTarihi",
                table: "RezSartlari",
                columns: new[] { "TenantId", "TalepTarihi" });

            // RLS (tenant izolasyonu) — EF üretmez, CLAUDE.md §5 gereği ELLE yazılır.
            // Operasyonel not (mali belge DEĞİL) → değişmezlik trigger'ı YOK; tam CRUD grant.
            migrationBuilder.Sql("ALTER TABLE \"RezSartlari\" ENABLE ROW LEVEL SECURITY;");
            migrationBuilder.Sql("ALTER TABLE \"RezSartlari\" FORCE ROW LEVEL SECURITY;");
            migrationBuilder.Sql("DROP POLICY IF EXISTS tenant_isolation ON \"RezSartlari\";");
            migrationBuilder.Sql(
                "CREATE POLICY tenant_isolation ON \"RezSartlari\" " +
                "USING (\"TenantId\" = NULLIF(current_setting('app.tenant_id', true), '')::uuid) " +
                "WITH CHECK (\"TenantId\" = NULLIF(current_setting('app.tenant_id', true), '')::uuid);");
            migrationBuilder.Sql("GRANT SELECT, INSERT, UPDATE, DELETE ON \"RezSartlari\" TO racar_app;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RezSartlari");
        }
    }
}
