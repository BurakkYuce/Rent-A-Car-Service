# Blazor saat kayması: envanter, ayırt etme, düzeltme

> Migration: `src/RentACar.Infrastructure/Migrations/20261016225116_FixBlazorSaatKaymasi.cs`.
> Kuru çalışma: `scripts/saat-kaymasi/kuru-calistir.sql` (migration SQL'inden üretilir; `BlazorTimeShiftTests` birebir kilitler).
> **Üretimde F2.2 ilk yayınıyla birlikte uygulanır** (aşağıda §5). Uygulanmadan HGS dosya içe aktarımı (#391) açılmamalı:
> geçişleri kiraya eşleştiren pencere kiranın teslim/dönüş saatidir, 3 saat kayık pencere yanlış kiraya borç yazar.

## 1. Sorun

Blazor formlarındaki `datetime-local` alanları minimal-API uçlarında `[FromForm] DateTimeOffset` ile bağlandı. ASP.NET Core
bu bağlamada ofsetsiz değeri **UTC** kabul eder: kullanıcının yazdığı İstanbul saati `09:00`, `09:00Z` (= İstanbul 12:00)
olarak saklandı. Blazor aynı değeri yine UTC duvar saatiyle (`.UtcDateTime`) geri gösterdiği için hata Blazor'da görünmedi.
SPA doğru UTC'yi (`06:00Z`) yazar ve İstanbul saatiyle gösterir: **Blazor dönemi kayıtları SPA'da 3 saat ileri görünür.**
Bu, sunucunun saat diliminden BAĞIMSIZDIR.

İkinci sınıf: `FormParse.Date` ile okunan `datetime-local` alanları (`DateTimeStyles.None` + `.ToUniversalTime()`) Blazor
SUNUCUSUNUN yerel saat dilimini kullanır: sunucu İstanbul'daysa doğru, UTC'deyse aynı 3 saat kayık. Üretimdeki systemd
birimi `TZ` tanımlamıyor; belirleyen sunucunun `/etc/localtime`'ıdır (`timedatectl show -p Timezone --value`).

## 2. Envanter

Kaynak: son Blazor ağacında (`3e1c886a^`) `datetime-local` geçen 9 sayfa ve onların POST uçları.

| Alan | Blazor ekranı → uç | Bağlama | Etki | Düzeltme |
|---|---|---|---|---|
| `Quotations.BasTar/BitTar` | QuotationList → teklif create | `[FromForm] DateTimeOffset` | her sunucuda kayık | kaydırılır |
| `Reservations.BasTar/BitTar` | ReservationList → rezervasyon create/update | `[FromForm] DateTimeOffset` | her sunucuda kayık | kaydırılır |
| `Rentals.BasTar/BitTar` | SekmeKiraBilgisi, SekmeHizliGiris → kira create | `[FromForm] DateTimeOffset` | her sunucuda kayık | kaydırılır |
| `Rentals.BitTar` (uzatma) | kira uzat | `[FromForm] DateTimeOffset` | her sunucuda kayık | kaydırılır |
| `Rentals.GercekDonusTar` | SekmeDonus → kira dönüş | `[FromForm] DateTimeOffset` | her sunucuda kayık | kaydırılır |
| `FaturaDonemleri.DonemBas/DonemBit` | türetilmiş: `BasTar.AddMonths(i)`, son sınır `BitTar` | — | kiradan miras | kiranın kararını izler |
| `VehicleKmLoglari.Tarih` (Kaynak=Dönüş) | türetilmiş: `= GercekDonusTar` | — | kiradan miras | dönüşün kararını izler |
| `AssistansTalepleri.Zaman` | AssistansTalepList | `FormParse.Date` | yalnız sunucu UTC idiyse | `sunucu_utc=true` ile |
| `Baflar.DonusTarihi` | BafList | `FormParse.Date` | yalnız sunucu UTC idiyse | `sunucu_utc=true` ile |
| `Invoices.Tarih` (manuel fatura) + defter satırı `AccountLedgerEntries.EntryDateUtc` | InvoiceList | `FormParse.Date` | yalnız sunucu UTC idiyse | **DEĞİŞMEZ tablo: yalnız rapor** |
| QuoteCalculator | hesaplayıcı | — | saklanmaz | — |

- **Gün alanları** (`type="date"`) etkilenmez: her iki yorumda da aynı İstanbul günü kalır.
- **Değişmez tablolar** (`rc_prevent_mutation` tetikli: AccountLedgerEntries, AuditLogs, CashTransactions, DepozitoIratlar,
  Expenses, GiderOdemeleri, InvoiceLines, Invoices, MtvOdemeleri, MuayeneOdemeleri, PenaltyOdemeleri, VehicleSales):
  migration HİÇBİRİNE yazmaz. Bunlardan yalnız manuel fatura tarihi `datetime-local`'den geldi; o da yalnız sunucu UTC
  idiyse kayıktır. Rapor satırı "Invoices / Tarih" (yalnız dakika hassasiyetli, yani formdan girilmiş olabilecek manuel
  faturalar). Düzeltilmesi gerekirse yol muhasebe işlemidir (iade/yeniden kesim), veri güncellemesi değil.
- Boş bırakılan alanlar `DateTimeOffset.UtcNow` alır (saniyeli, doğru UTC) — form değeri değildir, dokunulmaz.

## 3. Blazor kaydı mı, SPA kaydı mı?

**Kesiş (Blazor'un son çalıştığı an).** Üretim `/app`'i hiç sunmadı (F2.2 yapılmadı); SPA ve Blazor üretimde hiç birlikte
yazmadı. Blazor'u söken sürüm yayınlandığı an kesiştir: migration o yayının açılışında koştuğu için `simdi` (= migration
anı) **tam doğru** kesiştir. Geliştirme veritabanında (`racar`) iki arayüz 2026-09-21 (F1/F2) ile 2026-09-26 21:17:42+03
(#349, Blazor sayfaları silindi) arasında birlikte yazdı: tek bir kesiş yok, orada uygulanmamalı.

**Kaynak işareti yok.** `AuditLogs`'ta kanal/arayüz alanı yok; `IslemAnahtari` yalnız para ve birkaç ana kayıtta var ve
SPA'da kaydın Id'si olur (Guid, ayırt edilemez). Bu yüzden karar zamana ve kopya bağlarına dayanır:

1. Alanın **son gerçek yazımı** `AuditLogs`'tan: Create ya da eski ≠ yeni Update. Yazım kesişten önceyse Blazor formu.
   Denetim kaydı yoksa satır zamanı (`CreatedAtUtc`; dönüş/BAF dönüşü için `UpdatedAtUtc`).
2. **Tutarsız**: denetimdeki son değer güncel değer değilse (ham SQL ile değişmiş) dokunulmaz.
3. **Saniyeli değer** form değildir (`datetime-local` dakika hassasiyetinde gönderir): dokunulmaz.
4. **Kopyalar kaynağını izler** (Create değeri kaynağın güncel değerine eşitse): tekliften rezervasyon
   (`KaynakTeklifId`), rezervasyondan kira (`ReservationId`). Site talebinden rezervasyon (`SiteTalepleri.DonusenReservationId`)
   gün değeridir, form değildir: dokunulmaz. Böylece SPA'da kiraya çevrilen Blazor rezervasyonu doğru kayar; Blazor
   formunda geri gönderilen SPA değeri (görüntülenen UTC saati aynen döner) kaymaz.
5. **Türetilmişler**: dönem planı sınırı kiranın bitişine eşitse bitişi, değilse başlangıcı izler; dönüş km kaydı
   (aynı araç + aynı an) gerçek dönüşü izler.

**Kalan risk (ayırt edilemez):** kesişten önce harici `/api/v1` (JWT) ya da Excel içe aktarımıyla yazılmış dakika
hassasiyetli tarih kesişten önce olduğu için Blazor sayılır. Üretimde `/api/v1` kullanan bir entegrasyon bilinmiyor; varsa
kuru çalışmanın `-v detay=1` listesinde gözle kontrol edilmeli.

## 4. Kuru çalışma (veriye dokunmaz)

```bash
# Süper kullanıcıyla (tablo kilidi almaz) — kesiş ofsetli verilmeli:
sudo -u postgres psql racar -v kesis="$(date -Iseconds)" -f <repo>/scripts/saat-kaymasi/kuru-calistir.sql
# isteğe bağlı: -v sunucu_utc=true   -v kiraci=<firma uuid>   -v detay=1
```

Çıktı: tablo/kolon/karar/gerekçe başına adet ve eski/yeni aralığı; toplam (alan, kayıt, firma, en erken/en geç);
kaydırma sonrası çakışacak açık kira çiftleri (**boş olmalı** — doluysa uygulama reddedilir, önce elle çözülür).
**Üretimde yalnız süper kullanıcıyla (`postgres`) koş, `racar_owner` ile KOŞMA.** Owner ile koşunca RLS NO FORCE
adımı 10 tabloyu ROLLBACK'e kadar ACCESS EXCLUSIVE kilitler. O süre boyunca uygulamanın sorguları bekler ya da zaman
aşımına düşer (adversarial ölçüm: racar_app `SELECT count(*) FROM "Rentals"` → `55P03 lock timeout`). Owner ile koşmak
yalnız trafiksiz bir kopyada güvenlidir.

**Geliştirme veritabanı kopyasında sonuç** (`racar` → `racar_saat_kopya`, kesiş 2026-09-26 21:17:42+03 = #349; `racar`'a
uygulanmadı): **191 alan / 95 kayıt / 1 firma**, eski değer aralığı 2026-04-06 02:10 – 2027-06-24 13:00 (İstanbul);
çakışma 0. Kırılım: kira başlangıç/bitiş 76+76 (4'ü rezervasyondan kopya), gerçek dönüş 2, rezervasyon 9+9 (2'si
tekliften kopya), teklif 3+3, dönem planı 6+6, dönüş km kaydı 1. Dokunulmayan: SPA dönemi kira 3+3 ve plan 4 sınır,
site talebinden gelen rezervasyon ve kirası, saniyeli bir seed rezervasyonu. Migration aynı kopyada ayarla uygulandı
(191 alan, hepsi tam −3 saat, RLS FORCE geri geldi), aynı kesişle yeniden çağrı 0, farklı kesiş reddedildi, Down ile
sağlama toplamı kaynakla birebir aynı döndü.

## 5. Üretimde uygulama (F2.2 ilk yayını)

1. Yedek (F2.2 §4 zaten ister): `sudo -u postgres pg_dump racar | gzip > /var/backups/racar-$(date +%F-%H%M).sql.gz`
2. Blazor'un çalıştığı sunucunun saat dilimi: `timedatectl show -p Timezone --value`. `Etc/UTC` ya da `UTC` ise
   `sunucu_utc=true` (FormParse.Date alanları da kayık); `Europe/Istanbul` ise `false`.
3. Kuru çalışma (§4), `kesis` = şimdi. Çakışma satırı olmamalı; "Toplam" sayılarını not et.
4. `/etc/racar/racar.env` içinde Migrator bağlantısının SONUNA ekle (yalnız bu yayın için):

   ```
   ConnectionStrings__Migrator=...;Options=-c racar.saat_kaymasi_kesis=simdi
   # sunucu UTC idiyse:
   ConnectionStrings__Migrator=...;Options=-c racar.saat_kaymasi_kesis=simdi -c racar.saat_kaymasi_sunucu_utc=true
   ```

   (`ALTER DATABASE ... SET racar.saat_kaymasi_kesis` racar_owner ile reddedilir — PostgreSQL 15 yer tutucu parametreyi
   süper kullanıcıya bırakır; bağlantı `Options`'ı bu yüzden tercih edildi.)
5. `sudo <repo>/deploy/yayinla.sh` — migration açılışta koşar.
6. **Doğrula (ATLANAMAZ):** `sudo -u postgres psql racar -c 'SELECT * FROM "SaatKaymasiCalismalari"'`
   - Beklenen: tek satır, `Adet` ≈ 3. adımdaki "alan" sayısı. Aradaki dakikalarda yazılan Blazor kayıtları kadar
     fazla olabilir.
   - Ardından bir Blazor dönemi kirasını SPA'da aç: saat, formda girilenle aynı olmalı.
   - **Tablo BOŞSA kapı uygulanmamıştır.** 4. adımdaki ek unutulmuş ya da yanlış yazılmış olabilir. Migration yine de
     "uygulandı" olarak kaydedilir ve hiçbir uyarı vermez: EF çıktısında NOTICE görünmez. Veri kayık kalır; SPA
     saatleri 3 saat ileri gösterir ve HGS eşleştirmesi kayık pencerelerle çalışır.
   - Bu durumda HGS içe aktarmayı KULLANMA. Hemen aşağıdaki "elle uygulama" yolunu izle; kesiş, 5. adımın anıdır
     (yayın günlüğündeki zaman).
7. 4. adımdaki `Options` ekini racar.env'den **kaldır** (migration bir kez koşar; ek yine de kalmasın).

**Açılış "çakışma" hatasıyla durursa:** Kaydırma, aynı araçta açık kiraları çakıştıracaksa kapılı migration hata atar.
Migration açılışta koştuğu için uygulama açılmaz; transaction geri alınır, veri ve migration kaydı değişmez.
Kurtarma:
1. 4. adımdaki `Options` ekini kaldır ve yeniden başlat. Migration kapısız koşar: "uygulandı" olarak kaydedilir, veriye
   dokunmaz, uygulama açılır.
2. Kuru çalışmanın (§4) listelediği çakışan açık kiraları elle düzelt.
3. Aynı kesişle yeniden kuru çalışma yap; çakışma satırı kalmamalı.
4. Elle uygula: `sudo -u postgres psql racar -c "SELECT rc_time_shift_apply('<kesiş>+03', <sunucu_utc>)"`.
   - Kesiş, ilk denemenin anıdır (yayın günlüğündeki zaman); 1. adımdan sonra açılışın anı DEĞİL.
   - 1. adımdan sonra SPA'nın yazdığı alanların son yazımı kesişten sonradır; doğru (UTC) oldukları için kaydırılmazlar.
5. 6. adımdaki doğrulamayı yap.

1. ile 4. adım arasında HGS içe aktarmayı kullanma.

**Bu sürümden önce SPA yayına alındıysa** (migration ayarsız koştu ve SPA yazmaya başladıysa): kesiş, Blazor'u söken
yayının anıdır (`/opt/racar/releases/<zaman>` dizin adı / yayın günlüğü). Elle, ofsetli:
`sudo -u postgres psql racar -c "SELECT rc_time_shift_apply('<kesiş>+03', <sunucu_utc>)"` — önce aynı kesişle kuru çalışma.

**Geri alma:** `sudo -u postgres psql racar -c 'SELECT rc_time_shift_revert()'` — defterdeki (`SaatKaymasiDuzeltmeleri`)
her alanı, hâlâ düzeltilmiş değerdeyse eski değerine döndürür; sonradan değişmiş alanlar defterde kalır (ezilmez).
Migration Down aynısını yapar ve değişmiş alan kalırsa reddeder.

**Tekrar çalıştırma:** aynı kesişle zararsız (defter benzersiz; düzeltilmiş alan "zaten düzeltildi"). Farklı kesişle
reddedilir (`SaatKaymasiCalismalari`): sonradan yazılmış SPA kayıtlarını kaydırırdı.
