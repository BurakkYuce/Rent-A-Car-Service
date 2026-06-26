-- Kısıtlı runtime rolü: uygulama DAİMA bu rolle bağlanır (RLS bu role uygulanır).
-- racar_owner (POSTGRES_USER) migration/DDL içindir ve RLS'i bypass edebilir → app onu KULLANMAZ.
-- Bu script docker-entrypoint-initdb.d içinden racar_owner olarak çalışır.
-- Testcontainers fixture'ı da bu rolü (programatik olarak) oluşturur.

DO $$
BEGIN
   IF NOT EXISTS (SELECT FROM pg_roles WHERE rolname = 'racar_app') THEN
      CREATE ROLE racar_app LOGIN PASSWORD 'racar_app_pw'
         NOSUPERUSER NOCREATEDB NOCREATEROLE NOBYPASSRLS;
   END IF;
END
$$;

GRANT CONNECT ON DATABASE racar TO racar_app;
GRANT USAGE ON SCHEMA public TO racar_app;

-- racar_owner'ın bundan sonra oluşturacağı tablolara otomatik CRUD izni
-- (migration'lar ayrıca tablo bazında açık GRANT da yapar).
ALTER DEFAULT PRIVILEGES IN SCHEMA public
   GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO racar_app;
ALTER DEFAULT PRIVILEGES IN SCHEMA public
   GRANT USAGE, SELECT ON SEQUENCES TO racar_app;
