-- Blazor saat kayması: KURU ÇALIŞMA. Veriye dokunmaz: tek transaction, sonunda ROLLBACK.
-- ÜRETİLMİŞ DOSYA, elle düzenlemeyin. Kaynak: FixBlazorSaatKaymasi migration'ının SQL'i
-- (BlazorTimeShiftTests birebir kilitler; yeniden üretmek için RACAR_SAAT_BETIK_GUNCELLE=1 ile o testi koşun).
--
-- Kullanım (ayrıntı: docs/ops/blazor-saat-kaymasi.md):
--   psql "<bağlantı>" -v kesis='2026-10-20 03:00:00+03' -f scripts/saat-kaymasi/kuru-calistir.sql
--   isteğe bağlı:  -v sunucu_utc=true   FormParse.Date alanları da (Blazor sunucusu UTC saat dilimindeydiyse)
--                  -v kiraci=<uuid>     yalnız bir firma
--                  -v detay=1           kaydırılacak her alanı tek tek listele
-- Rol: süper kullanıcı (tablo kilidi almaz) ya da racar_owner (RLS NO FORCE parantezi bu transaction'da
-- kalır; ROLLBACK geri alır). Kesişi MUTLAKA ofsetle verin: ofsetsiz değer oturum saat dilimiyle yorumlanır.

\set ON_ERROR_STOP on
\if :{?kesis}
\else
  \echo 'kesis değişkeni gerekli, ör.: -v kesis=''2026-10-20 03:00:00+03'''
  \quit
\endif
\if :{?sunucu_utc}
\else
  \set sunucu_utc false
\endif
\if :{?kiraci}
\else
  \set kiraci ''
\endif

BEGIN;

CREATE TABLE IF NOT EXISTS "SaatKaymasiDuzeltmeleri" (
    "Id" bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    "TenantId" uuid NOT NULL,
    "Tablo" text NOT NULL,
    "Kolon" text NOT NULL,
    "KayitId" uuid NOT NULL,
    "Eski" timestamptz NOT NULL,
    "Yeni" timestamptz NOT NULL,
    "Gerekce" text NOT NULL,
    "Kesis" timestamptz NOT NULL,
    "SunucuUtc" boolean NOT NULL,
    "UygulamaZamani" timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT "UX_SaatKaymasiDuzeltmeleri" UNIQUE ("Tablo", "Kolon", "KayitId")
);
CREATE TABLE IF NOT EXISTS "SaatKaymasiCalismalari" (
    "Id" bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    "Kesis" timestamptz NOT NULL,
    "SunucuUtc" boolean NOT NULL,
    "Kiraci" uuid NULL,
    "Adet" integer NOT NULL,
    "Zaman" timestamptz NOT NULL DEFAULT now()
);
COMMENT ON TABLE "SaatKaymasiDuzeltmeleri" IS
    'Blazor saat kayması düzeltmesi defteri (migration FixBlazorSaatKaymasi): alan başına eski/yeni değer. Geri alma kaynağı; uygulama erişmez.';
COMMENT ON TABLE "SaatKaymasiCalismalari" IS
    'Blazor saat kayması düzeltmesi çalıştırmaları (kesiş anı). Farklı kesişle yeniden uygulama bu kayda bakılarak reddedilir.';
REVOKE ALL ON TABLE "SaatKaymasiDuzeltmeleri", "SaatKaymasiCalismalari" FROM PUBLIC;
REVOKE ALL ON TABLE "SaatKaymasiDuzeltmeleri", "SaatKaymasiCalismalari" FROM racar_app;

CREATE OR REPLACE FUNCTION rc_time_shift_unforce() RETURNS text[]
LANGUAGE plpgsql AS $fn$
DECLARE
    forced text[];
    t text;
BEGIN
    IF EXISTS (SELECT 1 FROM pg_roles r WHERE r.rolname = current_user AND (r.rolsuper OR r.rolbypassrls)) THEN
        RETURN ARRAY[]::text[];
    END IF;
    SELECT coalesce(array_agg(c.relname::text ORDER BY c.relname), ARRAY[]::text[]) INTO forced
    FROM pg_class c
    WHERE c.relnamespace = 'public'::regnamespace AND c.relkind = 'r' AND c.relforcerowsecurity
      AND c.relname = ANY (ARRAY['AuditLogs', 'Quotations', 'Reservations', 'Rentals', 'FaturaDonemleri',
                                 'VehicleKmLoglari', 'AssistansTalepleri', 'Baflar', 'SiteTalepleri', 'Invoices']);
    FOREACH t IN ARRAY forced LOOP
        EXECUTE format('ALTER TABLE %I NO FORCE ROW LEVEL SECURITY', t);
    END LOOP;
    RETURN forced;
