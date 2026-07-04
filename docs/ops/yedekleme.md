# Yedekleme ve Geri Dönüş (P0-1)

> Muhasebe verisi tutan sistemde yedek "alınmış" değil, **geri dönülebilir** olduğunda vardır.
> İki script: `scripts/db-backup.sh` (günlük yedek + rotasyon + off-site) ve
> `scripts/db-restore-drill.sh` (geri-yükleme tatbikatı — aylık koşun).

## 1. Günlük yedek (VPS, systemd timer)

```ini
# /etc/systemd/system/racar-backup.service
[Unit]
Description=RentACar gunluk DB yedegi
[Service]
Type=oneshot
User=racar
Environment=RACAR_DB=racar RACAR_PG_USER=racar_owner RACAR_BACKUP_DIR=/var/backups/racar
# Off-site icin (rclone remote kurulduktan sonra): Environment=RACAR_BACKUP_REMOTE=b2:racar-yedek
ExecStart=/opt/racar/scripts/db-backup.sh
```
```ini
# /etc/systemd/system/racar-backup.timer
[Unit]
Description=Her gece 03:15 yedek
[Timer]
OnCalendar=*-*-* 03:15:00
Persistent=true
[Install]
WantedBy=timers.target
```
```bash
systemctl enable --now racar-backup.timer && systemctl list-timers racar-backup*
```
Şifre: `~racar/.pgpass` (izin `0600`): `localhost:5432:*:racar_owner:<şifre>` — script şifre tutmaz.
(cron alternatifi: `15 3 * * * RACAR_DB=racar ... /opt/racar/scripts/db-backup.sh >> /var/log/racar-backup.log 2>&1`)

## 2. Off-site (3-2-1 kuralının "1"i)

Tek makinedeki yedek, disk ölümünde yedek değildir. `rclone` ile herhangi bir hedefe:
```bash
apt install rclone && rclone config   # ör. Backblaze B2 (~0.005$/GB-ay) veya S3/Drive
export RACAR_BACKUP_REMOTE=b2:racar-yedek   # script otomatik kopyalar
```

## 3. Geri-yükleme tatbikatı (ayda 1 + her altyapı değişikliğinde)

```bash
RACAR_DB=racar RACAR_PG_USER=racar_owner RACAR_BACKUP_DIR=/var/backups/racar \
  scripts/db-restore-drill.sh
```
En son yedeği geçici `racar_restore_drill` DB'sine açar, 7 kritik tablonun satır sayısını
kaynakla karşılaştırır, geçici DB'yi siler. Çıkış ≠ 0 ise **yedeğine güvenme** — araştır.

## 4. Gerçek felakette geri dönüş (runbook)

```bash
systemctl stop racar-web racar-api                    # 1) uygulamayı durdur
psql -U racar_owner -d postgres -c 'ALTER DATABASE racar RENAME TO racar_bozuk'   # 2) bozuk DB'yi kenara al (SİLME)
psql -U racar_owner -d postgres -c 'CREATE DATABASE racar'
pg_restore -U racar_owner -d racar --no-owner --exit-on-error /var/backups/racar/racar-<en-yeni>.dump
psql -U racar_owner -d racar -c 'GRANT USAGE ON SCHEMA public TO racar_app'       # 3) grant'ler dump'ta yoksa: scripts/db-init-roles.sql idempotent bloğunu koş
systemctl start racar-web racar-api                   # 4) başlat, /health = healthy doğrula
```
Sonra: son yedek ile felaket ANI arasındaki kayıp pencereyi tenant'lara bildir (günlük yedekte
en kötü 24 saat). Daha dar RPO gerekirse WAL arşivleme (`archive_mode=on` + `wal-g`/`pgbackrest`) kur.

## 5. Web + Api ortak anahtar ZORUNLULUĞU (kurulum)

İki uygulama da PII/sır cipher'larını aynı anahtarlarla çözebilmelidir — yoksa biri diğerinin
yazdığı TC'yi **sessizce null** okur (loglarda "Cipher çözülemedi" WARNING'i görürsünüz):

```ini
# HER İKİ systemd unit'inde de (racar-web VE racar-api) AYNI değerler:
Environment=RACAR_DP_KEYS=/var/lib/racar/dp-keys      # ortak DataProtection key-ring dizini
Environment=Pii__HmacKey=<64+ karakter rastgele sır>  # TC blind-index anahtarı (tek kaynak)
```
- `RACAR_DP_KEYS` verilmezse her binary kendi `dp-keys/` dizinini kullanır → Web'in şifrelediğini
  Api çözemez. Tek makinede bile ORTAK dizin şart.
- `Pii:HmacKey` Development dışında zorunludur (uygulama yoksa açılmaz); iki uygulamada
  FARKLI olursa TC benzersizliği/araması bölünür.

### 5.1 Platform süper-admin kimliği (SaaS operatörü konsolu `/platform`)

Platform konsolu operatörü tenant'lardan bağımsızdır; kimliği config'ten gelir ve **Development
dışında ZORUNLUDUR** (yoksa açılış reddeder — arka kapı yok). YALNIZ `racar-web` unit'inde:

```ini
Environment=Platform__AdminUser=<operatör-kullanıcı-adı>
Environment=Platform__AdminPasswordHash=<aşağıdaki komutun çıktısı>
```
Parola hash'ini üret (tek seferlik, SDK ile — çıktıyı `Platform__AdminPasswordHash`'e koy):
```bash
dotnet run --project src/RentACar.Web -- --platform-hash '<güçlü-parola>'
```
- Development'ta config verilmezse otomatik `admin` / `***REMOVED***` kullanılır (prod'a sızmaz).
- Kimlik DB'de User değildir → parola değişimi = yeni hash üretip env'i güncellemek + web'i yeniden başlatmak.

## 6. Neler yedeklenir / yedeklenMEZ

| Ne | Nasıl |
|---|---|
| PostgreSQL `racar` (tüm tenant verisi, RLS policy'ler, trigger'lar dahil) | `db-backup.sh` (pg_dump -Fc) |
| DataProtection anahtarları (`dp-keys/` — TenantSettings *Enc + PII cipher'larının kilidi) | dosya kopyası — off-site'a DAHİL ET; kaybı = şifreli sırlar/PII çözülemez |
| `Pii:HmacKey` (appsettings/env — TC blind-index anahtarı, KVKK/F2) | konfigürasyon yedeği/parola kasası; kaybı = TC arama+benzersizlik indeksi yeniden kurulamaz (veri `*Enc`'ten kurtarılır) |
| appsettings.json / systemd unit'leri | git + sunucu kurulum reçetesi |
| `logs/` | yedeklenmez (14 gün rotasyonlu, kayıp kabul edilebilir) |

> **Tatbikat kaydı:** 2026-07-02 — yerel ortamda `db-backup.sh` + `db-restore-drill.sh` koşuldu;
> 7/7 tablo satır sayısı birebir eşleşti (kanıt: PR açıklaması/commit gövdesi).
