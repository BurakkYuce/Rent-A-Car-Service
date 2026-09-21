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
sudo apt update && sudo apt install -y postgresql postgresql-contrib caddy curl jq
# .NET 10 SDK (runtime YETMEZ — deploy/yayinla.sh .NET'i sunucuda `dotnet publish` ile derler).
# Microsoft paket deposunu ekle, sonra:
sudo apt install -y dotnet-sdk-10.0
dotnet --list-sdks | grep '^10\.'      # kontrol (kurulum.sh da aynısını yapar)
```
PostgreSQL 15+ olmalı (Ubuntu 24.04 → 16 gelir, uygun).
**Node/npm KURULMAZ** (bilinçli): yeni arayüz (SPA) sunucuda derlenmez — CI'ın ürettiği artifact indirilir
(§10). Sunucuda `npm ci` = root olarak rastgele lifecycle betikleri + derleme OOM'unun Postgres'i öldürmesi.

## 2. PostgreSQL — roller + veritabanı (TEK SEFERLİK, kritik)
İki rol modeli (CLAUDE.md §4): `racar_owner` (DDL/migration, RLS bypass edebilir — uygulama KULLANMAZ),
`racar_app` (runtime, **NOSUPERUSER NOBYPASSRLS** — uygulama DAİMA bununla bağlanır → RLS zorunlu).
```bash
sudo -u postgres psql -f scripts/db-init-roles.sql   # racar_owner/racar_app + db racar + grant (idempotent)
```
- Rol şifrelerini **güçlü** yap (script'teki dev şifrelerini DEĞİŞTİR: `racar_owner_pw`/`racar_app_pw`).
- `racar_app`'in `NOBYPASSRLS` olduğunu DOĞRULA: `\du racar_app` → "Bypass RLS" YOK olmalı. (Tenant izolasyonunun temeli.)
- Migration + seed **açılışta otomatik** çalışır (Migrator bağlantısıyla); elle migration gerekmez.
  Üretimde `Seed__Parola` verilmezse demo firmalar (yucerent/demo) ve kullanıcıları **oluşturulmaz**
  (tahmin edilebilir parolalı kullanıcı = arka kapı); gerçek firmalar platform konsolundan (`/platform`) açılır.

## 3. ZORUNLU config / secrets (guard'lı — eksikse açılış reddeder)
Secret'ları **appsettings.json'a KOYMA** (repo'ya sızar). Env değişkeni veya `appsettings.Production.json`
(git-ignore'lu) veya secret manager kullan. ASP.NET Core env-var eşlemesi: `Pii__HmacKey`, `ConnectionStrings__Default` (çift alt-çizgi).

| Anahtar | Nerede | Nasıl üret | Not |
|---|---|---|---|
| `ConnectionStrings:Default` | Web + Api | — | **racar_app** ile (NOBYPASSRLS). Eksik → red. |
| `ConnectionStrings:Migrator` | Web | — | **racar_owner** ile (yalnız migration). Eksik → red. |
| `Pii:HmacKey` | Web + Api | `openssl rand -base64 48` | PII blind-index (TC arama). **Kaybolursa TC araması bozulur** — YEDEKLE. |
| `RACAR_DP_KEYS` (env var) | Web + Api + **PublicSite** | kalıcı dizin yolu, ör. `/var/lib/racar/dp-keys` | DataProtection key-ring KALICI dizini — **üç binary'ye de AYNI dizin.** Verilmezse her binary kendi ring'ini yaratır (Web'in şifrelediğini Api/PublicSite çözemez); publish çıktısı üzerine yazıldığında ring ile birlikte **tüm *Enc PII kalıcı çözülemez.** Yerelde birebir yaşandı: `dotnet clean` sonrası 29 cipher okunamaz oldu. |
| `Jwt:Key` | **yalnız Api** | `openssl rand -base64 48` (≥32 bayt, özgün) | API token imzalama. Dev/zayıf anahtar prod'da reddedilir. |
| `Platform:AdminUser` | Web | — | Platform süper-admin (/platform konsolu) kullanıcı adı. |
| `Platform:AdminPasswordHash` | Web | app'in hasher'ı (`PlatformCredentials.HashPassword`) | Düz şifre DEĞİL, hash. Üretmek için yardım iste (ya da geçici snippet). |
| `RACAR_GH_TOKEN` (env var) | **yalnız `yayinla.sh`** | GitHub → fine-grained token, **yalnız bu repo, Contents: Read-only** | Yeni arayüz artifact'ını (`spa-<sha>`) indirmek için. Uygulama OKUMAZ. Yoksa/geçersizse yayın reddedilir. Adımlar: [f2-2-sunucu-adimlari.md](f2-2-sunucu-adimlari.md). Süresi dolunca yenile. |

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

**Bu unit'e de ŞART:** `ASPNETCORE_ENVIRONMENT=Production`, `ConnectionStrings__Default` (racar_app),
`Pii__HmacKey` ve **`RACAR_DP_KEYS=/var/lib/racar/dp-keys`** — Web ile AYNI dizin (§3). En pratiği aynı
`EnvironmentFile=/etc/racar/racar-web.env`'i kullanmak. PublicSite bu ring'i antiforgery token'ları için
kullanıyor: ayrı/geçici ring, her redeploy'da açık sekmelerdeki talep ve müsaitlik formlarını 400'e düşürür.
Eksikse uygulama açılışta reddeder (Web/Api ile aynı guard).

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

## 6. Opsiyonel entegrasyonlar (config VARSA aktif, yoksa stub)
- **WhatsApp (Twilio):** `Twilio:AccountSid` + `Twilio:AuthToken` + `Twilio:WhatsAppFrom` verilirse gerçek
  gönderici devreye girer.
- **SMS (Twilio):** aynı hesap. `Twilio:AccountSid` + `Twilio:AuthToken` + (`Twilio:SmsFrom` **veya**
  `Twilio:MessagingServiceSid`) → gerçek gönderici. Bkz. §6.2.
- **E-posta (SMTP):** ortam değişkeni YOK — yapılandırma her tenant'ın Ayarlar ekranındadır. Sunucuda
  yapılacak tek şey giden 587/465 portunun açık olması.
- HGS / POS / e-Fatura / KABİS hâlâ stub — kimlik gelince bağlanır.

> **Stub davranışı değişti (dürüst stub kuralı):** yapılandırma yokken hiçbir stub artık "başarılı"
> dönmez. SMS, POS ve KABİS stub'ları `false` döndürür; e-Fatura zaten öyleydi. Sebebi: sahte başarı,
> çağıranın kalıcı kayda yanlış yazmasına yol açıyordu (sahte ETTN, var olmayan işlem referansı,
> yapılmamış yasal bildirim). Bir akış "gönderdim" diyorsa gerçekten göndermiştir.

### 6.2 SMS (Twilio) — gönderen kaynağı ve Türkiye uyarısı
| Anahtar | Ne işe yarar |
|---|---|
| `Twilio:SmsFrom` | Gönderen numara (E.164). |
| `Twilio:MessagingServiceSid` | Numara yerine Messaging Service (`MG…`). **İkisi birlikte gönderilemez** — açık gönderen varsa o kazanır. |

Tenant Ayarlar'da **SMS Başlığı** doluysa gönderen olarak o kullanılır. Alfanümerik başlık Türkiye'de
operatör kaydı ister; kayıtsız başlıkla mesaj Twilio'ya kabul edilse de **teslim edilmez** (hata 30007).
Kayıt yoksa başlığı boş bırakın, numara ile gidilsin. Ayrıca Twilio konsolunda **Geographic Permissions**
altında Türkiye açık olmalı (kapalıysa hata 21408).

Doğrulama: Ayarlar → **SMS Testi**. Tek mesaj gönderir ve teslim durumunu Twilio'dan SID ile okur —
"iletildi" ile "teslim edildi" ayrı raporlanır.

### 6.3 Ödeme (iyzico) — barındırılan sayfa, PCI kapsamı dışı
| Anahtar | Ne işe yarar |
|---|---|
| `Iyzico:BaseUrl` | `https://sandbox-api.iyzipay.com` (test) / `https://api.iyzipay.com` (üretim). |
| `Iyzico:ApiKey` / `Iyzico:SecretKey` | Firma API + güvenlik anahtarı (iyzico panelinde Ayarlar → Firma Ayarları). |