END
$fn$;

CREATE OR REPLACE FUNCTION rc_time_shift_reforce(p_tables text[]) RETURNS void
LANGUAGE plpgsql AS $fn$
DECLARE
    t text;
BEGIN
    FOREACH t IN ARRAY p_tables LOOP
        EXECUTE format('ALTER TABLE %I FORCE ROW LEVEL SECURITY', t);
    END LOOP;
END
$fn$;

CREATE OR REPLACE FUNCTION rc_time_shift_classify(p_cutover timestamptz, p_server_was_utc boolean,
                                                  p_tenant uuid DEFAULT NULL)
RETURNS TABLE ("TenantId" uuid, "Tablo" text, "Kolon" text, "KayitId" uuid, "Eski" timestamptz,
               "Yeni" timestamptz, "Kaydir" boolean, "Gerekce" text)
LANGUAGE sql STABLE AS $fn$
WITH last_write AS (
    -- Alanın SON GERÇEK yazımı: Create ya da eski ≠ yeni Update (aynı anda ikisi varsa Update).
    SELECT DISTINCT ON (a."EntityName", a."EntityId", k.col)
           a."EntityName"::text AS tbl, a."EntityId"::text AS rid, k.col, a."TimestampUtc" AS at,
           a."Action" AS act, (a."NewValues" ->> k.col)::timestamptz AS val
    FROM "AuditLogs" a
    CROSS JOIN LATERAL unnest(ARRAY['BasTar', 'BitTar', 'GercekDonusTar', 'Zaman', 'DonusTarihi']) AS k(col)
    WHERE a."EntityName" IN ('Quotations', 'Reservations', 'Rentals', 'AssistansTalepleri', 'Baflar')
      AND (p_tenant IS NULL OR a."TenantId" = p_tenant)
      AND a."NewValues" ? k.col
      AND (a."Action" = 0 OR (a."OldValues" ->> k.col) IS DISTINCT FROM (a."NewValues" ->> k.col))
    ORDER BY a."EntityName", a."EntityId", k.col, a."TimestampUtc" DESC, a."Action" DESC
),
fields AS (
    SELECT t."TenantId", 'Quotations'::text AS tbl, 'BasTar'::text AS col, t."Id" AS id, t."BasTar" AS val,
           t."CreatedAtUtc" AS created, coalesce(t."UpdatedAtUtc", t."CreatedAtUtc") AS touched
    FROM "Quotations" t
    UNION ALL SELECT t."TenantId", 'Quotations', 'BitTar', t."Id", t."BitTar", t."CreatedAtUtc",
           coalesce(t."UpdatedAtUtc", t."CreatedAtUtc") FROM "Quotations" t
    UNION ALL SELECT t."TenantId", 'Reservations', 'BasTar', t."Id", t."BasTar", t."CreatedAtUtc",
           coalesce(t."UpdatedAtUtc", t."CreatedAtUtc") FROM "Reservations" t
    UNION ALL SELECT t."TenantId", 'Reservations', 'BitTar', t."Id", t."BitTar", t."CreatedAtUtc",
           coalesce(t."UpdatedAtUtc", t."CreatedAtUtc") FROM "Reservations" t
    UNION ALL SELECT t."TenantId", 'Rentals', 'BasTar', t."Id", t."BasTar", t."CreatedAtUtc",
           coalesce(t."UpdatedAtUtc", t."CreatedAtUtc") FROM "Rentals" t
    UNION ALL SELECT t."TenantId", 'Rentals', 'BitTar', t."Id", t."BitTar", t."CreatedAtUtc",
           coalesce(t."UpdatedAtUtc", t."CreatedAtUtc") FROM "Rentals" t
    UNION ALL SELECT t."TenantId", 'Rentals', 'GercekDonusTar', t."Id", t."GercekDonusTar", t."CreatedAtUtc",
           coalesce(t."UpdatedAtUtc", t."CreatedAtUtc") FROM "Rentals" t WHERE t."GercekDonusTar" IS NOT NULL
    UNION ALL SELECT t."TenantId", 'AssistansTalepleri', 'Zaman', t."Id", t."Zaman", t."CreatedAtUtc",
           coalesce(t."UpdatedAtUtc", t."CreatedAtUtc") FROM "AssistansTalepleri" t
    UNION ALL SELECT t."TenantId", 'Baflar', 'DonusTarihi', t."Id", t."DonusTarihi", t."CreatedAtUtc",
           coalesce(t."UpdatedAtUtc", t."CreatedAtUtc") FROM "Baflar" t WHERE t."DonusTarihi" IS NOT NULL
),
era AS (
    SELECT f."TenantId", f.tbl, f.col, f.id, f.val, w.act, w.rid IS NULL AS no_audit,
           CASE
               WHEN w.rid IS NOT NULL AND (w.val IS NULL OR abs(extract(epoch FROM w.val - f.val)) > 0.001)
                   THEN 'tutarsiz'
               WHEN date_trunc('minute', f.val) <> f.val THEN 'saniyeli'
               WHEN coalesce(w.at, CASE WHEN f.col IN ('GercekDonusTar', 'DonusTarihi') THEN f.touched
                                        ELSE f.created END) < p_cutover THEN 'blazor'
               ELSE 'sonra'
           END AS donem
    FROM fields f
    LEFT JOIN last_write w ON w.tbl = f.tbl AND w.rid = f.id::text AND w.col = f.col
    WHERE p_tenant IS NULL OR f."TenantId" = p_tenant
),
decided AS (
    SELECT e."TenantId", e.tbl, e.col, e.id, e.val, e.act, e.donem = 'blazor' AS era_shift,
           CASE e.donem
               WHEN 'blazor' THEN CASE WHEN e.no_audit THEN 'Blazor formu (denetim kaydı yok; satır zamanı)'
                                       ELSE 'Blazor formu' END
               WHEN 'sonra' THEN 'kesişten sonra yazılmış (SPA/API)'
               WHEN 'saniyeli' THEN 'saniye taşıyor: datetime-local formu üretmez (kod/API değeri; dokunulmaz)'
               ELSE 'tutarsız: denetimdeki son değer güncel değer değil (dokunulmaz)'
           END AS era_reason
    FROM era e
),
q AS (
    SELECT d."TenantId", d.tbl, d.col, d.id, d.val, d.era_shift AS shift, d.era_reason AS reason
    FROM decided d WHERE d.tbl = 'Quotations'
),
r AS (
    SELECT d."TenantId", d.tbl, d.col, d.id, d.val,
           CASE WHEN st.hit THEN false WHEN qq.id IS NOT NULL THEN qq.shift ELSE d.era_shift END AS shift,
           CASE WHEN st.hit THEN 'site talebinden (gün değeri; Blazor formu değil)'
                WHEN qq.id IS NOT NULL THEN 'tekliften kopya: ' || qq.reason
                ELSE d.era_reason END AS reason
    FROM decided d
    JOIN "Reservations" res ON res."Id" = d.id
    LEFT JOIN q qq ON (d.act IS NULL OR d.act = 0) AND qq.id = res."KaynakTeklifId"
                  AND qq.col = d.col AND qq.val = d.val
    LEFT JOIN LATERAL (
        SELECT true AS hit FROM "SiteTalepleri" s
        WHERE s."DonusenReservationId" = res."Id"
          AND (CASE d.col WHEN 'BasTar' THEN s."BasTar" ELSE s."BitTar" END) = d.val
        LIMIT 1) st ON (d.act IS NULL OR d.act = 0)
    WHERE d.tbl = 'Reservations'
),
k AS (
    SELECT d."TenantId", d.tbl, d.col, d.id, d.val,
           CASE WHEN rr.id IS NOT NULL THEN rr.shift ELSE d.era_shift END AS shift,
           CASE WHEN rr.id IS NOT NULL THEN 'rezervasyondan kopya: ' || rr.reason ELSE d.era_reason END AS reason
    FROM decided d
    JOIN "Rentals" ren ON ren."Id" = d.id
    LEFT JOIN r rr ON d.col IN ('BasTar', 'BitTar') AND (d.act IS NULL OR d.act = 0)
                  AND rr.id = ren."ReservationId" AND rr.col = d.col AND rr.val = d.val
    WHERE d.tbl = 'Rentals'
),
fd AS (
    -- Dönem planı: sınır kiranın bitişine eşitse bitişi, değilse başlangıcı izler (BasTar.AddMonths(i)).
    SELECT p."TenantId", 'FaturaDonemleri'::text AS tbl, x.col, p."Id" AS id, x.val, src.shift,
           'kiradan türetilmiş (' || src.col || '): ' || src.reason AS reason
    FROM "FaturaDonemleri" p
    JOIN "Rentals" ren ON ren."Id" = p."RentalId"
    CROSS JOIN LATERAL (VALUES ('DonemBas'::text, p."DonemBas"), ('DonemBit'::text, p."DonemBit")) AS x(col, val)
    JOIN k src ON src.id = ren."Id"
              AND src.col = CASE WHEN x.val = ren."BitTar" THEN 'BitTar' ELSE 'BasTar' END
),
km AS (
    SELECT DISTINCT ON (v."Id") v."TenantId", 'VehicleKmLoglari'::text AS tbl, 'Tarih'::text AS col,
           v."Id" AS id, v."Tarih" AS val, src.shift, 'kira dönüşünden türetilmiş: ' || src.reason AS reason
    FROM "VehicleKmLoglari" v
    JOIN "Rentals" ren ON ren."VehicleId" = v."VehicleId" AND ren."GercekDonusTar" = v."Tarih"
    JOIN k src ON src.id = ren."Id" AND src.col = 'GercekDonusTar'
    WHERE v."Kaynak" = 1
    ORDER BY v."Id", src.shift DESC
),
env AS (
    SELECT d."TenantId", d.tbl, d.col, d.id, d.val, d.era_shift AND p_server_was_utc AS shift,
           CASE WHEN d.era_shift AND NOT p_server_was_utc
                THEN 'sunucu saat dilimine bağlı (FormParse.Date): Blazor sunucusu UTC idiyse kayık; sunucu_utc=true ile dahil edilir'
                ELSE d.era_reason END AS reason
    FROM decided d WHERE d.tbl IN ('AssistansTalepleri', 'Baflar')
),
inv AS (
    SELECT i."TenantId", 'Invoices'::text AS tbl, 'Tarih'::text AS col, i."Id" AS id, i."Tarih" AS val,
           false AS shift,
           'değişmez tablo (fatura): yalnız rapor; manuel fatura tarihi Blazor datetime-local + FormParse.Date (sunucu UTC idiyse kayık; defter satırının EntryDateUtc değeri de aynı)' AS reason
    FROM "Invoices" i
    WHERE i."RentalId" IS NULL AND i."KaynakKiraId" IS NULL AND i."KaynakFaturaId" IS NULL AND NOT i."IadeMi"
      AND date_trunc('minute', i."Tarih") = i."Tarih"
      AND i."CreatedAtUtc" < p_cutover AND (p_tenant IS NULL OR i."TenantId" = p_tenant)
),
alls AS (
    SELECT * FROM q UNION ALL SELECT * FROM r UNION ALL SELECT * FROM k UNION ALL SELECT * FROM fd
    UNION ALL SELECT * FROM km UNION ALL SELECT * FROM env UNION ALL SELECT * FROM inv
)
SELECT a."TenantId", a.tbl, a.col, a.id, a.val,
       CASE WHEN a.shift AND z."Id" IS NULL THEN (a.val AT TIME ZONE 'UTC') AT TIME ZONE 'Europe/Istanbul'
            ELSE a.val END,
       a.shift AND z."Id" IS NULL,
       CASE WHEN z."Id" IS NOT NULL THEN 'zaten düzeltildi (defterde)' ELSE a.reason END
