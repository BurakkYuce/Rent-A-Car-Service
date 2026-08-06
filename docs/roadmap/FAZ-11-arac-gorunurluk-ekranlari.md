# FAZ-11 — Araç Görünürlük Ekranları: Aksiyon Konsolu + Liste + Takvim Filtreleri

| | |
|---|---|
| **Desen** | D3 — liste/arama (yeni tablo yok) |
| **Efor** | 3,5 gün (2 + 1 + 0,5) |
| **Bağımlılık** | FAZ-10 (`PasifSebep`/`Konum`/`TakipNo`/`TeypKodu`/`Aciklama` alanları) |
| **Kapsanan canlı ekran** | `arac_guncel_durum.aspx`, `arac_listesi.aspx`, `arac_rac_takvim.aspx` |
| **Risk** | düşük — filtre/kolon/aksiyon-wiring, para yok |

## Amaç
Kullanıcı `/arac-durum`'dan satırdan doğrudan Kirala/Servis/Baf başlatabilir ve Söz.No/Ad
Soyad/Cep Tel/Kira Kalan gibi özet kolonları görür; `/vehicles` listesinde canlı kadar zengin
filtre/kolon kullanır; `/takvim`'de Grup/Bölge/Plaka ile araç kümesini daraltır.

## Neden (kanıt)
- `arac_guncel_durum.aspx`: K2=%27 (6/22), K3=%12 (8/68). Canlı asıl iş = satır-içi AKSİYON
  konsolu (Kirala/Servis/Baf butonları + aktif kira/servis/baf özet kolonları); bizim
  `/arac-durum` salt-okunur bir pano — satırdan kira/servis/baf başlatma yok, link yalnız
  `/vehicles/{id}`'ye gidiyor.
