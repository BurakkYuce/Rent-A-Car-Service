# FAZ-82 — Ayarlar Derinlik PR-3: Fiyat/Muhasebe Parametreleri + İş Kuralı Anahtarları

| | |
|---|---|
| **Desen** | D2 — kural taşıyan master (parçalı: bir kısım bu fazda GUARD'A bağlanır, formül-eşik kısmı PARA — Opus onayına kadar PASİF eklenir) |
| **Efor** | 2,5 gün (Grup 3: 1g + Grup 4: 1,5g) |
| **Bağımlılık** | Grup 3'ün eşik/varsayılan-değer kararı ve `ReturnMath`'e kablolanması **PARA — Opus** onayı ister; bu faz o onay GELMEDEN alanları ekler ama guard'sız/pasif bırakır |
| **Kapsanan canlı ekran** | `ayarlar.aspx` (Fiyatlandırma/Muhasebe Kuralları + İş Kuralı Checkbox'ları alt-bölümleri) |
| **Risk** | orta — Grup 4'teki guard'lar bugün "her zaman açık" sabit davranışı tenant-bazlı kapatılabilir yapıyor; varsayılan `true`/mevcut-davranış olsa da fatura/cari kuralını etkileyen canlı bir anahtar |

## Amaç
Tenant, fiyat/muhasebe formundaki bazı varsayılanları (fiyat türü, çıkış yakıt seviyesi) ve
bugün kodda sabit-açık olan iş kurallarından bir kısmını (cari TC benzersizliği, kur elle-giriş
kilidi, sigorta/BAF dahil-hariç) kendi ayarlarından aç/kapa hâline getirir.

## Neden (kanıt)

### Grup 3 — fiyat/muhasebe parametreleri
`src/RentACar.Domain/Entities/TenantSettings.cs` (78 satır, tam okundu) şu an
`VarsayilanDoviz`/`VarsayilanKdvOrani`/`MinKiraGun`/`MaxKiraGun` taşıyor ama fiyat-formu
varsayılanı için `VarsayilanFiyatTuru`/`VarsayilanYakitSeviyesi` yok. Doğrulanan somut hedefler:
- **VarsayilanFiyatTuru**: `src/RentACar.Web/Components/Pages/Bookings/ReservationList.razor`
  satır 42-47'deki `<select name="fiyatTuru">` bugün hiçbir `<option selected>` taşımıyor (ilk
  seçenek boş `"—"`). `BookingInput.FiyatTuru` boşsa `null` kalıyor
  (`ReservationService.cs` satır 68, 163: `string.IsNullOrWhiteSpace(input.FiyatTuru) ? null :
  ...`) — pricing motoru null'u zaten "Otomatik" gibi işliyor, yani bu alan SADECE formun
  ÖN-SEÇİLİ değerini değiştirir, kayıtlı/hesaplanan tutarı DEĞİŞTİRMEZ.
- **VarsayilanYakitSeviyesi**: `src/RentACar.Web/Components/Pages/Bookings/KiraFormPaneller/
  SekmeKiraBilgisi.razor` satır 126, çıkış-yakıt alanı **hardcoded** `value="8"` (0-12 skalası,
  aynı sayfa satır 126 etiketi "Çıkış Yakıt (0-12)"). Tip `int?` olmalı — `RentalContract.
  CikisYakit` (`src/RentACar.Domain/Entities/RentalContract.cs` satır 45) de `int?`.
- **DropMesafeYokIseSifir, SaatFarkiToleransDk, IadeIslemSaatSiniri**: grep doğrulandı, kodda HİÇ
  karşılığı yok — **TEK istisna/tuzak aşağıda**.

**KRİTİK TUZAK (bu fazı yazarken bulundu, planın YAZILDIĞI anda henüz yoktu):**
`src/RentACar.Application/Bookings/BookingMath.cs` satır 47, `KismiGunEsigiSaat = 3.0` — bu,
canlı referans sistem'in **`Saat_Farki_Hesap`** alanının (bugün `ayarlar.aspx`'te GLOBAL, tek değer)
2026-08-06 tarihli canlı-kalibrasyon PR'ıyla (`2ecdcbe`, bkz. `docs/parite/11-para-kalibrasyon.md`
madde 1) koda gömülmüş hâli — **gün SAYIMI** (kira başlangıcında kaç gün faturalanacağı,
`BookingMath.ComputeGun`) için kullanılıyor, `ceil` DEĞİL floor+eşik. Plandaki
`SaatFarkiToleransDk` alanı ise açıkça **`ReturnMath`'e girdi** olarak tanımlanmış — `ReturnMath.cs`
satır 48 (`uzatmaGun = Math.Max(1, (int)Math.Ceiling((gercikDonus - c.BitTar).TotalHours /
24.0));`) **saf ceil**, hiçbir tolerans/eşik YOK — bu, `KismiGunEsigiSaat`'ten TAMAMEN AYRI bir
mekanizma (gün-sayımı vs. geç-dönüş-uzatma). Yani plan'ın "hiçbir yerde yok" iddiası bu alan için
DOĞRU KALIYOR ama **uygulayan kişi bu iki benzer-görünen "3 saat eşiği" kavramını KARIŞTIRMAMALI** —
`KismiGunEsigiSaat` zaten canlıya kalibre edilmiş GLOBAL bir sabittir, tenant-bazlı override
edilirse parite kilidini (`tests/RentACar.IntegrationTests/CanliGunKalibrasyonTests.cs`) bozar.
Bu faz `KismiGunEsigiSaat`'e DOKUNMAZ.