FROM alls a
LEFT JOIN "SaatKaymasiDuzeltmeleri" z ON z."Tablo" = a.tbl AND z."Kolon" = a.col AND z."KayitId" = a.id;
$fn$;

CREATE OR REPLACE FUNCTION rc_time_shift_conflicts(p_cutover timestamptz, p_server_was_utc boolean DEFAULT false,
                                                   p_tenant uuid DEFAULT NULL)
RETURNS TABLE ("TenantId" uuid, "VehicleId" uuid, "KiraA" uuid, "KiraB" uuid)
LANGUAGE sql STABLE AS $fn$
WITH c AS MATERIALIZED (
    SELECT x."KayitId", x."Kolon", x."Yeni"
    FROM rc_time_shift_classify(p_cutover, p_server_was_utc, p_tenant) x
    WHERE x."Tablo" = 'Rentals' AND x."Kaydir"
),
nr AS (
    SELECT ren."TenantId", ren."VehicleId", ren."Id",
           coalesce(cb."Yeni", ren."BasTar") AS bas, coalesce(ce."Yeni", ren."BitTar") AS bit
    FROM "Rentals" ren
    LEFT JOIN c cb ON cb."KayitId" = ren."Id" AND cb."Kolon" = 'BasTar'
    LEFT JOIN c ce ON ce."KayitId" = ren."Id" AND ce."Kolon" = 'BitTar'
    WHERE ren."Durum" = 0 AND (p_tenant IS NULL OR ren."TenantId" = p_tenant)
)
SELECT a."TenantId", a."VehicleId", a."Id", b."Id"
FROM nr a
JOIN nr b ON b."TenantId" = a."TenantId" AND b."VehicleId" = a."VehicleId" AND a."Id" < b."Id"
WHERE tstzrange(a.bas, a.bit) && tstzrange(b.bas, b.bit);
$fn$;

