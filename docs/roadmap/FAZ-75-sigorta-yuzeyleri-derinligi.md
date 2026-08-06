# FAZ-75 — Sigorta Yüzeyleri Derinliği (Gider Filtresi + Muayene Raporu + Km Paket)

| | |
|---|---|
| **Desen** | D3 (`sigorta_gider_ara.aspx`) + D4/D1 (`sigorta_muayene.aspx`) + D2
  (`sigorta_tarife_listesi.aspx`) |
| **Efor** | 4 gün (`sigorta_gider_ara.aspx` 1g + `sigorta_muayene.aspx` 1,5g +
  `sigorta_tarife_listesi.aspx` 1,5g) |
| **Bağımlılık** | yok |
| **Kapsanan canlı ekran** | `sigorta_gider_ara.aspx`, `sigorta_muayene.aspx`,
  `sigorta_tarife_listesi.aspx` |
| **Risk** | düşük — additive alanlar + yeni salt-okunur rapor sayfası; ledger/para toplamı YOK |

## Amaç
(1) `/giderler` gerçek bir filtre formu kazanır (bugün SIFIR filtre). (2) Yeni birleşik
`/raporlar/sigorta-muayene` sayfası Sigorta/MTV/Muayene + Vehicle master bilgisini tek satırda
gösterir. (3) `/sigorta-urunleri`'ne Km Paket alt-kademeleri + "Listeleme Yöntemi" eklenir.

## Neden (kanıt)
- `src/RentACar.Application/Expenses/IExpenseRepository.cs` `ListAsync(kapsam: BranchFilter, ct)`
  (satır 10-11, doğrulandı) — parametre YALNIZ şube-kapsamı (yetki bazlı, kullanıcı tercihi değil);
  tarih/tip/araç/ofis filtresi YOK. `ExpenseList.razor` (`/giderler`) doğrulandı: filtre formu YOK,
  sadece "+ Yeni Gider" create formu + Excel/CSV/PDF export linki var; `OnInitializedAsync`
  parametresiz `Expenses.ListAsync()` çağırır — TÜM giderler filtresiz çekiliyor.
- `Expense.cs`: `VehicleId` (Guid?, mevcut), `Tarih` (mevcut) — **kısmi ödeme kavramı YOK**
  (`OdenenTutar`/`Kalan` gibi alan grep'te sıfır eşleşme; sadece `NetTutar`/`KdvTutar`/
  `GenelToplam` tam-tutar alanları var).
- `Vehicle.cs`: `SasiNo`/`MotorNo`/`AracSahibi`/`ZIzni` **VAR** (doğrulandı) — sadece rapor JOIN'i
  eksik. `BelgeNo`/`Kimde`/`SeyrusiferBitis`/`ZIzniBitis` **YOK** (grep doğrulandı, repo genelinde
  hiçbir dosyada Vehicle bağlamında geçmiyor).
- `CoverageProduct.cs`/`CoverageProductType` enum doğrulandı: `Scdw, MiniHasar, Lcf, Cdw, Pai, Imm,
  MuafiyetSigortasi, YolYardim, GencSurucu, MaxGuvence, SuperMini, PaketHizmet, KmPaketi (=12),
  Diger (=99)` — **`KmPaketi` DEĞERİ ZATEN VAR** ama alt-kademe (Km1-4) yapısı yok; tek-tip-tek-
  satır deseni Bebek Koltuğu/Navigasyon/Wifi/Şarj/Adrese Teslim/Kış Lastiği/Üyelik-İptal için zaten
  uygun (yeni enum değeri eklemek yeterli).

## Yapılacaklar
### A) sigorta_gider_ara.aspx (1 gün)
1. `src/RentACar.Application/Expenses/IExpenseRepository.cs` + `ExpenseService.cs` — `ListAsync`
   imzasına opsiyonel filtre paramları eklenir: `tip? (ExpenseType?)`, `from/to (DateTimeOffset?)`,
   `plaka (string?)`, `ofis/sube (string?)`, `sadeceAktifSigorta (bool)`.
