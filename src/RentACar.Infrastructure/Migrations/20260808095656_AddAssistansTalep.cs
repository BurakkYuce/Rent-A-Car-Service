using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RentACar.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddAssistansTalep : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddUniqueConstraint(
                name: "AK_Rentals_TenantId_Id",
                table: "Rentals",
                columns: new[] { "TenantId", "Id" });

            migrationBuilder.CreateTable(
                name: "AssistansTalepleri",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    RentalId = table.Column<Guid>(type: "uuid", nullable: true),
                    Plaka = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    AdSoyad = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    CepTel = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    Zaman = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Mesaj = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    Sebep = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    YedekLastikMi = table.Column<bool>(type: "boolean", nullable: false),
                    AracHareketMi = table.Column<bool>(type: "boolean", nullable: false),
                    Kapandi = table.Column<bool>(type: "boolean", nullable: false),
                    Cozum = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AssistansTalepleri", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AssistansTalepleri_Rentals_TenantId_RentalId",
                        columns: x => new { x.TenantId, x.RentalId },
                        principalTable: "Rentals",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AssistansTalepleri_TenantId_Plaka",
                table: "AssistansTalepleri",
                columns: new[] { "TenantId", "Plaka" });

            migrationBuilder.CreateIndex(
                name: "IX_AssistansTalepleri_TenantId_RentalId",
                table: "AssistansTalepleri",
                columns: new[] { "TenantId", "RentalId" });

            migrationBuilder.CreateIndex(
                name: "IX_AssistansTalepleri_TenantId_Zaman",
                table: "AssistansTalepleri",
                columns: new[] { "TenantId", "Zaman" });

            // RLS — EF ÜRETMEZ, elle eklenir (CLAUDE.md §5). Mali belge değil → tam CRUD grant.
            migrationBuilder.Sql("ALTER TABLE \"AssistansTalepleri\" ENABLE ROW LEVEL SECURITY;");
            migrationBuilder.Sql("ALTER TABLE \"AssistansTalepleri\" FORCE ROW LEVEL SECURITY;");
            migrationBuilder.Sql("DROP POLICY IF EXISTS tenant_isolation ON \"AssistansTalepleri\";");
            migrationBuilder.Sql(
                "CREATE POLICY tenant_isolation ON \"AssistansTalepleri\" " +
                "USING (\"TenantId\" = NULLIF(current_setting('app.tenant_id', true), '')::uuid) " +
                "WITH CHECK (\"TenantId\" = NULLIF(current_setting('app.tenant_id', true), '')::uuid);");
            migrationBuilder.Sql("GRANT SELECT, INSERT, UPDATE, DELETE ON \"AssistansTalepleri\" TO racar_app;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AssistansTalepleri");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_Rentals_TenantId_Id",
                table: "Rentals");
        }
    }
}