CREATE OR REPLACE FUNCTION rc_time_shift_apply(p_cutover timestamptz, p_server_was_utc boolean DEFAULT false,
                                               p_tenant uuid DEFAULT NULL)
RETURNS integer
LANGUAGE plpgsql AS $fn$
DECLARE
    forced text[];
    spec record;
    sets text;
    stmt text;
    rid uuid;
    conflicts integer;
    applied integer;
BEGIN
    IF p_cutover IS NULL THEN
        RAISE EXCEPTION 'Saat kayması: kesiş anı verilmeli.';
    END IF;
    IF EXISTS (SELECT 1 FROM "SaatKaymasiCalismalari" w
               WHERE (w."Kiraci" IS NULL OR p_tenant IS NULL OR w."Kiraci" = p_tenant) AND w."Kesis" <> p_cutover) THEN
        RAISE EXCEPTION 'Saat kayması daha önce FARKLI bir kesişle uygulandı (bkz. "SaatKaymasiCalismalari"); farklı kesiş SPA kayıtlarını kaydırabilir.';
    END IF;
    forced := rc_time_shift_unforce();
    SELECT count(*) INTO conflicts FROM rc_time_shift_conflicts(p_cutover, p_server_was_utc, p_tenant);
    IF conflicts > 0 THEN
        RAISE EXCEPTION 'Saat kayması uygulanamaz: kaydırma sonrası % açık kira çifti aynı araçta çakışıyor (rc_time_shift_conflicts).', conflicts;
    END IF;

    INSERT INTO "SaatKaymasiDuzeltmeleri" ("TenantId", "Tablo", "Kolon", "KayitId", "Eski", "Yeni", "Gerekce",
                                           "Kesis", "SunucuUtc")
    SELECT c."TenantId", c."Tablo", c."Kolon", c."KayitId", c."Eski", c."Yeni", c."Gerekce",
           p_cutover, p_server_was_utc
    FROM rc_time_shift_classify(p_cutover, p_server_was_utc, p_tenant) c
    WHERE c."Kaydir"
    ON CONFLICT ("Tablo", "Kolon", "KayitId") DO NOTHING;
    GET DIAGNOSTICS applied = ROW_COUNT;

    FOR spec IN SELECT * FROM (VALUES
        ('Quotations', ARRAY['BasTar', 'BitTar']),
        ('Reservations', ARRAY['BasTar', 'BitTar']),
        ('Rentals', ARRAY['BasTar', 'BitTar', 'GercekDonusTar']),
        ('FaturaDonemleri', ARRAY['DonemBas', 'DonemBit']),
        ('VehicleKmLoglari', ARRAY['Tarih']),
        ('AssistansTalepleri', ARRAY['Zaman']),
        ('Baflar', ARRAY['DonusTarihi'])) AS v(tbl, cols)
    LOOP
        SELECT string_agg(format(
            '%1$I = coalesce((SELECT d."Yeni" FROM "SaatKaymasiDuzeltmeleri" d WHERE d."Tablo" = %2$L '
            || 'AND d."Kolon" = %1$L AND d."KayitId" = t."Id" AND d."UygulamaZamani" = now() '
            || 'AND d."Eski" = t.%1$I), t.%1$I)', col, spec.tbl), ', ')
        INTO sets FROM unnest(spec.cols) AS col;
        stmt := format('UPDATE %I AS t SET %s WHERE t."Id" = $1', spec.tbl, sets);
        FOR rid IN EXECUTE format(
            'SELECT x."Id" FROM %I x WHERE x."Id" IN (SELECT d."KayitId" FROM "SaatKaymasiDuzeltmeleri" d '
            || 'WHERE d."Tablo" = %L AND d."UygulamaZamani" = now()) ORDER BY x.%I, x."Id"',
            spec.tbl, spec.tbl, spec.cols[1])
        LOOP
            EXECUTE stmt USING rid;
        END LOOP;
    END LOOP;

    INSERT INTO "SaatKaymasiCalismalari" ("Kesis", "SunucuUtc", "Kiraci", "Adet")
    VALUES (p_cutover, p_server_was_utc, p_tenant, applied);
    PERFORM rc_time_shift_reforce(forced);
    RETURN applied;
