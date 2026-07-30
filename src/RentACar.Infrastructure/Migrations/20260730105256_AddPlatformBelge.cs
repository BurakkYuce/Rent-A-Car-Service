using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RentACar.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPlatformBelge : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PlatformBelgeler",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Baslik = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Aciklama = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    DosyaAdi = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Bytes = table.Column<byte[]>(type: "bytea", nullable: false),
                    Boyut = table.Column<long>(type: "bigint", nullable: false),
                    Surum = table.Column<int>(type: "integer", nullable: false),
                    Durum = table.Column<int>(type: "integer", nullable: false),
                    YalnizYoneticiler = table.Column<bool>(type: "boolean", nullable: false),
                    YukleyenOperator = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    GuncellemeUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlatformBelgeler", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PlatformBelgeHedefler",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BelgeId = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlatformBelgeHedefler", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PlatformBelgeHedefler_PlatformBelgeler_BelgeId",
                        column: x => x.BelgeId,
                        principalTable: "PlatformBelgeler",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PlatformBelgeHedefler_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PlatformBelgeHedefler_BelgeId_TenantId",
                table: "PlatformBelgeHedefler",
                columns: new[] { "BelgeId", "TenantId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PlatformBelgeHedefler_TenantId",
                table: "PlatformBelgeHedefler",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_PlatformBelgeler_Durum_GuncellemeUtc",
                table: "PlatformBelgeler",
                columns: new[] { "Durum", "GuncellemeUtc" });

            // ---- PLATFORM tabloları: RLS **YOK** (Tenants/Users/TenantDomains gibi). ----
            // İzolasyon RLS'e yıkılamaz → uygulama katmanında (PlatformBelgeRepository.Gorunur, dört
            // koşul). Bu bilinçli: belge kitlesi tenant-ötesi bir kavram ("global belge" diye bir şey
            // var), TenantId kolonu olan bir tabloyla modellenemez.
            //
            // Grant: racar_app burada yalnız OKUR (tenant tarafı listeler/indirir); YAZMA platform
            // konsolundan owner bağlantısıyla yapılır (PlatformAdminService.OwnerDb — PR-12'de
            // kanıtlanmış runtime yolu). TenantDomains'te tam-CRUD grant vardı çünkü "Sitemi Aç"
            // self-servisi racar_app ile YAZIYOR; burada tenant-taraflı yazma YOK.
            migrationBuilder.Sql("GRANT SELECT ON \"PlatformBelgeler\" TO racar_app;");
            migrationBuilder.Sql("GRANT SELECT ON \"PlatformBelgeHedefler\" TO racar_app;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PlatformBelgeHedefler");

            migrationBuilder.DropTable(
                name: "PlatformBelgeler");
        }
    }
}
