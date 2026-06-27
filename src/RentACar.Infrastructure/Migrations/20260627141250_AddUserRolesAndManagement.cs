using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RentACar.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddUserRolesAndManagement : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Mevcut kullanıcılar (varsa) Admin'e set edilir (login süreklilik). Yeni kullanıcılar
            // EF üzerinden her zaman rol göndererek eklenir; sunucu varsayılanı yalnız backfill içindir.
            migrationBuilder.AddColumn<int>(
                name: "Rol",
                table: "Users",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            // Users RLS: ENABLE (FORCE DEĞİL) → owner/migrator/seeder bypass eder (cross-tenant
            // seed); racar_app (owner değil) politikalara tabidir.
            // SELECT: tenant GUC AYARLIYSA yalnız kendi tenant'ı (kimliği doğrulanmış istekler —
            // raw SQL dahil cross-tenant OKUMA engellenir, parola hash'leri sızmaz). GUC BOŞSA
            // (yalnız login bootstrap: kimlik henüz yok) tüm satırlar görünür; o yolda sorgu zaten
            // (tenantId, kullanıcıAdı) ile dar okur. YAZMA daima tenant'a kısıtlı (WITH CHECK).
            migrationBuilder.Sql("ALTER TABLE \"Users\" ENABLE ROW LEVEL SECURITY;");
            migrationBuilder.Sql("DROP POLICY IF EXISTS users_select ON \"Users\";");
            migrationBuilder.Sql(
                "CREATE POLICY users_select ON \"Users\" FOR SELECT USING (" +
                "NULLIF(current_setting('app.tenant_id', true), '') IS NULL " +
                "OR \"TenantId\" = NULLIF(current_setting('app.tenant_id', true), '')::uuid);");
            migrationBuilder.Sql("DROP POLICY IF EXISTS users_insert ON \"Users\";");
            migrationBuilder.Sql(
                "CREATE POLICY users_insert ON \"Users\" FOR INSERT " +
                "WITH CHECK (\"TenantId\" = NULLIF(current_setting('app.tenant_id', true), '')::uuid);");
            migrationBuilder.Sql("DROP POLICY IF EXISTS users_update ON \"Users\";");
            migrationBuilder.Sql(
                "CREATE POLICY users_update ON \"Users\" FOR UPDATE " +
                "USING (\"TenantId\" = NULLIF(current_setting('app.tenant_id', true), '')::uuid) " +
                "WITH CHECK (\"TenantId\" = NULLIF(current_setting('app.tenant_id', true), '')::uuid);");
            // racar_app artık kullanıcı oluşturabilir/güncelleyebilir (tenant-kısıtlı). DELETE yok.
            migrationBuilder.Sql("GRANT SELECT, INSERT, UPDATE ON \"Users\" TO racar_app;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("REVOKE INSERT, UPDATE ON \"Users\" FROM racar_app;");
            migrationBuilder.Sql("DROP POLICY IF EXISTS users_update ON \"Users\";");
            migrationBuilder.Sql("DROP POLICY IF EXISTS users_insert ON \"Users\";");
            migrationBuilder.Sql("DROP POLICY IF EXISTS users_select ON \"Users\";");
            migrationBuilder.Sql("ALTER TABLE \"Users\" DISABLE ROW LEVEL SECURITY;");
            migrationBuilder.DropColumn(
                name: "Rol",
                table: "Users");
        }
    }
}
