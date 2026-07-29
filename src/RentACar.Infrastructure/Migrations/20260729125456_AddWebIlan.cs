using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RentACar.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddWebIlan : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "WebIlanId",
                table: "Vehicles",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "WebIlanlar",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Baslik = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    EslesmeAnahtari = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    Sira = table.Column<int>(type: "integer", nullable: true),
                    Durum = table.Column<int>(type: "integer", nullable: false),
                    GunlukFiyat = table.Column<decimal>(type: "numeric(19,4)", nullable: false),
                    HaftalikToplam = table.Column<decimal>(type: "numeric(19,4)", nullable: true),
                    AylikToplam = table.Column<decimal>(type: "numeric(19,4)", nullable: true),
                    KdvDahil = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WebIlanlar", x => x.Id);
                    table.UniqueConstraint("AK_WebIlanlar_TenantId_Id", x => new { x.TenantId, x.Id });
                });

            migrationBuilder.CreateTable(
                name: "WebIlanOzellikler",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    IlanId = table.Column<Guid>(type: "uuid", nullable: false),
                    Etiket = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    Deger = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    Sira = table.Column<int>(type: "integer", nullable: false),
                    Gorunur = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WebIlanOzellikler", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WebIlanOzellikler_WebIlanlar_TenantId_IlanId",
                        columns: x => new { x.TenantId, x.IlanId },
                        principalTable: "WebIlanlar",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Vehicles_TenantId_WebIlanId",
                table: "Vehicles",
                columns: new[] { "TenantId", "WebIlanId" });

            migrationBuilder.CreateIndex(
                name: "IX_WebIlanlar_TenantId_Durum_Sira",
                table: "WebIlanlar",
                columns: new[] { "TenantId", "Durum", "Sira" });

            migrationBuilder.CreateIndex(
                name: "IX_WebIlanlar_TenantId_EslesmeAnahtari",
                table: "WebIlanlar",
                columns: new[] { "TenantId", "EslesmeAnahtari" });

            migrationBuilder.CreateIndex(
                name: "IX_WebIlanOzellikler_TenantId_IlanId_Sira",
                table: "WebIlanOzellikler",
                columns: new[] { "TenantId", "IlanId", "Sira" });

            migrationBuilder.AddForeignKey(
                name: "FK_Vehicles_WebIlanlar_TenantId_WebIlanId",
                table: "Vehicles",
                columns: new[] { "TenantId", "WebIlanId" },
                principalTable: "WebIlanlar",
                principalColumns: new[] { "TenantId", "Id" },
                onDelete: ReferentialAction.SetNull);

            // ---- RLS (EF ÜRETMEZ — elle; CLAUDE.md §5 reçetesi). Mali belge DEĞİL → tam CRUD grant.
            // İki tablo için de ayrı ayrı: ENABLE + FORCE + tenant_isolation + GRANT. ----
            foreach (var tablo in new[] { "WebIlanlar", "WebIlanOzellikler" })
            {
                migrationBuilder.Sql($"ALTER TABLE \"{tablo}\" ENABLE ROW LEVEL SECURITY;");
                migrationBuilder.Sql($"ALTER TABLE \"{tablo}\" FORCE ROW LEVEL SECURITY;");
                migrationBuilder.Sql($"DROP POLICY IF EXISTS tenant_isolation ON \"{tablo}\";");
                migrationBuilder.Sql(
                    $"CREATE POLICY tenant_isolation ON \"{tablo}\" " +
                    "USING (\"TenantId\" = NULLIF(current_setting('app.tenant_id', true), '')::uuid) " +
                    "WITH CHECK (\"TenantId\" = NULLIF(current_setting('app.tenant_id', true), '')::uuid);");
                migrationBuilder.Sql($"GRANT SELECT, INSERT, UPDATE, DELETE ON \"{tablo}\" TO racar_app;");
            }
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Vehicles_WebIlanlar_TenantId_WebIlanId",
                table: "Vehicles");

            migrationBuilder.DropTable(
                name: "WebIlanOzellikler");

            migrationBuilder.DropTable(
                name: "WebIlanlar");

            migrationBuilder.DropIndex(
                name: "IX_Vehicles_TenantId_WebIlanId",
                table: "Vehicles");

            migrationBuilder.DropColumn(
                name: "WebIlanId",
                table: "Vehicles");
        }
    }
}
