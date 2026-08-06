# FAZ-59 — Cari Virman: Bilgi Alanları + Toplu Geçmiş Listesi

| | |
|---|---|
| **Desen** | D2 (bilgi alanları) + D3 (geçmiş listesi) |
| **Efor** | 1,5 gün (1 gün cari_virman + 0,5 gün cari_virman_islem_ara) |
| **Bağımlılık** | yok |
| **Kapsanan canlı ekran** | `cari_virman.aspx`, `cari_virman_islem_ara.aspx` |
| **Risk** | düşük — mevcut dengeli-çift-kayıt mantığı DEĞİŞMEZ, yalnız bilgi alanı + okuma-yolu |

## Amaç
Kullanıcı cari↔cari virman formunda Tarih (manuel)/Vade/Makbuz No/İşlem Şube/İşlem Yapan bilgilerini
girebilir; ayrıca TÜM cariler arası virman geçmişini birleşik bir listede görebilir hale gelir.

## Neden (kanıt)
`CariVirman.razor`'da şu an Tarih manuel girilemiyor (sunucu "şimdi" kullanıyor), Vade/Makbuz_No/
İşlem Şube/İşlem Yapan alanları yok (grep doğrulandı). `TransferBetweenCariAsync`
(`CashService.cs` L209-241) `tarih`/`vade`/`makbuzNo` parametresi almıyor.

Veri ZATEN var (`TransferBetweenCariAsync` `SourceType="CariVirman"` ile ledger'a yazıyor, her iki
carinin kendi ekstresinde görünüyor — L232-240) ama TÜM cariler arası birleşik liste yok.

## Yapılacaklar
1. `CashService.TransferBetweenCariAsync`'e `tarih` (DateTimeOffset?), `vade` (DateTimeOffset?),
   `makbuzNo` (string?) parametreleri eklenir (mevcut dengeli-çift-kayıt mantığı DEĞİŞMEZ — yalnız
   ek bilgi alanları `AccountLedgerEntry.Description`'a veya —gerekirse— yeni bir hafif
   "VirmanMeta" alanına eklenir; ledger şeması bozulmaz).
2. `CariVirman.razor` formuna Tarih (manuel), Vade, Makbuz_No, İşlem Şube, İşlem Yapan alanları
   eklenir ("İşlem Yapan" oturumdan gelir, form alanı EKLENMEZ — audit zaten taşıyor).
3. `src/RentACar.Web/Finance/FinanceEndpoints.cs`'e (`/finans/cari-virman`) yeni form alanları
   okunur.
4. `CashService`'e yeni `ListTransfersAsync()` metodu: `AccountLedgerEntry WHERE
   SourceType='CariVirman'` çift-bacak `SourceId`'ye göre gruplanır (Kaynak Cari=Credit bacağı,
   Hedef Cari=Debit bacağı) → liste.
5. `CariVirman.razor`'a ikinci bir tablo (geçmiş listesi) eklenir.

## Dokunulacak dosyalar
- `src/RentACar.Web/Components/Pages/Finance/CariVirman.razor`
- `src/RentACar.Application/Finance/CashService.cs` (`TransferBetweenCariAsync`, yeni
  `ListTransfersAsync`)
- `src/RentACar.Web/Finance/FinanceEndpoints.cs` (`/finans/cari-virman`)

## Migration
Muhtemelen yok — Tarih/Vade/Makbuz_No `AccountLedgerEntry.Description`'a serbest-metin olarak
eklenebilir (basit, migration'sız) VEYA ayrı kolonlar istenirse `AccountLedgerEntry`'ye 3 nullable
kolon eklenir (additive, RLS bloğu ELLE EKLENMEZ — mevcut tabloya kolon). Bu fazda önerilen: basit
yol (Description'a yapılandırılmış metin), migration YOK — kolon ekleme gerekirse küçük bir takip
adımı olur.

## Test
- `CariVirmanTests.cs`'e ek senaryo: Tarih/Vade/Makbuz_No dolu bir virman oluşturulur → round-trip
  (bağımsız oracle: elle girilen değerlerle karşılaştır); mevcut dengeli-çift-kayıt testleri
  (Σ Borç=Σ Alacak) REGRESYONSUZ yeşil kalır.
- `ListTransfersAsync`: elle 3 cari virman (farklı cari çiftleri) oluşturulur → 3 satır dönüyor mu,
  her satırda Kaynak/Hedef Cari doğru mu (bağımsız oracle, koddan türetilmez).

## Exit
- [ ] Tarih/Vade/Makbuz_No/İşlem Şube formda + kaydediliyor
- [ ] Cari virman geçmiş listesi çalışıyor
- [ ] Mevcut dengeli-çift-kayıt davranışı regresyonsuz
- [ ] Tam suite yeşil

## Notlar
Yok.