### Grup 4 — iş kuralı checkbox'ları
Kodda gerçek karşılığı doğrulanan 3 kalem:
- **Cari_Tek**: `src/RentACar.Application/Customers/CustomerService.cs` satır 126-132,
  `EnsureUniqueAsync` — TC girilmişse **HER ZAMAN** blind-index (`TcKimlikHash`) üzerinden
  benzersizlik zorlanıyor (`DuplicateCariException`), tenant-bazlı aç/kapa YOK. Bugünkü davranış
  "Cari_Tek = her zaman AÇIK" ile eşdeğer.
- **TCMB_Otomatik → KurElleGirisKilitli**: `src/RentACar.Application/Kur/KurCozucu.cs` (27
  satır) `CozAsync` — açık `kur` parametresi verilirse (satır 19-23) hep AYNEN kullanılıyor,
  tenant'ın bunu KİLİTLEME (zorla otomatik TCMB) seçeneği yok. `KurCozucu` DI-scoped servis
  (`Application/DependencyInjection.cs`), 8+ çağrı noktası (`AracKrediService`, `ExpenseService`,
  `CashService`, `DepozitoService`, `ServiceRecordService`, `RegulationService`,
  `VehicleSaleService`, `DonemFaturaUretici`, `DisHizmetAlimi` vb.) VAR ama hepsi DI ile
  `KurCozucu`'yu enjekte ediyor — constructor'a yeni bağımlılık eklemek DI container tarafından
  otomatik çözülür, çağrı SITE'LERİ değişmez (blast radius düşük).
- **Baf_Bakim_Cikart / SCDW_Dahil / CDW_Dahil / TP_Dahil / Farkli_Nokta_Biralabilir**: ilgili
  modüller VAR (Araç Karnesi BAF-bakım ayrımı, `EkHizmetTanim`/fiyat motoru sigorta satırları,
  `DropTanim.cs` farklı-nokta drop) ama hepsi "her zaman açık" — tenant-bazlı kapatma anahtarı yok.

`Fatura_Donem_Yaz` zaten `TenantSettings.DonemselFaturalamaJob` ile karşılanıyor — **atlanır**.
`TC_Dogrula` e-Devlet kimlik doğrulama API'si gerektirir — **D8 bloke**, bu faza GİRMEZ (bkz.
toplama dosyası BLOKE listesi).

## Yapılacaklar

### Grup 3 — alan ekleme (guard'sız/pasif, PARA-Opus onayına kadar)
1. `src/RentACar.Domain/Entities/TenantSettings.cs` — ekle: `VarsayilanFiyatTuru` (`string?`),
   `VarsayilanYakitSeviyesi` (`int?`, 0-12), `DropMesafeYokIseSifir` (`bool?`),
   `SaatFarkiToleransDk` (`int?`), `IadeIslemSaatSiniri` (`int?`).
2. `TenantSettingsModel.cs` + `TenantSettingsService.cs` (`GetAsync`/`SaveAsync`) — 5 alanı
   map'e ekle; `VarsayilanYakitSeviyesi` için `0..12` aralık doğrulaması (`ValidationException`).
3. `TenantSettingsEndpoints.cs` `/kaydet` ucuna 5 form alanı (`FormParse.Int`/`FormParse.Dec`
   ile — hepsi opsiyonel sayısal, CLAUDE.md §5 tuzağı).
