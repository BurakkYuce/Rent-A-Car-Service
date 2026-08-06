# FAZ-22 — Lokasyon × Drop Derinlik Paketi

| | |
|---|---|
| **Desen** | D2 (büyük derinlik — 24+ yeni alan, kataloğun 1-2g tabanının üstünde) |
| **Efor** | 3,5 gün (Lokasyon 2,5g + Drop 1g; Drop, Lokasyon'un yeni alanlarına dropdown'la
  bağlandığı için AYNI faz içinde SIRALI uygulanır — önce Lokasyon) |
| **Bağımlılık** | yok (dış faz bağımlılığı yok; faz-içi sıra: Lokasyon adımları önce) |
| **Kapsanan canlı ekran** | `lokasyonlar.aspx`, `lokasyon_sube_ara.aspx` |
| **Risk** | orta — `DropTanim`'in mevcut `(TenantId, Lokasyon, Sube)` benzersizliği bozulmadan
  alan ekleniyor; migration'da anlam netleştirmesi var (bkz. Notlar) |

## Amaç
Lokasyon (ofis) tanımına canlıdaki İngilizce ad/IATA/harita/haftalık çalışma saati gibi 24+ alanı,
Drop (Lokasyon×Şube) tanımına Çıkış/Dönüş lokasyon ayrımı + minimum gün/ücret alanlarını ekleyerek
kullanıcı ofis ve drop tanımlarını canlı paritesine yakın detayla yönetebilir hale gelir.

## Neden (kanıt)
`src/RentACar.Domain/Entities/Location.cs` şu an `Kod, Ad, Adres, Telefon, Eposta,
CalismaSaatleri, TeslimUcreti, Sube, SubeId, Aktif` taşıyor — plandaki `IngilizceAd`,
`BulusmaNoktasi`, `Iata`, `WebdeGizle`, `LokasyonTuru`, `BinaNo`, `Tarif`, `Ulke`, `PostaKodu`,
`MapsKonumu`, `EkAciklama`, `WebSira`, haftalık açılış/kapanış, `DropKarsilamaTuru`,
`DropCalismaSekli`, `OzelMail`, `OzelTelefon` **hiçbiri yok** (grep doğrulandı).
`src/RentACar.Domain/Entities/DropTanim.cs` şu an `Lokasyon, Sube, KarsilamaSekli, CalismaSekli,
OzelIletisim, Ucret, Aktif` taşıyor — `CikisLokasyon`/`DonusLokasyon`/`ManSuresi`/`MinGun`/`Drop2`
**hiçbiri yok**.

## Yapılacaklar
1. `src/RentACar.Domain/Entities/Location.cs` — ekle: `IngilizceAd` (string?), `BulusmaNoktasi`
   (string?, "Ofis Teslim"/"Havalimanı"/"Shuttle"…), `Iata` (string?), `WebdeGizle` (bool, default
   false), `LokasyonTuru` (string?, "Havalimanı"/"Şehir Merkezi"/…), `BinaNo` (string?), `Tarif`
   (string?, yol tarifi metni), `Ulke` (string?), `PostaKodu` (string?), `MapsKonumu` (string?),
   `EkAciklama` (string?), `WebSira` (int?), `DropKarsilamaTuru` (string?), `DropCalismaSekli`
   (string?), `OzelMail` (string?), `OzelTelefon` (string?).
2. Aynı entity'ye `HaftalikCalismaSaatleri` ekle — **tek JSONB kolon** (`List<GunSaat>`: gün adı +
   açılış + kapanış, 7 satır), canlının 14 ayrı sütununu birebir kopyalamak yerine tek kolonda
   modellenir. Mevcut `CalismaSaatleri` (serbest metin) **korunur** (geriye uyum).
3. `src/RentACar.Infrastructure/Persistence/Configurations/MasterConfigs.cs` — `LocationConfig`'e
   yeni kolonları + `HaftalikCalismaSaatleri` için `.HasColumnType("jsonb")` +
   `.HasConversion`/`ValueComparer` (System.Text.Json serileştirme, mevcut projede JSONB kullanan
   başka bir config varsa aynı deseni kopyala — yoksa `HasConversion(v => JsonSerializer.Serialize(v),
   v => JsonSerializer.Deserialize<List<GunSaat>>(v))` + koleksiyon `ValueComparer`).
4. Migration: `dotnet ef migrations add AddLocationDerinlik --project src/RentACar.Infrastructure
   --startup-project src/RentACar.Infrastructure`.
