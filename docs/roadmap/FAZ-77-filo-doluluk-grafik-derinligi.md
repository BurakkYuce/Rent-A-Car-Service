# FAZ-77 — Filo & Doluluk Grafik Derinliği (Şube Kırılımı + Gün-Bazlı Doluluk)

| | |
|---|---|
| **Desen** | D4 |
| **Efor** | 3 gün (`arac_genel_durumu_grafik.aspx` 1,5g + `doluluk_grafik.aspx` 1,5g) |
| **Bağımlılık** | yok |
| **Kapsanan canlı ekran** | `arac_genel_durumu_grafik.aspx`, `doluluk_grafik.aspx` |
| **Risk** | düşük — envanter/doluluk SAYIMI, para toplamı yok (bkz. Test bölümü D4-kural notu) |

## Amaç
`/raporlar/filo` (bugün tek satır tenant-geneli özet) şube kırılımlı hale gelir; `/raporlar/doluluk`
(bugün tek dönem/tek yüzde kartı) gün-kırılımlı satır tabloya + Kira/Rez Doluluk ayrımına + Şube/
Araç Grubu/Rezervasyon Kaynağı "Karşılaştır" moduna genişler.

## Neden (kanıt)
- `ReportService.GetFleetUtilizationAsync(ct)` (doğrulandı, imza parametresiz) → `FleetUtilizationDto
  (Toplam, Musait, Kirada, Serviste, Pasif, Satildi, AktifKira)` — TEK satır, şube kırılımı YOK.
  `FiloDoluluk.razor` (`/raporlar/filo`) filtresiz, `OnInitializedAsync` doğrudan bu metodu çağırır;
  4 KPI kartı + 5 satırlık sabit durum tablosu (şube bazlı DEĞİL, tenant-geneli).
- Temel sorgu `OrtakSorgular.VehicleDurumlariAsync` — `db.Vehicles.Select(v => v.Durum)` SADECE
  Durum seçiyor, Şube seçmiyor (doğrulandı) — şube kırılımı için bu sorgunun `Vehicle.SubeId`'yi de
  seçmesi gerekiyor. `Vehicle.SubeId` **VAR** (doğrulandı).
  "Baf" sayısı `Baf.Durum==Acik`'ten (`BafDurum.Acik=1`, mevcut default — doğrulandı), "Satılık"
  `Vehicle.FiloDurum==IkinciElSatis` (`FiloStatus.IkinciElSatis=5`, mevcut — doğrulandı) — ikisi de
  MEVCUT alan, yeni tablo/kolon GEREKMEZ.
- `ReportService.GetDolulukAsync(from, to, ct)` (doğrulandı) → `DolulukDto(AracSayisi, DonemGun,
  AracGun, KiraGun, DolulukYuzde)` — TEK satır/kart, tablo yok. Temel sorgu
  `GetRentalIntervalsAsync` (`db.Rentals.Where(Durum!=Iptal).Select(BasTar, Bit)`) **VehicleId
  SEÇMİYOR** — araç/şube bazlı kırılım bugün YAPILAMIYOR (sadece toplam araç sayısı × gün).

## Yapılacaklar
### A) arac_genel_durumu_grafik (1,5 gün)
1. `OrtakSorgular.VehicleDurumlariAsync` (veya doğrudan `ReportRepository`) — `Vehicle.SubeId`'yi
   de seçecek şekilde genişletilir (`Select(v => new { v.Durum, v.SubeId })`).
