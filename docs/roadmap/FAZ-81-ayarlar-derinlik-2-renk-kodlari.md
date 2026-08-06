# FAZ-81 — Ayarlar Derinlik PR-2: Görsel Tema Renk Kodları

| | |
|---|---|
| **Desen** | D2 — kural taşıyan master |
| **Efor** | 1,5 gün |
| **Bağımlılık** | yok |
| **Kapsanan canlı ekran** | `ayarlar.aspx` (Görünüm/Renk Kodları alt-bölümü, 8 alan) |
| **Risk** | düşük-orta — kod değişikliği para/model'e dokunmuyor, ama geniş CSS dokunuş yüzeyi (birden çok sayfa) test ister |

## Amaç
Tenant, panoda/listelerde tekrar eden 8 durumun (geciken, limit-aşan bakiye, opsiyonlu rezervasyon,
bugün dönecek/çıkacak araç, atanmış plakalı rezervasyon, boştaki/kiralanmayan araç) rengini kendi
kurumsal paletine göre özelleştirebilir hâle gelir.

## Neden (kanıt)
Canlı `ayarlar.aspx`'te 8 renk-kod alanı var: `RenkLimitBakiye`, `RenkOpsiyonlu`, `RenkAlacakli`,
`RenkBugunDonecekler`, `RenkBugunCikacaklar`, `RenkRezAtananPlaka`, `RenkGecikenler`,
`RenkKiralanmayan`. Bizde bu 8 kavramın renk-kodu **hiçbir yerde tenant'a bağlı değil** —
`TenantSettings.cs` (78 satır, tam okundu) bu alanları taşımıyor.

**Doğrulanmış gerçek durum (kod okunarak, plan metnindeki iddia yerine):** yalnız İKİ kavram
bugün GERÇEKTEN renkli/rozetli gösteriliyor, geri kalan ALTISI bugün düz metin:
- **RenkGecikenler** — iki gerçek tüketim noktası VAR: `src/RentACar.Web/Components/Pages/Home.razor`
  satır 149 (`<span class="badge danger">@_gecmis geçmiş</span>`) ve
  `src/RentACar.Web/Components/Pages/Regulation/VadePanosu.razor` satır 59-64
  (`BucketClass`: `Gecmis`/`YediGun` → `"danger"`, `OtuzGun` → `"warn"`).
