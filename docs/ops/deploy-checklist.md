# Go-Live Deploy Checklist — RentACar

> Bu belge, sıfırdan bir üretim sunucusuna RentACar kurulumunun adım-adım rehberidir. Sıra önemlidir.
> Güvenlik notu: Uygulama **yanlış yapılandırılmışsa açılışta REDDEDER** (Jwt/Pii/DP/Platform/ConnectionStrings
> guard'ları) — bu bir özelliktir, arka kapı/sessiz-fallback yoktur. Aşağıdaki her ZORUNLU madde eksikse
> `InvalidOperationException` ile başlamaz.

---

## 0. Sunucu (VPS) — boyutlandırma
Tek makinede (uygulama + PostgreSQL + reverse-proxy) başlangıç için yeterli:
- **CPU:** 2–4 vCPU · **RAM:** 4–8 GB · **Disk:** 40–80 GB SSD (NVMe tercih).
- Öneri: **Hetzner CPX21/CPX31** (iyi reputation, AB veri-merkezi/KVKK, ucuz). Contabo'dan kaçınıldı (reputation).
- OS: **Ubuntu 24.04 LTS**.
- Yük büyürse: PostgreSQL'i ayrı makineye al; uygulamayı yatay ölçekle (stateless — DP key-ring paylaşımlı volume şartıyla).

## 1. Önkoşul paketler
```bash
sudo apt update && sudo apt install -y postgresql postgresql-contrib caddy
# .NET 10 ASP.NET Core runtime (SDK gerekmez — publish edilmiş çıktı çalışır):
# Microsoft paket deposunu ekle, sonra:
sudo apt install -y aspnetcore-runtime-10.0
```
PostgreSQL 15+ olmalı (Ubuntu 24.04 → 16 gelir, uygun).

## 2. PostgreSQL — roller + veritabanı (TEK SEFERLİK, kritik)
İki rol modeli (CLAUDE.md §4): `racar_owner` (DDL/migration, RLS bypass edebilir — uygulama KULLANMAZ),
`racar_app` (runtime, **NOSUPERUSER NOBYPASSRLS** — uygulama DAİMA bununla bağlanır → RLS zorunlu).
```bash
sudo -u postgres psql -f scripts/db-init-roles.sql   # racar_owner/racar_app + db racar + grant (idempotent)
```
- Rol şifrelerini **güçlü** yap (script'teki dev şifrelerini DEĞİŞTİR: `racar_owner_pw`/`racar_app_pw`).
- `racar_app`'in `NOBYPASSRLS` olduğunu DOĞRULA: `\du racar_app` → "Bypass RLS" YOK olmalı. (Tenant izolasyonunun temeli.)
- Migration + seed **açılışta otomatik** çalışır (Migrator bağlantısıyla); elle migration gerekmez.

## 3. ZORUNLU config / secrets (guard'lı — eksikse açılış reddeder)
Secret'ları **appsettings.json'a KOYMA** (repo'ya sızar). Env değişkeni veya `appsettings.Production.json`
(git-ignore'lu) veya secret manager kullan. ASP.NET Core env-var eşlemesi: `Pii__HmacKey`, `ConnectionStrings__Default` (çift alt-çizgi).

| Anahtar | Nerede | Nasıl üret | Not |
|---|---|---|---|
| `ConnectionStrings:Default` | Web + Api | — | **racar_app** ile (NOBYPASSRLS). Eksik → red. |
| `ConnectionStrings:Migrator` | Web | — | **racar_owner** ile (yalnız migration). Eksik → red. |
| `Pii:HmacKey` | Web + Api | `openssl rand -base64 48` | PII blind-index (TC arama). **Kaybolursa TC araması bozulur** — YEDEKLE. |
| `RACAR_DP_KEYS` (env var) | Web + Api | kalıcı dizin yolu, ör. `/var/lib/racar/dp-keys` | DataProtection key-ring KALICI dizini. **Geçici FS'te redeploy = tüm *Enc PII kalıcı çözülemez.** |
| `Jwt:Key` | **yalnız Api** | `openssl rand -base64 48` (≥32 bayt, özgün) | API token imzalama. Dev/zayıf anahtar prod'da reddedilir. |
| `Platform:AdminUser` | Web | — | Platform süper-admin (/platform konsolu) kullanıcı adı. |
| `Platform:AdminPasswordHash` | Web | app'in hasher'ı (`PlatformCredentials.HashPassword`) | Düz şifre DEĞİL, hash. Üretmek için yardım iste (ya da geçici snippet). |

Örnek `/etc/racar/racar-web.env` (systemd `EnvironmentFile`):
```
ASPNETCORE_ENVIRONMENT=Production
ConnectionStrings__Default=Host=localhost;Port=5432;Database=racar;Username=racar_app;Password=GÜÇLÜ_APP_PW
ConnectionStrings__Migrator=Host=localhost;Port=5432;Database=racar;Username=racar_owner;Password=GÜÇLÜ_OWNER_PW
Pii__HmacKey=<openssl rand -base64 48>
RACAR_DP_KEYS=/var/lib/racar/dp-keys
Platform__AdminUser=<kullanıcı>
Platform__AdminPasswordHash=<hash>
```
> `ASPNETCORE_ENVIRONMENT=Production` ŞART. Development olursa guard'lar atlanır **ve antiforgery (CSRF) kapanır**
> (Development-dışı her ortamda CSRF açık — Staging dahil). Staging kurarsan da Production-benzeri secret'ları ver.

```bash
sudo mkdir -p /var/lib/racar/dp-keys && sudo chown <appuser> /var/lib/racar/dp-keys
```

## 4. Uygulama — publish + systemd
```bash
dotnet publish src/RentACar.Web -c Release -o /opt/racar/web
# (API'yi de yayınlıyorsan: src/RentACar.Api -c Release -o /opt/racar/api, ayrı port + Jwt:Key)
```
systemd unit (`/etc/systemd/system/racar-web.service`): `EnvironmentFile=/etc/racar/racar-web.env`,
`ExecStart=/usr/bin/dotnet /opt/racar/web/RentACar.Web.dll`, `Environment=ASPNETCORE_URLS=http://127.0.0.1:5220`,
`User=<appuser>`, `Restart=always`. Sonra `systemctl enable --now racar-web`.

**Kaynak-tüketim sertleştirme (kod değil — `Kestrel` section'ından bind):** `appsettings.Production.json`'a
`Kestrel:Limits:MaxConcurrentConnections`, `MaxRequestBodySize`, `RequestHeadersTimeout` ekleyerek istek uçlarını
sınırla (rate-limit yalnız login'de olduğundan bu, veri uçlarına kaba bir DoS tamponu sağlar). Gerçek TLS/HTTP kabaca
Caddy'de (§5 `request_body max_size`); Kestrel iç-ağda dinlediği için bu ikisi tamamlayıcı.

## 4.1 Uygulama — `RentACar.PublicSite` (halka açık site, PR-1..5)
```bash
dotnet publish src/RentACar.PublicSite -c Release -o /opt/racar/publicsite
```
systemd unit (`/etc/systemd/system/racar-publicsite.service`): `ExecStart=/usr/bin/dotnet
/opt/racar/publicsite/RentACar.PublicSite.dll`, `Environment=ASPNETCORE_URLS=http://127.0.0.1:5230`,
`User=<appuser>`, `Restart=always`. `systemctl enable --now racar-publicsite`.

**KRİTİK — port 5230 YALNIZ `127.0.0.1`'e bind (dışarı AÇILMAZ):** PR-5'ten itibaren tenant çözümleme VE
`PendingVerification→Active` otomatik-flip (özel domain doğrulaması) TAMAMEN gelen isteğin Host header'ına
güveniyor. Port dışarıya açık olsaydı biri Caddy'yi tamamen atlayıp `curl -H "Host: kurban-domaini.com"
http://sunucu-ip:5230/` ile bir domain'in doğrulama durumunu prob edebilir/tetikleyebilirdi — `ASPNETCORE_URLS`
yukarıdaki gibi `127.0.0.1:5230` (0.0.0.0 DEĞİL) olduğu sürece bu erişilemez, ama deploy sonrası
`sudo ss -tlnp | grep 5230` ile DOĞRULA.

## 5. Reverse proxy (Caddy) — HTTPS + gerçek istemci IP
Caddy otomatik Let's Encrypt TLS verir. **Güvenlik başlıkları + CSP + cookie sertleştirme + HSTS artık UYGULAMADA**
(Program.cs middleware + AddCookie + AddHsts — defense-in-depth, proxy'den bağımsız). ERP/Api için Caddyfile sade:
```
rentpro.example.com {
    reverse_proxy 127.0.0.1:5220
    request_body { max_size 2MB }   # kaba gövde limiti (DoS; plugin gerekmez) — Kestrel:Limits ile tamamlayıcı
}
```

## 5.1 Reverse proxy (Caddy) — `RentACar.PublicSite` için `on_demand_tls` (PR-5, İKİ AYRI blok ŞART)
**Neden ayrı blok:** "Sitemi Aç" (Ayarlar ekranı) bir tenant için `{kod}.rentpro.com` alt-domainini ANINDA bir
DB satırı olarak yazar — ama Caddy'nin YUKARIDAKİ statik bloğu yalnız İÇİNDE YAZILI hostname'e (`rentpro.example.com`)
sertifika çıkarır. Yeni bir alt-domain/özel-domain için Caddy'nin cert'i YOKTUR — `on_demand_tls` (istek anında
sertifika iste) bu boşluğu kapatır. **TÜM host'ları TEK bloğa (`https://` catch-all + `on_demand`) toplamak
YANLIŞ** — ask-endpoint yalnız `TenantDomains`'i tanır, ERP'nin (`rentpro.example.com`) kendi host'u orada YOK;
tek blok olsaydı ERP'nin isteği de ask'a düşer, 404 alır, Caddy ERP'nin sertifikasını ÇIKARAMAZ/YENİLEYEMEZ. Bu
yüzden ERP/Api §5'teki statik bloğunda AYNEN kalır, PublicSite AYRI bir catch-all blokta:
```
# Global options — TEK YERDE
{
    on_demand_tls {
        ask http://127.0.0.1:5230/dogrulama/ask
    }
}

# rentpro.example.com { ... }  ← §5'teki ERP bloğu DEĞİŞMEDEN burada kalır

# Public Site — subdomain + özel domain, on_demand YALNIZ BURADA devreye girer
https:// {
    tls {
        on_demand
    }
    reverse_proxy 127.0.0.1:5230
    request_body { max_size 2MB }
}
```

**Söylenmemiş ön koşullar (deploy ÖNCESİ doğrula):**
- **Wildcard DNS**: `*.rentpro.com` için sunucunun IP'sine bir **A kaydı** DNS sağlayıcısında ÖNCEDEN kurulu
  olmalı (cert'ten önce çözünürlük şart) — yoksa hiçbir yeni tenant subdomain'i asla açılmaz.
- **Let's Encrypt kotası**: kayıtlı-domain başına haftalık ~50 yeni sertifika sınırı (yenilemeler MUAF).
  `on_demand_tls`'te her yeni tenant/subdomain AYRI bir sertifika demek — haftada 50'den fazla yeni tenant
  açılışı bu kotayı doldurabilir. Şu an gerçekçi değil, ama BİLİNÇLİ kabul edilen bir sınır (izlenmeli).
- **İlk ziyaret gecikmesi**: yeni bir subdomain'e ilk HTTPS isteği ACME handshake'i (birkaç saniye) bekler.
  Uygulama "Sitemi Aç" akışının SONUNDA yeni subdomain'e kendi kendine tek bir "ısınma" isteği atarak bu
  gecikmeyi admin görmeden eritir.
- **Kabul edilen davranış**: ask-endpoint sertifika YENİLEMELERİNDE de sorulur — bir tenant `PublicSiteEnabled`'ı
  kapatsa bile `TenantDomains` satırı DURDUKÇA sertifika yenilenmeye devam eder (site kapalıyken bile domain'in
  "bizim" olduğu gerçeği değişmiyor — bilinçli kabul, ayrı bir temizlik işi DEĞİL).

**Rollback:** Sorun çıkarsa yalnız `https:// { ... }` catch-all bloğu Caddyfile'dan kaldırılıp `caddy reload`
yapılır — public site geçici olarak erişilemez hale gelir ama ERP/Api (AYRI blok sayesinde) ETKİLENMEZ.

**⚠ GELECEK-GUARDRAIL — HTML cache eklenirse `Vary: Host` ZORUNLU (PR-9 notu):** Bugün hiçbir katmanda
(Caddy dahil) HTML cache'i YOK. Ama ileride public site'a sayfa cache'i eklenirse (`cache` direktifi,
CDN, reverse-proxy cache) **`Vary: Host` başlığı ŞART**: aynı yol (`/`, `/musaitlik`, `/blog`) HER TENANT
için FARKLI içerik döndürür (host→tenant çözümlemesi) — Vary olmadan A firmasının ana sayfası B firmasının
ziyaretçisine servis edilir. Bu, çok-kiracılı bir sitede sessiz ve ciddi bir veri sızıntısıdır.
Statik varlıklar (`/foto/...`, `/blog-kapak/...`, `site.css`) bu kuraldan MUAF değildir: foto uçları
tenant-özel bytea döndürür (yalnız `site.css`/`app.css` gerçekten tenant-bağımsızdır).
- **CSP (uygulamada, katı):** `script-src 'self'` — `'unsafe-inline'` YOK, hash YOK. 52 inline event handler harici
  JS'e taşındı (`data-confirm`/`data-select-all` → `wwwroot/js/rc-ui.js`). Blazor'ın `<ImportMap>` inline script'i
  App.razor'dan **kaldırıldı** (tam statik SSR'de gereksizdi ve fingerprint'i her asset/CSS değişiminde dönüp CSP
  hash'ini bozuyordu) → artık hiç inline script yok, CSP asset değişimlerinde kırılmaz. `style-src 'unsafe-inline'`
  bilinçli (inline style attr'ları; XSS riski script'e göre düşük). `frame-ancestors 'self'` + `X-Frame-Options SAMEORIGIN` → PDF-yazdır iframe çalışır.
- **Cookie Secure (KRİTİK — sessiz başarısızlık noktası):** uygulama `UseForwardedHeaders` ile **KnownProxies=loopback**
  varsayar. Caddy AYNI makinede (127.0.0.1) ise `X-Forwarded-Proto` okunur → cookie Secure + rate-limiter gerçek IP'yi görür.
  Caddy AYRI makine/container ise header DÜŞER → **şema http kalır → cookie Secure OLMAZ.** O durumda
  `ASPNETCORE_FORWARDEDHEADERS_ENABLED=true` (env; KnownProxies/Networks temizler) — **yalnız Kestrel dışarı kapalı/iç-ağdaysa güvenli**
  (aksi halde sahte forwarded header'a güvenilir). DevTools kontrolü (§9) sonucu yakalar.
- `sudo systemctl reload caddy`.

## 6. Opsiyonel entegrasyonlar (config VARSA aktif, yoksa no-op stub)
- **WhatsApp/SMS (Twilio):** `Twilio:AccountSid` + `Twilio:AuthToken` + gönderen numara verilirse gerçek gönderici
  devreye girer; yoksa stub no-op. (SMS/HGS/POS/e-Fatura stub — kimlik gelince bağlanır.)
- **TCMB kur:** otomatik (config'siz çalışır; günlük çeker).

## 6.1 Gözlemlenebilirlik (opsiyonel, config-gated) — bkz. [observability.md](observability.md)
Telemetri (metrik/trace/log OTLP push) **yalnız `OTEL_EXPORTER_OTLP_ENDPOINT` set ise** akar; UNSET ise hiç
exporter kurulmaz (dev/CI/telemetri istemeyen prod etkilenmez). Backend yığını `ops/observability/` altında
tek docker-compose (OTel Collector + Prometheus + Loki + Tempo + Grafana).
- Kurmak istiyorsan: `cd ops/observability && cp .env.example .env` (GRAFANA_ADMIN_PASSWORD + ALERT_TOKEN doldur) →
  `docker compose -f docker-compose.observability.yml up -d`; sonra uygulamayı `OTEL_EXPORTER_OTLP_ENDPOINT=http://127.0.0.1:4317`
  ve `Observability__AlertToken=<.env ALERT_TOKEN>` env'leriyle başlat (systemd EnvironmentFile'a ekle).
- **Güvenlik (ZORUNLU):** Prometheus/Loki/Tempo/Collector yalnız `127.0.0.1`/iç-docker ağına bağlanır (host portu YOK).
  DIŞARI **yalnız Grafana** açılır → Caddy + auth arkasına al; admin şifresi env'den, anonim erişim/kayıt KAPALI.
- **Alarm ucu:** `Observability:AlertToken` set ise `/internal/alert` açılır (Bearer/`X-Alert-Token` anahtarlı,
  makine-uç); anahtar yoksa uç 404 (kapalı). `.env`'i **commit ETME** (git-ignore'lu).
- **Grafana'sız watchdog:** `Twilio:AlertPhone` + `Twilio:Templates:ops_alert` set ise `OpsWatchdogJob` aktif —
  Grafana çökse bile TCMB kur bayatlığı + tekrarlayan job hatasını doğrudan WhatsApp'a bildirir (config yoksa PASİF).

## 7. İlk açılış doğrulaması
1. `systemctl status racar-web` → çalışıyor (guard reddi varsa log'da net mesaj: hangi anahtar eksik).
2. `curl -fsS http://127.0.0.1:5220/health` → 200.
3. HTTPS'ten giriş: firma/kullanıcı/şifre ile login → panel açılıyor.
4. **RLS duman testi:** iki farklı tenant kullanıcısıyla gir; birinin verisi diğerinde GÖRÜNMEMELİ.
5. `/platform` → Platform:AdminUser ile süper-admin konsolu açılıyor (tenant list/aç-kapa).
6. Bir tahsilat yap, formu iki kez hızlı gönder (çift-tık) → tek kayıt (idempotency).

## 8. Yedekleme (go-live'dan ÖNCE kur) — bkz. [yedekleme.md](yedekleme.md)
Üçü birden yedeklenmeli (biri eksikse veri kurtarılamaz):
1. **PostgreSQL** (`pg_dump` günlük + WAL/PITR tercih) — asıl veri.
2. **`Pii:HmacKey`** — kaybolursa TC blind-index araması çalışmaz.
3. **`RACAR_DP_KEYS` key-ring dizini** — kaybolursa şifreli PII (`*Enc`: TC/ehliyet/maaş) kalıcı çözülemez.
Yedekleri şifreli + sunucu-dışı sakla. Restore tatbikatı yap (yedeğin gerçekten açıldığını doğrula).

## 9. Go-live sonrası güvenlik kontrolü (hızlı)
- [ ] `racar_app` NOBYPASSRLS (`\du`).
- [ ] **Env teyidi (EN KRİTİK):** `ASPNETCORE_ENVIRONMENT=Production` — antiforgery + HSTS ikisi de `!IsDevelopment()`'e
      kapılı, env yanlışsa **ikisi birden sessizce kapanır.** Kanıt testi: prod URL'ine **token'sız POST** at →
      **400** dönmeli (`curl -si -X POST https://<domain>/kiralar/create -d x=1` → 400 Bad Request). 200/302 → env yanlış.
- [ ] **Güvenlik başlıkları (uygulamadan):** `curl -sI https://<domain>/` → `content-security-policy` (script-src 'self' …),
      `x-content-type-options: nosniff`, `x-frame-options: SAMEORIGIN`, `referrer-policy`, `permissions-policy`,
      `strict-transport-security: max-age=31536000; includeSubDomains`; `server` başlığı YOK. **securityheaders.com** ile A+ hedefle.
- [ ] **CSP:** DevTools konsolunda CSP ihlali OLMAMALI (`script-src 'self'` — inline script yok, hash-bakımı gerekmiyor).
- [ ] **Cookie:** DevTools → Application → Cookies → `racar.session` satırında **Secure ✓ / HttpOnly ✓ / SameSite=Lax**.
      Secure değilse → §5 forwarded-headers tuzağı (`ASPNETCORE_FORWARDEDHEADERS_ENABLED=true`).
- [ ] **UI bozulmadı:** PDF-yazdır (gizli iframe) + bir sil/iptal onayı (data-confirm dialogu) hâlâ çalışıyor.
- [ ] Secret'lar repo'da/appsettings.json'da DEĞİL (env/Production.json git-ignore).
- [ ] Rol şifreleri dev-varsayılanından değişti.
- [ ] TLS zorunlu (Caddy HTTPS), `/health` dışı uçlar auth-gated.
- [ ] Yedekleme cron çalışıyor + restore denendi.
- [ ] Firewall: yalnız 80/443 dışarı; PostgreSQL (5432) yalnız localhost.
- [ ] **Observability (kurulmuşsa):** `ss -ltnp` → Prometheus/Loki/Tempo/Collector yalnız `127.0.0.1`; Grafana
      dışarıdan yalnız Caddy-auth arkasından erişilir (default admin/admin DEĞİL). `.env` repo'da değil.

---

### Kod tarafında kalan (ayrı — kimlik/karar gerektirir)
- e-Fatura (GİB kimliği), SMS/HGS/POS (sağlayıcı kimliği) — stub; kimlik gelince config'le aktif.
- Fiyat motoru kalibrasyonu (canlı parite 403) — makul kurallarla çalışıyor, birebir kalibrasyon ertelendi.
- Bağımlılık hijyeni (opsiyonel): transitive IdentityModel 8.0.1 pin'i güncelle; QuestPDF gelir eşiği aşılırsa ticari lisans.

---

## 10. SÜRÜM GÜNCELLEME (mevcut kurulumu yenileme)

Yukarıdaki bölümler **sıfırdan kurulum** içindir. Zaten çalışan bir sunucuyu güncellerken sıra şudur:

```bash
# 1) YEDEK — migration'dan ÖNCE, her seferinde (geri dönüşü olmayan göç olabilir, bkz. aşağıdaki tablo)
sudo -u postgres pg_dump racar | gzip > /var/backups/racar-$(date +%F-%H%M).sql.gz

# 2) Kodu al + yayınla
git -C /opt/racar/src pull
dotnet publish /opt/racar/src/src/RentACar.Web        -c Release -o /opt/racar/web
dotnet publish /opt/racar/src/src/RentACar.PublicSite -c Release -o /opt/racar/publicsite

# 3) Yeniden başlat — MIGRATION AÇILIŞTA OTOMATİK KOŞAR (elle `dotnet ef` GEREKMEZ)
sudo systemctl restart racar-web racar-publicsite
sudo systemctl status racar-web --no-pager | head -5
```

### 10.1 Geri dönüşü OLMAYAN göçler (yükseltmeden önce oku)

Migration'ların çoğu şema ekler ve `Down()` ile geri alınabilir. **Veriye dokunanlar alınamaz.**
Bu tabloya, veri değiştiren her yeni migration eklenmelidir.

| Migration | Ne yapar | Geri alınabilir mi |
|---|---|---|
| `YakitNullable` (PR-21) | `Vehicles.Yakit` nullable yapar **ve tüm satırları NULL'a çeker** | **HAYIR.** Hangi satırın gerçekten "Benzin" olduğu bilinmiyordu (kolon NOT NULL + varsayılanlıydı, hiçbir değer bilinçli giriş sayılamaz). `Down()` kolonu NOT NULL'a döndürüp hepsini Benzin yapar — yani eski hâli DEĞİL, eski hâlin tahminini üretir. **Yükseltmeden önce yedek şart.** Yükseltme sonrası personel araç ekranından doğru yakıtları girer. |

### 10.2 Veriye dokunan migration yazarken — RLS TUZAĞI

Migration'lar `racar_owner` ile koşar ve **bu rolün BYPASSRLS yetkisi YOKTUR** (bilinçli, CLAUDE.md §4).
Tenant tablolarının çoğu `FORCE ROW LEVEL SECURITY` taşır. Bu yüzden migration içinde düz bir

```sql
UPDATE "Vehicles" SET "Yakit" = NULL;      -- ❌ SESSİZCE 0 SATIR
```

**hata vermeden hiçbir şey yapmaz**: `app.tenant_id` GUC'u set edilmediği için policy hiçbir satırı
eşleştirmez. PR-21'de tam olarak bu yaşandı — kolon nullable oldu, veri olduğu gibi kaldı ve bu ancak
veri ÖLÇÜLDÜĞÜ için fark edildi. Doğrusu tenant döngüsü + işlem-yerel `set_config`:

```sql
DO $$
DECLARE t uuid;
BEGIN
    FOR t IN SELECT "Id" FROM "Tenants" LOOP
        PERFORM set_config('app.tenant_id', t::text, true);
        UPDATE "Vehicles" SET "Yakit" = NULL;
    END LOOP;
END $$;
```

**Kural:** veri değiştiren her migration'dan sonra, etkilenen satır sayısını DOĞRULA
(`select count(*) … where <yeni durum>`). Migration'ın "uygulandı" yazması, veriyi değiştirdiği
anlamına GELMEZ.

### 10.3 Bu sürüme özel doğrulama (PR-15…21)

Genel kontroller §9'da. Bu sürümde değişen ve **canlıda ayrıca bakılması gereken** üç şey:

1. **Statik varlıklar (PR-19 — `UseStaticFiles` → `MapStaticAssets`).** PublicSite'ın statik dosya
   servisi değişti. Yükseltme sonrası:
   ```bash
   curl -sI https://<tenant-domaini>/site.css | head -3     # 200 + content-type: text/css
   curl -so /dev/null -w '%{http_code}\n' https://<tenant-domaini>/favicon.svg
   ```
   **200 değilse site CSS'siz kalır.** (Yerelde bu tam olarak yaşandı: eski API sıkıştırılmış `.gz`
   varyantını çözemeyince istek kök-slug rotasına düşüp 500 veriyordu.)
2. **`blazor.web.js` artık YOK (PR-19).** Sayfa kaynağında `_framework/blazor.web.js` referansı
   **olmamalı**; formlar (müsaitlik araması, talep formu) ve SSS `<details>` JS'siz çalışmalı.
3. **Yakıt alanı (PR-21).** Yükseltme sonrası araç listesinde yakıt sütunu `—` görünür; bu BEKLENEN
   davranıştır (bkz. 10.1). Personel doğru değerleri girene kadar ilan başlıklarında yakıt yazmaz.

### 10.4 Geri alma (rollback)

```bash
# Kod: bir önceki yayına dön
sudo systemctl stop racar-web racar-publicsite
# (önceki publish çıktısını sakladıysan geri kopyala; saklamıyorsan git'te bir önceki etikete dönüp yeniden publish et)
sudo systemctl start racar-web racar-publicsite
```
**Şema geri alınmaz.** Yeni migration'lar eski kodla uyumsuzsa (kolon tipi değiştiyse) tek güvenli
yol §8'deki yedekten geri yüklemektir — bu yüzden 10. adımın 1. maddesi (yedek) atlanamaz.