2. `ReportDtos.cs`'e yeni `FleetUtilizationSubeRow(string? Sube, int Filo, int Bos, int Kirada, int
   Bakimda, decimal DolulukYuzde, int Donecekler, int Cikislar, int Cikacaklar, int GidenRez, int
   Donusler, int Baf, int Satilik)` DTO.
3. `ReportService.cs`'e **YENİ** `GetFleetUtilizationBySubeAsync(ct)` eklenir — **mevcut
   `GetFleetUtilizationAsync` imzası/davranışı GERİYE UYUMLU KALIR, değiştirilmez** (Home.razor gibi
   başka tüketicileri bozmamak için). "Dönecekler/Çıkacaklar" = bugün+N gün penceresinde
   BasTar/BitTar'ı olan `Rentals` sayımı (N = konfigüre edilebilir, varsayılan 7).
4. `ReportRepository.cs`'e karşılık gelen repo metodu; `Baf` sayımı `Baf.Durum==Acik` + `SubeId`
   (Baf'ın hangi araca bağlı olduğu üzerinden şubeye atanır), `Satilik` `Vehicle.FiloDurum==
   IkinciElSatis` + `SubeId`.
5. `FiloDoluluk.razor` — şube bazlı satır tablosu (yukarıdaki 13 kolon) eklenir, MEVCUT tenant-geneli
   4 KPI kartı KALIR (üstte özet, altta şube detay tablosu).

### B) doluluk_grafik (1,5 gün)
6. `GetRentalIntervalsAsync` — `VehicleId` de SEÇİLECEK şekilde genişletilir.
7. Gün-bazlı `OverlapDays` mantığı (bugün `GetDolulukAsync` içinde muhtemelen tek-blokta) ortak
   PRIVATE HELPER'a çıkarılır (kod tekrarı yasak — hem tek-dönem hem gün-kırılımlı hesap AYNI
   helper'ı çağırır).
8. `ReportService.cs`'e **YENİ** `GetDolulukGunlukAsync(from, to, boyut?, ct)` — `boyut` = Şube/
   Araç Grubu/Rezervasyon Kaynağı (Karşılaştır modu); **mevcut `GetDolulukAsync` API'si
   KORUNUR** (geriye uyumlu). Dönüş: gün-bazlı satır listesi, her satırda Kira Doluluk/Rez Doluluk
   ayrımı (Rentals vs Reservations üzerinden ayrı hesap).
9. `DolulukRaporu.razor` — mevcut 5-KPI-kart görünümü ÜSTTE kalır, ALTTA gün-kırılımlı satır tablo +
   Şube/Araç Grubu/Rezervasyon Kaynağı "Karşılaştır" seçici (seçilince aynı tablo boyuta göre
   grup-grup gösterilir, çoklu seri).

## Dokunulacak dosyalar
- `src/RentACar.Infrastructure/Persistence/OrtakSorgular.cs`
- `src/RentACar.Application/Reporting/ReportDtos.cs`
- `src/RentACar.Application/Reporting/ReportService.cs`
- `src/RentACar.Infrastructure/Persistence/Repositories/ReportRepository.cs`
- `src/RentACar.Web/Components/Pages/Reports/FiloDoluluk.razor`
- `src/RentACar.Web/Components/Pages/Reports/DolulukRaporu.razor`

## Migration
Yok — mevcut tablolardan (`Vehicle`, `Baf`, `Rentals`, `Reservations`) okuma, yeni kolon/tablo
gerekmiyor.

## Test
- **D4-kural notu (Test bölümünde açıkça):** bu iki rapor **PARA/DEFTER TOPLAMI YAPMAZ** — envanter
  sayımı (araç adedi, gün adedi) ve yüzde hesabıdır. "P&L yalnız defterden okunur, kaynak-varlık
  tutarı asla toplanmaz" kuralı bu fazda BAĞLAYICI DEĞİLDİR çünkü toplanan hiçbir değer parasal
  değildir (kanıt: `FleetUtilizationSubeRow`/gün-kırılımlı doluluk DTO'larında `decimal` alanı
  sadece `DolulukYuzde` — bir ORAN, `Money`/ledger kaynaklı değil). Çift-sayım riski YOKTUR çünkü
  toplanan şey "araç sayısı"/"gün sayısı", iki kaynaktan aynı anda gelmiyor.
- `GetFleetUtilizationBySubeAsync` testi: elle kurulan 2 şube × 3 araç (durum karışık) → her şube
  satırının Filo/Bos/Kirada/Bakimda sayıları TESTTE sabit yazılır; `GetFleetUtilizationAsync`
  (mevcut, parametresiz) AYNI test verisiyle DEĞİŞMEDEN aynı toplam sonucu verir (regresyon).
- `GetDolulukGunlukAsync` testi: elle kurulan 3 kira (farklı gün aralıkları) → her gün için Kira
  Doluluk yüzdesi sabit hesaplanıp test'e yazılır; `OverlapDays` helper'ının tek-dönem
  (`GetDolulukAsync`) ve gün-kırılımlı çağrıda AYNI sonucu ürettiği (toplamda) çapraz-doğrulanır.
- Baf/Satılık sayım testi: `BafDurum.Acik` olmayan bir Baf sayılmıyor; `FiloDurum != IkinciElSatis`
  olan araç Satılık'a girmiyor.

## Exit
- [ ] `/raporlar/filo` şube kırılımlı tablo gösteriyor, mevcut API geriye uyumlu
- [ ] `/raporlar/doluluk` gün-kırılımlı + Karşılaştır modu çalışıyor, mevcut API geriye uyumlu
- [ ] `OverlapDays` kod tekrarı olmadan ortak helper'dan çağrılıyor
- [ ] Tam suite yeşil

## Notlar
"Dönecekler/Çıkacaklar" penceresi (N gün) sabit varsayılan ile başlar; kullanıcı ileride bunu
ayarlanabilir isterse `TenantSettings`'e taşınabilir (bu fazda sabit kod-içi değer yeterli).