Kart verisi **sunucumuza hiç uğramaz**: müşteri iyzico'nun kendi sayfasında kartını girer, biz
yalnız bir jeton ve sonuç görürüz. Doğrudan API'nin kart alan uçları bilinçli olarak kullanılmıyor.

Kullanılan uçlar (sandbox'a karşı ampirik doğrulandı): ön provizyon
`/payment/iyzipos/checkoutform/initialize/preauth/ecom`, tahsilat `.../auth/ecom`, sonuç sorgusu
`/payment/iyzipos/checkoutform/auth/ecom/detail`, kapatma `/payment/postauth`, iptal
`/payment/cancel`, iade `/payment/refund`.

**Üretim uyarısı:** `sandbox-` ile başlayan anahtarla üretimde çalışmak *hiç para tahsil etmemek*
demektir. Uygulama açılışta gürültülü uyarır ama açılışı engellemez (staging bilinçli olarak
sandbox kullanabilir). `BaseUrl`'i de birlikte değiştirin.

**Doğrulama:** kimlikleri `~/.racar-iyzico.env` dosyasına koyup
`dotnet test --filter IyzicoCanliSandboxTests` koşun — gerçek adaptör gerçek sandbox'a çıkar.
Kimlik yoksa bu testler atlanır (CI'da böyle).

### 6.4 E-posta (SMTP) — tenant başına
Ayarlar → SMTP: host, port, kullanıcı, şifre (at-rest şifreli), **gönderen adres**, gönderen ad.
Port 465 örtük SSL, 587 STARTTLS olarak bağlanır; SSL kutusu yalnız STARTTLS'in zorunlu tutulup
tutulmayacağını belirler. Gönderen adres boşsa kullanıcı adı e-posta biçimindeyse ona düşülür; ikisi de
yoksa **gönderim yapılmaz** (uydurma "Kimden" üretilmez — SPF/DKIM uyumsuz gönderen spam'e düşer).
Alan adınızın SPF ve DKIM kaydını gönderen adrese göre ayarlayın.

Doğrulama: Ayarlar → **E-posta Testi**.

### 6.1 WhatsApp — üretim (onaylı şablon) vs SANDBOX (serbest metin)
| Anahtar | Ne işe yarar |
|---|---|
| `Twilio:AccountSid` / `Twilio:AuthToken` | Hesap kimliği (Basic auth). |
| `Twilio:WhatsAppFrom` | Gönderen numara. Sandbox'ta Twilio'nun verdiği numara (ör. `+14155238886`). |
| `Twilio:Templates:operasyon_ozet` | Günlük özet şablonunun ContentSid'i (`HX…`). |
| `Twilio:Templates:ops_alert` | Watchdog alarm şablonunun ContentSid'i. |
| `Twilio:AllowFreeform` | **Yalnız sandbox/test.** Şablon SID'i YOKSA serbest metin gönderilir. |

**ÜRETİM:** şablon SID'leri tanımlı olmalı. İş-başlatımlı (proaktif) WhatsApp mesajı, WhatsApp
tarafından onaylanmış bir şablon gerektirir — serbest metin 24 saatlik müşteri-hizmetleri
penceresi dışında REDDEDİLİR. `Twilio:AllowFreeform` üretimde **AÇILMAZ**.

**SANDBOX (onay çıkana kadar test):** Twilio'nun kendi dokümanı — *"You can't use custom message
templates with the Sandbox."* Sandbox yalnız (a) hedef numaranın sandbox'a katılmasından sonraki
24 saatlik pencerede serbest metni, (b) Twilio'nun 3 sabit hazır şablonunu kabul eder. Bizim iki
şablonumuz ikisi de değil. Bu yüzden test için:
```
Twilio__AccountSid=AC…
Twilio__AuthToken=…
Twilio__WhatsAppFrom=+14155238886
Twilio__AllowFreeform=true          # şablon SID'i VERME — serbest metin yolu açılsın
```
Hedef numara önce sandbox'a katılmalı (Twilio konsolundaki `join <kelime>` mesajını göndererek) ve
son 24 saat içinde yazmış olmalıdır; aksi halde Twilio 63015/63016 döner.

**Denemek için:** Ayarlar ekranı → **WhatsApp Testi** → numarayı gir → *Test Mesajı Gönder*.
Günlük özetin kullandığı kod yolunun AYNISINI çalıştırır (sabah 08:00 penceresini ve günlük
idempotency'yi beklemeden). Gönderim başarısızsa ekranda hata döner — sessiz başarı yoktur.

**Bayrak neden var ve neden varsayılan KAPALI:** şablon SID'i üretimde unutulup sessizce serbest
metne düşülseydi, net bir yapılandırma uyarısı yerine WhatsApp'ın reddettiği bir gönderim elde
ederdik. Bayrak kapalıyken davranış eskisiyle birebir aynıdır (şablon yoksa uyar, hiç istek atma) —
`TwilioWhatsAppTests` bunu kalıcı olarak kilitler.
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

Yukarıdaki bölümler **sıfırdan kurulum** içindir. Zaten çalışan bir sunucuyu güncellerken sıra şudur
(repo checkout'u sunucuda, ör. `/opt/racar/src`; aşağıda `<repo>`):

```bash
# 1) YEDEK — migration'dan ÖNCE, her seferinde (geri dönüşü olmayan göç olabilir, bkz. aşağıdaki tablo)
sudo -u postgres pg_dump racar | gzip > /var/backups/racar-$(date +%F-%H%M).sql.gz

# 2) Kodu al — YALNIZ main'deki ve CI'ı YEŞİL bir commit (yeni arayüz artifact'ı yalnız onlar için var)
git -C <repo> fetch origin && git -C <repo> checkout --detach origin/main

# 3) Yayınla: .NET derlenir + o SHA'nın SPA artifact'ı indirilir/doğrulanır + symlink çevrilir +
#    servisler yeniden başlar (MIGRATION AÇILIŞTA OTOMATİK) + sağlık kapısı (geçmezse otomatik geri alma)
sudo <repo>/deploy/yayinla.sh

# 4) Doğrula (başlıklar, CSRF, /app/ 200, /api/ui/v1/oturum/ben 401 …)
<repo>/deploy/dogrula.sh <domain>
```

### 10.0 Yeni arayüz (SPA) artifact'ı — `yayinla.sh` ne yapar, neyi REDDEDER

CI (`.github/workflows/ci.yml` → `spa-surum`), `main`'e her push'ta **tüm kapılar yeşilse** SPA'yı derler ve
`spa-<sha>` adlı (ön-sürüm) GitHub release'ine üç asset yükler: `spa-<sha>.tar.gz`, `spa-<sha>.tar.gz.sha256`,
`chunks.txt` (hash'li dosya listesi). `yayinla.sh`:

1. Checkout edilen commit'in SHA'sını alır (`git rev-parse HEAD`); commit'lenmemiş değişiklik varsa uyarır.
2. `RACAR_GH_TOKEN`'ı `/etc/racar/racar.env`'den okur (**değeri hiçbir çıktıya yazılmaz**; curl'e stdin'den
   verilir, `ps`'te görünmez; depolama sunucusuna yönlendirmede taşınmaz) ve `spa-<sha>` asset'lerini indirir.
3. `sha256` doğrular; arşiv içeriğini kilitler (yalnız `browser/`, `chunks.txt`, `SURUM`) ve arşivin içindeki
   `SURUM` = checkout SHA'sı olmalı.
4. `releases/<zaman>/app/`'e açar (Web `Spa:Dizin` varsayılanı `../app/browser` → her release KENDİ SPA'sını
   servis eder; symlink swap ile eski/yeni karışmaz).
5. Önceki release'in **kendi** `chunks.txt`'indeki dosyaları `cp -n` ile yeni release'e kopyalar (**chown'dan
   önce**): yayın sırasında açık sekmeler eski chunk'ları bulmaya devam eder. Kopyalananlar yeni `chunks.txt`'e
   girmez → bir sonraki yayında taşınmaz, birikmez.
6. .NET publish → `chown` → symlink swap → sağlık kapısı (`/health/ready`, `/health/live`, **`/app/`**); geçmezse
   önceki release'e geri döner.

**Reddedilen durumlar** (çıkış ≠ 0, yarım release silinir, `current` DEĞİŞMEZ): `RACAR_GH_TOKEN` yok/biçimsiz;
token geçersiz (401/403); o SHA için release yok (main'e push edilmemiş ya da CI bitmemiş/kırmızı); `.sha256`
asset'i eksik; checksum uyuşmuyor; `.sha256` ya da arşivdeki `SURUM` başka bir SHA'yı gösteriyor.
Hepsi `deploy/spa-testi.sh` ile sahte bir GitHub API'sine karşı sınanır (CI `deploy-betikleri` işi).

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

`yayinla.sh` sağlık kapısı geçmezse **kendisi** geri alır. Elle geri alma (ya da prova):

```bash
ls -1t /opt/racar/releases                  # en yeni üstte; son 3 release tutulur
readlink -f /opt/racar/current              # şu anki
sudo ln -sfn /opt/racar/releases/<önceki-zaman> /opt/racar/current
sudo systemctl restart racar-web racar-publicsite
curl -fsS -o /dev/null -w '%{http_code}\n' http://127.0.0.1:5220/health/ready   # 200
curl -fsS -o /dev/null -w '%{http_code}\n' http://127.0.0.1:5220/app/           # 200 (eski release'in SPA'sı)
```
SPA release'in içinde olduğu için symlink ile birlikte geri döner (ayrı işlem yok). İleri dönmek için aynı
komutu yeni release ile çalıştır ya da `yayinla.sh`'ı yeniden koş.
**Şema geri alınmaz.** Yeni migration'lar eski kodla uyumsuzsa (kolon tipi değiştiyse) tek güvenli
yol §8'deki yedekten geri yüklemektir — bu yüzden 10. adımın 1. maddesi (yedek) atlanamaz.