4. `Ayarlar.razor`'a yeni `<fieldset><legend>Fiyatlandırma Varsayılanları</legend>` — 5 alan.
5. **SADECE `VarsayilanYakitSeviyesi` bu fazda TÜKETİLİR** (para riski yok — bir form
   ön-doldurma): `KiraFormPaneller/KiraFormVm.cs`'e `int? TenantVarsayilanYakit` eklenir;
   `KiraForm.razor` (VM'i dolduran ana sayfa — tüm alt-sekmeler onu tüketiyor, VM'in TEK üretim
   noktası) `@inject RentACar.Application.TenantSettings.ITenantSettingsRepository AyarRepo`
   ekleyip `OnInitializedAsync`'te okuduğu `(await AyarRepo.GetAsync())?.VarsayilanYakitSeviyesi`
   değerini `Vm.TenantVarsayilanYakit`'e yazar; `SekmeKiraBilgisi.razor` satır 126'daki
   `value="8"` → `value="@(Vm.TenantVarsayilanYakit ?? 8)"`. `VarsayilanFiyatTuru` de aynı
   `ITenantSettingsRepository` deseniyle `ReservationList.razor` satır 42-47 `<select>`'in ilk
   seçili option'ını değiştirebilir (opsiyonel, düşük risk — FiyatTuru boşsa zaten null/
   Otomatik'e düşüyor, davranış AYNI).
6. `DropMesafeYokIseSifir`/`SaatFarkiToleransDk`/`IadeIslemSaatSiniri` — **BU FAZDA SADECE
   ALAN + FORM eklenir, hiçbir servise (ReturnMath/BookingMath/DropTanim ilgili resolver)
   BAĞLANMAZ.** Formda "Beklemede — onay gerekiyor" notu gösterilir (Beklemede-çit deseni,
   FAZ 6 `/tarife-aktar` desenindeki gibi: alan var, motor okumuyor).

### Grup 4 — guard'lar (bu fazda AKTİF bağlanır)
7. `TenantSettings.cs` — ekle: `CariTcBenzersizlikKapali` (`bool`, default `false` — **false =
   mevcut davranış**, yani benzersizlik hâlâ zorlanır; `true` = tenant TC tekrarına izin verir),
   `KurElleGirisKilitli` (`bool`, default `false`), `SigortaVeDropDahilKurallari` — ayrı ayrı 4
   bool: `BafBakimCikartAktif`, `ScdwDahilAktif`, `CdwDahilAktif`, `TpDahilAktif` (hepsi default
   `true` — **true = mevcut "her zaman açık" davranışı korur**).
8. `KdvVarsayilan.cs` (`src/RentACar.Application/Finance/KdvVarsayilan.cs`) İLE AYNI DESENDE
   (hassas-olmayan ayar → `ITenantSettingsRepository` doğrudan, `TenantSettingsService`'in
   `PermissionGuard.Require(ManageUsers)` kapısından GEÇMEDEN okunur — fiyatlama/cari yolları
   operatör yetkisiyle çalışır) yeni bir resolver: `src/RentACar.Application/Kur/
   KurKilidiCozucu.cs` — `ITenantSettingsRepository` enjekte eder, `KilitliMi()` döner.
9. `KurCozucu.cs` constructor'ına `KurKilidiCozucu` (veya doğrudan `ITenantSettingsRepository`)
   eklenir; `CozAsync` satır 19-23'teki `if (kur is { } k)` bloğunun EN BAŞINA:
   `if (await KilitliMi(ct)) throw new ValidationException("Bu tenant'ta kur elle girilemez.");`
   eklenir (guard GİRİŞ noktasında — CLAUDE.md "yorumdaki hafifletme bayatlar" dersi).
10. `CustomerService.cs` `EnsureUniqueAsync` (satır 126) BAŞINA: `if (ayarlar.CariTcBenzersizlikKapali)
    return;` guard'ı eklenir (constructor'a `ITenantSettingsRepository` enjekte edilir).
11. Sigorta/drop dahil-hariç guard'ları — `BafBakimCikartAktif`/`ScdwDahilAktif`/`CdwDahilAktif`/
    `TpDahilAktif` için TÜKETİM noktası (Araç Karnesi maliyet ayrımı, fiyat motoru sigorta
    satırları) plan metninde net değil — **bu fazda alan+DI resolver eklenir, gerçek servise
    bağlama (hangi hesaplamanın hangi satırı) ayrı bir küçük takip fazı olarak açık bırakılır**
    (kapsam netleşmeden bağlarsa "guard var ama neyi koruduğu belirsiz" riski — CLAUDE.md
    "yorumdaki hafifletme bayatlar" dersinin tam tersi hatası).
12. Migration: `dotnet ef migrations add AddTenantSettingsFiyatVeIsKurallari --project
    src/RentACar.Infrastructure --startup-project src/RentACar.Infrastructure`.

