# FAZ-79 — Karlılık/Gelir Tablosu Çok-Boyutlu Genişleme

| | |
|---|---|
| **Desen** | D4 — **PARA — Opus** (referans-maliyet gösterimi + Potansiyel-gelir formülü, aşağıda) |
| **Efor** | 3 gün |
| **Bağımlılık** | yok (yapısal olarak) |
| **Kapsanan canlı ekran** | `gelir_tablosu.aspx` |
| **Risk** | orta — modülün en büyük tek genişlemesi (40 canlı kolonun ~15'i); **P&L-defter kuralı
  ihlali riski en yüksek olan faz** (aşağıda Test bölümünde bağlayıcı) |

## Amaç
`/raporlar/karlilik` (`Karlilik.razor`) Doluluk/Araç Başı Gelir/RentTo ayrımı/SIPP/Rez. Kaynağı
kırılımı/Cari Bilgi-Bakiye/Otopark/Kdv-Durum-modu/Maliyet-referans/Potansiyel-gelir kolonlarıyla
genişler — **P&L toplamı (Gelir/Gider/NetKar) DAİMA ledger'dan gelir, yeni kolonların hiçbiri bu
toplama karışmaz.**

## Neden (kanıt) — P&L kaynağı KRİTİK doğrulama
- `ReportService.GetKarlilikAsync(from?, to?, sube?, grup?, plaka?, ct)` (imza doğrulandı) →
  `_repository.GetKarlilikRowsAsync(...)`. Repo implementasyonu (`ReportRepository.cs:362-479`,
  doğrulandı): Gider = `db.AccountLedgerEntries.Where(AccountType==Gider ...)` (tutar `Amount ×
  Rate`, ledger'dan); Gelir = `db.AccountLedgerEntries.Where(AccountType==Gelir ...)` (ledger'dan).
  `Invoices, Rentals, VehicleSales, Penalties, ServiceRecords, DepozitoIratlar,
  DisHizmetAlimlari` **SADECE SourceId→araç atıf haritası kurmak için** okunuyor — bu tablolardaki
  `Expense.Tutar`/`Rental.GenelToplam` gibi alanlar P&L'e **KATILMIYOR**. Kod-içi yorum bunu
  doğruluyor: `ReportService.cs:8` *"çift-taraflı defter (AccountLedgerEntry) ÜSTÜNDE toplama...
  Yeni tablo/yazım YOK"*; `ReportDtos.cs:143` *"Tutarlar DEFTERDEN: Gider = Σ Gider(Debit)
  AccountRef=araç; Gelir = Σ Gelir(Credit) base, SourceId→Fatura→Kira→Araç ile atfedilir."*
- `AccountLedgerEntry.cs` alanları doğrulandı: `Id, TenantId, EntryDateUtc, AccountType, AccountRef
  (Guid?), Direction, Amount (Money), Description, SourceType, SourceId, SignedBase` —
  **`VehicleId` kolonu YOK** (grep sıfır eşreşme; plan dosyasının iddiası DOĞRULANDI). Araç ilişkisi
  yalnızca generic `AccountRef` (Gider tarafı) veya `SourceType`/`SourceId` dolaylı zinciri (Gelir
  tarafı) üzerinden kurulur.
- `KarlilikSatirDto` bugünkü alanları doğrulandı: `(VehicleId?, Plaka, Sube?, Grup?, Segment?,
  Gelir, Gider, NetKar)` — RentTo ayrımı/SIPP/Rez.Kaynağı/Cari-Bakiye/Otopark/Kdv-Durum YOK.
  `AracKpiDto` (Araç Karnesi'nden, mevcut) alanları doğrulandı — Doluluk/RevPACD/ADR ZATEN VAR,
  `KarlilikSatirDto`'ya henüz BAĞLANMAMIŞ.
- `Vehicle.cs` satır 93/95: `AylikMaliyet (decimal?)`, `FiloYonetimMaliyeti (decimal?)` — **MEVCUT**
  master alanlar (doğrulandı), "referans maliyet" kolonunun kaynağı.

## Yapılacaklar
1. `src/RentACar.Application/Reporting/ReportDtos.cs` — `KarlilikSatirDto`'ya EK (P&L'e KARIŞMAYAN,
   ayrı/referans) alanlar: `Doluluk, AracBasiGelir, OrtalamaFiyat` (mevcut `AracKpiDto`'dan bağlanır
   — YENİ hesap değil, var olan KPI'nın bu satıra JOIN'i), `RentToTuru (Rental/Reservation)`,
   `SippKodu` (`VehicleGroup.Sipp` join), `RezKaynagi`, `CariBakiye` (`GetCariBalancesAsync` join),
   `Otopark` (`Vehicle.SubeId`), `ReferansAylikMaliyet` (`Vehicle.AylikMaliyet`),
   `ReferansFiloYonetimMaliyeti` (`Vehicle.FiloYonetimMaliyeti`), `PotansiyelGelir (decimal?)`.
2. `ReportService.GetKarlilikAsync` imzasına `kdvDurum` parametresi (`Kdvsiz`/`KdvDahil`) — **SALT
   GÖSTERİM anahtarı**: `Gelir`/`Gider`/`NetKar` DEĞERLERİ DEĞİŞMEZ, yalnız UI'da KDV dahil/hariç
   nasıl formatlandığı değişir (ledger tutarları zaten net taşınıyor — KDV ayrıştırması varsa
   `Invoice.KdvTutar` referans amaçlı EKLENİR, ana toplamdan ÇIKARILMAZ).
3. `ReportRepository.GetKarlilikRowsAsync` — Doluluk/RevPACD/ADR için mevcut Araç Karnesi KPI
   hesabını (`AracKpiDto` üreten sorgu) ÇAĞIRIR/JOIN eder (kod tekrarı yasak — aynı hesap iki yerde
   yeniden yazılmaz); SIPP için `VehicleGroup` JOIN; Cari Bakiye için `GetCariBalancesAsync`
   (mevcut, cari modülünden) JOIN; `ReferansAylikMaliyet`/`ReferansFiloYonetimMaliyeti` DÜZ
   `Vehicle` alan okuması (ledger'a KARIŞMAZ, ayrı sütun olarak taşınır).
4. `PotansiyelGelir` formülü **PARA — Opus kararı gerektirir** (hangi tarife kaynağından —
   `RateCard` mı `RateMatrix` mi — türetileceği): bu fazda formül YAZILMAZ, kolon `null`
   döner + UI'da "Hesaplanmadı" notu; Opus kararı geldiğinde ayrı takip-PR'ı doldurur.
5. `Karlilik.razor` — yeni kolonlar tabloya eklenir; **Maliyet kırılımı (Ana/Yönetim Maliyeti) ve
   Potansiyel gelir kolonları GÖRSEL OLARAK AYRI bir blok/renk ile "Referans — P&L Toplamına
   Katılmaz" etiketiyle gösterilir** (kullanıcının bu sayıları yanlışlıkla Gelir/Gider/NetKar ile
   karıştırmaması için UI'da açık ayrım — mutabık kalması BEKLENMEDİĞİ AÇIKÇA işaretlenir).
   `KdvDurum` toggle (Kdvsiz/Kdv Dahil) eklenir.

## Dokunulacak dosyalar
- `src/RentACar.Application/Reporting/ReportDtos.cs`
- `src/RentACar.Application/Reporting/ReportService.cs`
- `src/RentACar.Infrastructure/Persistence/Repositories/ReportRepository.cs`
- `src/RentACar.Web/Components/Pages/Reports/Karlilik.razor`

## Migration
Yok — tüm yeni alanlar mevcut tablolardan (JOIN) veya hesaplanan (KPI) değerler; yeni
tablo/kolon gerekmiyor.

## Test — P&L-defter kuralı BAĞLAYICI (bu fazın en kritik bölümü)
- **Çift-sayım yasağı testi (zorunlu, bağımsız oracle):** elle kurulan senaryo — 1 araç, ledger'a
  postlanmış `Gelir=10.000` + `Gider=4.000` (SABİT, testte elle yazılan defter kayıtları) + AYNI
  aracın `Vehicle.AylikMaliyet=1.500` (referans, deftere POSTLANMAMIŞ). Beklenen:
  `KarlilikSatirDto.Gelir==10.000`, `Gider==4.000`, `NetKar==6.000` (`Gelir-Gider`) —
  `ReferansAylikMaliyet` alanı SATIRDA GÖRÜNÜR (`==1.500`) ama `Gider`/`NetKar` hesabına HİÇ
  KARIŞMAZ (yani `Gider` DEĞERİ 4.000, ASLA 5.500 olmaz). Bu test, kaynak-varlık tutarının (Vehicle
  master alanı) P&L toplamına sızmadığının doğrudan kanıtıdır.
- **Σ kontrolü:** `GetKarlilikRowsAsync`'in döndürdüğü TÜM satırların `Gelir`/`Gider` toplamı, aynı
  dönem için `AccountLedgerEntries` tablosundan bağımsız bir SQL/LINQ ile (test kodunda, servis
  kodunu ÇAĞIRMADAN) hesaplanan Σ Gelir(Credit)/Σ Gider(Debit) ile EŞİT çıkar (bağımsız
  çapraz-doğrulama — raporun kendi kendini doğrulaması DEĞİL, testin ayrı bir sorguyla kontrol
  etmesi).
- **PotansiyelGelir testi:** bu fazda formül yazılmadığından, `PotansiyelGelir` her satırda `null`
  döner + UI'da "Hesaplanmadı" render edildiği doğrulanır (yanlışlıkla 0 ya da yanıltıcı bir değer
  DÖNMEDİĞİ negatif test).
- `KdvDurum` toggle testi: `Kdvsiz`/`KdvDahil` iki modda da `NetKar` DEĞERİ AYNI kalır (salt
  gösterim biçimi değişir, tutar değişmez) — regresyon testi.
- Regresyon: mevcut `GetKarlilikAsync` çağıranları (Karlilik.razor dışındaki, varsa) bu fazdan
  önce/sonra aynı `Gelir`/`Gider`/`NetKar` sonuçlarını üretir.

## Exit
- [ ] Yeni kolonlar (Doluluk/AracBasiGelir/SIPP/RezKaynagi/CariBakiye/Otopark/KdvDurum) çalışıyor
- [ ] Referans-maliyet kolonları UI'da P&L'den görsel olarak ayrık, "katılmaz" etiketli
- [ ] Çift-sayım yasağı testi (bağımsız oracle) yeşil
- [ ] Σ çapraz-doğrulama testi yeşil
- [ ] `PotansiyelGelir` Opus kararına kadar `null` + "Hesaplanmadı" (negatif test yeşil)
- [ ] Tam suite yeşil

## Notlar
"Ana Maliyet/Yönetim Maliyeti"nin defter P&L'iyle YAN YANA gösterimi ve "Potansiyel gelir"
formülünün tarife kaynağı (RateCard/RateMatrix) **AYRI Opus kararlarıdır**; kolon iskeleti bu
kararlara bağlı değildir ve bu fazda eklenir. Bu faz, modülün P&L-ilgili TEK gerçek karlılık
raporu olduğundan CLAUDE.md'nin "P&L yalnız defterden okunur" kuralının en sıkı uygulanması
gereken yerdir — kod incelemesinde bu kuralın ihlali (herhangi bir kaynak-varlık alanının
`Gelir`/`Gider`/`NetKar` toplamına eklenmesi) **Critical bulgu** sayılmalıdır.