- `arac_listesi.aspx`: K2=%25 (3/12), K3=%35 (17/49). Filtre ekleri: `Grup_Turu` toggle,
  `Ofis`, `ArTarih_Listesi` (tarih tipi seçimi+aralık), `Arac_Sahibi` toggle. Kolon ekleri
  (çoğu Vehicle'da zaten var): Alış Bedeli, Filo Gir/Çık Tarihi, İlk Tescil Tarihi, Alınan
  Firma, Yedek Anahtar, HGS/OGS, Ruhsat No, Lastik Bilgisi, 2.El Değeri, Z-İzni, Statü, Kar
  Lastiği + Lokasyon/TakipNo/TeypKodu/Açıklama (FAZ-10'un yeni alanları).
- `arac_rac_takvim.aspx`: K2=%25 (1/4). `AracGrubu`/`Bolge`/`SearchBox` filtreleri yok —
  `ReservationCalendar.razor`'da grup/bölge filtresi hiç yok (Operatör rolünde örtük
  şube-kapsamı var ama seçilebilir bir alan değil).

## Yapılacaklar

### Bölüm A — arac_guncel_durum (`FleetStatus`)
1. `src/RentACar.Application/Fleet/FleetStatusRow.cs` — `SozlesmeNo`/`MusteriTel`/
   `RezMusteriAd`/`DosyaNo` alanlarını ekle (mevcut `KiraSozlesmeNo`/`MusteriAd`/`KiraBitTar`/
   `KiraBakiye` ile çakışmayanlar); `Rentals`+`Reservations` join'ini genişlet.
2. `src/RentACar.Application/Fleet/FleetStatusFilter.cs` — `PasifSebep` (FAZ-10'un yeni
   alanına bağlı) ekle; `KarLastigi`/`WebRezKapat`/`OfisRezKapat`/`HgsNo` (Vehicle'da zaten
   var) ve GPS (`TakipNo`, FAZ-10) için UI parametrelerini bağla — filtre alanı zaten `Sube`
   taşıyor, yalnız UI select eksik.
3. `src/RentACar.Application/Fleet/FleetStatusService.cs` /
   `src/RentACar.Infrastructure/Persistence/Repositories/FleetStatusRepository.cs` — sorguyu
   yeni filtre/kolonlara göre genişlet.
4. `src/RentACar.Web/Components/Pages/Fleet/FleetStatus.razor` — her satıra 3 mini-aksiyon
   formu (ana tablo formunun DIŞINDA, `form=` attribute'üyle — kira mega-form deseni):
   Kirala → `/kiralar/yeni?vehicleId=`, Servis → `/servisler/create`, Baf → `/baf/create`
   (mevcut uçlara post, **yeni endpoint YOK**); Ofis/Pasif Sebep/GPS filtre select'leri.

### Bölüm B — arac_listesi (`VehicleList`)
5. `src/RentACar.Application/Vehicles/VehicleFilter.cs` — `GrupTuru` (Grup↔SIPP toggle),
   `Ofis`, `ArTarihTuru`+aralık (Filo Giriş/Çıkış/Tescil), `AracSahibi` toggle (Hepsi/Bizim/Dış)
   ekle.
6. `src/RentACar.Application/Vehicles/VehicleService.cs` /
   `src/RentACar.Infrastructure/Persistence/Repositories/VehicleRepository.cs` — Sözleşme
   No/Servis-Baf-Satış bayrak/Kasko Bedeli/Kredi Kuruluşu-Son Tarih için `EXISTS` alt-sorguları.
7. `src/RentACar.Web/Components/Pages/Vehicles/VehicleList.razor` — Alış Bedeli, Filo Gir/Çık
   Tarihi, İlk Tescil Tarihi, Alınan Firma, Yedek Anahtar, HGS/OGS, Ruhsat No, Lastik Bilgisi,
   2.El Değeri, Z-İzni, Statü, Kar Lastiği + Lokasyon/TakipNo/TeypKodu/Açıklama kolonları
   (FAZ-10).

### Bölüm C — arac_rac_takvim (`ReservationCalendar`)
8. `src/RentACar.Web/Components/Pages/Bookings/ReservationCalendar.razor` — Grup ve
   Şube/Bölge filtresi (query param + select, `VehicleFilter` alanları) + plaka arama kutusu.
   `CalendarService.GetOccupancyAsync` **DEĞİŞMEZ** (zaten aralık bazlı) — yalnız gösterilecek
   araç kümesi filtrelenir.

## Dokunulacak dosyalar
- `src/RentACar.Application/Fleet/FleetStatusRow.cs`, `FleetStatusFilter.cs`,
  `FleetStatusService.cs`
- `src/RentACar.Infrastructure/Persistence/Repositories/FleetStatusRepository.cs`
- `src/RentACar.Web/Components/Pages/Fleet/FleetStatus.razor`
- `src/RentACar.Application/Vehicles/VehicleFilter.cs`, `VehicleService.cs`
- `src/RentACar.Infrastructure/Persistence/Repositories/VehicleRepository.cs`
- `src/RentACar.Web/Components/Pages/Vehicles/VehicleList.razor`
- `src/RentACar.Web/Components/Pages/Bookings/ReservationCalendar.razor`

## Migration
Yok — yalnız filtre/kolon/aksiyon-wiring; FAZ-10 kolonları zaten migration'la ekledi.

## Test
- `FleetStatusServiceTests`: bağımsız oracle — 3 araç seed (1'i `PasifSebep="Kaza"`), filtre
  `PasifSebep="Kaza"` uygulanınca **1** sonuç bekleniyor (sabit sayı, koddan türetilmez).
- `VehicleServiceTests`: `GrupTuru=SIPP` toggle'ıyla sonuç kümesinin `Sipp` alanına göre
  gruplandığı, `GrupTuru=Grup` ile `Grup` alanına göre gruplandığı — 2 sabit senaryo.
- `CalendarServiceTests` (yoksa yeni): Grup filtresiyle daralan araç kümesi — 5 araç seed (2'si
  `Grup="A"`), filtre `Grup="A"` ile **2** sonuç bekleniyor.

## Exit
- [ ] `/arac-durum`'da satır-içi Kirala/Servis/Baf formları çalışıyor (mevcut uçlara post)
- [ ] `/vehicles` listesinde tüm yeni filtre/kolonlar görünüyor ve doğru sonuç veriyor
- [ ] `/takvim`'de Grup/Bölge/Plaka filtresi araç kümesini daraltıyor
- [ ] Tam suite yeşil

## Notlar
Bu faz FAZ-10'a bağımlı: FAZ-10 gitmeden `PasifSebep`/GPS filtreleri ve
Lokasyon/TakipNo/TeypKodu/Açıklama kolonları derlenmez. AJAN-TALIMATI'nın rule-3 örneği tam bu
çifti (arac_guncel_durum ↔ `/arac-durum`) işaret ediyor — ikisi benzer isimli ama farklı iş
(aksiyon konsolu vs pasif durum panosu); bu faz "farklı iş" farkını kapatıyor, "aynı isim"
yanılgısına düşülmemeli.
