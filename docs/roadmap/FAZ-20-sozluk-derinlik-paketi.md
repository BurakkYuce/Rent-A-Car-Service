# FAZ-20 — Basit Sözlük Derinlik Paketi (Araç Grubu + Döviz + Hesap No)

| | |
|---|---|
| **Desen** | D2 — kural taşıyan master (×2) + D1 (Döviz'de Ülke alanı) |
| **Efor** | 1,5 gün (plan 1,75g idi; Hesap No'daki `Sube` wire-in kalemi kodda zaten yapılmış çıktı — 0,25g düştü, madde altında not) |
| **Bağımlılık** | yok |
| **Kapsanan canlı ekran** | `arac_grubu.aspx`, `para_tanimlama.aspx`, `hesap_no_tanimlama.aspx` |
| **Risk** | düşük — migration var ama saf ek-kolon, hesaplama/defter mantığı değişmiyor |

## Amaç
Araç Grubu, Döviz ve Hesap No sözlüklerindeki canlıda var-bizde-yok alanları forma ve grid'e taşıyarak
kullanıcı bu üç tanım ekranında entegrasyon kodu, web/servis ID, provizyon dövizi, ülke ve hediye
çeki/özel kod gibi alanları görüp düzenleyebilir hale gelir.

## Neden (kanıt)
- `src/RentACar.Domain/Entities/VehicleGroup.cs` içinde `ProvizyonDoviz`, `Provizyon2Doviz`,
  `YakitTuru`, `Vites`, `EntegrasyonKod1`, `WebId`, `ServisId` alanları **yok** (entity'de doğrulandı).
  Ayrıca `SurucuMinYas`/`EhliyetMinYil` zaten entity'de var ama
  `src/RentACar.Web/Components/Pages/VehicleGroups/VehicleGroupList.razor` grid'i (satır 103) yalnız
  `Kod/Ad/SIPP/Segment/GünlükKM/Provizyon/Durum` (7 kolon) gösteriyor — Açıklama, Araç Sayısı, Web ID,
  Servis ID, Sürücü Yaşı, Ehliyet Yılı grid'e taşınmamış.
- `src/RentACar.Domain/Entities/Currency.cs` içinde `Ulke` alanı **yok**.
- `src/RentACar.Domain/Entities/FinancialAccount.cs` içinde `HediyeCek`, `OzelKod`,
  `UyariMailListesi` **yok**.
- **Plan-vs-repo düzeltmesi:** plan dosyası `FinancialAccount.Sube` alanının "entity'de var ama forma
  hiç bağlanmamış" olduğunu söylüyordu — bu artık **doğru değil**. `FinancialAccountList.razor` satır
  4/49/63/93'te `Sube` zaten ComboBox olarak forma VE grid'e bağlı (`BranchService` üzerinden).
  Bu maddeyi **atla** — tekrar iş yok.

## Yapılacaklar
1. `src/RentACar.Domain/Entities/VehicleGroup.cs` — 7 yeni nullable alan ekle: `ProvizyonDoviz`
   (string?, 3 harf), `Provizyon2Doviz` (string?), `YakitTuru` (`FuelType?`), `Vites` (`Vites?`),
   `EntegrasyonKod1` (string?), `WebId` (string?), `ServisId` (string?).
2. `src/RentACar.Infrastructure/Persistence/Configurations/PricingConfigs.cs` —
   `VehicleGroupConfig` (satır 31) sınıfına yeni kolonların tip/uzunluk ayarlarını ekle.
3. Migration: `dotnet ef migrations add AddVehicleGroupDerinlik --project src/RentACar.Infrastructure
   --startup-project src/RentACar.Infrastructure`.
4. `src/RentACar.Web/Components/Pages/VehicleGroups/VehicleGroupList.razor` — create formuna (satır
   63-98) ve edit formuna (satır 122-168) 7 yeni input ekle (`YakitTuru`/`Vites` için mevcut enum
   `<select>` konvansiyonu; diğerleri düz `<input>`). Grid'e (satır 103, 8→14 kolon) **Açıklama**,
   **Araç Sayısı**, **Web ID**, **Servis ID**, **Sürücü Yaşı**, **Ehliyet Yılı** kolonlarını ekle.
   Araç Sayısı için `VehicleGroupService`'e `COUNT(Vehicle WHERE Grup=g.Ad)` döndüren küçük bir
   sorgu ekle (mevcut `ListUnmatchedGrupValuesAsync`'teki grup-eşleştirme mantığına benzer).
5. `src/RentACar.Domain/Entities/Currency.cs` — `Ulke` (string?) ekle.
6. `src/RentACar.Infrastructure/Persistence/Configurations/MasterConfigs.cs` — Currency config'e
   `Ulke` kolonu ekle. **Kur alanı EKLENMEZ** (kur `/kurlar` TCMB+tenant-sabit akışından tek kaynaklı
   yönetiliyor; `Currency`'ye ikinci bir kur alanı çift-kaynak riski yaratır — kural 6 mimari
   gerileme, bilinçli atlanır).
7. `src/RentACar.Web/Components/Pages/Currencies/CurrencyList.razor` — create/edit formuna ve grid'e
   (Kod/Ad/Sembol/Durum → +Ülke) `Ulke` ekle.
8. `src/RentACar.Domain/Entities/FinancialAccount.cs` — `HediyeCek` (bool, default false), `OzelKod`
   (string?), `UyariMailListesi` (string?, virgülle ayrılmış e-posta — yalnız alan, tetikleme mantığı
   bu fazda YOK) ekle.
9. `src/RentACar.Infrastructure/Persistence/Configurations/MasterConfigs.cs` — FinancialAccount
   config'e 3 yeni kolon ekle.
10. `src/RentACar.Web/Components/Pages/FinancialAccounts/FinancialAccountList.razor` — create/edit
    formuna ve grid'e `HediyeCek` (checkbox), `OzelKod`, `UyariMailListesi` ekle. `Sube` zaten var,
    dokunma.
11. Tek migration'da 3 entity'nin kolonlarını birlikte topla (aynı reçete, aynı PR — CLAUDE.md §5
    tek-tek migration yerine bu fazda birleştirilebilir çünkü hepsi additive nullable kolon, RLS
    değişmiyor — mevcut tablolar zaten RLS'li).

## Dokunulacak dosyalar
- `src/RentACar.Domain/Entities/VehicleGroup.cs` — 7 alan
- `src/RentACar.Domain/Entities/Currency.cs` — 1 alan
- `src/RentACar.Domain/Entities/FinancialAccount.cs` — 3 alan
- `src/RentACar.Infrastructure/Persistence/Configurations/PricingConfigs.cs` — `VehicleGroupConfig`
- `src/RentACar.Infrastructure/Persistence/Configurations/MasterConfigs.cs` — Currency + FinancialAccount config
- (yeni) migration dosyası
- `src/RentACar.Web/Components/Pages/VehicleGroups/VehicleGroupList.razor`
- `src/RentACar.Web/Components/Pages/Currencies/CurrencyList.razor`
- `src/RentACar.Web/Components/Pages/FinancialAccounts/FinancialAccountList.razor`
- `src/RentACar.Application/VehicleGroups/VehicleGroupService.cs` — Araç Sayısı sorgusu

## Migration
Var — 3 entity'ye toplam 11 nullable kolon ekleniyor (`AddVehicleGroupDerinlik` + aynı migration'a
Currency/FinancialAccount kolonları eklenebilir ya da 3 ayrı migration; hangisi seçilirse RLS bloğu
gerekmez çünkü **yeni tablo yok**, mevcut tenant-owned tablolara kolon ekleniyor — RLS zaten o
tablolarda aktif.

## Test
- `VehicleGroupTests` / `VehicleGroupRuleTests`: yeni 7 alan kaydedilip geri okunuyor (round-trip).
  Araç Sayısı testi: bağımsız oracle — 3 araç elle "EKO" grubuna atanır, grup listesinde
  `AracSayisi == 3` beklenir (sayı testten türetilir, servis kodundan değil).
- `CurrencyTests` (yoksa yeni): `Ulke` round-trip.
- `FinancialAccountTests`: `HediyeCek`/`OzelKod`/`UyariMailListesi` round-trip; mevcut `Sube` testi
  varsa dokunulmaz (zaten geçiyor).

## Exit
- [ ] 3 entity'de yeni alanlar formda görünüyor, kaydediliyor, grid'de gösteriliyor
- [ ] Araç Grubu grid'i 6 yeni kolonu (Açıklama/Araç Sayısı/Web ID/Servis ID/Sürücü Yaşı/Ehliyet Yılı)
      gösteriyor
- [ ] Currency'ye Kur alanı **eklenmedi** (bilinçli kısıt doğrulandı)
- [ ] Tam suite yeşil

## Notlar
`hesap_tanimalama.aspx` bu pakette YOKTUR — plan onu YAPILMAZ olarak işaretlemiş (tam örtüşme var,
gerçek fark yok); `docs/roadmap/_toplama-02-tanim-master.md`'de listelidir, faz dosyası almaz.
