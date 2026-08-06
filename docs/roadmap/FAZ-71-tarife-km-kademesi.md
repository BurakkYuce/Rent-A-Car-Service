# FAZ-71 — Tarife Km Kademesi (Gün-Kademesi Bazlı KM Limiti + Aşım Ücreti)

| | |
|---|---|
| **Desen** | D2 — **PARA — Opus** (fiyat motoru formül kararı, aşağıda) |
| **Efor** | 1,5 gün |
| **Bağımlılık** | yok |
| **Kapsanan canlı ekran** | `tarifeler.aspx` (bölüm a — KM kademesi kısmı; teminat/görünürlük
  kısmı FAZ-72'de) |
| **Risk** | orta — fiyat motorunun ön-izleme (preview) hesabına dokunuyor; sözleşmeye yazılan
  gerçek KM-aşım parası (`RentalContract.KmLimit`/`FazlaKmUcret`, dönüş-zamanı) BU FAZDA
  DEĞİŞMİYOR (aşağıda "Neden" bölümündeki mimari netlik önemli) |

## Amaç
Fiyat motorunun (`RentalQuoteEngine`) ön-izleme ekranında gösterdiği KM-aşım tahmini, canlı
TürevRent'teki gibi **kira süresinin düştüğü gün-kademesinin KENDİ km limiti + KENDİ aşım
ücretiyle** hesaplanır hale gelir — bugün TEK bir global (araç-grubu bazlı) değer kullanılıyor.

## Neden (kanıt) — kalibrasyon bulgusu + mimari düzeltme
**Canlı bulgu (para kalibrasyonu sırasında):** TürevRent'te her gün-kademesinin (`Km1..Km6`) KENDİ
km limiti ve KENDİ aşım ücreti (`Km1_Ucret..Km6_Ucret`) var — 1-3 gün kademesi farklı bir km
limiti/ücreti taşıyabilir, 30+ gün kademesi başka bir değer taşıyabilir. Bizde bu GLOBAL:
`src/RentACar.Domain/Entities/VehicleGroup.cs` satır 64-67 — `GunlukKmLimiti`/`AylikMaxKm`/
`AsimKmUcreti` araç grubu başına TEK değer, gün-kademesinden bağımsız.

**Mimari düzeltme (kod okunarak doğrulandı — plan dosyasının "RateCard zaten kademe-bazlı" iddiası
YANLIŞ hedefe işaret ediyor):**
- Plan dosyası bu alanların `RateCard`'a eklenmesini öneriyor. Ama `RateCard` **DEPRECATED**:
  `src/RentACar.Application/Pricing/RateCardService.cs:31` → `[Obsolete("RentalQuoteEngine/
  RateMatrix kullanın; RateCard fiyat çözümü yalnız geriye-uyum fallback'idir.")]`. Aktif fiyat
  motoru yolu `PricingService.cs` içinde AÇIKÇA yazılı: `RateMatrix` eşleşmezse `#pragma warning
  disable CS0618` ile `RateCard`'a DÜŞÜLÜYOR (satır ~112-118) — yani RateCard yalnız RateMatrix
  BULUNAMADIĞINDA devreye giren ikincil bir yoldur.
- Aktif/birincil gün-kademesi yapısı `RateMatrix`'te: `Gun1..Gun7` + `GunHaftalik`/`GunAylik`
  (`src/RentACar.Domain/Entities/RateMatrix.cs` satır 42-53), seçim mantığı
  `RentalQuoteEngine.ResolveTierRate` (satır 287-315). **Bu fazın Km alanları buraya eklenmelidir**
  — RateCard'a eklenirse yalnız (nadir kullanılan) fallback yolunu besler, çoğu "Otomatik" fiyatlı
  kirada hiç okunmaz.
- KM-aşım tahmininin bugünkü tek okuma noktası: `RentalQuoteEngine.cs` satır 102-123 —
  `grup.GunlukKmLimiti`/`grup.AsimKmUcreti` (VehicleGroup, tek/global) `req.TahminiKm` ile
  çarpılıyor (satır 112-117). Bu blok `matris` (RateMatrix, tier bilgisi elinde) değişkenine hiç
  bakmıyor.
- **KRİTİK sınır (mimari netlik):** bu tahmin YALNIZ ön-izlemedir. Gerçek parayı belirleyen tek
  otorite dönüş-zamanı hesabıdır: `src/RentACar.Application/Bookings/ReturnMath.cs` satır 23-25
  yorumu — *"KM-aşım PARASININ TEK OTORİTESİ dönüş-zamanıdır (KURAL A); fiyat motorunun create-
  zamanı KmAsimTutar TAHMİNİ asla para olarak persist edilmez."* — `ReturnMath.Compute`
  `c.KmLimit`/`c.FazlaKmUcret` (RentalContract'a MANUEL girilen, TOPLAM/tek değer — grep doğrulandı:
  `BookingInput.KmLimit`/`FazlaKmUcret`, `BookingEndpoints.cs:259-260` form alanları `kmLimit`/
  `fazlaKmUcret`) okur. Bu faz `RentalContract.KmLimit`/`FazlaKmUcret`'in nasıl dolduğuna
  DOKUNMAZ — yalnız ön-izleme tahminini kademe-farkındalı yapar.

## Yapılacaklar
1. `src/RentACar.Domain/Entities/RateMatrix.cs` — `Gun1..Gun6` fiyat kolonlarının yanına 12
   additive nullable kolon: `Km1..Km6 (int?)` + `Km1Ucret..Km6Ucret (decimal?)` (canlı alan adlarıyla
   birebir — `Km1_Ucret` → `Km1Ucret`). **Gün7 / `GunHaftalik` / `GunAylik` kademeleri için ayrı Km
   alanı EKLENMEZ** (canlı yalnız `Km1..Km6` taşıyor) — bu kademelerde adım 3'teki resolver `Km6`'ya
   düşer (en yüksek tanımlı kademe), o da boşsa `VehicleGroup` global değerine (geriye uyum).
2. `src/RentACar.Application/RateMatrices/RateMatrixInput.cs` + `RateMatrixService.cs` — 12 yeni
   alan `Normalize()` (negatif red — `RequireNonNegativeInt`/`RequireNonNegativeDec` deseniyle) +
   `Apply()`'a eklenir.
3. `src/RentACar.Application/Pricing/RentalQuoteEngine.cs` — `ResolveTierRate`'e paralel yeni
   private helper `ResolveTierKm(RateMatrix m, int gun, List<string> notlar)` → `(int? limit,
   decimal? ucret)` döner; TIER SEÇİMİ `ResolveTierRate` ile AYNI algoritma (1-6 aralığında clamp +
   boşsa önce-aşağı-sonra-yukarı en-yakın-dolu; `gun>=7` ise `Km6` kullanılır, not düşülür). Satır
   102-123'teki blok GÜNCELLENİR: önce `matris`'ten `ResolveTierKm` çağrılır; `limit`/`ucret` NULL
   ise (tenant henüz doldurmadıysa) mevcut `grup.GunlukKmLimiti`/`AsimKmUcreti × gün` davranışına
   DÜŞÜLÜR (geriye uyum — bugünkü davranış hiç bozulmaz).
4. `RateMatrixList.razor` — `Gun1..Gun6` alanlarının yanına eşlenik `Km1..Km6`/`Km1Ucret..Km6Ucret`
   form input çifti (6 satır × 2 kolon) eklenir.
5. `RateMatrixEndpoints.cs` — form-parse: 12 yeni alan `FormParse.Int`/`FormParse.Dec` ile
   (opsiyonel — CLAUDE.md §5 tuzağı: boş string `""` gelirse 400 vermemesi için `string?` alıp
   `FormParse` ile çevir).

## Dokunulacak dosyalar
- `src/RentACar.Domain/Entities/RateMatrix.cs` — additive `Km1..Km6`, `Km1Ucret..Km6Ucret`
- `src/RentACar.Application/RateMatrices/RateMatrixInput.cs`
- `src/RentACar.Application/RateMatrices/RateMatrixService.cs`
- `src/RentACar.Application/Pricing/RentalQuoteEngine.cs` — `ResolveTierKm` + km-aşım bloğu (satır
  ~102-123)
- `src/RentACar.Web/Components/Pages/RateMatrices/RateMatrixList.razor`
- `src/RentACar.Web/RateMatrices/RateMatrixEndpoints.cs`

## Migration
`RateMatrix`'e 12 additive nullable kolon (`Km1..Km6 int?`, `Km1Ucret..Km6Ucret decimal(19,4)?`) —
tablo zaten RLS'li; yeni kolon RLS bloğu **gerektirmez**.

## Test
- `RentalQuoteEngineTests` — bağımsız oracle, elle kurulan `RateMatrix` satırı: `Km2=500,
  Km2Ucret=2.50m` (2. kademe = 4-7 gün, mevcut `Gun1..Gun7` tier mantığıyla hizalı), `TahminiKm=1200`
  isteği ile `gun=5`. Beklenen: `kmAsim = max(0, 1200-500) × 2.50 = 700 × 2.50 = 1750.00` — bu değer
  TESTTE SABİT yazılır (motor kodundan türetilmez).
- Aynı senaryo `Km2` NULL bırakılıp `VehicleGroup.GunlukKmLimiti=200`/`AsimKmUcreti=3m` set
  edildiğinde: beklenen `kmAsim = max(0, 1200-200×5) × 3 = 200×3 = 600.00` (geriye-uyum davranışı —
  bugünkü koddan bağımsız, elle hesaplanmış).
- Tier-eleme testi: `gun=9` (Gün7/üstü) isteğinde `Km6`'nın kullanıldığı + notlar listesine bilgi
  notu düştüğü doğrulanır.
- **KURAL A regresyon testi (zorunlu):** bu fazın hiçbir değişikliği `RentalContract.KmLimit`/
  `FazlaKmUcret`'in create-zamanında OTOMATİK doldurulmasına YOL AÇMADIĞINI kanıtlayan bir test —
  yeni `RateMatrix` Km alanları dolu olsa bile, booking create sonrası `RentalContract.KmLimit`
  hâlâ SADECE `BookingInput.KmLimit` (kullanıcı girdisi) değerini taşır (motor tahmini sözleşmeye
  SIZMAZ). Bu, ReturnMath.cs'teki "TEK OTORİTE dönüş-zamanı" kuralının bu fazda bozulmadığının
  kanıtıdır.

## Exit
- [ ] `RateMatrix` 12 Km alanı CRUD'da çalışıyor
- [ ] `ResolveTierKm` tier-seçim algoritması `ResolveTierRate` ile birebir tutarlı (aynı clamp/
      en-yakın-dolu davranışı) — birim testli
- [ ] Km alanı NULL'sa `VehicleGroup` global fallback'i BOZULMADAN çalışıyor (geriye uyum testli)
- [ ] KURAL A regresyon testi yeşil (sözleşmeye otomatik sızma yok)
- [ ] Tam suite yeşil

## Notlar
**PARA — Opus kararı gereken açık noktalar** (bu faz bunları VARSAYIMLA ilerletir, ama kod-review'da
kesinleştirilmeli):
1. `Km{n}` değeri KADEME için TOPLAM mı yoksa GÜNLÜK mü (canlı ekranda bu netleşmedi)? Bu faz
   `VehicleGroup.GunlukKmLimiti` ile PARALEL kalması için GÜNLÜK yorumlayıp `limit × gun`
   uyguluyor — canlı-tarama doğrulaması bunu değiştirebilir.
2. Gün7/haftalık/aylık kademelerin `Km6`'ya düşmesi bir VARSAYIMDIR (canlı yalnız 6 kademe
   tanımlıyor, bizim fiyat kademesi 7+uzun-dönem) — alternatif: bu kademelerde limit UYGULANMAZ
   (sınırsız). Karar Opus/kullanıcı onayı gerektirir, bu faz varsayımı NOT olarak koda yazar.
3. Bu tahminin gelecekte `BookingInput.KmLimit`/`FazlaKmUcret`'i OTOMATİK doldurup doldurmayacağı
   (bugün tamamen manuel) AYRI bir davranış-değişikliği kararıdır — bu faz kapsamında DEĞİL.
