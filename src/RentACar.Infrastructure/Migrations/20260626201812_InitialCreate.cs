using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RentACar.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AccountLedgerEntries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CariId = table.Column<Guid>(type: "uuid", nullable: false),
                    EntryDateUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Direction = table.Column<int>(type: "integer", nullable: false),
                    Description = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    Amount_Value = table.Column<decimal>(type: "numeric(19,4)", nullable: false),
                    Amount_Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    Amount_Rate = table.Column<decimal>(type: "numeric(19,6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AccountLedgerEntries", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AuditLogs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    EntityName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    EntityId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Action = table.Column<int>(type: "integer", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: true),
                    UserName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    TimestampUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    OldValues = table.Column<string>(type: "jsonb", nullable: true),
                    NewValues = table.Column<string>(type: "jsonb", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuditLogs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Tenants",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Tenants", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Users",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    PasswordHash = table.Column<string>(type: "text", nullable: false),
                    DisplayName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Users", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Vehicles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Plaka = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Marka = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    Grup = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    Sube = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    Durum = table.Column<int>(type: "integer", nullable: false),
                    Km = table.Column<int>(type: "integer", nullable: false),
                    Yakit = table.Column<int>(type: "integer", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Vehicles", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_TenantId_EntityName_EntityId",
                table: "AuditLogs",
                columns: new[] { "TenantId", "EntityName", "EntityId" });

            migrationBuilder.CreateIndex(
                name: "IX_Tenants_Code",
                table: "Tenants",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Users_TenantId_UserName",
                table: "Users",
                columns: new[] { "TenantId", "UserName" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Vehicles_TenantId_Plaka",
                table: "Vehicles",
                columns: new[] { "TenantId", "Plaka" },
                unique: true);

            // ---------------------------------------------------------------
            // Row-Level Security (tenant izolasyonunun ASIL sınırı).
            // Tenant-owned tablolarda RLS + FORCE: app racar_app rolüyle bağlanır,
            // policy app.tenant_id GUC'una göre filtreler. GUC boş/yok ise
            // NULLIF(...,'')::uuid -> NULL -> hiçbir satır eşleşmez (default-deny).
            // FORCE: tablo sahibi (racar_owner) bağlansa bile RLS uygulanır.
            // GUC, bağlantı başına TenantConnectionInterceptor tarafından set edilir.
            // ---------------------------------------------------------------
            foreach (var table in new[] { "Vehicles", "AuditLogs", "AccountLedgerEntries" })
            {
                migrationBuilder.Sql($"ALTER TABLE \"{table}\" ENABLE ROW LEVEL SECURITY;");
                migrationBuilder.Sql($"ALTER TABLE \"{table}\" FORCE ROW LEVEL SECURITY;");
                migrationBuilder.Sql($"DROP POLICY IF EXISTS tenant_isolation ON \"{table}\";");
                migrationBuilder.Sql(
                    $"CREATE POLICY tenant_isolation ON \"{table}\" " +
                    "USING (\"TenantId\" = NULLIF(current_setting('app.tenant_id', true), '')::uuid) " +
                    "WITH CHECK (\"TenantId\" = NULLIF(current_setting('app.tenant_id', true), '')::uuid);");
            }

            // Runtime rolü (racar_app) izinleri. Rol, migration'dan ÖNCE
            // (init script / test fixture tarafından) oluşturulmuş olmalıdır.
            migrationBuilder.Sql("GRANT USAGE ON SCHEMA public TO racar_app;");
            foreach (var table in new[] { "Vehicles", "AuditLogs", "AccountLedgerEntries" })
                migrationBuilder.Sql($"GRANT SELECT, INSERT, UPDATE, DELETE ON \"{table}\" TO racar_app;");
            // Platform tabloları: app yalnız okur (login). RLS yok.
            migrationBuilder.Sql("GRANT SELECT ON \"Tenants\" TO racar_app;");
            migrationBuilder.Sql("GRANT SELECT ON \"Users\" TO racar_app;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AccountLedgerEntries");

            migrationBuilder.DropTable(
                name: "AuditLogs");

            migrationBuilder.DropTable(
                name: "Tenants");

            migrationBuilder.DropTable(
                name: "Users");

            migrationBuilder.DropTable(
                name: "Vehicles");
        }
    }
}
