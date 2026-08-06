# FAZ-80 — Ayarlar Derinlik PR-1: Ek Hizmet Açıklama/Max-Gün + Belge Şablon İmza Alanı

| | |
|---|---|
| **Desen** | D2 — kural taşıyan master |
| **Efor** | 1 gün (Grup 1: 0,5g + Grup 5: 0,5g) |
| **Bağımlılık** | yok |
| **Kapsanan canlı ekran** | `ayarlar.aspx` (Grup 1 — Sigorta/Ek Hizmet Tanımları alt-bölümü; Grup 5 — Sözleşme/Belge Şablonu alt-bölümü) |
| **Risk** | düşük — yalnız bilgi/kolon eklemesi, hiçbir tutar/fiyat hesabı değişmiyor |

## Amaç
Ek hizmet tanımlarına (GPS, Bebek Koltuğu, Genç Sürücü, Ek Sürücü…) pazarlama/hukuki açıklama
metni + maksimum-gün sınırı eklenir ve kira formunda görünür hâle gelir; ayrıca sözleşme/fatura/
makbuz belge şablonlarına, fiziksel imza satırının basılıp basılmayacağını kontrol eden bir anahtar
eklenir.

## Neden (kanıt)
**Grup 1:** `src/RentACar.Domain/Entities/EkHizmetTanim.cs` (29 satır) yalnız
`Kod/Ad/BirimUcret/KdvOrani/Aktif` taşıyor — `Aciklama`/`MaxGun` yok (dosya tam okundu). Canlının
ek-hizmet kategorileri (PAI/IMM/Muafiyet/Genç Sürücü/Bebek Koltuğu/Navigasyon/Mini Hasar/SCDW/LCF/
Ek Sürücü) her biri açıklama metni + max-gün taşıyor (ör. "Genç Sürücü max 30 gün"). Desen zaten
kod içinde ÖRNEK olarak var: kira mega-formunun ek-hizmet sekmesinde
(`src/RentACar.Web/Components/Pages/Bookings/KiraFormPaneller/SekmeEkHizmet.razor`) Sigorta
katalogu (`CoverageProduct`) satırları için satır 47 zaten
`@(s.MaxGun is { } mg ? $"maks {mg} gün" : "—")` basıyor — `EkHizmetTanim` satırları (satır 25-37)
için AYNI kolon yok, çünkü entity'de alan yok.

**Grup 5:** `src/RentACar.Web/Reports/PdfExportService.cs` satır 197-210, sözleşme PDF'inin sabit
imza bloğu: kart-sahibi imza satırı (satır 202: `İmza : {Dots(18)}`) + "AD SOYAD - NAME SURNAME" /
"İMZA - SİGNATURE" bloğu (satır 206-209, aralarında `Height(22)` boş fiziksel-imza alanı). Bu blok
HER ZAMAN basılıyor, tenant-bazlı kapatma anahtarı yok. `BelgeSablon.cs` (45 satır) bölüm-metni
override modeli (`BelgeBasligi/HukukiMetinSol/HukukiMetinSag/EkKosullarVarsayilan/AltBilgi`)
taşıyor ama imza-alanı gösterme/gizleme anahtarı yok.

## Yapılacaklar

### Grup 1 — Ek Hizmet açıklama + max-gün
1. `src/RentACar.Domain/Entities/EkHizmetTanim.cs` — ekle: `public string? Aciklama { get; set; }`,
   `public int? MaxGun { get; set; }`.