## Dokunulacak dosyalar
- `src/RentACar.Domain/Entities/TenantSettings.cs`
- `src/RentACar.Application/TenantSettings/TenantSettingsModel.cs`
- `src/RentACar.Application/TenantSettings/TenantSettingsService.cs`
- `src/RentACar.Web/TenantSettings/TenantSettingsEndpoints.cs`
- `src/RentACar.Web/Components/Pages/Settings/Ayarlar.razor`
- `src/RentACar.Web/Components/Pages/Bookings/KiraFormPaneller/SekmeKiraBilgisi.razor`
- `src/RentACar.Web/Components/Pages/Bookings/KiraFormPaneller/KiraFormVm.cs`
- `src/RentACar.Web/Components/Pages/Bookings/ReservationList.razor`
- (yeni) `src/RentACar.Application/Kur/KurKilidiCozucu.cs`
- `src/RentACar.Application/Kur/KurCozucu.cs`
- `src/RentACar.Application/Customers/CustomerService.cs`
- (yeni) migration `AddTenantSettingsFiyatVeIsKurallari`

## Migration
Var — `Ayarlar` tablosuna 9 nullable/bool kolon (5 Grup 3 + 4 Grup 4 boolean; `CariTcBenzersizlikKapali`/
`KurElleGirisKilitli` dahil = toplam ~11 kolon, tam liste madde 1+7). Tablo zaten RLS'li — RLS
bloğu tekrar eklenmez.

## Test
- `TenantSettingsTests.cs` — 11 alan round-trip; `VarsayilanYakitSeviyesi` aralık-dışı (13, -1)
  → `ValidationException`.
- `tests/RentACar.IntegrationTests/KurCozucuTests.cs` (yoksa yeni dosya) — **bağımsız oracle**:
  `KurElleGirisKilitli=true` iken açık `kur=5.5m` verilerek çağrılan `CozAsync` her zaman
  `ValidationException` fırlatır (mesaj testte elle yazılır); `false` (varsayılan) iken AYNI
  çağrı eski davranışla `5.5m` döner — **regresyon testi zorunlu** (mevcut 8+ çağrı noktasının
  hiçbiri bugünkü testlerinde kırılmamalı).
- `CustomerServiceTests` (mevcut dosyada) — `CariTcBenzersizlikKapali=true` iken AYNI TC ile
  2. cari açılabildiği; `false` (varsayılan) iken hâlâ `DuplicateCariException` fırlatıldığı
  (regresyon).
- `CanliGunKalibrasyonTests.cs` **DOKUNULMADAN** yeşil kalmalı (Grup 3 madde 6, `KismiGunEsigiSaat`
  bu PR'da elle değiştirilmedi — testin kendisi bunu zaten garantiler, ama PR açılırken bilinçli
  kontrol edilir).

## Exit
- [ ] Grup 3'ün 5 alanı formda var; `VarsayilanYakitSeviyesi` dışındakiler "Beklemede" etiketiyle
      GÖRÜNÜYOR ama hiçbir hesaplamaya bağlı DEĞİL
- [ ] `VarsayilanYakitSeviyesi` set edilince kira teslim formunun ön-değeri değişiyor, set
      edilmezse `8` (eski davranış) korunuyor
- [ ] `KurElleGirisKilitli=true` → açık kur girişi HER YERDE (8+ çağrı noktası) reddediliyor;
      `false` → hiçbir mevcut test kırılmıyor
- [ ] `CariTcBenzersizlikKapali=true` → aynı TC ile 2. cari açılabiliyor; `false` → mevcut kural
      korunuyor
- [ ] `KismiGunEsigiSaat` (BookingMath) bu PR'da DEĞİŞMEDİ, `CanliGunKalibrasyonTests.cs` yeşil
- [ ] Tam suite yeşil

## Notlar
Bu faz PARA-ADJACENT ama D5 (yeni para-hareketi-yazan akış) değil — hiçbir yeni ledger kaydı
üretmiyor. Yine de CLAUDE.md §3 madde 5 ("para tutan her PR için zorunlu adversarial inceleme")
bu PR için de UYGULANIR, çünkü `KurElleGirisKilitli` ve `CariTcBenzersizlikKapali` gerçek
fiyatlama/muhasebe davranışını (kur seçimi, cari tekilliği) DEĞİŞTİREBİLİR — commit öncesi ayrı
bir ajanın en az şu ikisini çürütmeye çalışması istenir: (a) `KurElleGirisKilitli` guard'ının 8+
çağrı noktasından birini KAÇIRIP KAÇIRMADIĞI (grep ile tam liste doğrulanmalı), (b)
`CariTcBenzersizlikKapali=true` iken blind-index kısmi unique index'in (DB seviyesi, ikinci
savunma) hâlâ patlamadığı — patlıyorsa bu bir DB-seviye kısıtı gevşetme migration'ı da gerektirir
(bu faz DB kısıtını GEVŞETMİYOR, yalnız uygulama-seviyesi throw'u atlıyor; ikisi arasındaki
çelişki adversarial turda netleşmeli).