END
$fn$;

CREATE OR REPLACE FUNCTION rc_time_shift_revert(p_tenant uuid DEFAULT NULL) RETURNS integer
LANGUAGE plpgsql AS $fn$
DECLARE
    forced text[];
    spec record;
    sets text;
    matched text;
    stmt text;
    rid uuid;
    reverted integer := 0;
    n integer;
BEGIN
    forced := rc_time_shift_unforce();
    FOR spec IN SELECT * FROM (VALUES
        ('Quotations', ARRAY['BasTar', 'BitTar']),
        ('Reservations', ARRAY['BasTar', 'BitTar']),
        ('Rentals', ARRAY['BasTar', 'BitTar', 'GercekDonusTar']),
        ('FaturaDonemleri', ARRAY['DonemBas', 'DonemBit']),
        ('VehicleKmLoglari', ARRAY['Tarih']),
        ('AssistansTalepleri', ARRAY['Zaman']),
        ('Baflar', ARRAY['DonusTarihi'])) AS v(tbl, cols)
    LOOP
        SELECT string_agg(format(
                   '%1$I = coalesce((SELECT d."Eski" FROM "SaatKaymasiDuzeltmeleri" d WHERE d."Tablo" = %2$L '
                   || 'AND d."Kolon" = %1$L AND d."KayitId" = t."Id" AND d."Yeni" = t.%1$I), t.%1$I)', col, spec.tbl), ', '),
               string_agg(format('(d."Kolon" = %1$L AND x.%1$I = d."Eski")', col), ' OR ')
        INTO sets, matched FROM unnest(spec.cols) AS col;
        stmt := format('UPDATE %I AS t SET %s WHERE t."Id" = $1', spec.tbl, sets);
        FOR rid IN EXECUTE format(
            'SELECT x."Id" FROM %I x WHERE x."Id" IN (SELECT d."KayitId" FROM "SaatKaymasiDuzeltmeleri" d '
            || 'WHERE d."Tablo" = %L AND ($1 IS NULL OR d."TenantId" = $1)) ORDER BY x.%I DESC, x."Id" DESC',
            spec.tbl, spec.tbl, spec.cols[1]) USING p_tenant
        LOOP
            EXECUTE stmt USING rid;
        END LOOP;
        EXECUTE format('DELETE FROM "SaatKaymasiDuzeltmeleri" d USING %I x WHERE d."Tablo" = %L '
                       || 'AND x."Id" = d."KayitId" AND ($1 IS NULL OR d."TenantId" = $1) AND (%s)',
                       spec.tbl, spec.tbl, matched) USING p_tenant;
        GET DIAGNOSTICS n = ROW_COUNT;
        reverted := reverted + n;
        EXECUTE format('DELETE FROM "SaatKaymasiDuzeltmeleri" d WHERE d."Tablo" = %L '
                       || 'AND ($1 IS NULL OR d."TenantId" = $1) '
                       || 'AND NOT EXISTS (SELECT 1 FROM %I x WHERE x."Id" = d."KayitId")',
                       spec.tbl, spec.tbl) USING p_tenant;
    END LOOP;
    DELETE FROM "SaatKaymasiCalismalari" w
    WHERE (p_tenant IS NULL OR w."Kiraci" = p_tenant)
      AND NOT EXISTS (SELECT 1 FROM "SaatKaymasiDuzeltmeleri" d WHERE w."Kiraci" IS NULL OR d."TenantId" = w."Kiraci");
    PERFORM rc_time_shift_reforce(forced);
    RETURN reverted;
