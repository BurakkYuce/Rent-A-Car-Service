# FAZ-10 — Araç Kayıt: Alan Zenginleştirmesi

| | |
|---|---|
| **Desen** | D1 — basit alan ekleme |
| **Efor** | 1,5 gün |
| **Bağımlılık** | yok |
| **Kapsanan canlı ekran** | `arac_kayit.aspx` |
| **Risk** | düşük — additive nullable kolonlar, mevcut tabloya migration; finansal kur alanları hiçbir hesaba bağlanmıyor (salt-bilgi) |

## Amaç
Operatör `/vehicles/{id}` (VehicleEdit) formundan canlıda var olan 17 alanı (TSRB/entegrasyon
kodları, GPS takip, araç sahipliği kırılımı, kapanış/kredi bilgisi, finansal kur alanları,
pasif sebep, konum) tek ekrandan girebilir hale gelir; VehicleDetail'de son 3 KM kaydını
salt-okunur görür.

## Neden (kanıt)
`docs/parite/01-arac-filo.md` — `arac_kayit.aspx`: K2=%30 (46/152), K3=%11 (4/35). Canlı
fazlası (alan bazlı, doğrulandı): `TSRB_Marka_Kodu/Tip_Kodu`, `Alt_Grup_Adi`,
`Entegrasyon_Kodu`, `Teyp_Kodu`, `Takip_Marka/No` (GPS — `HgsNo/OgsNo`'dan ayrı),
`Sahip_Grup`, `Arac_Sahibi_No/2`, `Kredi_Firma`, `Kapatma_Tarih`, `Hizmet_Assist_Firma`,
`Cikmasi_Planan_Tarih`, `Arac_Satis_KM`, genel `Aciklama`, `Pasif_Sebep`, `Konum`; finansal
döviz alanları `Alim_Bedeli_Kur`, `Alis_Euro`, `Arac_2_Fiyat_Kur`, `SimdiKur`,
`AylikMaliyetDoviz`. Periyodik bakım/km-tespit/lastik geçmişi (`Son_Per_No/Km/Tar`,
`Km_Tespit_No/Km/Tar`, Son Lastik No/Km/Tar) `VehicleKmLog` (Kaynak=Manuel/Servis) tarafından
zaten tutuluyor — yeni tablo gerekmiyor, sadece salt-okunur özet eksik.

## Yapılacaklar
1. `src/RentACar.Domain/Entities/Vehicle.cs`'e additive nullable alanlar ekle:
   `TsrbMarkaKodu`, `TsrbTipKodu`, `AltGrupAdi`, `EntegrasyonKodu`, `TeypKodu`, `TakipMarka`,
   `TakipNo`, `SahipGrup`, `AracSahibiNo`, `AracSahibi2`, `KrediFirma`, `KapatmaTarih`,
   `HizmetAssistFirma`, `CikmasiPlananTarih`, `AracSatisKm`, `Aciklama`, `PasifSebep`, `Konum`.
2. Aynı entity'ye finansal kur alanlarını ekle: `AlimBedeliKur`, `AlisEuro`, `Arac2FiyatKur`,
   `SimdiKur`, `AylikMaliyetDoviz` (hepsi `decimal?`). **PARA — Opus not:** bu 5 alan bu fazda
   SALT-BİLGİ kolonudur; Karne/Karlılık P&L hesaplarına (bugün TL sabit, CLAUDE.md §4 "P&L
   yalnız defterden") bağlanıp bağlanmayacağı Opus kararı — bu fazda HİÇBİR rapor sorgusuna
   bağlanmaz.
3. `src/RentACar.Infrastructure/Persistence/Configurations/VehicleConfigs.cs` (`VehicleConfig`)
   — yeni alanların kolon tipi/maxlength eşlemesi (`numeric(19,4)` kur alanları için,
   `HasMaxLength` metin alanları için).
4. Migration: `dotnet ef migrations add AddVehicleKayitAlanlari2 --project
   src/RentACar.Infrastructure --startup-project src/RentACar.Infrastructure`.
5. `src/RentACar.Application/Vehicles/VehicleInput.cs` — 22 alanın karşılığını ekle.
6. `src/RentACar.Application/Vehicles/VehicleService.cs` — `CreateAsync`/`UpdateAsync` map'ini
   genişlet.
7. `src/RentACar.Web/Components/Pages/Vehicles/VehicleEdit.razor` — mevcut "her alanı tek tek
   expose et" deseni tekrarlanarak 22 input eklenir (yeni ComboBox/select gerekmiyor —
   `PasifSebep` küçük bir sabit-liste select olabilir, diğerleri düz metin/sayı/tarih).
8. `src/RentACar.Web/Components/Pages/Vehicles/VehicleEdit.razor` (veya `VehicleDetail.razor`)
   — "Son 3 KM Kaydı" salt-okunur mini-liste: mevcut `VehicleKmLog` sorgusuna `Take(3)
   OrderByDescending(Tarih)` ile son 3 kayıt.
9. `src/RentACar.Web/Vehicles/VehicleEndpoints.cs` — yeni alanları form'dan oku; opsiyonel
   `decimal?`/`int?`/`DateTimeOffset?` alanlar boş string ("") gelirse `[FromForm]` 400 verir →
   `string?` parametre + `FormParse.Dec/Int/Date` ile çevir (CLAUDE.md §5 tuzağı).

## Dokunulacak dosyalar
- `src/RentACar.Domain/Entities/Vehicle.cs` — 22 yeni alan
- `src/RentACar.Infrastructure/Persistence/Configurations/VehicleConfigs.cs`
- (yeni) migration dosyası (`AddVehicleKayitAlanlari2`)
- `src/RentACar.Application/Vehicles/VehicleInput.cs`
- `src/RentACar.Application/Vehicles/VehicleService.cs`
- `src/RentACar.Web/Components/Pages/Vehicles/VehicleEdit.razor`
- `src/RentACar.Web/Vehicles/VehicleEndpoints.cs`

## Migration
Var — mevcut tenant-owned `Vehicles` tablosuna 22 nullable kolon. RLS zaten aktif (mevcut
tabloda `tenant_isolation` policy var) → **yeni RLS bloğu gerekmez**, sadece kolon ekleniyor.

## Test
- `VehicleServiceTests` (mevcut dosyaya ekleme) — 22 alanın round-trip testi: bağımsız oracle
  ("şu 22 alana şu sabit değerleri yaz, geri okuyunca AYNI değerleri bekle" — değerler elle
  seçilir, koddan türetilmez).
- Son 3 KM kaydı testi: 5 `VehicleKmLog` satırı elle seed edilir (tarih sırasıyla), mini-liste
  sorgusu ÇAĞRILDIĞINDA en yeni 3'ünün ID'lerinin sabit-listede olduğu doğrulanır.
- Mevcut `VehicleTests` tenant izolasyonu (racar_app) zaten kapsıyor — yeni alanlar bu testin
  regresyonunu bozmamalı.

## Exit
- [ ] 22 yeni alan `VehicleEdit.razor`'da görünür, kaydedilip okunuyor
- [ ] Migration racar_app grant'lı, mevcut RLS policy'yi bozmuyor
- [ ] VehicleDetail/VehicleEdit'te son 3 KM kaydı görünüyor
- [ ] Finansal kur alanları hiçbir rapor/hesaplamaya bağlı DEĞİL (regresyon testiyle doğrulanır)
- [ ] Tam suite yeşil

## Notlar
Sigorta/Trafik/Kasko/Muayene bloğunu bu forma SEKME olarak gömme fikri değerlendirildi ve
**YAPILMAZ** olarak kapatıldı (gerekçenin tamamı `docs/roadmap/_toplama-01-arac-filo.md`'de) —
bu veri zaten `/regulasyon`'da birincil kaynakta yaşıyor, ikinci bir formda tekrar yazılabilir
hale getirmek CLAUDE.md §5 katmanlı mimari ilkesini bozar. Yerine VehicleDetail'e sigorta/muayene
bitiş tarihi özet-rozeti + `/regulasyon` linki düşünülebilir ama bu fazın kapsamı DIŞINDA (küçük,
ayrı, önceliksiz bir ek).
