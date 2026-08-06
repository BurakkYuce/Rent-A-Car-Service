# FAZ-85 — Web (Acente) Rezervasyonları: Kolon Derinliği

| | |
|---|---|
| **Desen** | D3 — liste/arama (yeni tablo YOK) |
| **Efor** | 0,5 gün |
| **Bağımlılık** | yok |
| **Kapsanan canlı ekran** | `web_rezervasyon.aspx` |
| **Risk** | düşük — salt görüntüleme, domain/servis değişikliği yok |

## Amaç
`/rezervasyonlar` listesinde bugün girilmiş ama grid'e yansımayan alanlar (teslim tarihi ayrı
kolon, müşteri cep telefonu, alış şubesi, kaynak) görünür hâle gelir + "Kaynak = Web/Acente"
filtresi eklenir — canlının kanal-filtreli izleme ekranına ayrı sayfa açmadan karşılık verilir.

## Neden (kanıt)
`src/RentACar.Web/Components/Pages/Bookings/ReservationList.razor` satır 159, `_list` tipi
`IReadOnlyList<Reservation>` — **RAW ENTITY**, projeksiyon/DTO değil. Yani ihtiyaç duyulan tüm
alanlar ZATEN `_list` üzerinden erişilebilir, hiçbir servis/domain değişikliği gerekmez:
- `Reservation.CikisOfisi` (`src/RentACar.Domain/Entities/Reservation.cs` satır 27) — var.
- `Reservation.Kaynak` (aynı dosya satır 68, "roadmap H2: rezervasyon kaynağı") — var.
- `Customer.CepTel`/`Ad`/`Soyad` (`src/RentACar.Domain/Entities/Customer.cs` satır 22-23, 41) —
  var; `_customers` (satır 160) zaten `IReadOnlyList<Customer>` (FULL entity, sadece
  `_custMap` isim-dictionary'sine indirgeniyor, satır 176).

Bugünkü grid (satır 84-102): kolonlar No/Müşteri/Araç/Tarih (BİRLEŞİK başlangıç-bitiş tek
hücrede, satır 99: `@r.BasTar... – @r.BitTar...`)/Gün/Tutar/Durum — Teslim Tarihi AYRI kolon
yok, Cep Telefonu/Alış Şube/Kaynak kolonu YOK, "Kaynak=Web/Acente" filtre dropdown'u yok (yalnız
create-formunda satır 72-77'de bir Kaynak SEÇİCİ var, listede filtre değil).

## Yapılacaklar
1. `ReservationList.razor` `@code` bloğuna (satır 156-182 civarı) `_custPhoneMap`
   (`Dictionary<Guid, string>`, `_customers.ToDictionary(c => c.Id, c => c.CepTel ?? "—")`)
   eklenir; `Phone(Guid id)` yardımcı metodu (`Cust`/`Veh` desenindeki gibi, satır 180-181).
2. Tablo başlığına (satır 86) 4 kolon eklenir: `<th>Teslim</th>`, `<th>Cep Tel</th>`,
   `<th>Alış Şube</th>`, `<th>Kaynak</th>`.
3. Her satıra (satır 95-103 arası) karşılık gelen hücreler eklenir:
   - Mevcut birleşik "Tarih" hücresi (satır 99) **"Başlangıç"** olarak SADECE `r.BasTar`
     gösterecek şekilde kısaltılır (bitiş tarihi ayrı "Teslim" kolonuna taşınır — bilgi
     kaybı yok, ayrıştırma).
   - Yeni "Teslim" hücresi: `@r.BitTar.LocalDateTime.ToString("dd.MM.yyyy HH:mm")`.
   - "Cep Tel": `@Phone(r.MusteriId)`.
   - "Alış Şube": `@(r.CikisOfisi ?? "—")`.
   - "Kaynak": `@(r.Kaynak ?? "—")`.
   - `colspan` değeri satır 91'de (boş-liste satırı) 8'den 12'ye güncellenir (4 yeni kolon).
4. Grid üstüne (satır 83, `<div class="table-scroll">` öncesi) bir "Kaynak" filtre `<select>`
   eklenir (mevcut `_kaynaklar` listesinden, satır 163) + `<option>Web</option>`/
   `<option>Acente</option>` gibi canlı-eşdeğeri seçenekler (tenant'ın tanımlı `ReservationSource`
   kayıtlarından hangileri "Web"/"Acente" anlamına geliyorsa — statik metin eşleşmesi, yeni enum
   YOK); seçim `[SupplyParameterFromQuery]` ile URL query param'a bağlanır (`?kaynak=Web`),
   `_list` render edilirken `.Where(r => string.IsNullOrEmpty(Kaynak) || string.Equals(r.Kaynak,
   Kaynak, StringComparison.OrdinalIgnoreCase))` filtre uygulanır (sunucu tarafı, yeni servis
   metodu GEREKMEZ — mevcut `Reservations.ListAsync()` sonucu üstünde bellek-içi filtre, liste
   zaten tüm tenant kapsamında küçük hacimli).

## Dokunulacak dosyalar
- `src/RentACar.Web/Components/Pages/Bookings/ReservationList.razor` (tek dosya — servis/domain
  değişikliği YOK)

## Migration
Yok — hiçbir domain/entity değişikliği yok, salt sayfa-içi projeksiyon/görüntüleme.

## Test
- Bu sayfa için mevcut bir component/entegrasyon testi varsa (`tests/RentACar.IntegrationTests/
  ReservationListTests.cs` veya benzeri) yoksa, en azından `ReservationService`/`CustomerService`
  üzerinden **bağımsız oracle** ile bir smoke test: 3 rezervasyon (elle farklı `Kaynak` değerleri
  ile: "Web", "Acente", null) oluşturulur; `?kaynak=Web` filtresiyle render edilen liste SADECE
  1 satır döner (test bunu elle kurduğu 3 rezervasyonun ID'siyle doğrular, sayfanın kendi filtre
  mantığından değil).
- Cep telefonu/Alış şube/Teslim tarihi hücrelerinin doğru `Reservation`/`Customer` alanından
  geldiği (ör. `Customer.CepTel = "05551112233"` set edilmiş bir cariyle oluşturulan
  rezervasyonun render çıktısında bu değerin BİREBİR göründüğü) — oracle: test kendi elle girdiği
  sabit telefon numarasını render sonucunda arar.

## Exit
- [ ] Grid'de Teslim/Cep Tel/Alış Şube/Kaynak kolonları görünüyor, mevcut kolonlar bozulmadı
- [ ] "Kaynak" filtresi URL query param ile çalışıyor, filtre boşken TÜM kayıtlar görünüyor
      (regresyon)
- [ ] Hiçbir domain/servis dosyası değişmedi (yalnız `ReservationList.razor`)
- [ ] Tam suite yeşil

## Notlar
Bu, desen kataloğunun D3 tanımıyla birebir örtüşen "en ucuz sınıf" örneği (10-ekleme-desenleri.md:
"veri zaten var, görünmüyor... parite kazancı/maliyet oranı en yüksek olan burası") — hiçbir yeni
tablo/domain alanı gerektirmiyor, salt var olan entity alanlarının sayfaya taşınması.
