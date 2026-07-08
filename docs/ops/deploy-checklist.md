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

## 5. Reverse proxy (Caddy) — HTTPS + gerçek istemci IP
Caddy otomatik Let's Encrypt TLS verir. `/etc/caddy/Caddyfile`:
```
rentpro.example.com {
    reverse_proxy 127.0.0.1:5220
}
```
- Uygulama `UseForwardedHeaders` ile **KnownProxies=loopback** varsayar → proxy AYNI makinede (127.0.0.1) olmalı ki
  login rate-limiter gerçek istemci IP'sini görsün (uzaktan sahte `X-Forwarded-For` reddedilir). Proxy ayrı makinedeyse
  `KnownProxies`/`KnownNetworks` ayarını genişletmek gerekir.
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
- [ ] `ASPNETCORE_ENVIRONMENT=Production` (antiforgery açık, guard'lar aktif).
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