END
$fn$;

CREATE OR REPLACE FUNCTION rc_time_shift_report(p_cutover timestamptz, p_server_was_utc boolean DEFAULT false,
                                                p_tenant uuid DEFAULT NULL)
RETURNS TABLE ("Tablo" text, "Kolon" text, "Kaydir" boolean, "Gerekce" text, "Adet" bigint, "Kiraci" bigint,
               "EnErkenEski" timestamptz, "EnGecEski" timestamptz, "EnErkenYeni" timestamptz,
               "EnGecYeni" timestamptz)
LANGUAGE plpgsql AS $fn$
#variable_conflict use_column
DECLARE
    forced text[];
BEGIN
    forced := rc_time_shift_unforce();
    RETURN QUERY
    SELECT c."Tablo", c."Kolon", c."Kaydir", c."Gerekce", count(*), count(DISTINCT c."TenantId"),
           min(c."Eski"), max(c."Eski"), min(c."Yeni"), max(c."Yeni")
    FROM rc_time_shift_classify(p_cutover, p_server_was_utc, p_tenant) c
    GROUP BY 1, 2, 3, 4
    UNION ALL
    SELECT 'Rentals', 'Period', false,
           'ÇAKIŞMA: kaydırma sonrası aynı araçta açık kiralar çakışıyor; uygulama REDDEDİLİR, önce elle çözülmeli',
           count(*), count(DISTINCT x."TenantId"), NULL::timestamptz, NULL::timestamptz, NULL::timestamptz,
           NULL::timestamptz
    FROM rc_time_shift_conflicts(p_cutover, p_server_was_utc, p_tenant) x
    HAVING count(*) > 0
    ORDER BY 1, 2, 3 DESC, 4;
    PERFORM rc_time_shift_reforce(forced);
