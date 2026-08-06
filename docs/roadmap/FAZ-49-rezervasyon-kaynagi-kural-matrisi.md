# FAZ-49 — Rezervasyon Kaynağı Kural Matrisi

| | |
|---|---|
| **Desen** | D2 — kural taşıyan master (D8 alt-parça HARİÇ, bkz. `_toplama-03-04-cari-kira.md` BLOKE) |
| **Efor** | 3 gün (D2 yapısal + kural-tüketim testleri; komisyon formülü Opus sonrası ayrı efor; D8
  parçası hariç) |
| **Bağımlılık** | komisyon/bakiye formülü Opus kararına bağlı (bu fazda YOK); D8 alt-parçası
  kimlik/credential + kullanıcı onayı gerektirir (bu fazda YOK, bkz. Notlar) |
| **Kapsanan canlı ekran** | `rezervasyon_kaynagi.aspx` (kısmi — D8 alt-parça hariç) |
| **Risk** | yüksek — kural matrisi `ReservationService`/`RentalService`'te GERÇEK davranışı gater
  (ör. `Uzatamaz=true` iken uzatma reddi); mevcut kira/rezervasyon akışlarında regresyon riski, geniş
  test kapsamı zorunlu |

## Amaç
Kullanıcı bir rezervasyon kaynağına (OfisSatış/Broker/Acente/RentACar/Otel/Diğer) iş kuralı matrisi
(uzatma yasağı, tarih değiştirilemezliği, provizyon/km-sınırsızlığı, maliyet yansıtma, erken/gecikme/
iptal/no-show/uzatma davranış bayrakları) ve sigorta/tedarik/ek-hizmet varsayılanları tanımlayabilir;
bu kurallar rezervasyon/kira akışında GERÇEKTEN uygulanır (yalnız alan değil, tüketim noktası).

## Neden (kanıt)
`src/RentACar.Domain/Entities/ReservationSource.cs` şu an `Id, TenantId, Kod, Ad, Aktif,
CreatedAtUtc, UpdatedAtUtc` taşıyor — plandaki `KaynakGrubu`, iş kuralı bool matrisi (`Uzatamaz`,
`RezTarihleriDegisemez`, `ProvizyonYok`, `KmSinirsiz`, `MaliyetYansitma`, `MatrisErken`,
`MatrisGecikme`, `MatrisIptal`, `MatrisNoShow`, `MatrisUzatma`), sigorta/tedarik seçenekleri
(`SigortaKaynakNo`, `DropKaynakNo`, `ProvizyonSecenek`, `MuafiyatSecenek`, `ScdwDahil`/`CdwDahil`/
`LcfDahil`/`PaiDahil`), ek hizmet varsayılan tutarları (`BebekKoltugu`, `Navigasyon`, `EkSurucu`,
`Wifi`), `MaxGun`, `MailAdres`, `OtomatikMailGitme`, `RiskAnalizYapma`, `SubeGor`, `AyniYonDrop`,
`AcenteFiyatDegistir`, `Gizle`, `SadeceMusteriOdeme` **hiçbiri yok** (grep doğrulandı — entity
tamamen minimal). `ReservationSourceService` şu an `MasterTanimService<ReservationSource>`'un İNCE
alt sınıfı (`ReservationSourceInput` yalnız Kod/Ad/Aktif) — bu fazda genişletilecek 30+ alan
`CreateCoreAsync`/`UpdateCoreAsync`'in kapsamı DIŞINDA, custom mapping gerekir. Liste
(`ReservationSourceList.razor:27`) yalnız `Kod/Ad/Durum/İşlem` — Cari Bilgi, Tarife, Özel Kod yok.

