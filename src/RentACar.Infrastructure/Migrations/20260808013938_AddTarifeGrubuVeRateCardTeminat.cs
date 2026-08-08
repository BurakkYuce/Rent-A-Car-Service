using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RentACar.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddTarifeGrubuVeRateCardTeminat : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "Gosterme",
                table: "RateCards",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "HirsizlikDahil",
                table: "RateCards",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "MiniHasarDahil",
                table: "RateCards",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "ScdwDahil",
                table: "RateCards",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "ScdwZorunlu",
                table: "RateCards",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<Guid>(
                name: "TarifeGrubuId",
                table: "RateCards",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "TarifeGruplari",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Kod = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Ad = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Oran = table.Column<decimal>(type: "numeric(9,4)", nullable: false),
                    KullaniciAdi = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    SifreHash = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    Aktif = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TarifeGruplari", x => x.Id);
                    table.UniqueConstraint("AK_TarifeGruplari_TenantId_Id", x => new { x.TenantId, x.Id });
                });

            migrationBuilder.CreateIndex(
                name: "IX_RateCards_TenantId_TarifeGrubuId",
                table: "RateCards",
                columns: new[] { "TenantId", "TarifeGrubuId" });

            migrationBuilder.CreateIndex(
                name: "IX_TarifeGruplari_TenantId_Kod",
                table: "TarifeGruplari",
                columns: new[] { "TenantId", "Kod" },
                unique: true);

            // RLS (tenant izolasyonu) — EF üretmez, CLAUDE.md §5 gereği ELLE yazılır.
            // Master/tanım tablosu (mali belge DEĞİL) → değişmezlik trigger'ı YOK, tam CRUD grant.
            migrationBuilder.Sql("ALTER TABLE \"TarifeGruplari\" ENABLE ROW LEVEL SECURITY;");
            migrationBuilder.Sql("ALTER TABLE \"TarifeGruplari\" FORCE ROW LEVEL SECURITY;");
            migrationBuilder.Sql("DROP POLICY IF EXISTS tenant_isolation ON \"TarifeGruplari\";");
            migrationBuilder.Sql(
                "CREATE POLICY tenant_isolation ON \"TarifeGruplari\" " +
                "USING (\"TenantId\" = NULLIF(current_setting('app.tenant_id', true), '')::uuid) " +
                "WITH CHECK (\"TenantId\" = NULLIF(current_setting('app.tenant_id', true), '')::uuid);");
            migrationBuilder.Sql("GRANT SELECT, INSERT, UPDATE, DELETE ON \"TarifeGruplari\" TO racar_app;");

            migrationBuilder.AddForeignKey(
                name: "FK_RateCards_TarifeGruplari_TenantId_TarifeGrubuId",
                table: "RateCards",
                columns: new[] { "TenantId", "TarifeGrubuId" },
                principalTable: "TarifeGruplari",
                principalColumns: new[] { "TenantId", "Id" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_RateCards_TarifeGruplari_TenantId_TarifeGrubuId",
                table: "RateCards");

            migrationBuilder.DropTable(
                name: "TarifeGruplari");

            migrationBuilder.DropIndex(
                name: "IX_RateCards_TenantId_TarifeGrubuId",
                table: "RateCards");

            migrationBuilder.DropColumn(
                name: "Gosterme",
                table: "RateCards");

            migrationBuilder.DropColumn(
                name: "HirsizlikDahil",
                table: "RateCards");

            migrationBuilder.DropColumn(
                name: "MiniHasarDahil",
                table: "RateCards");

            migrationBuilder.DropColumn(
                name: "ScdwDahil",
                table: "RateCards");

            migrationBuilder.DropColumn(
                name: "ScdwZorunlu",
                table: "RateCards");

            migrationBuilder.DropColumn(
                name: "TarifeGrubuId",
                table: "RateCards");
        }
    }
}