END
$fn$;

SELECT rc_time_shift_unforce() AS gecici_no_force;

\echo '== Özet: tablo / kolon / karar / gerekçe =='
SELECT * FROM rc_time_shift_report(:'kesis'::timestamptz, :'sunucu_utc'::boolean, nullif(:'kiraci', '')::uuid);

\echo '== Toplam (kaydırılacak) =='
SELECT count(*) FILTER (WHERE c."Kaydir") AS alan,
       count(DISTINCT (c."Tablo", c."KayitId")) FILTER (WHERE c."Kaydir") AS kayit,
       count(DISTINCT c."TenantId") FILTER (WHERE c."Kaydir") AS firma,
       min(c."Eski") FILTER (WHERE c."Kaydir") AS en_erken,
       max(c."Eski") FILTER (WHERE c."Kaydir") AS en_gec
FROM rc_time_shift_classify(:'kesis'::timestamptz, :'sunucu_utc'::boolean, nullif(:'kiraci', '')::uuid) c;

\echo '== Kaydırma sonrası çakışacak açık kiralar (boş olmalı; doluysa uygulama reddedilir) =='
SELECT * FROM rc_time_shift_conflicts(:'kesis'::timestamptz, :'sunucu_utc'::boolean, nullif(:'kiraci', '')::uuid);

\if :{?detay}
\echo '== Ayrıntı (kaydırılacak alanlar) =='
SELECT * FROM rc_time_shift_classify(:'kesis'::timestamptz, :'sunucu_utc'::boolean, nullif(:'kiraci', '')::uuid) c
WHERE c."Kaydir"
ORDER BY c."TenantId", c."Tablo", c."Kolon", c."Eski";
\endif

ROLLBACK;