- Diğer altısı (**RenkLimitBakiye, RenkOpsiyonlu, RenkAlacakli, RenkBugunDonecekler,
  RenkBugunCikacaklar, RenkRezAtananPlaka, RenkKiralanmayan**) bugün **hiçbir sayfada renk/rozet
  ile ayrışmıyor** — doğrulama: `ReservationList.razor` Durum kolonu (satır 102, düz
  `@r.Durum`), `RentalList.razor` Durum kolonu (satır 73 civarı, düz metin), Home.razor'daki
  Dönüşler/Çıkışlar tabloları (`src/RentACar.Web/Components/Shared/DcTable.razor`, satır 1-29,
  hiçbir `<tr>`'ye class YOK) — bunların hepsi bugün renksiz düz tablo satırı. `Customer.RiskLimiti`
  (satır 106) var ama cari listesinde/ekstrede bakiye-limit karşılaştırması renkle gösterilmiyor.
  `VehicleStatus.Musait` (boşta) durumu da liste/panoda renkle ayrışmıyor.

Bu, plan metninin "bugün bu durumlar CSS'te SABİT sınıflarla basılıyor" iddiasından daha DAR bir
gerçek: yalnız `k-red`/`k-green`/`k-orange`/`k-purple` (Home.razor KPI kartları, `app.css` satır
365-368) ve `badge.danger`/`badge.warn` (`app.css` satır 216-218) VAR ama bunlar 4 filo-durumu KPI
kartına (Kirada/Boşta/Serviste/Açık Rez) ve vade-bucket'ına bağlı — **8 renk-kodu kavramının 6'sı
için bugün BASILAN bir renk/rozet YOK**, yalnız CSS değişkeni tanımlamak yetmez, o 6 kavram için
**yeni** bir görsel işaretleme de eklenmesi gerekiyor. Bu fazın "en az 4 tüketim noktası" hedefi bu
yüzden hem var-olanı yeniden-yönlendirmeyi (Gecikenler) hem de gerçekten yeni rozet eklemeyi
(kalan 3+) kapsar.

## Yapılacaklar
1. `src/RentACar.Domain/Entities/TenantSettings.cs` — 8 nullable hex-string alan ekle:
   `RenkLimitBakiye`, `RenkOpsiyonlu`, `RenkAlacakli`, `RenkBugunDonecekler`, `RenkBugunCikacaklar`,
   `RenkRezAtananPlaka`, `RenkGecikenler`, `RenkKiralanmayan` (hepsi `string?`, ör. `"#dc2626"`).
2. `src/RentACar.Application/TenantSettings/TenantSettingsModel.cs` — aynı 8 alanı ekle.
3. `src/RentACar.Application/TenantSettings/TenantSettingsService.cs` — `GetAsync()` (satır 17-66)
   ve `SaveAsync()` (satır 100-143) map'lerine 8 alanı ekle; hex-format doğrulaması eklenir
   (`^#[0-9A-Fa-f]{6}$` regex, boş → null serbest; format hatalıysa `ValidationException`).
4. `src/RentACar.Web/TenantSettings/TenantSettingsEndpoints.cs` — `/kaydet` ucuna
   `f["renkLimitBakiye"].ToString()` vb. 8 satır eklenir.
5. `src/RentACar.Web/Components/Pages/Settings/Ayarlar.razor` — mevcut "Görünüm + Operasyon
   Kuralları" fieldset'inin (satır 43-66) altına yeni `<fieldset><legend>Görünüm Renk
   Kodları</legend>` bloğu, 8 `<input type="color" name="renk...">` ile eklenir (aynı ana
   `/ayarlar/kaydet` formunun İÇİNDE — yeni form açılmaz).
6. `src/RentACar.Web/Components/Layout/MainLayout.razor` — **`ITenantContext` (satır 4 inject)
   yalnız `TenantId` taşır, renk alanlarını taşımaz** (arayüz doğrulandı,
   `src/RentACar.Domain/Common/ITenantContext.cs`) — bu yüzden ayrıca
   `@inject RentACar.Application.TenantSettings.ITenantSettingsRepository AyarRepo` eklenir
   (hassas-olmayan ayar → `KdvVarsayilan.cs`/`VarsayilanGrupCozucu.cs` İLE AYNI DESEN: admin-gate'li
   `TenantSettingsService` yerine repo doğrudan okunur — layout HER rolde render olur, ManageUsers
   guard'ına çarpmamalı). `OnParametersSetAsync`'te `var ayar = Tenant.TenantId is { } tid ? await
   AyarRepo.GetAsync() : null;` okunur, `<head>`'e `<style>@("--tr-renk-gecikenler:" + (ayar?.
   RenkGecikenler ?? "inherit") + ";")</style>` vb. 8 satır basılır; boş alan mevcut sabit
   tasarım-tokenına düşer (`inherit`/tanımsız → CSS zaten fallback zincirini
   `var(--tr-renk-x, var(--mevcut-token))` ile çözer, aşağıdaki adımda).
7. `src/RentACar.Web/wwwroot/app.css` — `.badge.danger` (satır 217) ve `.kpi.k-red`
   (satır 367) tanımlarını `background: var(--tr-renk-gecikenler, #fdecea);` gibi fallback'li
   CSS-var'a yönlendir (mevcut sabit değer FALLBACK olarak kalır — tenant boşsa görünüm birebir
   aynı).
8. `src/RentACar.Web/Components/Pages/Regulation/VadePanosu.razor` — `BucketClass` (satır 59-64)
   çıktısı olan `"danger"`/`"warn"` sınıflarının CSS tanımı da adım 7'deki aynı `--tr-renk-*`
   değişkenine bağlanır.
9. `src/RentACar.Web/Components/Shared/DcTable.razor` — yeni opsiyonel `[Parameter] string?
   RowStyle` eklenir; `Home.razor` (zaten `ITenantSettingsRepository` enjekte etmeye uygun, madde 6
   ile AYNI okuma deseni) kendi `OnInitializedAsync`'inde okuduğu ayar değerini Dönüşler paneli
   `Df=="bugun"` (varsayılan) tab'ında `RowStyle="@($"border-left:3px solid
   {_ayar?.RenkBugunDonecekler}")"` olarak DcTable'a geçer (`_ayar` — Home.razor code-behind'a
   eklenen alan), Çıkışlar paneli aynı mekanizmayla `RenkBugunCikacaklar` kullanır — **yeni
   rozet/vurgu, önceden YOKTU** (madde 4'teki bulguya bağlı).
10. `src/RentACar.Web/Components/Pages/Bookings/ReservationList.razor` — sayfaya
    `@inject RentACar.Application.TenantSettings.ITenantSettingsRepository AyarRepo` eklenir,
    `OnInitializedAsync`'te `_ayar = await AyarRepo.GetAsync();` okunur; Durum kolonuna
    (satır 102) `r.Durum == ReservationStatus.Rezerv` iken `RenkOpsiyonlu` ile arka-plan rengi
    basan bir `<span style="background:@_ayar?.RenkOpsiyonlu">` eklenir (canlının "Opsiyonlu"
    kavramı en yakın bizim `Rezerv` durumuna karşılık geliyor — henüz onaylanmamış tutma).
11. `RenkLimitBakiye`, `RenkAlacakli`, `RenkRezAtananPlaka`, `RenkKiralanmayan` için **bu fazda**
    CSS değişkeni tanımlanır (`MainLayout.razor` `<style>` bloğunda) ama tüketim noktası
    EKLENMEZ — mevcut sayfalarda (cari listesi/ekstre, `ReservationList.razor` araç-atama kolonu,
    `VehicleList.razor`) bugün bu kavramların net bir görsel karşılığı olmadığından, hangi satırın
    hangi eşiği geçtiğinde rengin uygulanacağı (ör. "Alacaklı" ne demek — bakiye pozitif mi, kaç
    gündür hareketsiz mi) ayrı bir netleştirme ister; değişken TANIMLI ama BAĞLANMAMIŞ bırakılır
    (gelecekteki bir fazın kolay-ekleme noktası).

## Dokunulacak dosyalar
- `src/RentACar.Domain/Entities/TenantSettings.cs`
- `src/RentACar.Application/TenantSettings/TenantSettingsModel.cs`
- `src/RentACar.Application/TenantSettings/TenantSettingsService.cs`
- `src/RentACar.Web/TenantSettings/TenantSettingsEndpoints.cs`
- `src/RentACar.Web/Components/Pages/Settings/Ayarlar.razor`
- `src/RentACar.Web/Components/Layout/MainLayout.razor`
- `src/RentACar.Web/wwwroot/app.css`
- `src/RentACar.Web/Components/Pages/Regulation/VadePanosu.razor`
- `src/RentACar.Web/Components/Shared/DcTable.razor`
- `src/RentACar.Web/Components/Pages/Home.razor`
- `src/RentACar.Web/Components/Pages/Bookings/ReservationList.razor`
- (yeni) migration `AddTenantSettingsRenkKodlari`

## Migration
Var — `Ayarlar` tablosuna 8 nullable `varchar(7)` kolon. Tablo zaten RLS'li (ilk migration'da
kurulu) — RLS bloğu tekrar eklenmez.

## Test
- `tests/RentACar.IntegrationTests/TenantSettingsTests.cs` — 8 alan round-trip; geçersiz hex
  (`"kirmizi"`, `"#12"`) → `ValidationException` (beklenen mesaj bağımsız yazılır); boş → null
  serbest.
- Component/entegrasyon testi yoksa (Blazor statik-SSR render testleri sınırlıysa) en azından
  `MainLayout` render edilen HTML çıktısında `--tr-renk-gecikenler:` satırının tenant değeriyle
  BİREBİR eşit çıktığı bir smoke test (bağımsız oracle: test kendi elle bir hex değeri kurar, HTML
  çıktısında AYNEN aratır — CSS'in kendisinden değil).
- Regresyon: renk alanları BOŞ bir tenant'ta `.badge.danger`/`.kpi.k-red`/vade-bucket görünümünün
  eski (sabit) renklerle PİKSEL-ÖZDEŞ kaldığı doğrulanır (fallback zinciri kırılmadı).

## Exit
- [ ] 8 renk alanı Ayarlar formunda `<input type="color">` ile kaydediliyor
- [ ] `RenkGecikenler` boşken görünüm eski sabit kırmızı/turuncu ile birebir aynı (regresyon)
- [ ] `RenkGecikenler` set edilince Home.razor `_gecmis` rozeti VE VadePanosu bucket rozetleri
      AYNI anda değişiyor (tek kaynak — çift bakım riski yok)
- [ ] `RenkBugunDonecekler`/`RenkBugunCikacaklar`/`RenkOpsiyonlu` en az 3 SAYFADA görünür yeni
      vurgu üretiyor (önceden yoktu — bu fazda eklendi)
- [ ] Tam suite yeşil

## Notlar
Plandaki "kod değişikliği yok, yalnız CSS değişkeni + ayar formu" varsayımı BU KADAR DAR değil —
kod okunduğunda 8 kavramın 6'sının bugün hiç görsel karşılığı olmadığı görüldü (madde 4). Bu fazın
efor tahmini (1,5 gün) bu yüzden hem CSS-var kablolamasını hem de en az 4 sayfada YENİ rozet/vurgu
eklemeyi kapsayacak şekilde korundu; `RenkLimitBakiye`/`RenkAlacakli`/`RenkRezAtananPlaka`/
`RenkKiralanmayan` için tüketim noktası bilinçli olarak bu fazda AÇILMADI (madde 11) — her biri
"hangi eşik/durumda hangi satır" sorusuna netleşmeden bağlanırsa yanlış/gürültülü bir görsel kural
donar. Kullanıcıya bu 4 kavramın canlıdaki tam tetikleme koşulu (ör. "Alacaklı" = bakiye>0 mu,
RiskLimiti aşıldı mı) sorulmalı; netleşince ayrı bir küçük takip fazı (D2, ~0,5g) ile bağlanabilir.
