# FAZ-21 — Filo Kiralama Derinlik + Liste/Arama

| | |
|---|---|
| **Desen** | D2 (alan derinliği) + D3 (filtre formu) |
| **Efor** | 1,5 gün |
| **Bağımlılık** | yok |
| **Kapsanan canlı ekran** | `filo_arac_kiralama.aspx`, `filo_kiralama_listesi.aspx` |
| **Risk** | düşük — `FiloKiralama` **defter postlamıyor** (bkz. entity XML doc: "DEFTER POSTLAMAZ —
  çok-aylık gelir peşin tanınmaz, gelir aylık faturalama ile tanınır"); bu faz yalnız sözleşme
  meta-alanları + arama ekliyor, para akışına dokunmuyor |

## Amaç
Filo (uzun-dönem) kiralama sözleşmesine canlıdaki satış temsilcisi/fatura türü/sözleşme no/vade gün
gibi eksik alanları ekleyip, liste ekranına cari/tarih/plaka/araç arama filtresi kazandırarak
kullanıcı `/filo-kiralama` üzerinde canlı paritesine yakın arama+kayıt yapabilir hale gelir.

## Neden (kanıt)
`src/RentACar.Domain/Entities/FiloKiralama.cs` içinde şu an yalnız `No, MusteriId, VehicleId, BasTar,
SureAy, AylikUcret, KdvOrani, Currency, Kur, ToplamKmLimiti, DamgaVergisi, Durum, Aciklama` var.
Plandaki 11 alan (`SatisTemsilcisi`, `FaturaTuru`, `SozlesmeTarihi`, `MakbuzNo`, `DosyaNo`,
`ImzaTarih`, `SozlesmeNo`, `VadeGun`, `FiyatTuru`, `Kaynak`, `CikisKm`, `ToplamKm`) **hiçbiri yok**
(grep doğrulandı). `FiloKiralamaList.razor`'da cari/tarih/plaka/araç arama filtresi yok — liste tüm
kayıtları filtresiz döküyor.

## Yapılacaklar
1. `src/RentACar.Domain/Entities/FiloKiralama.cs` — 11 nullable alan ekle: `SatisTemsilcisi`
   (string?), `FaturaTuru` (string?, "Dönem"/"Kırık" — serbest metin + ComboBox öneri listesi),
   `SozlesmeTarihi` (DateTimeOffset?, `BasTar`'dan ayrı — imza tarihi), `MakbuzNo` (string?),
   `DosyaNo` (string?), `ImzaTarih` (DateTimeOffset?), `SozlesmeNo` (string?), `VadeGun` (int?),
   `FiyatTuru` (string?, "Aylık"/"30 Gün Aylık"/"KDV Dahil"/"30 Gün Dahil"), `Kaynak` (string?),
   `CikisKm` (int?), `ToplamKm` (int?). **Dikkat:** entity'de zaten `ToplamKmLimiti` (int?, KM
   limiti) var — yeni `ToplamKm` (fiilen sürülen km) ONUNLA KARIŞTIRILMAMALI; isim çakışmasını
   önlemek için alan yorum satırında ikisinin farkı açıkça yazılır ("Limiti"=sözleşme limiti,
   "ToplamKm"=dönüşte okunan fiili km).
2. `src/RentACar.Infrastructure/Persistence/Configurations/VehicleConfigs.cs` — `FiloKiralamaConfig`
   (satır 106) sınıfına 11 yeni kolon tip/uzunluk ayarı ekle.
3. Migration: `dotnet ef migrations add AddFiloKiralamaDerinlik --project src/RentACar.Infrastructure
   --startup-project src/RentACar.Infrastructure`.
4. `src/RentACar.Web/Components/Pages/FiloKiralamalar/FiloKiralamaList.razor` — create/edit formuna
   11 yeni input ekle (`FaturaTuru`/`FiyatTuru` ComboBox seç-veya-yaz konvansiyonu; tarihler
   `datetime-local`; `FormParse.Date/Int` ile opsiyonel-alan-boş-string tuzağına karşı uçta çevir).
5. `src/RentACar.Application/FiloKiralamalar/FiloKiralamaService.cs` — create/update input modeline
   (`FiloKiralamaModels.cs`) 11 alanı ekle, map et.
6. `FiloKiralamaList.razor`'a filtre formu ekle: Cari Bilgi (No/Ad/Soyad —
   `CustomerService.ListAsync` üzerinden), Tarih aralığı (başlangıç/bitiş), Plaka arama, Araç arama.
   "Tablo Ayarlarını Kaydet" (kullanıcı bazlı grid düzeni) **eklenmez** — bizde altyapı yok, kapsamı
   büyütmemek için bilinçli atlanır.

## Dokunulacak dosyalar
- `src/RentACar.Domain/Entities/FiloKiralama.cs` — 11 alan
- `src/RentACar.Infrastructure/Persistence/Configurations/VehicleConfigs.cs` — `FiloKiralamaConfig`
- (yeni) migration dosyası
- `src/RentACar.Web/Components/Pages/FiloKiralamalar/FiloKiralamaList.razor` — form + filtre
- `src/RentACar.Application/FiloKiralamalar/FiloKiralamaModels.cs`
- `src/RentACar.Application/FiloKiralamalar/FiloKiralamaService.cs`

## Migration
Var — mevcut tenant-owned `FiloKiralamalar` tablosuna 11 nullable kolon. RLS zaten aktif tabloda,
yeni tablo yok → RLS bloğu **gerekmez**.

## Test
- `FiloKiralamaTests`: 11 yeni alan round-trip (kaydet→oku, bağımsız oracle: elle kurulan sözleşme
  verisiyle karşılaştır).
- Filtre testi: 3 filo kiralama (2 farklı cari, 2 farklı plaka) elle oluşturulur; cari adıyla arama
  yalnız o cariye ait kaydı döndürüyor mu (beklenen sayı testte sabit, servis kodundan türetilmez).

## Exit
- [ ] 11 yeni alan formda + grid'de + kaydediliyor
- [ ] Cari/Tarih/Plaka/Araç filtresi çalışıyor
- [ ] `FiloKiralama` hâlâ deftere postlamıyor (para davranışı değişmedi — regresyon testiyle doğrulanır)
- [ ] Tam suite yeşil

## Notlar
Canlının **çok-araçlı sözleşme modeli** (bir sözleşmeye birden çok araç/fiyat satırı) BU FAZA
GİRMEZ — tek-araç/tek-sözleşme modelini çok-araçlı hale getirmek CLAUDE.md §2 mimari kararına dokunan
bir yeniden-tasarım; ayrı bir roadmap kararı olarak kullanıcıya sorulmalı, burada açık bırakılır.