5. `src/RentACar.Web/Components/Pages/Locations/LocationList.razor` — create/edit formuna 16 yeni
   alan + haftalık çalışma saati mini-tablosu (7 satır: gün + açılış + kapanış input) ekle; grid'e
   İngilizce Ad/IATA/Tür/WebdeGizle kolonlarını ekle.
6. `src/RentACar.Application/Locations/LocationService.cs` — input modeline yeni alanları ekle, map.
7. `src/RentACar.Domain/Entities/DropTanim.cs` — mevcut `Lokasyon` alanı **BÖLÜNMEZ** (benzersizlik
   `(TenantId, Lokasyon, Sube)` bozulmasın diye); bunun yerine `CikisLokasyon` (yeni — mevcut
   `Lokasyon`'un anlamı budur, ad netleştirilir ama KOLON AYNI KALIR, C# tarafında sadece property
   adı değişebilir ya da `[Column("Lokasyon")]` ile eski kolona map edilir) + `DonusLokasyon` (yeni,
   nullable — null=tek yön/aynı lokasyon) + `ManSuresi` (int?, dakika) + `MinGun` (int?) + `Drop2`
   (decimal?, ikinci ücret kalemi) ekle.
8. `src/RentACar.Infrastructure/Persistence/Configurations/MasterConfigs.cs` — `DropTanim` config'e
   4 yeni kolon (+ olası `CikisLokasyon` yeniden-adlandırma map'i) ekle.
9. Migration: `dotnet ef migrations add AddDropTanimDerinlik --project src/RentACar.Infrastructure
   --startup-project src/RentACar.Infrastructure` (Lokasyon migration'ından SONRA, ayrı dosya).
10. `src/RentACar.Web/Components/Pages/DropTanimlari/DropTanimList.razor` — filtre formu ekle:
    İşlem Şube, Çıkış Lokasyon, Dönüş Lokasyon (dropdown'lar `LocationService.ListActiveAsync`/
    `BranchService.ListActiveAsync`'ten beslenir); create/edit formuna `ManSuresi`/`MinGun`/`Drop2`
    input'ları ekle.

## Dokunulacak dosyalar
- `src/RentACar.Domain/Entities/Location.cs` — 16+1(JSONB) alan
- `src/RentACar.Domain/Entities/DropTanim.cs` — 4 alan (+ rename netleştirme)
- `src/RentACar.Infrastructure/Persistence/Configurations/MasterConfigs.cs` — `LocationConfig` +
  `DropTanim` config
- (yeni) 2 migration dosyası (Location önce, DropTanim sonra)
- `src/RentACar.Web/Components/Pages/Locations/LocationList.razor`
- `src/RentACar.Web/Components/Pages/DropTanimlari/DropTanimList.razor`
- `src/RentACar.Application/Locations/LocationService.cs`

## Migration
Var — 2 migration, ikisi de mevcut tenant-owned tablolara (Locations, DropTanimlari) additive
nullable kolon ekliyor. **Yeni tablo yok** → RLS bloğu elle eklenmez (tablolar zaten RLS'li).
JSONB kolonun `ValueComparer` eksikliği EF'te "değişiklik izlenmiyor" hatasına yol açar — dikkat.

## Test
- `LocationTests`: 16 yeni alan + haftalık çalışma saati (7 gün) round-trip; bağımsız oracle — elle
  kurulan bir lokasyon nesnesiyle geri okunan değer birebir karşılaştırılır.
- `DropTanimTests`: mevcut `(TenantId, Lokasyon, Sube)` benzersizlik testi **regresyon olarak**
  tekrar çalıştırılır (kırılmadığını doğrula); `CikisLokasyon`/`DonusLokasyon`/`ManSuresi`/`MinGun`/
  `Drop2` round-trip; filtre testi (Çıkış/Dönüş lokasyon dropdown sonucu elle kurulan 3 kayıttan
  doğru alt-kümeyi döndürüyor mu).

## Exit
- [ ] Lokasyon formunda 16 yeni alan + haftalık çalışma saati tablosu var, kaydediliyor
- [ ] Drop tanımında Çıkış/Dönüş ayrımı + filtre çalışıyor, mevcut benzersizlik testi kırılmadı
- [ ] Tam suite yeşil

## Notlar
`CalismaSaatleri` serbest-metin alanı JSONB `HaftalikCalismaSaatleri` eklendikten sonra da GERİYE
UYUM için silinmez — iki alan bir arada durur (biri legacy görüntü, biri yapılı veri).