2. `ExpenseList.razor` — Tip/Tarih/Plaka/Ofis filtre formu (Tip=Sigorta ön-seçili varyant) + Sözleşme
   No + Kira Müşteri kolonu (`Expense.VehicleId`+`Tarih` üzerinden aktif/en-yakın `RentalContract`
   ataması — Araç Karnesi'nde kurulan atıf desenine benzer, DOĞRUDAN `Rentals` sorgusu, ledger
   DEĞİL) + Hazır Açıklama (canonik şablon listesi, datalist seç-veya-yaz) + Marka/Tipi/Yakıt/Vites/
   Model (Vehicle JOIN) + "Sat_Aktif_Sigorta" checkbox filtresi.
3. "Kalan" kolonu **sabit `0` gösterilir** (not-sütunu) — gerçek kısmi-ödeme mekanizması bu fazda
   YOK (Expense'te yapısal olarak eksik, D5 sınıfı ayrı bir plan gerektirir, PLAN DIŞI).

### B) sigorta_muayene.aspx (1,5 gün)
4. `Vehicle.cs` — additive alanlar: `BelgeNo (string?)`, `Kimde (string?)`, `SeyrusiferBitis
   (DateTimeOffset?)`, `ZIzniBitis (DateTimeOffset?)` (`ZIzni bool` GERİYE-UYUM için kalır,
   silinmez).
5. `OrtakSorgular.cs`'e (paylaşılan sorgu deseni, O12a) veya `ReportRepository.cs`'e yeni birleşik
   sorgu: `InsurancePolicy` (Trafik/Kasko) + `MtvRecord` + `InspectionRecord` + Vehicle master
   (Marka/Tip/Yıl/Yakıt/Vites/Şube/Grup) TEK satırda JOIN edilir.
6. `ReportDtos.cs`'e yeni `SigortaMuayeneRow` DTO + `ReportService.cs`'e `GetSigortaMuayeneAsync`
   (filtreler: Arac_Sahibi [Bizim/Dış], Turu [Muayene/Trafik/Kasko/Z-İzni/Seyrüsefer]).
7. Yeni `src/RentACar.Web/Components/Pages/Reports/SigortaMuayeneRaporu.razor`
   (`/raporlar/sigorta-muayene`). **Mevcut `/vade` ve `/regulasyon` DEĞİŞMEZ** — bu yeni birleşik
   rapor onların ÜSTÜNE kurulur, mükerrer değil.
8. **D4 kuralı bu ekranda uygulanmaz** (bkz. Test bölümü) — envanter/vade raporu, defter toplaması
   yok, PARA değil.

### C) sigorta_tarife_listesi.aspx (1,5 gün)
9. `CoverageProductType` enum'una yeni değerler: `BebekKoltugu`, `Navigasyon`, `Wifi`, `SarjCihazi`,
   `AdreseTeslim`, `KisLastigi`, `UyelikIptalBedeli` — entity şeması DEĞİŞMEZ, her biri kendi
   `CoverageProduct` satırı olur.
10. Yeni `src/RentACar.Domain/Entities/KmPaket.cs`: `Id, TenantId, CoverageProductId (Guid, FK —
    `Tur=KmPaketi` olan üst satıra), KmMin (int), KmMax (int), Ucret (decimal)` + `ITenantOwned,
    IAuditable` — bir `CoverageProduct` "KM_PAKET" tipinde birden çok kademe satırı taşır.
11. `CoverageProduct.cs` — additive `ListelemeYontemi` enum (`SadeceParktakiler`/`TumGruplar`).
12. `src/RentACar.Application/CoverageProducts/` altına `KmPaket` CRUD (Input/Service/IRepository,
    üst `CoverageProduct`'a bağlı alt-liste) + `CoverageProductInput.cs`'e `ListelemeYontemi`.
13. `CoverageProductList.razor` — `Tur=KmPaketi` seçilince alt-kademe grid'i (KmMin/KmMax/Ucret,
    çoklu satır ekle/sil) + `ListelemeYontemi` dropdown.

## Dokunulacak dosyalar
- `src/RentACar.Application/Expenses/ExpenseService.cs`, `IExpenseRepository.cs`
- `src/RentACar.Web/Components/Pages/Expenses/ExpenseList.razor`
- `src/RentACar.Web/Expenses/ExpenseEndpoints.cs`
- `src/RentACar.Domain/Entities/Vehicle.cs` — additive `BelgeNo`, `Kimde`, `SeyrusiferBitis`,
  `ZIzniBitis`
- `src/RentACar.Infrastructure/Persistence/OrtakSorgular.cs`
- `src/RentACar.Application/Reporting/ReportDtos.cs`, `ReportService.cs`
- `src/RentACar.Infrastructure/Persistence/Repositories/ReportRepository.cs`
- (yeni) `src/RentACar.Web/Components/Pages/Reports/SigortaMuayeneRaporu.razor`
- `src/RentACar.Domain/Entities/CoverageProduct.cs`, `Enums/CoverageProductType.cs`
- (yeni) `src/RentACar.Domain/Entities/KmPaket.cs`
- `src/RentACar.Application/CoverageProducts/*` (Input/Service/IRepository)
- `src/RentACar.Web/Components/Pages/CoverageProducts/CoverageProductList.razor`

## Migration
- `Vehicle`'a 4 additive kolon (`BelgeNo`, `Kimde` string?, `SeyrusiferBitis`, `ZIzniBitis`
  timestamptz?) — tablo zaten RLS'li, RLS bloğu gerektirmez.
