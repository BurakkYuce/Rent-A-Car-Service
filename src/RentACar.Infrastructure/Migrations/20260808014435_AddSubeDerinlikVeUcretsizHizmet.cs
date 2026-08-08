using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RentACar.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddSubeDerinlikVeUcretsizHizmet : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "AlisSubesiDegilMi",
                table: "Branches",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<Guid>(
                name: "BankaHesapId",
                table: "Branches",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BayiCariKod",
                table: "Branches",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BayiOfisId",
                table: "Branches",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "Boylam",
                table: "Branches",
                type: "numeric(9,6)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "Enlem",
                table: "Branches",
                type: "numeric(9,6)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EntegrasyonKodu",
                table: "Branches",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FirmaUnvani",
                table: "Branches",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "HaftalikCalismaSaatleri",
                table: "Branches",
                type: "character varying(1024)",
                maxLength: 1024,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "HizmetKomisyonOran",
                table: "Branches",
                type: "numeric(9,4)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "KomisyonHesabi",
                table: "Branches",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "NakitHesapId",
                table: "Branches",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OnlineRezId",
                table: "Branches",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ResimDosyasi",
                table: "Branches",
                type: "character varying(512)",
                maxLength: 512,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RezervasyonRengi",
                table: "Branches",
                type: "character varying(7)",
                maxLength: 7,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SozlesmeNoFormati",
                table: "Branches",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "WebIsim",
                table: "Branches",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "WebOtoparkId",
                table: "Branches",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "WebRezOncesiSaat",
                table: "Branches",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "WebSira",
                table: "Branches",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "SubeUcretsizHizmetler",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    SubeId = table.Column<Guid>(type: "uuid", nullable: false),
                    HizmetAdi = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Aciklama = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SubeUcretsizHizmetler", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SubeUcretsizHizmetler_Branches_TenantId_SubeId",
                        columns: x => new { x.TenantId, x.SubeId },
                        principalTable: "Branches",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SubeUcretsizHizmetler_TenantId_SubeId",
                table: "SubeUcretsizHizmetler",
                columns: new[] { "TenantId", "SubeId" });

            // RLS (tenant izolasyonu) — EF üretmez, CLAUDE.md §5 gereği ELLE yazılır.
            // Vitrin/bilgi listesi (mali belge DEĞİL) → değişmezlik trigger'ı YOK, tam CRUD grant.
            migrationBuilder.Sql("ALTER TABLE \"SubeUcretsizHizmetler\" ENABLE ROW LEVEL SECURITY;");
            migrationBuilder.Sql("ALTER TABLE \"SubeUcretsizHizmetler\" FORCE ROW LEVEL SECURITY;");
            migrationBuilder.Sql("DROP POLICY IF EXISTS tenant_isolation ON \"SubeUcretsizHizmetler\";");
            migrationBuilder.Sql(
                "CREATE POLICY tenant_isolation ON \"SubeUcretsizHizmetler\" " +
                "USING (\"TenantId\" = NULLIF(current_setting('app.tenant_id', true), '')::uuid) " +
                "WITH CHECK (\"TenantId\" = NULLIF(current_setting('app.tenant_id', true), '')::uuid);");
            migrationBuilder.Sql("GRANT SELECT, INSERT, UPDATE, DELETE ON \"SubeUcretsizHizmetler\" TO racar_app;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SubeUcretsizHizmetler");

            migrationBuilder.DropColumn(
                name: "AlisSubesiDegilMi",
                table: "Branches");

            migrationBuilder.DropColumn(
                name: "BankaHesapId",
                table: "Branches");

            migrationBuilder.DropColumn(
                name: "BayiCariKod",
                table: "Branches");

            migrationBuilder.DropColumn(
                name: "BayiOfisId",
                table: "Branches");

            migrationBuilder.DropColumn(
                name: "Boylam",
                table: "Branches");

            migrationBuilder.DropColumn(
                name: "Enlem",
                table: "Branches");

            migrationBuilder.DropColumn(
                name: "EntegrasyonKodu",
                table: "Branches");

            migrationBuilder.DropColumn(
                name: "FirmaUnvani",
                table: "Branches");

            migrationBuilder.DropColumn(
                name: "HaftalikCalismaSaatleri",
                table: "Branches");

            migrationBuilder.DropColumn(
                name: "HizmetKomisyonOran",
                table: "Branches");

            migrationBuilder.DropColumn(
                name: "KomisyonHesabi",
                table: "Branches");

            migrationBuilder.DropColumn(
                name: "NakitHesapId",
                table: "Branches");

            migrationBuilder.DropColumn(
                name: "OnlineRezId",
                table: "Branches");

            migrationBuilder.DropColumn(
                name: "ResimDosyasi",
                table: "Branches");

            migrationBuilder.DropColumn(
                name: "RezervasyonRengi",
                table: "Branches");

            migrationBuilder.DropColumn(
                name: "SozlesmeNoFormati",
                table: "Branches");

            migrationBuilder.DropColumn(
                name: "WebIsim",
                table: "Branches");

            migrationBuilder.DropColumn(
                name: "WebOtoparkId",
                table: "Branches");

            migrationBuilder.DropColumn(
                name: "WebRezOncesiSaat",
                table: "Branches");

            migrationBuilder.DropColumn(
                name: "WebSira",
                table: "Branches");
        }
    }
}
