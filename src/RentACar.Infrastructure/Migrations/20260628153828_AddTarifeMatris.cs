using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RentACar.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddTarifeMatris : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TarifeMatris",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Kod = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Ad = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Aciklama = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    Kanal = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    Sube = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    Lokasyon = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    AracGrupKod = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    ParaBirimi = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: true),
                    BasTar = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    BitTar = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Gun1 = table.Column<decimal>(type: "numeric(19,4)", nullable: true),
                    Gun2 = table.Column<decimal>(type: "numeric(19,4)", nullable: true),
                    Gun3 = table.Column<decimal>(type: "numeric(19,4)", nullable: true),
                    Gun4 = table.Column<decimal>(type: "numeric(19,4)", nullable: true),
                    Gun5 = table.Column<decimal>(type: "numeric(19,4)", nullable: true),
                    Gun6 = table.Column<decimal>(type: "numeric(19,4)", nullable: true),
                    Gun7 = table.Column<decimal>(type: "numeric(19,4)", nullable: true),
                    MaxEsneklik = table.Column<decimal>(type: "numeric(9,4)", nullable: true),
                    OnayDurumu = table.Column<int>(type: "integer", nullable: false),
                    Onaylayan = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    OnayZaman = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Aktif = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TarifeMatris", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TarifeMatris_TenantId_Kod",
                table: "TarifeMatris",
                columns: new[] { "TenantId", "Kod" },
                unique: true);

            // RLS (tenant izolasyonu). Master/fiyat-tanım (mali belge DEĞİL) → değişmezlik trigger'ı
            // YOK; tam CRUD grant.
            migrationBuilder.Sql("ALTER TABLE \"TarifeMatris\" ENABLE ROW LEVEL SECURITY;");
            migrationBuilder.Sql("ALTER TABLE \"TarifeMatris\" FORCE ROW LEVEL SECURITY;");
            migrationBuilder.Sql("DROP POLICY IF EXISTS tenant_isolation ON \"TarifeMatris\";");
            migrationBuilder.Sql(
                "CREATE POLICY tenant_isolation ON \"TarifeMatris\" " +
                "USING (\"TenantId\" = NULLIF(current_setting('app.tenant_id', true), '')::uuid) " +
                "WITH CHECK (\"TenantId\" = NULLIF(current_setting('app.tenant_id', true), '')::uuid);");
            migrationBuilder.Sql("GRANT SELECT, INSERT, UPDATE, DELETE ON \"TarifeMatris\" TO racar_app;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TarifeMatris");
        }
    }
}
