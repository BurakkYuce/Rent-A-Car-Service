# FAZ-68 — Tahsilat Raporu: Sözleşme-Satırı Mutabakat Modu

| | |
|---|---|
| **Desen** | D4 |
| **Efor** | 1,5 gün ("Mail At" hariç) |
| **Bağımlılık** | yok |
| **Kapsanan canlı ekran** | `tahsilat_raporu.aspx` ("Mail At" aksiyonu HARİÇ — bkz. Notlar) |
| **Risk** | düşük — salt-okunur rapor genişletmesi, para yazmıyor |

## Amaç
Kullanıcı tahsilat raporunda dönem-toplamının yanında, sözleşme-SATIRI düzeyinde mutabakat
(Sözleşme No, Plaka, Müşteri, Matrah, Damga Vergisi, Sözleşme/Müşteri/TPC Toplam-Tahsilat-Bakiye
üçlüleri, Faturalanan/Fark) görebilir hale gelir.

## Neden (kanıt)
`ReportService.GetTahsilatFaturaAsync` (`src/RentACar.Application/Reporting/ReportService.cs`
L663-665 → `ReportRepository.GetTahsilatFaturaAsync`, L91-117) yalnız dönem-toplamı (Fatura Adet/
Toplam, Tahsilat Adet/Toplam, Fark) döndürüyor — canlı sözleşme-SATIRI düzeyinde mutabakat yok
(grep doğrulandı: metod içinde `Rental`/`GroupBy(RentalId)` yok, düz toplam).

## Yapılacaklar
1. `ReportRepository.GetTahsilatFaturaAsync`'e (~L91) yeni bir satır-modu eklenir: `Rental` ×
   `Invoice` (`RentalId`/`KaynakKiraId`) × `CashTransaction` (`RentalId`) JOIN'iyle sözleşme başına
   satır. Kolon: Sözleşme No, Plaka, Müşteri, Matrah, Damga Vergisi, Sözleşme/Müşteri/TPC Toplam-
   Tahsilat-Bakiye üçlüleri, Faturalanan/Fark.
2. `ReportService.cs`'e satır-modu parametresi eklenir (mevcut dönem-toplamı modu SİLİNMEZ, yeni bir
   mod/sekme olur).
3. `Components/Pages/Reports/TahsilatFatura.razor`'a filtre (Sözleşme No, Bakiye durumu, Hizmet) +
   satır-modu görünümü eklenir.

## Dokunulacak dosyalar
- `src/RentACar.Infrastructure/Persistence/Repositories/ReportRepository.cs`
  (`GetTahsilatFaturaAsync`, ~L91 — satır modu eklenir)
- `src/RentACar.Application/Reporting/ReportService.cs`
- `src/RentACar.Web/Components/Pages/Reports/TahsilatFatura.razor`

## Migration
Yok — mevcut `Rental`/`Invoice`/`CashTransaction` tabloları okunuyor.

## Test
- Mevcut tahsilat raporu testine ek senaryo: elle 3 kira (1 tam-faturalı-tam-tahsilatlı, 1 kısmi-
  tahsilatlı, 1 faturasız) oluşturulur; satır modu her kiranın Bakiye/Fark değerini doğru hesaplıyor
  mu (bağımsız oracle: elle hesaplanan Bakiye/Fark, koddan türetilmez); dönem-toplamı modu
  REGRESYONSUZ (mevcut test tam suite yeşil).

## Exit
- [ ] Satır-modu (sözleşme başına mutabakat) çalışıyor
- [ ] Dönem-toplamı modu regresyonsuz duruyor
- [ ] Filtre (Sözleşme No, Bakiye durumu, Hizmet) çalışıyor
- [ ] Tam suite yeşil

## Notlar
"Mail At" aksiyonu BU PR'A DAHİL DEĞİL (e-posta gönderim altyapısı gerektirir) — ayrı küçük bir ek
olarak değerlendirilmeli, efor bu fazın toplamına dahil edilmedi.
