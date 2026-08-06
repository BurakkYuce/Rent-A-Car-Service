# FAZ-65 — Hesap Ekstresi: Filtre/Görünüm-Modu Paneli

| | |
|---|---|
| **Desen** | D3 |
| **Efor** | 1 gün |
| **Bağımlılık** | yok |
| **Kapsanan canlı ekran** | `hesap_extresi.aspx` |
| **Risk** | düşük — mevcut çekirdek işlev (tarih/borç/alacak/bakiye+export+ters kayıt) ÇALIŞIYOR,
  bu faz yalnız filtre paneli ekliyor |

## Amaç
Kullanıcı `/cariler/{Id}/ekstre` sayfasında Tarih aralığı, Döviz filtresi ve "Toplu/Taksitli"
görünüm modu, Sözleşme durumu filtresiyle daha derin arama yapabilir hale gelir.

## Neden (kanıt)
`CustomerStatement.razor` çekirdek işlev (tarih/borç/alacak/bakiye + export + ters kayıt) ÇALIŞIYOR
— eksik olan filtre/görünüm-modu paneli: Tarih aralığı filtresi, Döviz filtresi, "Toplu/Taksitli"
görünüm modu, Sözleşme durumu filtresi (grep doğrulandı, mevcut sayfada bu 4 filtre yok).

## Yapılacaklar
1. `CustomerStatement.razor`'a Tarih aralığı filtresi eklenir.
2. Döviz filtresi eklenir (mevcut çok-dövizli `AccountLedgerEntry.Amount.Currency` üzerinden).
3. "Toplu/Taksitli" görünüm modu eklenir (mevcut satır listesini gruplama/ayrıştırma UI'sı, veri
   kaynağı DEĞİŞMEZ).
4. Sözleşme durumu filtresi eklenir (`Rental.Durum` üzerinden, ekstredeki `SourceType='Tahsilat'`
   satırlarının bağlı olduğu kira).

## Dokunulacak dosyalar
- `src/RentACar.Web/Components/Pages/Customers/CustomerStatement.razor`

## Migration
Yok — mevcut `AccountLedgerEntry`/`Rental` tabloları okunuyor.

## Test
- Mevcut ekstre testine ek senaryo: elle 4 hareket (2 farklı döviz, 2 farklı tarih) oluşturulur;
  Tarih/Döviz filtre kombinasyonları beklenen alt-kümeyi döndürüyor mu (bağımsız oracle: sabit
  sayı/toplam).
- Mevcut export + ters kayıt davranışı REGRESYONSUZ (tam suite yeşil).

## Exit
- [ ] Tarih aralığı/Döviz/Sözleşme durumu filtresi çalışıyor
- [ ] "Toplu/Taksitli" görünüm modu çalışıyor
- [ ] Mevcut export/ters kayıt davranışı regresyonsuz
- [ ] Tam suite yeşil

## Notlar
Musteri_No/Ad/Soyad arama kutusu BİLİNÇLİ EKLENMEZ — bizde zaten URL parametresiyle önceden seçilmiş
cari (bu bilinçli fark, canlı fazlası olarak not edilir, gerileme SAYILMAZ).
