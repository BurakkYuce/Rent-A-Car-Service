# FAZ-70 — Çoklu-Seçim Kapsam Alanları (Broker Yasakları + Tarife Matrisi)

| | |
|---|---|
| **Desen** | D2 |
| **Efor** | 2 gün (`broker_yasaklari.aspx` 1g + `tarifeler_xml.aspx` 1g) |
| **Bağımlılık** | yok |
| **Kapsanan canlı ekran** | `broker_yasaklari.aspx`, `tarifeler_xml.aspx` |
| **Risk** | düşük (additive alanlar + yeni UI bileşeni; `KiraSuresi` guard'ı hariç motor davranışı değişmiyor) |

## Amaç
Kullanıcı Broker Yasakları'nda Araç Grubu/Bölge kısıtını TEK bir değer yerine birden çok değer
seçerek girebilir (checkbox/çoklu-seçim); Tarife Matrisi'nde de Lokasyon aynı çoklu-seçim
bileşeniyle girilir, ayrıca matris satırına Kayıt Türü (Fiyat/Kampanya) etiketi ve Max Kira
Kapsamı (gün sınırı — aşan kiralarda satır motor tarafından elenir) eklenir.

## Neden (kanıt)
- `src/RentACar.Domain/Entities/BrokerYasak.cs`: `AracGrupKod` (string?, satır 26), `Bolge`
  (string?, satır 28) — TEKİL string alan, doğrulandı.
- `src/RentACar.Web/Components/Pages/BrokerYasaklari/BrokerYasakList.razor` satır 24-25 (ve
  düzenle formunda 64-65): düz `<input>` (text, `placeholder="Boş → tümü"`), multi-select/checkbox
  YOK.
- `src/RentACar.Application/BrokerYasaklari/BrokerYasakService.cs`: kapsam-eşleşme (scope-match)
  kontrolü DOSYADA YOK — servis yalnız CRUD/normalize/kod-benzersizlik yapıyor. Repo genelinde
  `BrokerYasak` grep'i (`RentalQuoteEngine.cs` dahil) sıfır tüketici tespit etti — bu tablo bugün
  SALT tanım/CRUD, hiçbir motor akışına bağlı değil. **Önemli düzeltme:** plan dosyasının
  "`BrokerYasakService`'teki kapsam-eşleşme kontrolü CSV listeyi `Contains` ile arar" ifadesi
  bugünkü kod tabanında karşılığı olmayan ileriye-dönük bir varsayımdır; bu faz SADECE veri
  modelini (CSV) ve UI'yı hazırlar, motor-bağlama plan dışıdır (bkz. Notlar).
- `src/RentACar.Domain/Entities/RateMatrix.cs`: `Lokasyon` (string?, satır 33) TEKİL;
  `RateMatrixInput.cs`'teki 19 alan listesinde `Turu`/`KiraSuresi` YOK (doğrulandı — grep sıfır
  eşleşme).
- `src/RentACar.Application/Pricing/RentalQuoteEngine.cs`: gün-sayısı sınırı (Max Kira Kapsamı)
  kontrolü yok — `KiraSuresi` grep'i sıfır eşleşme; `RateCard.Covers()` benzeri bir eleme mekanizması
  `RateMatrix` seçiminde YOK.

## Yapılacaklar
1. `src/RentACar.Domain/Entities/BrokerYasak.cs` — `AracGrupKod`/`Bolge` tipleri STRING kalır (yeni
   kolon yok); XML doc yorumuna "CSV, virgülle ayrılmış çoklu değer (ör. `EKO,STD,LUX`); mevcut
   tekil değer otomatik tek-elemanlı liste sayılır (geriye uyum)" notu eklenir.
2. `src/RentACar.Application/BrokerYasaklari/BrokerYasakService.cs` — CSV normalize helper'ı: girilen
   çoklu-seçim değerleri `string.Join(",", secilenler.Select(s => s.Trim().ToUpperInvariant())
   .Distinct())` ile tek stringe paketlenir (`Normalize()` içine). Ayrıca `public static bool
   KapsarMi(string? csv, string deger)` static yardımcı eklenir (`csv.Split(',').Select(x =>
   x.Trim()).Contains(deger, StringComparer.OrdinalIgnoreCase)`, `csv` boş/null → `false`) —
   bu metodun bu fazda TÜKETİCİSİ yoktur (motor-bağlama plan dışı), yalnız birim testle kanıtlanır.
