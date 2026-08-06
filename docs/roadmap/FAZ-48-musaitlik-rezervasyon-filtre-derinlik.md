# FAZ-48 — Müsaitlik & Rezervasyon Filtre+Alan Derinliği

| | |
|---|---|
| **Desen** | D3 (×2 ekran grubu: müsaitlik arama + rezervasyon) |
| **Efor** | 4 gün (= müsaitlik grubu 1,5g [musait_arac_listesi 1,0 + musaitlik_durum 0,5] +
  rezervasyon grubu 2,5g [rezervasyon.aspx 1,5 + rezervasyon_listesi.aspx 1,0] — çok-taraflı bakiye/
  komisyon Opus kararı sonrası ayrı efor, bu toplamın DIŞINDA) |
| **Bağımlılık** | yok (çok-taraflı bakiye/komisyon kolonlarının Opus kararı ayrı bir takip fazına
  bırakılır — bkz. Notlar) |
| **Kapsanan canlı ekran** | `musait_arac_listesi.aspx`, `musaitlik_durum.aspx`, `rezervasyon.aspx`,
  `rezervasyon_listesi.aspx` |
| **Risk** | düşük — para hesaplaması bu fazda YOK (mevcut `RentalQuoteEngine` davranışı değişmiyor,
  yalnız girdi parametresi artıyor); rezervasyon tarafı salt-okur Ota* yüzeyleme + arama filtresi |

## Amaç
Müsaitlik arama sonuç tablosuna araç detay kolonları (SIPP/Km Limiti/Yakıt/Vites/Yaş/Ehliyet) ve
Döviz/Dönüş-ofis/Rez Kaynak/Gün/Saat parametrelerini; rezervasyon formuna canlıdaki "Brokerden Gelen
Bilgisi" (Ota* — zaten entity'de var) sekmesini ve 4 yeni alanı (Talep Türü, Geldiği Birim, Onay
Kodu, Proje Adı); rezervasyon listesine SIFIR olan arama/filtre barını ekleyerek kullanıcı
`/musaitlik` ve `/rezervasyonlar`'da canlı paritesine yakın arayabilir hale gelir.

## Neden (kanıt)
- `src/RentACar.Web/Components/Pages/Availability/MusaitlikArama.razor` (satır 32) sonuç tablosu
  `<thead>`'i `Plaka/Marka/Grup/Şube/Günlük/Toplam/Döviz` — SIPP (`Vehicle.Sipp` satır 33), Km
  Limiti (`Vehicle.KiraKmLimiti` satır 117), Yakıt Türü (`Vehicle.Yakit` satır 69), Vites
  (`Vehicle.Vites` satır 40), Yaş (`Vehicle.ModelYili` satır 38'den hesaplanır), Ehliyet
  (`VehicleGroup.SurucuMinYas` satır 42) **kolonda yok** — hepsi entity'de zaten var (D3, en ucuz
  sınıf). Form (satır 19-25) yalnız `from/to/grup/sube` — `Doviz`, `Donus` (ayrı dönüş ofisi),
  `RezKaynak`, `KiraGun` (elle gün girişi), `BasSaat`/`BitSaat` **yok**.
  `IAvailabilityRepository.GetAvailableAsync(from, to, grup, sube, kapsam, ct)` bu parametreleri
  almıyor (imza doğrulandı).
- `musaitlik_durum.aspx`: canlı ekranın gerçek çıktısı DOM'dan yakalanamadığı için **düşük güvenle**
  eşleştirildi (K3 doğrulanamadı) — aynı `/musaitlik` sayfasına tek-tarihli anlık-durum modu
  eklenmesi öneriliyor, kanıt seviyesi düşük.