- Yeni tablo `KmPaket` (tenant-owned, `CoverageProductId` FK) — **RLS bloğu ELLE eklenir**
  (`ENABLE`+`FORCE`+`tenant_isolation`+`GRANT racar_app`).
- `CoverageProduct`'a additive `ListelemeYontemi` kolonu — RLS bloğu gerektirmez.
- `CoverageProductType` enum'una 7 yeni değer — additive (mevcut değerlerin sayısal karşılığı
  DEĞİŞMEZ, yeni değerler enum'un SONUNA eklenir; DB'de int olarak saklanıyorsa mevcut satırlar
  bozulmaz).

## Test
- `ExpenseServiceTests`: filtre kombinasyonları (Tip=Sigorta+tarih aralığı+plaka) elle kurulan 5
  gider satırından beklenen alt-küme SAYISI sabit yazılır (bağımsız oracle).
- `SigortaMuayeneRaporu` testi: elle kurulan 1 araç + 1 aktif poliçe + 1 MTV + 1 muayene kaydı → tek
  satırda TÜM alanların doğru JOIN edildiği doğrulanır; **D4/Test-bölümü notu:** bu rapor PARA
  TOPLAMI YAPMAZ (envanter/vade listesi) — "P&L yalnız defterden" kuralı burada UYGULANMAZ, ledger
  sorgusu bu raporda YOKTUR (kanıt: adım 5'teki sorgu `AccountLedgerEntry`'ye hiç dokunmuyor,
  sadece `InsurancePolicy`/`MtvRecord`/`InspectionRecord`/`Vehicle` okuyor).
- `KmPaketTests`: 4 kademe (`0-500→0, 501-1000→50, 1001-2000→120, 2001+→250` gibi elle sabitlenmiş
  değerler) CRUD + tenant izolasyonu (`racar_app`).
- `PeriyodikServisAsync` regresyon testi GEREKMEZ bu fazda (bu faz `OrtakSorgular.
  PeriyodikServisAsync`'e dokunmuyor — FAZ-76'nın kapsamı).

## Exit
- [ ] `/giderler` filtre formu çalışıyor, "Kalan=0" not-sütunu görünür
- [ ] `/raporlar/sigorta-muayene` yeni rapor çalışıyor, `/vade`+`/regulasyon` değişmedi
- [ ] Km Paket alt-kademeleri CRUD'da çalışıyor
- [ ] Tam suite yeşil

## Notlar
Kısmi-ödeme/"Kalan" gerçek mekanizması (D5 sınıfı, defter etkili) bu PR'da **PLAN DIŞI** — ayrı bir
plan gerekir. `sigorta_muayene` bölümünde D4 etiketi biçimsel (canlı ekranı raporlar modülünde
sınıflandırma amaçlı); gerçek davranış envanter/vade raporu olduğundan defter kuralı bağlayıcı
değildir.