3. `src/RentACar.Web/Components/Pages/BrokerYasaklari/BrokerYasakList.razor` — satır 24-25 (ve
   64-65) düz `<input>` yerine çoklu-seçim: Araç Grubu için `VehicleGroupService.ListActiveAsync`
   listesinden checkbox-grid; Bölge için repoda ayrı bir "Bölge" master tablosu YOK — serbest-metin
   chip-input (Enter/virgülle ekleme, seçilenler CSV'ye birleşir) kullanılır. Bu, **repodaki ilk
   çoklu-seçim UI bileşeni** olduğundan paylaşılabilir küçük bir Razor bileşeni olarak yazılır
   (adım 7'de `tarifeler_xml` aynı bileşeni kullanacak).
4. `src/RentACar.Domain/Entities/RateMatrix.cs` — iki additive nullable alan: `public string? Turu
   { get; set; }` (Fiyat/Kampanya, salt-etiket — motor davranışını değiştirmez) + `public int?
   KiraSuresi { get; set; }` (Max Kira Kapsamı, gün).
5. `src/RentACar.Application/RateMatrices/RateMatrixInput.cs` + `RateMatrixService.cs` — `Turu`/
   `KiraSuresi` alanları `Normalize()`/`Apply()`'a eklenir (mevcut 19-alan kopyalama bloğuna 2 satır).
6. `src/RentACar.Application/Pricing/RentalQuoteEngine.cs` — matris seçildikten sonra,
   `ResolveTierRate` çağrısından ÖNCE guard: `matris.KiraSuresi is { } maxGun && gun > maxGun` ise bu
   matris satırı ELENİR + `notlar.Add($"Tarife '{matris.Kod}' Max Kira Kapsamı ({maxGun} gün) aşıldı;
   satır elendi.")`; eleme sonrası davranış `RateCard.Covers()`'daki gibi — eşleşme yoksa mevcut
   "tarife bulunamadı" akışına düşer (yeni red yolu YOK, sessiz-güvenli davranış korunur).
7. `src/RentACar.Web/Components/Pages/RateMatrices/RateMatrixList.razor` — `Lokasyon` tekil alanı
   yerine adım 3'teki AYNI çoklu-seçim bileşeni (Lokasyon master listesi varsa ondan, yoksa serbest
   chip-input) + `Turu` (Fiyat/Kampanya select) + `KiraSuresi` (sayısal input) form alanları eklenir.

## Dokunulacak dosyalar
- `src/RentACar.Domain/Entities/BrokerYasak.cs` — yorum/semantik (kolon değişmiyor)
- `src/RentACar.Application/BrokerYasaklari/BrokerYasakService.cs` — CSV normalize + `KapsarMi`
- `src/RentACar.Application/BrokerYasaklari/BrokerYasakInput.cs`
- `src/RentACar.Web/Components/Pages/BrokerYasaklari/BrokerYasakList.razor`
- `src/RentACar.Web/BrokerYasaklari/BrokerYasakEndpoints.cs`
- (yeni, paylaşılan) `src/RentACar.Web/Components/Shared/` altında küçük çoklu-seçim/chip bileşeni
- `src/RentACar.Domain/Entities/RateMatrix.cs` — additive `Turu`, `KiraSuresi`
- `src/RentACar.Application/RateMatrices/RateMatrixInput.cs`
- `src/RentACar.Application/RateMatrices/RateMatrixService.cs`
- `src/RentACar.Application/Pricing/RentalQuoteEngine.cs` — `KiraSuresi` eleme guard'ı
- `src/RentACar.Web/Components/Pages/RateMatrices/RateMatrixList.razor`
- `src/RentACar.Web/RateMatrices/RateMatrixEndpoints.cs`

## Migration
`RateMatrix`'e 2 additive nullable kolon (`Turu string?`, `KiraSuresi int?`) — tablo zaten RLS'li
(mevcut migration'larda `ENABLE`+`FORCE`+`tenant_isolation` policy kurulu); yeni kolon eklemek RLS
bloğu **gerektirmez**. `BrokerYasak`'ta kolon değişikliği yok (mevcut string alan CSV anlamına
geçiyor, migration yok).

## Test
- `BrokerYasakTests`: `KapsarMi("EKO,STD,LUX", "STD")` → `true`; `KapsarMi("EKO,STD,LUX", "SUV")` →
  `false`; `KapsarMi(null, "EKO")` → `false` (bağımsız oracle — sabit örnek CSV, servis kodundan
  türetilmez).
- `RateMatrixTests`: elle kurulan 2 satır — satır A `KiraSuresi=14`, satır B `KiraSuresi=null` (aynı
  kanal/grup/şube kapsamında). `QuoteAsync(gun: 20)` çağrısı satır A'yı ELER, satır B ile fiyatlanır
  (bağımsız oracle: hangi satırın fiyatı döndüğü elle sabitlenir). `QuoteAsync(gun: 10)` her ikisiyle
  de eşleşebilir — seçim kuralı (M3: müşteri lehine) devam eder, sadece eleme testi eklenir.
- UI/servis testi: çoklu-seçimden gelen değerlerin CSV'ye doğru (tekrar temizlenmiş, büyük harf)
  birleştiği doğrulanır.

## Exit
- [ ] Broker Yasakları formu çoklu-seçimle CSV kaydediyor
- [ ] `BrokerYasakService.KapsarMi` birim testli (motor bağlantısı YOK, bilinçli — bkz. Notlar)
- [ ] `RateMatrix.Turu`/`KiraSuresi` CRUD'da çalışıyor
- [ ] `RentalQuoteEngine` `KiraSuresi` aşımında satırı elip not düşüyor (bağımsız oracle testli)
- [ ] Lokasyon çoklu-seçime geçti (aynı paylaşılan bileşen)
- [ ] Tam suite yeşil

## Notlar
`BrokerYasak`'ın motor tarafında hiç tüketilmediği bilinçli kabul edilir — `KapsarMi` bu PR'da
YAZILIR ama çağırıcısı yoktur (gerçek rezervasyon/booking akışına bağlanması AYRI ve daha büyük bir
iş; bu plan dosyasında kapsam dışı, ileride ayrı bir faz gerekir). Paylaşılan çoklu-seçim bileşeni
repodaki İLK örnektir; CLAUDE.md'nin ComboBox-seç-veya-yaz konvansiyonundan FARKLIDIR (enum/FK
select'e dokunmaz) — yalnız serbest-CSV kapsam alanları için kullanılır.
