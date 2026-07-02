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

## 5. Neler yedeklenir / yedeklenMEZ

| Ne | Nasıl |
|---|---|
| PostgreSQL `racar` (tüm tenant verisi, RLS policy'ler, trigger'lar dahil) | `db-backup.sh` (pg_dump -Fc) |
| DataProtection anahtarları (`~/.aspnet/DataProtection-Keys` — TenantSettings *Enc sırlarının kilidi) | dosya kopyası — off-site'a DAHİL ET; kaybı = şifreli sırlar çözülemez |
| appsettings.json / systemd unit'leri | git + sunucu kurulum reçetesi |
| `logs/` | yedeklenmez (14 gün rotasyonlu, kayıp kabul edilebilir) |

> **Tatbikat kaydı:** 2026-07-02 — yerel ortamda `db-backup.sh` + `db-restore-drill.sh` koşuldu;
> 7/7 tablo satır sayısı birebir eşleşti (kanıt: PR açıklaması/commit gövdesi).
