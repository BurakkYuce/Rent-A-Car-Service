# FAZ-31 — XML Fiyat Aktar: Canlı Izgara + Toplu Silme

| | |
|---|---|
| **Desen** | D3 (canlı ızgara görüntüleme) + D5-bitişik (toplu silme) |
| **Efor** | 1 gün (yapısal); toplu-silme güvenliği/onay-akışı etkileşimi **PARA — Opus'a devredilir** |
| **Bağımlılık** | yok |
| **Kapsanan canlı ekran** | `xml_fiyat_aktar.aspx` |
| **Risk** | orta-yüksek — toplu silme fiyat motorunun O AN aktif kullandığı bir tarifeyi kaybettirebilir |
| **Zorunlu** | adversarial inceleme — Critical/High/Medium bulgu kalmadan commit yok. |

## Amaç
`/tarife-aktar` sayfasına Rezervasyon Kaynağı/Şube ön-filtreli canlı fiyat ızgarası görünümü ve
"Sadece Seçili Rezervasyon Kaynağını Sil" toplu silme butonu eklenerek kullanıcı içe aktardığı
tarife matrisini aynı ekranda görüp, hatalı/yinelenen bir kaynağın satırlarını toplu temizleyebilir
hale gelir.

## Neden (kanıt)
`src/RentACar.Web/Components/Pages/Import/TarifeAktar.razor` şu an SADECE içe-aktarım (CSV/Excel →
`Beklemede` durumunda satır ekleme) yapıyor; içe aktarılan satırları GÖSTEREN bir ızgara yok — onay
`/tarife-matris` sayfasına yönlendiriliyor (satır 6-10 doğrulandı: "onay Tarife Matrisi ekranından
verilir"). `src/RentACar.Domain/Entities/RateMatrix.cs`'te `OnayDurumu` (`TarifeOnayDurumu`:
`Bekliyor`/`Onayli`, satır 63), `Kanal` (=Rez Kaynağı, satır 24), `Sube`/`SubeId` (satır 25-26)
alanları **zaten var** — yeni tablo gerekmiyor, mevcut `RateMatrixService.ListAsync` üzerinden
filtrelenebilir. `RateMatrixService.cs`'te tek-satır `DeleteAsync(id)` (satır 65) var, **toplu/kaynak-
bazlı silme yok**.

## Yapılacaklar
1. `src/RentACar.Web/Components/Pages/Import/TarifeAktar.razor` — sayfaya, içe-aktarım formunun
   ALTINA, Rez Kaynağı (`Kanal`)/Şube ön-filtreli canlı ızgara ekle: mevcut
   `RateMatrixService.ListAsync()` çağrılıp `Kanal`/`SubeId` filtresiyle grid'de gösterilir (Kod/Ad/
   Kanal/Şube/Araç Grubu/OnayDurumu/BasTar-BitTar kolonları). **Yeni tablo YOK** — mevcut
   `RateMatrix` okunuyor (D3).