- `src/RentACar.Domain/Entities/Reservation.cs` (satır 45-52) — `OtaKiraBedeli, OtaDropBedeli,
  OtaBebekKoltugu, OtaNavigasyon, OtaLcf, OtaCdw, OtaScdw, OtaEkSurucu` **ZATEN ENTITY'DE VAR**
  (doğrulandı) ama `ReservationList.razor`'ın formunda görünmüyor (grep — form alanları arasında
  yok). `TalepTuru`, `GeldigiBirim`, `OnayKodu`, `ProjeAdi` `RentalContract`'ta var, `Reservation`'da
  **yok**.
- `src/RentACar.Web/Components/Pages/Bookings/ReservationList.razor:171-173` —
  `_list = await Reservations.ListAsync(); _customers = await Customers.ListAsync(); _vehicles =
  await Vehicles.ListAsync();` — **filtresiz** (doğrulandı). **Plan-vs-repo düzeltmesi:** plan
  ayrı bir `IReservationRepository` varsayıyordu; gerçek arayüz `IBookingRepository`
  (`ListReservationsAsync`/`FindReservationAsync`/vb., satır 13-17) — yeni arayüz AÇILMAZ, mevcut
  `IBookingRepository`'ye metod eklenir.

## Yapılacaklar
1. `src/RentACar.Application/Availability/IAvailabilityRepository.cs` —
   `GetAvailableAsync`'e `string? doviz, string? donusSube, string? rezKaynak, TimeSpan? basSaat,
   TimeSpan? bitSaat` parametrelerini ekle (geriye uyum için hepsi opsiyonel, varsayılan null).
2. `src/RentACar.Infrastructure/Persistence/Repositories/AvailabilityRepository.cs` — sorguya
   `donusSube` (dönüş ofisinde de müsaitlik kontrolü — `AvailabilityConflictException` mantığına
   dokunmadan ek filtre), `rezKaynak` (Reservation.Kaynak JOIN, opsiyonel eşleşme) uygular; `doviz`/
   `basSaat`/`bitSaat` doğrudan filtre değil, çağırana (`AvailabilityService`) iletilir (fiyat motoru
   girdisi olarak kullanılacak, bkz. madde 4).
3. `src/RentACar.Application/Availability/AvailabilityService.cs` — `FindAvailableAsync` imzasına
   aynı 5 parametreyi ekle (MEVCUT davranış/imza geriye uyumlu kalır, yeni parametreler opsiyonel).
4. `src/RentACar.Web/Components/Pages/Availability/MusaitlikArama.razor` — form'a `Doviz` seçici
   (`RentalQuoteEngine`'in döndürdüğü `ParaBirimi` zaten var, seçilebilir hale getirilir — fiyat
   motoru davranışı DEĞİŞMEZ, yalnız girdi parametresi), `Donus` (ayrı dönüş ofisi select), `RezKaynak`
   (ComboBox), `KiraGun` (elle gün girişi — tarih aralığı yerine, girilirse `to = from + KiraGun`
   hesaplanır), `BasSaat`/`BitSaat` (saat input, `time` type) ekle. Sonuç tablosuna SIPP, Km Limiti,
   Yakıt Türü, Vites, Yaş (`DateTimeOffset.UtcNow.Year - v.ModelYili`), Ehliyet (`VehicleGroup.
   SurucuMinYas`, grup JOIN'i zaten `_grupOpts` doldurma sırasında elde ediliyor — genişlet) kolonu
   ekle.
5. `src/RentACar.Web/Reports/ListExportCatalog.cs` — yeni `MusaitAraclar(IReadOnlyList<Vehicle>)`
   export tablosu (Plaka, Marka, Grup, Şube, SIPP, Km Limiti, Yakıt, Vites, Yaş, Ehliyet, Günlük,
   Toplam, Döviz).
6. `src/RentACar.Web/Components/Pages/Availability/MusaitlikArama.razor` — `musaitlik_durum.aspx`
   için: "SIPP" checkbox + tek-tarihli anlık-durum modu (mevcut `from`/`to` formunun yanına, `To ==
   From` özel durumu olarak — aynı sayfa, ayrı route AÇILMAZ, kanıt seviyesi düşük olduğundan minimal
   ek).
7. `src/RentACar.Domain/Entities/Reservation.cs` — `TalepTuru`, `GeldigiBirim`, `OnayKodu`,
   `ProjeAdi` (hepsi `string?`) ekle.
8. `src/RentACar.Infrastructure/Persistence/Configurations/BookingConfigs.cs` — `Reservation`
   config sınıfına 4 yeni kolon ekle.
9. Migration: `dotnet ef migrations add AddReservationTalepVeOtaYuzey --project
   src/RentACar.Infrastructure --startup-project src/RentACar.Infrastructure`.
10. `src/RentACar.Application/Bookings/ReservationService.cs` — create/update input/map'ine 4 yeni
    alan + "Kiraya Çevir" (`ConvertToRentalAsync`) dönüşümünde bu 4 alanın `RentalContract`'a
    taşındığını doğrula/bağla (zaten `RentalContract`'ta var olan aynı adlı alanlara kopyalanır).
11. `src/RentACar.Web/Components/Pages/Bookings/ReservationList.razor` — forma "Brokerden Gelen
    Bilgisi" `<details>` bloğu ekle (mevcut "Ödeme/komisyon" bloğunun deseniyle) — 8 Ota* alanı
    (salt veri girişi, hesaplama YOK); ayrıca `TalepTuru`/`GeldigiBirim`/`OnayKodu`/`ProjeAdi` input'u
    ekle.
12. `src/RentACar.Application/Bookings/IBookingRepository.cs` — yeni
    `Task<IReadOnlyList<Reservation>> SearchReservationsAsync(ReservationFilter filter,
    BranchScope.BranchFilter kapsam = default, CancellationToken ct = default);` ekle (mevcut
    `SearchRentalRowsAsync` deseniyle, **`IReservationRepository` AÇILMAZ**).
13. Yeni `src/RentACar.Application/Bookings/ReservationFilter.cs` — `RentalFilter`'ın deseniyle: `Query`
    (Ad Soyad/Dosya No/Plaka contains), `Durum` (`ReservationStatus?`), `TarihMin`/`TarihMax`,
    `RezKaynak`, `Kapsam`.
14. Infrastructure'da `SearchReservationsAsync`'i implemente et (Customer/Vehicle JOIN, Ad Soyad/
    Dosya No/Plaka arama, `ReservationStatus` filtresi, tarih aralığı, Kaynak eşleşmesi).
15. `src/RentACar.Web/Components/Pages/Bookings/ReservationList.razor:171-173` — `Reservations.
    ListAsync()` çağrısını filtre destekli `SearchReservationsAsync` çağrısına çevir; arama/filtre
    barı ekle (Ad Soyad, Dosya No, Plaka, Rezervasyon Durum, Tarih aralığı, Rez Kaynak). Liste
    kolonlarına Talep Türü/Geldiği Birim/Proje Adı/Onay Kodu ekle.

## Dokunulacak dosyalar
- `src/RentACar.Application/Availability/{AvailabilityService.cs,IAvailabilityRepository.cs}`
- `src/RentACar.Infrastructure/Persistence/Repositories/AvailabilityRepository.cs`
- `src/RentACar.Web/Components/Pages/Availability/MusaitlikArama.razor`
- `src/RentACar.Web/Reports/ListExportCatalog.cs`
- `src/RentACar.Domain/Entities/Reservation.cs` — 4 alan
- `src/RentACar.Infrastructure/Persistence/Configurations/BookingConfigs.cs`
- (yeni) migration `AddReservationTalepVeOtaYuzey`
- `src/RentACar.Application/Bookings/{ReservationService.cs,IBookingRepository.cs}`
- (yeni) `src/RentACar.Application/Bookings/ReservationFilter.cs`
- `src/RentACar.Infrastructure/Persistence/Repositories/BookingRepository.cs` — `IBookingRepository`
  implementasyonu (doğrulandı)
- `src/RentACar.Web/Components/Pages/Bookings/ReservationList.razor`

## Migration
Var — `Reservation`'a (mevcut tenant-owned tablo, RLS zaten aktif) 4 nullable kolon. **RLS bloğu
gerekmez** — yeni tablo yok. Müsaitlik tarafı için migration **yok** (salt servis/UI parametresi).

## Test
- `AvailabilityTests` (mevcut dosyaya ekleme): yeni parametrelerle çağrıldığında MEVCUT davranış
  regresyonu yok (parametre verilmeden çağrı öncekiyle AYNI sonucu döner — geriye uyum testi).
  `donusSube` verildiğinde dönüş ofisinde de müsait olmayan araç listeden çıkarılıyor mu (2 araç,
  biri dönüş ofisinde dolu, elle kurulan senaryo, beklenen sayı 1).
- Müsaitlik kolon testi: 1 araç (Sipp="ECMR", KiraKmLimiti=200, ModelYili=2022) elle kurulur; sonuç
  satırında bu değerler doğru gösteriliyor mu (bağımsız oracle, sabit).
- `ReservationSourceTests`/yeni `ReservationSearchTests`: 4 yeni alan round-trip; "Kiraya Çevir"
  testi — 4 alanı dolu bir Reservation elle kurulur, dönüşüm sonrası `RentalContract`'ta AYNI
  değerler beklenir (bağımsız oracle, sabit).
- Arama testi: 3 rezervasyon (2 farklı durum, 1 farklı plaka) elle kurulur; `Durum=Onayli` filtresiyle
  arama yalnız o durumdaki kaydı döndürüyor mu (beklenen sayı testte sabit).

## Exit
- [ ] Müsaitlik arama sonuçlarında 6 yeni kolon (SIPP/Km Limiti/Yakıt/Vites/Yaş/Ehliyet) görünüyor
- [ ] Doviz/Donus/RezKaynak/KiraGun/BasSaat/BitSaat parametreleri çalışıyor, fiyat motoru davranışı
      DEĞİŞMEDİ (regresyon yeşil)
- [ ] Rezervasyon formunda "Brokerden Gelen Bilgisi" (Ota*) + 4 yeni alan çalışıyor
- [ ] "Kiraya Çevir" 4 yeni alanı kiraya doğru taşıyor
- [ ] Rezervasyon listesinde arama/filtre barı çalışıyor (önceden SIFIR'dı)
- [ ] Tam suite yeşil

## Notlar
**PARA — Opus kararı bekleyen, bu fazın DIŞINDA:** rezervasyon.aspx/rezervasyon_listesi.aspx'teki
çok-taraflı bakiye (Alacağımız Komisyon, Dış Bakiye/Hesaplanan, Müşteri/Firma Bakiye) —
`kiralama.aspx` (FAZ-47) ile AYNI Opus kararına bağlı, tutarlılık şartı var (rezervasyon ve kira aynı
üç-taraflı modeli paylaşmalı). Bu faz o karardan bağımsız çalışır.

**YAPILMAZ (kısmi, onay gerekmez):** rezervasyon aşamasında sürücü serbest-metin (16 alan), ~30
sabit-kodlanmış ek hizmet/sigorta tipi, B2B görsel-ama-kayıtsız alanlar, HGS/hasar manuel alanları,
kart/provizyon erken-aşama detayları — `kiralama.aspx` alt-not #1/#2 gerekçesiyle AYNI + ek gerekçe:
bu derinlik rezervasyon aşamasında toplanırsa "Kiraya Çevir" ile kira mega-formuna geçişte İKİ KOPYA
veri kaynağı oluşur (senkron/çakışma riski) — "Kiraya Çevir"den sonra derinlik zaten mega-formda
geliyor.

`musaitlik_durum.aspx` maddesi **düşük kanıt güveniyle** planlandı (canlı DOM yakalanamadı) — eğer
canlı gerçekten SIPP×gün matris (transpoze rapor) ise bu madde D4'e kayabilir; ekip canlıyı tekrar
erişip DOM yakalarsa madde 6 gözden geçirilmeli.