## Yapılacaklar
1. `src/RentACar.Domain/Entities/ReservationSource.cs` — `KaynakGrubu` (enum `OfisSatis`/`Broker`/
   `Acente`/`RentACar`/`Otel`/`Diger`, yeni `src/RentACar.Domain/Enums/RezKaynakGrubu.cs`),
   10 bool bayrak (`Uzatamaz`, `RezTarihleriDegisemez`, `ProvizyonYok`, `KmSinirsiz`,
   `MaliyetYansitma`, `MatrisErken`, `MatrisGecikme`, `MatrisIptal`, `MatrisNoShow`,
   `MatrisUzatma` — default false), `SigortaKaynakNo`/`DropKaynakNo` (string?), `ProvizyonSecenek`/
   `MuafiyatSecenek` (string?), `ScdwDahil`/`CdwDahil`/`LcfDahil`/`PaiDahil` (bool, default false),
   `BebekKoltugu`/`Navigasyon`/`EkSurucu`/`Wifi` (decimal?, `numeric(19,4)`), `MaxGun` (int?),
   `MailAdres` (string?), `OtomatikMailGitme`/`RiskAnalizYapma`/`SubeGor`/`AyniYonDrop`/
   `AcenteFiyatDegistir`/`Gizle`/`SadeceMusteriOdeme` (bool, default false) ekle.
2. `src/RentACar.Infrastructure/Persistence/Configurations/MasterConfigs.cs` — `ReservationSource`
   config sınıfına (`IEntityTypeConfiguration<ReservationSource>`, doğrulandı) 30+ kolon ekle.
3. Migration: `dotnet ef migrations add AddReservationSourceKuralMatrisi --project
   src/RentACar.Infrastructure --startup-project src/RentACar.Infrastructure`.
4. `src/RentACar.Application/ReservationSources/ReservationSourceInput.cs` — 30+ alanı ekle.
5. `src/RentACar.Application/ReservationSources/ReservationSourceService.cs` — **artık ince
   `MasterTanimService` alt sınıfı yeterli değil**: `CreateAsync`/`UpdateAsync`'i `CreateCoreAsync`/
   `UpdateCoreAsync` çağrısından SONRA ikinci bir `_repository.UpdateAsync(id, entity => Apply(entity,
   input), ct)` adımıyla genişlet (Kod/Ad/Aktif çekirdek doğrulaması/benzersizliği KORUNUR, yeni
   alanlar ayrı `Apply` metoduyla map edilir — çekirdek davranış değişmez, dış yüzey bozulmaz).
6. `src/RentACar.Application/Bookings/ReservationService.cs` — rezervasyon oluşturma/güncellemede
   `ReservationSource.Uzatamaz`/`RezTarihleriDegisemez` bayraklarını OKUYUP guard uygula (kaynak
   `Uzatamaz=true` ise tarih uzatma isteği `ValidationException` ile reddedilir).
7. `src/RentACar.Application/Bookings/RentalService.cs:197` — `UpdateOpenAsync`'te, kira
   rezervasyondan geldiyse (`Reservation.Kaynak`/kaynak-FK üzerinden `ReservationSource` bulunur)
   `Uzatamaz=true` ise tarih uzatma alanları (`BasTar`/`BitTar` değişikliği) reddedilir — GERÇEK
   tüketim noktası, D2'nin özü.
8. `src/RentACar.Web/Components/Pages/ReservationSources/ReservationSourceList.razor` — form'a 30+
   alanı ekle (bool bayraklar checkbox grubu, `KaynakGrubu` select, tutarlar `numeric` input); liste
   `<thead>`'ine (satır 27) Cari Bilgi, Tarife, Özel Kod kolonlarını ekle (mevcut alanlarla JOIN,
   veri yoksa boş).

## Dokunulacak dosyalar
- `src/RentACar.Domain/Entities/ReservationSource.cs` — 30+ alan
- (yeni) `src/RentACar.Domain/Enums/RezKaynakGrubu.cs`
- `src/RentACar.Infrastructure/Persistence/Configurations/MasterConfigs.cs` — `ReservationSource`
  config
- (yeni) migration `AddReservationSourceKuralMatrisi`
- `src/RentACar.Application/ReservationSources/{ReservationSourceInput.cs,ReservationSourceService.cs}`
- `src/RentACar.Application/Bookings/{ReservationService.cs,RentalService.cs}` — kural tüketimi
- `src/RentACar.Web/Components/Pages/ReservationSources/ReservationSourceList.razor`

