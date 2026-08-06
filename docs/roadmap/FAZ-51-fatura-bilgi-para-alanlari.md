# FAZ-51 — Manuel Fatura Formu: Bilgi Alanları + Vergi/Belge Alanlarının Deftere Yansıması

| | |
|---|---|
| **Desen** | D1/D2 (bilgi alanları) + D5 (Otv/Tevkifat/Damga ledger yansıması — PARA) |
| **Efor** | 3,5 gün (1 gün bilgi alanları + 2,5 gün PARA/Opus) |
| **Bağımlılık** | yok |
| **Kapsanan canlı ekran** | `fatura.aspx` (e-Fatura/GİB'e özgü alanlar hariç — bkz. `_toplama-05-finans.md` BLOKE) |
| **Risk** | orta — (a) kısmı risksiz (yalnız bilgi alanı ekleme); (b) kısmı YÜKSEK (mevcut dengeli
  fatura defterine yeni satır türü ekleniyor, formül kararı Opus'a bırakılır) |

**Zorunlu:** adversarial inceleme (bu fazın PARA/(b) kısmı için) — Critical/High/Medium bulgu
kalmadan commit yok.

## Amaç
Kullanıcı manuel fatura formunda canlıdaki İşlem Şube/Fatura Saat/Evrak No/Fatura Özel Kod/Ödeme
Türü/Gönderim Şekli/KDV Sıfır Sebep bilgi alanlarını girebilir; ayrıca entity'de zaten var olan ama
hiç forma açılmamış ÖTV/Tevkifat/Damga Vergisi alanlarını girip bunların deftere DENGELİ şekilde
yansıdığını görür (şu an bu alanlar entity'de dolsa da ledger'a hiç yansımıyor).

## Neden (kanıt)
`src/RentACar.Web/Components/Pages/Finance/InvoiceList.razor`'daki manuel-fatura formu
(`ManualInvoiceInput`, `src/RentACar.Application/Finance/ManualInvoiceInput.cs`) yalnız 6 alan
taşıyor: `CariId`, `Aciklama`, `NetTutar`, `KdvOrani`, `Tarih`, `VadeTarihi` — `Islem_Sube`,
`Fatura_Saat`, `Evrak_No`, `Fatura_Ozel_Kod`, `Odeme_Turu`, `Gonderim_Sekli`, `Kdv_Sifir_Sebep`
canlıda var, bizde hiçbiri yok (grep doğrulandı).

`Invoice.cs` (L37-53) `Otv`/`TevkifatOran`/`TevkifatTutar`/`DamgaVergisi`/`KaynakFaturaId` alanlarını
ZATEN taşıyor ama entity'nin kendi XML yorumu şunu açıkça söylüyor (L38-40): *"Defter postlamasını
DEĞİŞTİRMEZ: kayıt halen Borç Cari (GenelToplam) / Alacak Gelir (NetTutar) / Alacak KDV (KdvTutar) —
dengeli. Bu alanlar faturada gösterilir; v1'de ledger'a yansımaz."* `InvoiceService.CreateManualAsync`
(L344-383) bu alanları HİÇ okumuyor/set etmiyor — form da bunları hiç sormuyor.

## Yapılacaklar
1. **(a) Bilgi alanları (D1/D2, PARA DEĞİL):**
   - `src/RentACar.Domain/Entities/Invoice.cs`: `IslemSube` (string?), `FaturaSaat` (TimeSpan? veya
     `Tarih` ile birleşik saat bileşeni — mevcut `Tarih` DateTimeOffset zaten saat taşıyor, bu yüzden
     ayrı kolon EKLENMEZ, form'da tek `datetime-local` input'la Tarih+Saat birlikte girilir — canlı
     paritesi işlevsel olarak korunur, kolon çoğaltılmaz), `EvrakNo` (string?), `FaturaOzelKod`
     (string?), `OdemeTuru` (string?, "Kart"/"Havale"/"Nakit" — ComboBox seç-veya-yaz), `GonderimSekli`
     (string?, "Mail"/"Kargo"/"Posta"), `KdvSifirSebep` (string?, 4 seçenekli select) eklenir.
   - `src/RentACar.Application/Finance/ManualInvoiceInput.cs`: yukarıdaki 6 alan eklenir.
   - `src/RentACar.Application/Finance/InvoiceService.cs` `CreateManualAsync` (L344): yeni alanları
     `Invoice`'a map eder (defter postlaması `BuildEntries` DEĞİŞMEZ).
   - `InvoiceList.razor`: manuel-fatura formuna 6 input eklenir (`OdemeTuru`/`GonderimSekli`
     `<input list>`+`<datalist>` ComboBox konvansiyonu — CLAUDE.md memory "combobox-sec-veya-yaz").
2. **(b) PARA alanları — Otv/Tevkifat/Damga ledger yansıması (D5, Opus kararı gerekli):**
   - `ManualInvoiceInput.cs`: `Otv` (decimal?), `TevkifatOran` (decimal?), `DamgaVergisi` (decimal?)
     eklenir (yapısal — form alanı + servis parametresi).
   - `InvoiceList.razor`: bu 3 alan forma açılır (opsiyonel, boş bırakılabilir).
   - **KARAR GEREKLİ — Opus/kullanıcı kararı (bu faz kararı VERMEZ):** bu tutarların deftere NASIL
     yansıyacağı iki alternatiften biri seçilmeli:
     **(i) Ayrı ledger satırı** — ör. Damga Vergisi için `Borç Cari(damga) / Alacak Gider(damga
     beyan yükümlülüğü)` şeklinde EK bir dengeli çift, `GenelToplam`'a dahil edilir edilmez ayrı
     karar; Tevkifat için tutulan/ödenmeyen kısmı `Borç Kdv(tevkifat-tutulan)` gibi ayrı hesap türü
     gerekebilir (`LedgerAccountType`'a yeni değer eklenmesi gerekip gerekmediği de bu kararın
     parçası). **(ii) GenelToplam'a dahil, ayrı satır yok** — bilgi amaçlı kalır, defter formülü
     değişmez (bugünkü davranış, sadece alan artık forma açık). (ii) daha risksiz ama canlı paritesi
     eksik kalır (canlıda bu tutarlar muhasebeleşiyor); (i) tam parite ama `LedgerAccountType` enum
     genişlemesi + yeni test seti gerektirir.
   - Bu faz **yapısal iskeleti** kurar (form alanları + servis parametresi + entity'ye yazma) ve
     VARSAYILAN olarak (ii)'yi uygular (mevcut `BuildEntries` dokunulmaz, regresyon riski sıfır);
     (i) seçilirse AYRI bir sonraki faz olarak (yeni `LedgerAccountType` değeri + migration + dengeli
     satır formülü + zorunlu adversarial) kullanıcı onayı sonrası açılır.
3. `InvoiceService.CreateManualAsync`'e `input.Otv`/`TevkifatOran`/`DamgaVergisi` (varsa
   `TevkifatTutar = NetTutar * TevkifatOran`) map edilir, `Invoice` entity'sine yazılır (defter
   postlaması değişmez — madde 2'nin (ii) kararı).

## Dokunulacak dosyalar
- `src/RentACar.Web/Components/Pages/Finance/InvoiceList.razor`
- `src/RentACar.Application/Finance/InvoiceService.cs` (`CreateManualAsync`)
- `src/RentACar.Application/Finance/ManualInvoiceInput.cs`
- `src/RentACar.Domain/Entities/Invoice.cs` (yalnız (a) için yeni alanlar; (b) alanları ZATEN var)
- `src/RentACar.Web/Finance/FinanceEndpoints.cs` (`/finans/fatura-manuel`, L175)
- (yeni) migration `AddInvoiceBilgiAlanlari` (yalnız (a)'nın 6 string alanı için)

## Migration
Var — `Invoices` tablosuna 6 nullable string kolon ((a) bilgi alanları). RLS bloğu **gerekmez**
(mevcut tenant-owned tabloya additive kolon, RLS zaten aktif). (b) kısmı bu fazda migration
GEREKTİRMEZ (`Otv`/`TevkifatOran`/`TevkifatTutar`/`DamgaVergisi` zaten kolon olarak var).

## Test
- (a) `InvoiceManualTests.cs`'e ek senaryo: 6 bilgi alanı dolu manuel fatura oluştur → round-trip
  (kaydet→oku, bağımsız oracle: elle girilen değerlerle karşılaştır); defter kaydı (Borç Cari/Alacak
  Gelir+KDV) DEĞİŞMEDİ (regresyon — mevcut `InvoiceTests.cs` tam suite yeşil).
- (b) Aynı test dosyasına: `Otv`/`TevkifatOran`/`DamgaVergisi` dolu manuel fatura oluştur →
  `Invoice` entity'sinde doğru saklanıyor MU (bağımsız oracle: elle hesaplanan `TevkifatTutar`);
  **defter dengesi** `Σ Borç(base) == Σ Alacak(base)` hâlâ tutuyor (bu değerler ledger'a
  YANSIMADIĞI için toplam DEĞİŞMEMELİ — bu regresyon testi (ii) kararının doğrulamasıdır).

## Exit
- [ ] `IslemSube`/`EvrakNo`/`FaturaOzelKod`/`OdemeTuru`/`GonderimSekli`/`KdvSifirSebep` formda +
  kaydediliyor
- [ ] `Otv`/`TevkifatOran`/`DamgaVergisi` formda + entity'ye yazılıyor (bilgi amaçlı, ledger'a
  yansımıyor — (ii) kararı doğrulanmış)
- [ ] Defter dengesi regresyonu YOK (mevcut testler yeşil)
- [ ] Adversarial inceleme (b) kısmı için: Critical/High/Medium bulgu YOK
- [ ] Tam suite yeşil

## Notlar
**Otv/Tevkifat/Damga'nın deftere ayrı satır olarak mı yansıyacağı (seçenek (i)) Opus/kullanıcı
kararıdır** — bu faz varsayılan güvenli yolu ((ii), bilgi amaçlı) uygular; tam parite istenirse ayrı
bir faz olarak (`LedgerAccountType` genişlemesi + migration + zorunlu adversarial) açılmalı.

e-Fatura/GİB'e özgü alanlar (`EFatura_Firma`, `Cari_Entegre`, `EFatura_Tipi`/`Fatura_Sablon`/
`EFatura_ID`/`FaturaSonDurum`, manuel çoklu döviz girişi, tenant-config bayrakları) BU FAZA DAHİL
DEĞİL — gerçek e-Fatura/GİB entegratör kimliği gerekir (bkz. `_toplama-05-finans.md` BLOKE).
