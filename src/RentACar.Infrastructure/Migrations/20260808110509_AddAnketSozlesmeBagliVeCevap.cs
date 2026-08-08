using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RentACar.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddAnketSozlesmeBagliVeCevap : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "AnketTuru",
                table: "Anketler",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CikisOfisi",
                table: "Anketler",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            // VARSAYILAN 1 (Yapildi) — EF 0 (Yapilmadi) üretmişti ve bu, MEVCUT tüm anketleri
            // "yapılmamış" gösterirdi. ADD COLUMN ... DEFAULT mevcut satırları DDL sırasında
            // doldurur (RLS'li UPDATE tuzağına takılmaz).
            migrationBuilder.AddColumn<int>(
                name: "Durum",
                table: "Anketler",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<Guid>(
                name: "RentalId",
                table: "Anketler",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddUniqueConstraint(
                name: "AK_Anketler_TenantId_Id",
                table: "Anketler",
                columns: new[] { "TenantId", "Id" });

            migrationBuilder.CreateTable(
                name: "AnketCevaplari",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    AnketId = table.Column<Guid>(type: "uuid", nullable: false),
                    SoruNo = table.Column<int>(type: "integer", nullable: false),
                    Soru = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    Cevap = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    Aciklama = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AnketCevaplari", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AnketCevaplari_Anketler_TenantId_AnketId",
                        columns: x => new { x.TenantId, x.AnketId },
                        principalTable: "Anketler",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Anketler_TenantId_RentalId",
                table: "Anketler",
                columns: new[] { "TenantId", "RentalId" });

            migrationBuilder.CreateIndex(
                name: "IX_AnketCevaplari_TenantId_AnketId_SoruNo",
                table: "AnketCevaplari",
                columns: new[] { "TenantId", "AnketId", "SoruNo" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Anketler_Rentals_TenantId_RentalId",
                table: "Anketler",
                columns: new[] { "TenantId", "RentalId" },
                principalTable: "Rentals",
                principalColumns: new[] { "TenantId", "Id" },
                onDelete: ReferentialAction.Restrict);

            // RLS — EF ÜRETMEZ, elle (CLAUDE.md §5). Mali belge değil → tam CRUD grant.
            migrationBuilder.Sql("ALTER TABLE \"AnketCevaplari\" ENABLE ROW LEVEL SECURITY;");
            migrationBuilder.Sql("ALTER TABLE \"AnketCevaplari\" FORCE ROW LEVEL SECURITY;");
            migrationBuilder.Sql("DROP POLICY IF EXISTS tenant_isolation ON \"AnketCevaplari\";");
            migrationBuilder.Sql(
                "CREATE POLICY tenant_isolation ON \"AnketCevaplari\" " +
                "USING (\"TenantId\" = NULLIF(current_setting('app.tenant_id', true), '')::uuid) " +
                "WITH CHECK (\"TenantId\" = NULLIF(current_setting('app.tenant_id', true), '')::uuid);");
            migrationBuilder.Sql("GRANT SELECT, INSERT, UPDATE, DELETE ON \"AnketCevaplari\" TO racar_app;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Anketler_Rentals_TenantId_RentalId",
                table: "Anketler");

            migrationBuilder.DropTable(
                name: "AnketCevaplari");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_Anketler_TenantId_Id",
                table: "Anketler");

            migrationBuilder.DropIndex(
                name: "IX_Anketler_TenantId_RentalId",
                table: "Anketler");

            migrationBuilder.DropColumn(
                name: "AnketTuru",
                table: "Anketler");

            migrationBuilder.DropColumn(
                name: "CikisOfisi",
                table: "Anketler");

            migrationBuilder.DropColumn(
                name: "Durum",
                table: "Anketler");

            migrationBuilder.DropColumn(
                name: "RentalId",
                table: "Anketler");
        }
    }
}