## Migration
Var — `ReservationSource`'a (mevcut tenant-owned tablo, RLS zaten aktif) 30+ nullable/bool kolon.
**RLS bloğu gerekmez** — yeni tablo yok.

## Test
- `ReservationSourceTests` (mevcut dosyaya ekleme): 30+ alan round-trip (bağımsız oracle — elle
  kurulan `ReservationSource` nesnesi); `ReservationSourceService.CreateAsync`/`UpdateAsync`
  çekirdek Kod-benzersizliği davranışının BOZULMADIĞINI doğrulayan regresyon senaryosu (aynı kod
  ikinci kez oluşturulmaya çalışılınca hâlâ `ValidationException`).
- **Kural-tüketim testi (D2'nin özü — bağımsız oracle):** `Uzatamaz=true` olan bir `ReservationSource`
  elle kurulur; ondan gelen bir rezervasyon "Kiraya Çevir" ile kiraya dönüştürülür; `RentalService.
  UpdateOpenAsync` ile tarih uzatma denenir → `ValidationException` beklenir (sabit senaryo,
  guard kodundan değil elle kurulan veriden). Karşıt senaryo: `Uzatamaz=false` aynı akışta uzatma
  BAŞARILI olmalı (negatif+pozitif çift test — guard'ın hem çalıştığı hem de yanlışlıkla her zaman
  reddetmediği doğrulanır).
- Liste testi: form Cari Bilgi/Tarife/Özel Kod doldurulmuş 1 kaynak elle kurulur, liste sorgusunda
  kolonlar dolu dönüyor mu.

## Exit
- [ ] Rezervasyon kaynağı formunda 30+ alan çalışıyor, kaydediliyor
- [ ] `ReservationSourceService` çekirdek Kod-benzersizliği davranışı BOZULMADI (regresyon yeşil)
- [ ] `Uzatamaz=true` kaynaklı rezervasyondan gelen kirada uzatma REDDEDİLİYOR (pozitif+negatif test)
- [ ] Liste 3 yeni kolon gösteriyor
- [ ] Tam suite yeşil

## Notlar
**PARA — Opus kararı bekleyen, bu fazın DIŞINDA:** `FKomisyonOran`/`Tutar`, `Bakiyelendirme`,
`RezKayBakiyeDvz`, `XmlKatsayi`, `OnOdemeOrani`, `Indirim`, `PuanOrani` — komisyon/bakiye hesaplama
formülü ve bu oranların fiyat motoruna/deftere nasıl bağlanacağı; karar sonrası AYRI bir D5 fazı
açılır (CLAUDE.md §3.5 zorunlu adversarial inceleme gerekir).

**D8 BLOKE — bu fazın DIŞINDA, efor yazılmadı:** `FrameKod`, `CalisilacakDoviz`, `Faz1Timeout`/
`Faz2Timeout`, `PaymentTuru`, `XmlHizmetOzel`, `XmlMailGitme`, acente paneli ödeme entegrasyonu,
`RezLogo` yükleme (gerçek dosya depolama + broker paneli) — XML broker/acente entegratör kimliği
gerektirir (canlı sistemin B2B ortak entegrasyon katmanı). **Açmadan önce kullanıcıya sorulur** —
port/stub hazırlanabilir ama kod yazılmaz. Ayrıntı: `_toplama-03-04-cari-kira.md` BLOKE listesi.

**Diğer 5 D2 kural-matrisi fazından (FAZ-46, kiralama kuralları) FARKI:** burada kuralın
TÜKETİLDİĞİ yer (`ReservationService`/`RentalService`) bu fazın İÇİNDE — FAZ-46'daki
`kiralama_kurallari`/`kiralama_sartlari` yalnız veri modeli ekliyor, tüketim noktası ayrı; bu fazda
madde 6-7 GERÇEK davranış değişikliği yapıyor, dolayısıyla Risk yüksek işaretlendi ve pozitif+negatif
test çifti zorunlu tutuldu.
