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

## 5. Reverse proxy (Caddy) — HTTPS + gerçek istemci IP
Caddy otomatik Let's Encrypt TLS verir. **Güvenlik başlıkları + CSP + cookie sertleştirme + HSTS artık UYGULAMADA**
(Program.cs middleware + AddCookie + AddHsts — defense-in-depth, proxy'den bağımsız). Caddyfile sade:
```
rentpro.example.com {
    reverse_proxy 127.0.0.1:5220
    request_body { max_size 2MB }   # kaba gövde limiti (DoS; plugin gerekmez) — Kestrel:Limits ile tamamlayıcı
}
```
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

---

### Kod tarafında kalan (ayrı — kimlik/karar gerektirir)
- e-Fatura (GİB kimliği), SMS/HGS/POS (sağlayıcı kimliği) — stub; kimlik gelince config'le aktif.
- Fiyat motoru kalibrasyonu (canlı parite 403) — makul kurallarla çalışıyor, birebir kalibrasyon ertelendi.
- Bağımlılık hijyeni (opsiyonel): transitive IdentityModel 8.0.1 pin'i güncelle; QuestPDF gelir eşiği aşılırsa ticari lisans.