2. `src/RentACar.Application/RateMatrices/RateMatrixService.cs`'e `DeleteByKanalAsync(string kanal,
   TarifeOnayDurumu? sadeceDurum, CancellationToken ct)` ekle — seçili `Kanal` (Rez Kaynağı) ile
   eşleşen VE (verilirse) `sadeceDurum` durumundaki satırları toplu siler. **Hangi durumdaki
   (Beklemede/Onaylı) satırların silinebileceği ve fiyat motorunun o an aktif tarifeyi kaybetme
   riski Opus incelemesine bırakılır** — bu fazda metod eklenir ama UI'daki buton, Opus onayına
   kadar bir güvenlik anahtarıyla (ör. `sadeceDurum` parametresi zorunlu `Bekliyor` olarak sabitlenir
   — yalnız ONAYLANMAMIŞ satırlar silinebilir, ONAYLI/AKTİF tarife bu fazda SİLİNEMEZ) korunur.
3. `TarifeAktar.razor`'a "Sadece Seçili Rezervasyon Kaynağını Sil" butonu ekle — yalnız `Bekliyor`
   durumundaki satırları siler (madde 2'deki güvenlik kısıtıyla uyumlu); silme öncesi kaç satırın
   etkileneceğini gösteren bir onay adımı (silme öncesi COUNT sorgusu + "Bu işlem N satırı silecek,
   onaylıyor musunuz?" confirm) ekle.
4. **Zorunlu adversarial inceleme**: (a) `Onayli` durumundaki bir satırın YANLIŞLIKLA silinip
   silinemediğini kanıtla (guard'ın gerçekten `Bekliyor`'a kilitli olduğunu doğrula), (b) fiyat
   motorunun (PricingService) o an bir rezervasyon/kira hesaplarken kullandığı bir tarifenin toplu
   silmeden hemen önce/sonra tutarlı kaldığını (ya da güvenli biçimde reddedildiğini) doğrula,
   (c) yetki (Admin dışında erişim kapalı mı — sayfa zaten `[Authorize(Roles = "Admin")]`), (d) RLS
   (başka tenant'ın tarife satırı silinemiyor). Critical/High/Medium bulgu varsa düzeltilmeden
   commit yok.

## Dokunulacak dosyalar
- `src/RentACar.Web/Components/Pages/Import/TarifeAktar.razor` — canlı ızgara + toplu silme butonu
- `src/RentACar.Application/RateMatrices/RateMatrixService.cs` — `DeleteByKanalAsync`

## Migration
Yok — mevcut `RateMatrix` tablosundan okuyor/siliyor, yeni tablo/kolon eklenmiyor.

## Test
- `RateMatrixTests`: `DeleteByKanalAsync` — 5 satır elle kurulur (3'ü Kanal="ACENTA" ve `Bekliyor`,
  1'i Kanal="ACENTA" ve `Onayli`, 1'i başka kanal); `DeleteByKanalAsync("ACENTA", Bekliyor)`
  çağrılınca **tam olarak 3** satırın silindiği, `Onayli` olan 1 satırın VE başka kanaldaki 1
  satırın DOKUNULMADIĞI doğrulanır (bağımsız oracle — sayı testte sabit, servis kodundan türetilmez).
- Negatif test: `DeleteByKanalAsync("ACENTA", Onayli)` çağrısı (varsayılan UI kısıtı atlanıp
  doğrudan servis çağrılırsa) — bu fazın güvenlik kararına göre ya guard'la REDDEDİLİR ya da
  açıkça izin verilip test onu doğrular; hangisi seçilirse davranış testte açıkça yazılır.
- RLS testi: `racar_app` ile başka tenant'ın `RateMatrix` satırı silinemiyor.
- Canlı ızgara filtre testi: Kanal/Şube filtresi doğru alt-kümeyi gösteriyor.

## Exit
- [ ] Canlı ızgara Kanal/Şube filtreli gösteriyor
- [ ] Toplu silme yalnız `Bekliyor` durumundaki satırları siliyor (ya da adversarial onaylı açık
      genişletme kararı test ile kanıtlı)
- [ ] Adversarial bulguları (Critical/High/Medium) düzeltildi
- [ ] Tam suite yeşil

## Notlar
Bu faz `RateMatrix` deftere POSTLAMIYOR (entity XML doc: "Saf fiyat-tanım tablosu — deftere kayıt
POSTLAMAZ") — risk parasal ledger hatası değil, **fiyat motorunun girdisini kaybetme** riski (bir
kira/rezervasyon hesaplarken aktif tarifeyi bulamayıp yanlış/varsayılan fiyata düşmesi). Bu yüzden
"Σ Borç=Σ Alacak" testi bu fazda UYGULANMAZ (ledger yazımı yok); yerine fiyat motorunun tutarlılığı
test edilir.