2. `src/RentACar.Application/EkHizmetler/EkHizmetTanimInput.cs` — aynı iki alanı ekle.
3. `src/RentACar.Application/EkHizmetler/EkHizmetTanimService.cs` — `Normalize()`'a
   `Aciklama = string.IsNullOrWhiteSpace(input.Aciklama) ? null : input.Aciklama.Trim()`,
   `MaxGun = input.MaxGun` ekle; `Validate()`'e `if (n.MaxGun is <= 0) throw new
   ValidationException("Max gün pozitif olmalıdır.");` ekle (null serbest — sınırsız); `Apply()`'a
   iki alanı yaz.
4. `src/RentACar.Web/EkHizmetler/EkHizmetEndpoints.cs` — `/create` ve `/update` uçlarına
   `[FromForm] string? aciklama, [FromForm] string? maxGun` parametreleri eklenir, giriş modeline
   `Aciklama = aciklama, MaxGun = FormParse.Int(maxGun)` ile aktarılır (CLAUDE.md §5 tuzağı: `int?`
   form alanı boş string'de `[FromForm] int?` olarak alınırsa 400 verir — `string?` alıp
   `FormParse.Int` ile çevrilir).
5. `src/RentACar.Web/Components/Pages/EkHizmetler/EkHizmetList.razor` — create formuna (satır
   19-23 civarı) ve update formuna (satır 47-54 civarı) "Açıklama" (`<textarea name="aciklama">`)
   ve "Max Gün" (`<input name="maxGun" type="number" min="1">`) alanları eklenir.
6. `src/RentACar.Web/Components/Pages/Bookings/KiraFormPaneller/SekmeEkHizmet.razor` satır 25-37
   `@foreach (var t in Vm.EkHizmetler)` bloğunda: "Hizmet" hücresine
   `title="@t.Aciklama"` tooltip eklenir; `t.MaxGun` doluysa "Miktar/Gün" hücresinin yanına
   Sigortalar satırındaki (satır 47) İLE AYNI biçimde `maks {mg} gün` bilgi rozeti eklenir
   (blokaj DEĞİL — miktar alanı yine serbestçe girilir, sadece bilgi amaçlı).
7. Migration: `dotnet ef migrations add AddEkHizmetAciklamaMaxGun --project
   src/RentACar.Infrastructure --startup-project src/RentACar.Infrastructure` — 2 nullable kolon.

### Grup 5 — Belge şablonu imza alanı
8. `src/RentACar.Domain/Entities/BelgeSablon.cs` — ekle:
   `public bool ImzaAlaniGoster { get; set; } = true;` (default `true` — mevcut PDF davranışını
   BAYT-ÖZDEŞ korur, yeni satır bulunamadığında/varsayılan şablonda hep basılır).
9. `src/RentACar.Application/Bookings/SozlesmeService.cs` — `SozlesmeView` record'una
   (satır 20-49) `bool SablonImzaAlaniGoster = true` eklenir, seçilen `BelgeSablon`'dan okunur
   (null şablon → `true`).
10. `src/RentACar.Web/Reports/PdfExportService.cs` satır 186-211'deki kart-sahibi+imza bloğu
    `s.SablonImzaAlaniGoster` `false` ise ATLANIR (fiziksel imza gerekmeyen elektronik-onaylı
    sözleşme senaryosu). Diğer bölümler (kart bilgisi vb. hariç, SADECE imza satırları) etkilenir —
    blok kesin sınırları uygulama sırasında `col.Item()` seviyesinde netleştirilir.
11. `src/RentACar.Web/Components/Pages/Settings/BelgeSablonList.razor` — create/update formuna
    "İmza Alanını Göster" checkbox'ı eklenir (default işaretli).
12. Migration: `dotnet ef migrations add AddBelgeSablonImzaAlani --project
    src/RentACar.Infrastructure --startup-project src/RentACar.Infrastructure` — 1 `bool NOT NULL
    DEFAULT true` kolon.

## Dokunulacak dosyalar
- `src/RentACar.Domain/Entities/EkHizmetTanim.cs`
- `src/RentACar.Application/EkHizmetler/EkHizmetTanimInput.cs`
- `src/RentACar.Application/EkHizmetler/EkHizmetTanimService.cs`
- `src/RentACar.Web/EkHizmetler/EkHizmetEndpoints.cs`
- `src/RentACar.Web/Components/Pages/EkHizmetler/EkHizmetList.razor`
- `src/RentACar.Web/Components/Pages/Bookings/KiraFormPaneller/SekmeEkHizmet.razor`
- `src/RentACar.Domain/Entities/BelgeSablon.cs`
- `src/RentACar.Application/Bookings/SozlesmeService.cs`
- `src/RentACar.Web/Reports/PdfExportService.cs`
- `src/RentACar.Web/Components/Pages/Settings/BelgeSablonList.razor`
- (yeni) 2 migration dosyası

## Migration
Var — iki ayrı additive migration (`AddEkHizmetAciklamaMaxGun`: `EkHizmetTanims` tablosuna 2
nullable kolon; `AddBelgeSablonImzaAlani`: `BelgeSablons` tablosuna 1 bool kolon, default `true`).
Her iki tablo zaten RLS'li (ilk oluşturma migration'ında `ENABLE`+`FORCE ROW LEVEL SECURITY` +
`tenant_isolation` policy kurulu) — **RLS bloğu TEKRAR eklenmez**, salt kolon ekleme.

## Test
- `tests/RentACar.IntegrationTests/EkHizmetTanimTests.cs` — yeni test: `Aciklama`/`MaxGun`
  round-trip (create+update); `MaxGun = 0` ve `MaxGun = -5` → `ValidationException` (beklenen
  mesaj testte elle yazılır, servis kodundan kopyalanmaz — bağımsız oracle); `MaxGun = null` kabul.
- `tests/RentACar.IntegrationTests/BelgeSablonTests.cs` — `ImzaAlaniGoster` round-trip;
  varsayılan (yeni satır, alan set edilmeden) `true` döndüğü doğrulanır.
- `tests/RentACar.IntegrationTests/PdfExportTests.cs` veya `SozlesmeViewTests.cs` — iki senaryo:
  (1) `ImzaAlaniGoster=true` (veya şablonsuz) → üretilen PDF metninde "İMZA - SİGNATURE" GEÇER;
  (2) `ImzaAlaniGoster=false` → üretilen PDF metninde "İMZA - SİGNATURE" GEÇMEZ. Oracle: literal
  string arama PDF'in text-extract çıktısında (QuestPDF render mantığından değil, üretilmiş byte
  çıktısından).

## Exit
- [ ] `EkHizmetTanim` create/update formunda Açıklama + Max Gün çalışıyor, mevcut satırlarda
      (alan boş) davranış DEĞİŞMEDİ
- [ ] Kira formu Ek Hizmet sekmesinde tooltip + "maks X gün" rozeti görünüyor (Sigorta satırıyla
      birebir aynı görsel dil)
- [ ] `BelgeSablon` için imza-alanı anahtarı formda var, varsayılan `true` (regresyon yok)
- [ ] `ImzaAlaniGoster=false` seçilen şablonla üretilen PDF'te imza satırları basılmıyor,
      DİĞER bölümler (hukuki metin, alt bilgi vb.) değişmedi
- [ ] Tam suite yeşil

## Notlar
Grup 5'te canlının RTF-dosya-yükle + serbest-metin Find/Replace editörü BİLİNÇLİ OLARAK
tekrarlanmıyor — bizim `BelgeSablon` bölüm+token modeli (`BelgeSablonCozumleyici.cs`,
`{FirmaMarka}` vb.) aynı işi güvenli/yapılandırılmış şekilde görüyor; serbest RTF editörü mimari
yinelenme olurdu. Bu fazın kapsamı SADECE eksik "kaşe/imza görseli" boşluğunu kapatıyor
(görüntüleme anahtarı; gerçek imza-görseli gömme — logo benzeri byte[] upload — ayrı bir istek
gelirse D2 olarak genişletilebilir, bu fazda YOK).
