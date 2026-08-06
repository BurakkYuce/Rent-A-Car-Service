# Ekleme Planı — 01-arac-filo (25 ekran)

Kaynaklar: `plan/eksikler.json` (modul=01-arac-filo) + `docs/parite/01-arac-filo.md` (kanıt/alan
listeleri) + `docs/parite/10-ekleme-desenleri.md` (D1–D9) + repo kod okuması (`src/RentACar.*`,
bkz. her satırdaki `dokunulacak`). Kural 5 uygulandı: bu modülde **14 ekran PARA durumlu**
(kredi/sipariş/satış/mtv/muayene/servis/sigorta/gelir-gider) — bunlarda yapısal eylem yazıldı,
tutar/formül/model kararı `PARA — Opus` diye işaretlendi; desen ve efor yine de verildi (D8 değil,
bloke değil — sadece tutar doğruluğu incelenmedi).

## Desen dağılımı (25 ekran)

| Desen | Adet | Ekranlar |
|---|---|---|
| D1 — basit alan ekleme | 3 | arac_kayit, arac_satis, baf_islemleri |
| D2 — kural taşıyan master/durum | 6 | arac_kredi, arac_mtv_islemleri, arac_muayene_islemleri, arac_siparis, servis_rezervasyon, servis_tanim_tablosu |
| D3 — liste/arama (yeni tablo yok) | 9 | arac_guncel_durum, arac_listesi, arac_rac_takvim, arac_kredi_listesi, arac_satis_ara, arac_siparis_detay_listesi, arac_siparis_listesi, baf_ara, bos_arac_listesi |
| D4 — rapor (agrega) | 3 | arac_durum_takip, arac_gelir_gider_tablosu, arac_gunluk_durum |
| D5 — para hareketi/defter etkili derinlik | 2 | arac_servis_islemleri, arac_sigorta_islemleri |
| D7 — yeni dikey | 1 | arac_plan_yonetim |
| Belirsiz (PARA + ekran işlevi doğrulanamadı) | 1 | arac_satis_bedeli |

`YAPILMAZ` tam ekran ölçeğinde yok; **arac_kayit** içinde tek bir alt-karar (sigorta/muayene/servis
geçmişini sekme olarak gömme) `YAPILMAZ` işaretlendi — gerekçe kendi bloğunda.

## Toplam efor

- **Yapısal toplam (25 ekranın 24'ü, `arac_satis_bedeli` hariç): ~31 gün** (11 gün PARA-olmayan
  bölüm hariç ~14,5 gün + PARA yapısal bölüm ~16,5 gün).
  - PARA-olmayan 11 ekran: **~14,5 gün**.
  - PARA 13 ekran (yapısal kısım, tutar kararı hariç): **~16,5 gün**.
  - `arac_satis_bedeli` (14. PARA ekran): efor verilmedi — ekranın gerçek işlevi ("RentTo" kavramı)
    doğrulanmadan yapısal eylem de netleşmiyor; önce doğrulama adımı gerekir (bkz. kendi bloğu).
- **Gruplanabilir PR'lar** (rule 7) toplamı ~9 ekranı ~5 PR'a indiriyor — bkz. her bloktaki
  `**gruplama:**` etiketleri.
- Bu efor **yalnız yapısal iş**; PARA ekranlarda tutar/formül/muhasebe-modeli tasarımı ayrı bir
  Opus turu gerektirir ve bu sayıya dahil değildir.

---

### arac_durum_takip.aspx
- **desen:** D4
- **eylem:** `ReportRepository.GetAracDurumTakipRowsAsync` şu an yalnız GÜN bazında (gün→Dolu/Bakım/Boş
  toplamı) hesaplıyor; canlının ARAÇ bazlı (Plaka/SIPP) grain'ini üretmek için aynı `kiralar`/`servisler`
  sorgusuna `Baflar` eklenir (bugün Baf hiç sayılmıyor — Dolu/Bakım'a hiç girmiyor, sessizce kayboluyor)
  ve sonuç VehicleId'ye göre gruplanıp seçilen aralıktaki toplam Dolu-Gün/Bakım-Gün/Baf-Gün/Boş-Gün
  hesaplanır. `AracDurumTakip.razor`'a "Gün" ↔ "Araç" görünüm anahtarı eklenir (arac_listesi'ndeki
  `gorunum=grup` desenindeki gibi). Filtreler: Ofis/AracSahibi/Grup (SIPP↔Grup toggle)/Plaka eklenir.
- **dokunulacak:** `src/RentACar.Infrastructure/Persistence/Repositories/ReportRepository.cs` (yeni
  `GetAracDurumTakipAracBazliRowsAsync`), `src/RentACar.Application/Reporting/ReportDtos.cs` (yeni
  `AracDurumTakipAracRow`), `src/RentACar.Application/Reporting/IReportRepository.cs`,
  `src/RentACar.Application/Reporting/ReportService.cs`,
  `src/RentACar.Web/Components/Pages/Reports/AracDurumTakip.razor`
- **efor:** 1 gün
- **bağımlılık:** yok
- **not — PARA — Opus:** "Potansiyel" (olası kira geliri, boş-gün × günlük ücret) kolonu canlıda var;
  hangi günlük ücretin (grup ortalaması mı, o aracın son kirası mı) kullanılacağı bir formül kararı —
  yapısal görünüm anahtarı bu kolon olmadan da teslim edilebilir, kolonun kendisi PARA — Opus.

### arac_gelir_gider_tablosu.aspx
- **desen:** D4 — **PARA — OPUS'A DEVİR**
- **eylem (yapısal):** Canlının 5-modlu (Şube/Grup/Araç/Detay/Hizmet) export'u bizde 3 ayrı rapora
  bölünmüş (`Karlilik.razor` boyut=sube/grup/araç, `FiloAnaliz.razor`, `EkHizmetRaporu.razor`).
  Yapısal seçenek A: üçünü TEK sayfada sekmeli export moduna indir (mevcut `KarlilikSatirDto` +
  `EkHizmetTanim` pivotunu ARAÇ eksenine çevirerek birleştir). Yapısal seçenek B: `EkHizmetRaporu`'na
  araç-bazlı pivot modu eklenir (bugün hizmet-adı bazlı pivotluyor). İkisi de kod değişikliği
  gerektirir; hangisi canlının GERÇEK dengi sayılacağı — ve CDW/LCF/Genç Sürücü/Bebek Koltuğu/Wifi/Kış
  Lastiği/Paket1-6/SCDW gibi ~25 ek-hizmet kolonunun tek satırda araç bazında nasıl gösterileceği —
  muhasebe/para uzmanlığı gerektiriyor.
- **dokunulacak:** `src/RentACar.Web/Components/Pages/Reports/Karlilik.razor`,
  `src/RentACar.Web/Components/Pages/Reports/FiloAnaliz.razor`,
  `src/RentACar.Web/Components/Pages/Reports/EkHizmetRaporu.razor`,
  `src/RentACar.Application/Reporting/ReportService.cs`, `ReportDtos.cs`
- **efor:** 1,5 gün (seçenek netleşince)
- **bağımlılık:** yok
- **not — PARA — Opus:** hangi rapor(lar) canlının gerçek dengi sayılır + Ort. Kira/Hizmet Oran/Çalış.
  Araç KPI'larının formülü.

### arac_gunluk_durum.aspx
- **desen:** D4 — **PARA — OPUS'A DEVİR**
- **eylem (yapısal):** Canlı ekran ARAÇ × GÜN bazında Günlük Kira/Günlük Hizmet/Günlük Toplam
  gösteriyor (o günkü aktif kiraların günlük gelir kesiti, tarih aralığı YOK — anlık/o güne özel).
  Bu grain hiçbir bizdeki rapor'da yok (`Karlilik.razor` dönem TOPLAMI verir, günlük kesit değil).
  Yapısal eylem: yeni bir rapor sayfası — `ReportRepository`'ye o gün aktif olan (`BasTar<=gün<=Bit`)
  her kiranın `GenelToplam`'ını kira süresine bölerek (BasTar..Bit arası gün sayısı) günlük payını
  hesaplayan bir sorgu eklenir, VehicleId bazında gruplanır. Filtreler: Plaka, Araç Grubu.
- **dokunulacak:** yeni `src/RentACar.Application/Reporting/ReportDtos.cs` (yeni
  `AracGunlukDurumRow`), `IReportRepository.cs`, `ReportService.cs`,
  `src/RentACar.Infrastructure/Persistence/Repositories/ReportRepository.cs`, yeni
  `src/RentACar.Web/Components/Pages/Reports/AracGunlukDurum.razor`
- **efor:** 1 gün (yeni sayfa ama basit tek-sorgu — D4 taban 1 gün)
- **bağımlılık:** arac_durum_takip'in araç-bazlı görünümüyle aynı temel veriyi (araç×gün) paylaşır;
  aynı PR'da BİRLEŞTİRİLEBİLİR ama grain'i (gün-doluluk vs günlük-gelir-TUTARI) farklı olduğundan
  ayrı satır olarak bırakıldı — istenirse `**gruplama:** arac_durum_takip ile aynı PR` yapılabilir.
- **not — PARA — Opus:** günlük payın nasıl bölüştürüleceği (kira süresine düz bölüm mü, ek hizmet
  günlükleri dahil mi, kısmi gün nasıl sayılır) tamamen formül kararı.

### arac_guncel_durum.aspx
- **desen:** D3
- **eylem:** `FleetStatus.razor` (`/arac-durum`) zaten aktif-kira özetini (Müşteri/Bitiş/Bakiye)
  gösteriyor — canlının "aksiyon konsolu" farkı: (1) satırda **Kirala/Servis/Baf** hızlı-aksiyon
  mini-formları (ana tablo formunun DIŞINDA, `form=` attribute'üyle — kira mega-form deseni; mevcut
  `/kiralar/yeni?vehicleId=`, `/servisler/create`, `/baf/create` uçlarına post eder, yeni endpoint
  YOK), (2) ek özet kolonları: Söz.No/Ad Soyad/Cep Tel/Rez Müşteri/Kira Kalan/Dosya No (mevcut
  `FleetStatusRow`'a `Rentals`+`Reservations` join'inden eklenir), (3) filtreler: Ofis (VehicleFilter
  zaten `Sube` taşıyor, sadece UI param eksik), Pasif_Sebep (yeni alan, bkz. arac_kayit),
  KarLastigi/WebRezKapat/OfisRezKapat/HgsNo (Vehicle'da ZATEN var, sadece filtreye bağlanmamış),
  GPS (yeni alan, bkz. arac_kayit).
- **dokunulacak:** `src/RentACar.Application/Fleet/FleetStatusRow.cs`,
  `src/RentACar.Application/Fleet/FleetStatusFilter.cs`,
  `src/RentACar.Application/Fleet/FleetStatusService.cs`,
  `src/RentACar.Infrastructure/Persistence/Repositories/FleetStatusRepository.cs`,
  `src/RentACar.Web/Components/Pages/Fleet/FleetStatus.razor`
- **efor:** 2 gün (D3 taban ücreti 0,5 gün — sapma nedeni: 3 ayrı aksiyon-akışı wiring'i + 2 yeni join)
- **bağımlılık:** GPS/Pasif_Sebep filtresi arac_kayit'in yeni alanlarına bağlı (o PR önce/birlikte gitmeli)

### arac_kayit.aspx
- **desen:** D1
- **eylem:** `Vehicle.cs`'e additive nullable kolonlar: `TsrbMarkaKodu`, `TsrbTipKodu`, `AltGrupAdi`,
  `EntegrasyonKodu`, `TeypKodu`, `TakipMarka`/`TakipNo` (GPS cihazı — HgsNo/OgsNo'dan ayrı),
  `SahipGrup`, `AracSahibiNo`, `AracSahibi2`, `KrediFirma`, `KapatmaTarih`, `HizmetAssistFirma`,
  `CikmasiPlananTarih`, `AracSatisKm`, `Aciklama` (genel — bugün VehicleEdit'te YOK), `PasifSebep`,
  `Konum`. `VehicleEdit.razor`'a karşılık gelen input'lar eklenir (form zaten Vehicle'ın ~40 alanının
  hepsini tek tek expose ediyor — bu desen tekrar edilir, yeni ComboBox/select gerekmiyor çoğunda).
  Periyodik bakım/km-tespit/lastik geçmişi (Son_Per_No/Km/Tar, Km_Tespit_No/Km/Tar, Son Lastik No/Km/Tar):
  `VehicleKmLog` (Kaynak=Manuel/Servis) ZATEN bu geçmişi tutuyor — yeni tablo YOK, VehicleEdit'e
  "Son 3 KM kaydı" salt-okunur mini-liste eklenir (D3 parçası).
- **YAPILMAZ (alt-karar):** Sigorta/Trafik/Kasko/Muayene bloğunu VehicleEdit'e SEKME olarak gömmek —
  gerekçe: bu veri zaten `/regulasyon`'da birincil kaynakta yaşıyor (InsurancePolicy/MtvRecord/
  InspectionRecord, kendi ödeme akışlarıyla); aynı veriyi ikinci bir formda tekrar yazılabilir hale
  getirmek CLAUDE.md §5'in "katmanlı mimari" ilkesini bozar ve iki kopyayı senkron tutma riski
  yaratır. Yerine: VehicleDetail'e (zaten var) sigorta/muayene bitiş tarihi özet-rozeti + `/regulasyon`
  linki (D3, ayrı küçük ek, bu PR'ın parçası değil — zaten VehicleDetail'de kısmen var, doğrulanmalı).
- **dokunulacak:** `src/RentACar.Domain/Entities/Vehicle.cs`,
  `src/RentACar.Infrastructure/Persistence/Configurations/VehicleConfigs.cs`, yeni migration
  (`Add-Migration AddVehicleKayitAlanlari2`), `src/RentACar.Application/Vehicles/VehicleInput.cs`,
  `src/RentACar.Application/Vehicles/VehicleService.cs`,
  `src/RentACar.Web/Components/Pages/Vehicles/VehicleEdit.razor`,
  `src/RentACar.Web/Vehicles/VehicleEndpoints.cs`
- **efor:** 1,5 gün (D1 taban ücreti ~0,5 gün/5 ekran — burada TEK ekranda 17 yeni alan olduğu için
  yükseltildi)
- **bağımlılık:** yok
- **not — PARA — Opus:** finansal döviz alanları (`Alim_Bedeli_Kur`, `Alis_Euro`, `Arac_2_Fiyat_Kur`,
  `SimdiKur`, `AylikMaliyetDoviz`) additive kolon olarak D1 yapısal eylemdir, ama bu değerlerin
  Karne/Karlılık P&L hesaplarına (bugün TL sabit tutuluyor, CLAUDE.md §4 "P&L yalnız defterden")
  karışıp karışmayacağı — yoksa salt bilgi alanı mı kalacağı — PARA — Opus.

### arac_kredi.aspx
- **desen:** D2 — **PARA — OPUS'A DEVİR**
- **eylem (yapısal):** `AracKrediInput.VehicleId` ZATEN var ama `AracKrediList.razor`'ın "Yeni Kredi"
  formunda araç seçici input hiç YOK (klasik wire-in eksikliği — model/servis destekliyor, form
  sormuyor) → forma eklenir (ücretsiz). `AracKredi.BankaAdi` (serbest metin) yanına `CariId` (Guid?,
  additive — eski alan kalır) eklenir + Cari arama/seç. `AracKrediService.Hesapla`'ya
  `ToplamFaiz`/`SonVadeGunu`/`SonTaksitTutari`/`BuAyToplamTaksit`/`ToplamKrediBorcu` özet alanları
  eklenir (mevcut `AracKrediOzet` record genişletilir). Toplu "Taksitleri İptal Et" aksiyonu eklenir.
- **dokunulacak:** `src/RentACar.Application/AracKredileri/AracKrediModels.cs`,
  `src/RentACar.Application/AracKredileri/AracKrediService.cs`,
  `src/RentACar.Domain/Entities/AracKredi.cs` (CariId), yeni migration,
  `src/RentACar.Web/Components/Pages/AracKredileri/AracKrediList.razor`,
  `src/RentACar.Web/AracKredileri/AracKrediEndpoints.cs`
- **efor:** 1,5 gün
- **bağımlılık:** yok
- **gruplama:** arac_kredi_listesi ile AYNI PR ("Araç Kredisi Zenginleştirme")
- **not — PARA — Opus:** cari-bağlama modelinin (kredi VEREN cari mi, ilişkili cari mi) ve
  faiz/vade hesabının (`Hesapla` metodu bugün basit yıllık faiz/eşit taksit varsayıyor) canlıyla
  uyuşup uyuşmadığı.

### arac_kredi_listesi.aspx
- **desen:** D3 — **PARA — OPUS'A DEVİR**
- **eylem (yapısal):** `AracKrediList.razor`'da hiç filtre formu yok (`OnInitializedAsync` düz
  `ListAsync()`) → yeni `AracKrediFilter.cs` (Cari/Plaka/Tarih aralığı) + `AracKrediService.SearchAsync`
  + repository'ye filtre + Dosya No kolonu eklenir. Bu satırda tutar kararı YOK (salt arama/filtre);
  PARA işareti aynı route'u paylaştığı için (kural gereği) taşınıyor.
- **dokunulacak:** yeni `src/RentACar.Application/AracKredileri/AracKrediFilter.cs`,
  `AracKrediService.cs`, `IAracKrediRepository.cs`,
  `src/RentACar.Infrastructure/Persistence/Repositories/AracKrediRepository.cs`,
  `src/RentACar.Web/Components/Pages/AracKredileri/AracKrediList.razor`
- **efor:** 0,5 gün
- **bağımlılık:** arac_kredi ile aynı PR
- **gruplama:** arac_kredi ile AYNI PR ("Araç Kredisi Zenginleştirme")

### arac_listesi.aspx
- **desen:** D3
- **eylem:** Büyük kısım veri ZATEN Vehicle entity'de var, `VehicleList.razor`'a görünmüyor — filtre
  ekleri: `Grup_Turu` toggle (Grup↔SIPP — `Vehicle.Sipp` zaten var), `Ofis` filtresi
  (`VehicleFilter.Sube` zaten var, sadece query-param+select eksik), `ArTarih_Listesi` (Filo Giriş/Çıkış
  tarihi ZATEN var, aralık filtresi eklenir), `Arac_Sahibi` toggle (Hepsi/Bizim/Dış — `AracSahibi` text
  eşleşmesiyle basit). Kolon ekleri (çoğu ZATEN Vehicle'da var, sadece tabloya eklenir): Alış Bedeli
  (`AlimBedeli`), Filo Gir/Çık Tarihi, İlk Tescil Tarihi (`TescilTarihi`), Alınan Firma
  (`AlimYapilanFirma`), Yedek Anahtar, HGS/OGS, Ruhsat No, Lastik Bilgisi (`LastikDurumu`), 2.El Değeri
  (`IkinciElDeger`), Z-İzni, Statü (`FiloDurum`), Kar Lastiği. Gerçekten YENİ alan gerektirenler
  (Lokasyon/Konum, Takip No, Teyp Kodu, Açıklama, Çık.Plan.Tarih, Araç Belge No) arac_kayit PR'ından
  gelir. Join gerektirenler (Sözleşme No, Servis/Baf/Satış bayrak kolonu, Kasko Bedeli, Kredi
  Kuruluşu/Son Tarih) `VehicleFilter`/repository'de basit `EXISTS` alt-sorgularıyla eklenir.
- **dokunulacak:** `src/RentACar.Application/Vehicles/VehicleFilter.cs`,
  `src/RentACar.Application/Vehicles/VehicleService.cs`,
  `src/RentACar.Infrastructure/Persistence/Repositories/VehicleRepository.cs`,
  `src/RentACar.Web/Components/Pages/Vehicles/VehicleList.razor`
- **efor:** 1 gün
- **bağımlılık:** Lokasyon/TakipNo/TeypKodu/Açıklama kolonları arac_kayit'in yeni alanlarına bağlı

### arac_mtv_islemleri.aspx
- **desen:** D2 — **PARA — OPUS'A DEVİR**
- **eylem (yapısal):** `MtvRecord`'a `Kalan` (decimal, kısmi ödeme bakiyesi), `EvrakNo`, `IslemYapan`,
  `Aciklama`, `OdemeTarihi` (bugün "Öde" = anlık, ayrı tarih girilemiyor), `TutarDoviz`/`TutarKur`,
  `KasaKodu`/`HesapNo` (bugün sadece Kasa/Banka ikili select var) eklenir. `RegulationList.razor` MTV
  bölümüne aynı alanlar + kısmi ödeme akışı ("Öde" butonunda kısmi tutar girilebilir, `Kalan` düşer,
  `Odendi` yalnız `Kalan=0` olunca true olur).
- **dokunulacak:** `src/RentACar.Domain/Entities/MtvRecord.cs`,
  `src/RentACar.Infrastructure/Persistence/Configurations/RegulationConfigs.cs`, yeni migration,
  `src/RentACar.Application/Regulation/RegulationService.cs`,
  `src/RentACar.Web/Components/Pages/Regulation/RegulationList.razor`,
  `src/RentACar.Web/Regulation/RegulationEndpoints.cs`
- **efor:** 1,5 gün
- **bağımlılık:** yok
- **gruplama:** arac_muayene_islemleri ile AYNI PR ("Regülasyon Kısmi Ödeme Genişletmesi")
- **not — PARA — Opus:** kısmi ödemede defter dengesinin (Σ Borç=Σ Alacak) her kısmi tahsilat/ödeme
  ADIMINDA mı yoksa yalnız kapanışta mı yazılacağı — idempotency anahtarının kısmi ödeme sırasına
  (monoton bileşen) göre tasarımı zorunlu (D5 idempotency deseni burada da geçerli olabilir).

### arac_muayene_islemleri.aspx
- **desen:** D2 — **PARA — OPUS'A DEVİR**
- **eylem (yapısal):** `InspectionRecord`'a `IslemKm`, `EvrakNo`, `IslemYapan`, `Kalan`, `OdemeTarihi`
  (ayrı), `KasaKodu`/`HesapNo`, `Aciklama` eklenir. Aynı desende MTV — `RegulationList.razor` Muayene
  bölümüne wiring. Bugün `Ceza` alanı zaten var ama ödeme formunda (ana kayıtta değil) giriliyor —
  bu akış farkı korunabilir ya da ana kayda taşınabilir (yapısal karar, tutar etkisi yok).
- **dokunulacak:** `src/RentACar.Domain/Entities/InspectionRecord.cs`, `RegulationConfigs.cs`,
  yeni migration, `RegulationService.cs`, `RegulationList.razor`, `RegulationEndpoints.cs`
- **efor:** 1 gün
- **bağımlılık:** arac_mtv_islemleri ile aynı PR (kısmi ödeme altyapısı paylaşılır)
- **gruplama:** arac_mtv_islemleri ile AYNI PR ("Regülasyon Kısmi Ödeme Genişletmesi")
- **not — PARA — Opus:** kısmi ödeme + Ceza'nın ana kayıtta mı ödemede mi tutulacağı.

### arac_plan_yonetim.aspx
- **desen:** D7
- **eylem:** Kod tabanında "hedef adet/filo planı" kavramı yok (`grep` boş). SIPP/Araç Grubu bazında
  HEDEF filo adedi + gerçekleşen adet (mevcut `Vehicle.Grup`/`Sipp` sayımından) karşılaştırması + Artır/
  Azalt aksiyonu için yeni dikey: `FiloPlanHedefi` entity (TenantId, AracGrupAdi veya Sipp, HedefAdet,
  Donem — nullable, açık uçlu plan). CLAUDE.md §5 tam reçete: entity→config→migration+RLS→repository→
  servis→DI→liste sayfası+endpoint+nav→test.
- **dokunulacak:** yeni `src/RentACar.Domain/Entities/FiloPlanHedefi.cs`, yeni
  `src/RentACar.Application/FiloPlan/{IFiloPlanRepository.cs,FiloPlanInput.cs,FiloPlanService.cs}`,
  yeni `src/RentACar.Infrastructure/Persistence/{Configurations/FiloPlanConfigs.cs,
  Repositories/FiloPlanRepository.cs}` + migration (elle RLS bloğu), yeni
  `src/RentACar.Web/Components/Pages/FiloPlan/FiloPlanList.razor`, yeni
  `src/RentACar.Web/FiloPlan/FiloPlanEndpoints.cs`, `Program.cs` (`MapFiloPlanEndpoints`),
  `MainLayout.razor` (nav), yeni `tests/.../FiloPlanTests.cs`
- **efor:** 3 gün (D7 taban 3-5 gün — basit dikey olduğu için alt sınır)
- **bağımlılık:** yok — defter postlamaz (salt planlama, D5 değil)

### arac_rac_takvim.aspx
- **desen:** D3
- **eylem:** `ReservationCalendar.razor` (`/takvim`) zaten satır=araç/sütun=gün Gantt mekaniğini
  taşıyor; `Vehicles.ListAsync()` çağrısına Grup ve Şube/Bölge filtresi (query param + select, aynı
  `VehicleFilter` alanları) + plaka arama kutusu eklenir. `CalendarService.GetOccupancyAsync`
  DEĞİŞMEZ (zaten aralık bazlı); sadece gösterilecek araç kümesi filtrelenir.
- **dokunulacak:** `src/RentACar.Web/Components/Pages/Bookings/ReservationCalendar.razor`
- **efor:** 0,5 gün
- **bağımlılık:** yok

### arac_satis.aspx
- **desen:** D1 — **PARA — OPUS'A DEVİR**
- **eylem (yapısal):** `VehicleSaleInput.HedefFiyat`/`SatisKm`/`SatisKanali`/`Devir` ZATEN entity+input
  katmanında var ama `VehicleSaleList.razor`'ın "Yeni Satış" formu bunları HİÇ SORMUYOR (wire-in
  eksikliği — ücretsiz kazanç: forma 4 input eklenir). Gerçekten YENİ alanlar: `KirayaVerme` (bool),
  `IlanKm` (SatisKm'den ayrı — ilan anındaki km), `UygulananKampanya`, İhale bloğu
  (`IhaleFirmasi`/`IhaleTarihi`/`IhaleSayisi`), `SatisiVerildi` (bool, devir durumu — `Devir` text
  alanından ayrı), `YevmiyeNumarasi`, `Aciklama2`, `ListeDoviz` (HedefFiyat'ın döviz karşılığı).
- **dokunulacak:** `src/RentACar.Domain/Entities/VehicleSale.cs`,
  `src/RentACar.Application/VehicleSales/VehicleSaleInput.cs`,
  `src/RentACar.Application/VehicleSales/VehicleSaleService.cs`, yeni migration,
  `src/RentACar.Web/Components/Pages/VehicleSales/VehicleSaleList.razor`,
  `src/RentACar.Web/VehicleSales/VehicleSaleEndpoints.cs`
- **efor:** 1 gün (kısmen ücretsiz — 4 alan zaten var, forma bağlanması yeterli)
- **bağımlılık:** yok
- **gruplama:** arac_satis_ara ile AYNI PR ("Araç Satış Alan+Filtre Zenginleştirmesi")
- **not — PARA — Opus:** hedef-fiyat (`HedefFiyat`/Liste Fiyatı) vs satış-fiyatı (`SatisNet`) ayrımının
  KDV/kur hesabına etkisi (bugün yalnız `SatisNet` KDV'lendiriliyor); `SatisiVerildi` devir durumunun
  defter kaydını (immutable `VehicleSale`) etkileyip etkilemeyeceği.

### arac_satis_ara.aspx
- **desen:** D3 — **PARA — OPUS'A DEVİR**
- **eylem (yapısal):** `VehicleSaleList.razor`'da HİÇ filtre formu yok (düz liste) → yeni
  `VehicleSaleFilter.cs` (Tarih aralığı, `SatisiVerildi`, Durum, Plaka, Ofis) + `SearchAsync`. Kolonlarda
  Kasko/Trafik poliçe özeti (`/regulasyon` join), İhale bilgisi ve Kredi Firma (arac_satis/arac_kredi
  PR'larının alanlarına bağlı), Geçen Süre (`Tarih` − bugün, hesap, alan gerektirmez).
- **dokunulacak:** yeni `src/RentACar.Application/VehicleSales/VehicleSaleFilter.cs`,
  `VehicleSaleService.cs`, `IVehicleSaleRepository.cs`,
  `src/RentACar.Infrastructure/Persistence/Repositories/VehicleSaleRepository.cs`,
  `src/RentACar.Web/Components/Pages/VehicleSales/VehicleSaleList.razor`
- **efor:** 0,5 gün
- **bağımlılık:** arac_satis ile aynı PR; İhale/Kredi kolonu arac_satis+arac_kredi'nin alanlarına bağlı
- **gruplama:** arac_satis ile AYNI PR ("Araç Satış Alan+Filtre Zenginleştirmesi")

### arac_satis_bedeli.aspx
- **desen:** belirsiz — **PARA — OPUS'A DEVİR**
- **eylem:** Yapısal eylem YAZILAMAZ — canlı ekranın gerçek işlevi doğrulanamadı. "RentTo" kavramı
  (muhtemelen alt-kiralama/ortak filo partneri) kod tabanında hiçbir karşılığı yok (`grep` boş); canlı
  başlığı jenerik "TürevRent"; K3=%7 (2/27, yalnız Plaka/Marka gibi zayıf tekil eşleşme). İlk adım kod
  değil ARAŞTIRMA: canlı ekran tekrar gezilip RentTo/Ofis_Durum (İşlem Ofisi vs Çıkış Ofisi) alan
  anlamları teyit edilmeli; ancak sonra bir desen/dosya iddia edilebilir.
- **dokunulacak:** belirsiz — muhtemelen `src/RentACar.Application/Reporting/*` (Karlılık/FiloAnaliz
  ailesine yeni bir kırılım) ama teyitsiz iddia edilemez
- **efor:** verilmedi — önce doğrulama, sonra Opus kararı
- **bağımlılık:** yok

### arac_servis_islemleri.aspx
- **desen:** D5 — **PARA — OPUS'A DEVİR**
- **eylem (yapısal):** `ServiceRecord`'a kaza/hasar detay bloğu (`BeyanTuru`, `KarsiPlaka`,
  `KarsiTrafikSigortasi`, `KazaTarihi`, `KazaSorumlusu`, `HasarDosyaNo`, `DegerKaybi`) + fatura bloğu
  (`FaturaTarihi`, `FaturaNo`, `FaturaTutar`, `FaturaKdv`, `FaturaGenelToplam`) + ödeme bloğu
  (`OdemeTarihi`, `Odeme`, `OdemeDoviz`, `OdemeKur`, `OdemeTuru`, `KasaKodu`, `HesapNo`) +
  `CikisYakit`/`DonusYakit` (bugün sadece Km granülü var) eklenir. `ServiceLine` (bugün yalnız
  `Aciklama`+`Tutar`) `BirimFiyat`/`Miktar`/`Indirim`/`Kdv` ile genişletilir (canlının Açıklama/Birim
  Fiyat/Toplam Fiyat/İndirim/Tutar/KDV/Genel Toplam satırına yakınsar). İşçilik 6-kırılımı
  (Kaporta/Boya/Trim/Elektrik/Mekanik/Şase) — mevcut serbest `ServiceLine` listesiyle KARŞILANABİLİR
  (6 ayrı satır girilir) ya da 6 sabit alan eklenir; ikisi de yapısal, tercih Opus'a.
- **dokunulacak:** `src/RentACar.Domain/Entities/ServiceRecord.cs` (ServiceLine dahil),
  `src/RentACar.Application/ServiceRecords/ServiceRecordInput.cs`,
  `src/RentACar.Application/ServiceRecords/ServiceRecordService.cs`, yeni migration,
  `src/RentACar.Web/Components/Pages/ServiceRecords/ServiceRecordList.razor`,
  `src/RentACar.Web/ServiceRecords/ServiceRecordEndpoints.cs`
- **efor:** 3 gün (D5 taban 2-4 gün — 114 alanlı canlı ekranın büyüklüğü üst sınıra çekiyor)
- **bağımlılık:** yok
- **not — PARA — Opus:** fatura/ödeme bloğunun GERÇEK gider+defter kaydına (`ExpenseType`) bağlanıp
  bağlanmayacağı — bugün yorum "gerçek gider Gider dilimine bağlanır, follow-up" diyor, yani
  ServiceRecord'un kendisi deftersiz; bu blok eklenirse defter entegrasyonu kararı ZORUNLU adversarial
  incelemeye girer (D5 kuralı). İşçilik kırılımı biçimi de bu karara bağlı.

### arac_sigorta_islemleri.aspx
- **desen:** D5 — **PARA — OPUS'A DEVİR**
- **eylem (yapısal):** **Zeyil (poliçe eki) TAMAMEN yok** — canlıda Zeyil Listele/Yeni Zeyil/Sil +
  9 kolonlu geçmiş var; bizde ödeme formunda tek seferlik `ZeyilPrim` sayısı var, kalıcı kayıt yok.
  Yeni alt-tablo `InsurancePolicyZeyil` (PolicyId, ZeyilNo, Tarih, Tanzim, Deger, Brut, Net, FonVergi,
  Tipi, Neden) + CRUD + `RegulationList.razor` Sigorta bölümüne "Zeyil" alt-liste/form eklenir.
  `InsurancePolicy`'ye `AracDegeri`/`ImmDegeri`/`AksesuarDegeri` (sigorta değer tabanı) + `Kalan`
  (bakiye) eklenir.
- **dokunulacak:** yeni `src/RentACar.Domain/Entities/InsurancePolicyZeyil.cs`,
  `src/RentACar.Domain/Entities/InsurancePolicy.cs` (3 yeni alan), `RegulationConfigs.cs`
  (yeni config + elle RLS bloğu), yeni migration, `src/RentACar.Application/Regulation/
  RegulationService.cs`, `IRegulationRepository.cs`,
  `src/RentACar.Infrastructure/Persistence/Repositories/RegulationRepository.cs`,
  `src/RentACar.Web/Components/Pages/Regulation/RegulationList.razor`,
  `src/RentACar.Web/Regulation/RegulationEndpoints.cs`
- **efor:** 2,5 gün (yeni alt-tablo + RLS + D5 sınıfı defter-etkisi olasılığı)
- **bağımlılık:** yok
- **not — PARA — Opus:** Zeyil'in prime nasıl ekleneceği (kalıcı geçmiş mi, her zeyil kendi defter
  kaydını mı postlar) ve `AracDegeri`/`ImmDegeri`'nin herhangi bir hesaba (örn. hasar/rücu tavanı)
  girip girmeyeceği. Bugünkü FX kur-doğrulama davranışı (kur boşsa TCMB/sabit, yoksa red) korunmalı —
  bu zaten bizde VAR, canlıda yok, kaybedilmemeli.

### arac_siparis.aspx
- **desen:** D2 — **PARA — OPUS'A DEVİR**
- **eylem (yapısal):** `AracSiparis.Tedarikci` (serbest metin) yanına `TedarikciCariId` (Guid?,
  additive — eski metin alanı KALIR, CLAUDE.md §3 additive ilkesi) eklenir + Cari arama/seç.
  `DosyaNo`, `ImzaTarih`, `SatisTemsilci`/`OzelTemsilci`, `Versiyon`/`Opsiyon`/`Renk`/`IcRenk`,
  `KaynakTip`, `SatisTipi`, `PiyasaFiyat`/`OpsFiyat`/`FiloFiyat` (bugün tek `BirimFiyat`),
  `TsbKayitNo`, `KrediNo` (AracKredi'ye bağlama) eklenir.
- **dokunulacak:** `src/RentACar.Domain/Entities/AracSiparis.cs`,
  `src/RentACar.Application/AracSiparisleri/AracSiparisInput.cs`,
  `src/RentACar.Application/AracSiparisleri/AracSiparisService.cs`, yeni migration,
  `src/RentACar.Web/Components/Pages/AracSiparisleri/AracSiparisList.razor`,
  `src/RentACar.Web/AracSiparisleri/AracSiparisEndpoints.cs`
- **efor:** 1,5 gün
- **bağımlılık:** `KrediNo` alanı arac_kredi PR'ından sonra anlamlı
- **gruplama:** arac_siparis_detay_listesi + arac_siparis_listesi ile AYNI PR
  ("Araç Sipariş Cari-FK + Çok-Katmanlı Fiyat + Filtre")
- **not — PARA — Opus:** hangi fiyat katmanının (Liste/Piyasa/Ops/Filo/Onay) "resmi" sipariş tutarı
  sayılacağı ve `KrediNo` bağlamasının defter/AracKredi ilişkisine etkisi.

### arac_siparis_detay_listesi.aspx
- **desen:** D3 — **PARA — OPUS'A DEVİR**
- **eylem (yapısal):** Aynı `/arac-siparis` route'una filtre formu (Cari/Ad-Soyad arama, Tarih
  aralığı, Plaka/Araç) + çok-katmanlı fiyat kolonları (arac_siparis'in yeni alanlarından) eklenir.
  Yeni `AracSiparisFilter.cs`.
- **dokunulacak:** yeni `src/RentACar.Application/AracSiparisleri/AracSiparisFilter.cs`,
  `AracSiparisService.cs`, `IAracSiparisRepository.cs`,
  `src/RentACar.Web/Components/Pages/AracSiparisleri/AracSiparisList.razor`
- **efor:** 0,5 gün
- **bağımlılık:** arac_siparis ile aynı PR
- **gruplama:** arac_siparis ile AYNI PR

### arac_siparis_listesi.aspx
- **desen:** D3 — **PARA — OPUS'A DEVİR**
- **eylem (yapısal):** Aynı route'un en dar varyantı (4 kolon canlıda); arac_siparis_detay_listesi'nin
  filtre PR'ıyla birlikte karşılanır — ayrı kod gerekmez, sade görünüm zaten kapsanıyor.
- **dokunulacak:** aynı — `AracSiparisList.razor`
- **efor:** 0,5 gün (esasen arac_siparis_detay_listesi'nin bir alt-kümesi)
- **bağımlılık:** arac_siparis ile aynı PR
- **gruplama:** arac_siparis ile AYNI PR

### baf_ara.aspx
- **desen:** D3
- **eylem:** `BafList.razor` düz liste, hiç filtre formu yok → yeni `BafFilter.cs`
  (Personel/Plaka/Durum/Lokasyon [Aynı Ofis-Farklı Ofis — `CikisSube` vs `DonusSube` karşılaştırması,
  bkz. baf_islemleri]/KullanimAmaci [baf_islemleri'nde eklenecek enum]/Tarih aralığı/Ofis) +
  `BafService.SearchAsync`.
- **dokunulacak:** yeni `src/RentACar.Application/Baflar/BafFilter.cs`,
  `src/RentACar.Application/Baflar/BafService.cs`,
  `src/RentACar.Application/Baflar/IBafRepository.cs`,
  `src/RentACar.Infrastructure/Persistence/Repositories/BafRepository.cs`,
  `src/RentACar.Web/Components/Pages/Baflar/BafList.razor`
- **efor:** 0,5 gün
- **bağımlılık:** `KullanimAmaci`/`Lokasyon` filtresi baf_islemleri'nin yeni alanlarına bağlı
- **gruplama:** baf_islemleri ile AYNI PR ("Baf Derinliği")

### baf_islemleri.aspx
- **desen:** D1
- **eylem:** `Baf.DonusTarihi`/`DonusKm`/`DonusYakit` ZATEN entity'de var ama "Teslim Al" formu
  (`BafList.razor`) yalnız `donusKm` soruyor — `DonusTarihi`/`DonusYakit` inputları eklenir (kısmen
  ücretsiz kazanç). Gerçekten yeni: `KullanimAmaci` enum (11 seçenek: Araç Ayırma/Dönüşü/Teslimatı/
  Yıkama vb.), `Onaylayan` (Guid? personelId — onay iş akışı), `KirayaVer` (bool),
  `DonusSube` (string?, çıkış şubesinden AYRI — farklı ofise teslim senaryosu), `CikisSaat`/`DonusSaat`
  (TimeOnly? — bugün sadece tarih var).
- **dokunulacak:** yeni `src/RentACar.Domain/Enums/BafKullanimAmaci.cs`,
  `src/RentACar.Domain/Entities/Baf.cs` (5 yeni alan), yeni migration,
  `src/RentACar.Application/Baflar/BafInput.cs`, `BafService.cs`,
  `src/RentACar.Web/Components/Pages/Baflar/BafList.razor`, `src/RentACar.Web/Baflar/BafEndpoints.cs`
- **efor:** 1 gün
- **bağımlılık:** yok
- **gruplama:** baf_ara ile AYNI PR ("Baf Derinliği")

### bos_arac_listesi.aspx
- **desen:** D3
- **eylem:** `MusaitlikArama.razor`'ın sonuç listesi `IReadOnlyList<Vehicle>` — Tipi/Yılı/YakıtTürü/
  Vites/Renk/SIPP/KarLastiği/Temizlik kolonları ZATEN Vehicle'da var, tabloya eklenir (D3, ücretsiz).
  Plaka arama kutusu eklenir. "Boştaki Süresi" (son tamamlanan kira/servisin dönüş tarihinden bugüne
  gün sayısı) ve "Müşteri (son kullanan)" — `AvailabilityService.FindAvailableAsync` sonucuna küçük
  bir join (`Rentals` üzerinde `VehicleId` bazında son `GercekDonusTar`) eklenerek hesaplanır, yeni
  tablo gerekmez. Direkt "Kirala" linki: `/kiralar/yeni?vehicleId=X&from=&to=` prefill (mega-form
  zaten querystring prefill destekliyor — kontrol edilip yoksa küçük ek).
- **dokunulacak:** `src/RentACar.Application/Availability/AvailabilityService.cs`,
  `src/RentACar.Application/Availability/IAvailabilityRepository.cs`,
  `src/RentACar.Infrastructure/Persistence/Repositories/AvailabilityRepository.cs`,
  `src/RentACar.Web/Components/Pages/Availability/MusaitlikArama.razor`
- **efor:** 1 gün (D3 taban 0,5 gün — "boştaki süre" join'i sapma nedeni)
- **bağımlılık:** yok
- **not:** tarih-aralığı zorunluluğu (canlı anlık-snapshot çalışıyor, bizde aralık zorunlu) bilinçli
  UX farkı olarak KALABİLİR; istenirse from/to boşsa "şu an" davranışına düşen küçük bir ek yapılabilir
  (bu PR'ın kapsamı dışında, ayrı not olarak bırakıldı — YAPILMAZ değil, sadece önceliksiz).

### servis_rezervasyon.aspx
- **desen:** D2
- **eylem:** `ServisDurum` enumuna `Rezerve` değeri eklenir (Rezerve→Acik→Serviste→Tamamlandi/Iptal
  akışı) + `ServiceRecord`'a `PlanBasTarihi`/`PlanBitTarihi` (nullable — planlanan randevu penceresi,
  gerçek `GirisTarihi`/`CikisTarihi`'den AYRI) eklenir. `ServiceRecordList.razor`'a "Rezervasyonlar"
  filtresi/sekmesi (Durum=Rezerve) + "Servise Al" aksiyonu (Rezerve→Acik geçişi, GirisTarihi/GirisKm
  o an doldurulur) eklenir. Vade panosuna (varsa) bu pencere dahil edilebilir (ayrı küçük ek).
- **dokunulacak:** `src/RentACar.Domain/Enums/ServisDurum.cs`,
  `src/RentACar.Domain/Entities/ServiceRecord.cs`, yeni migration,
  `src/RentACar.Application/ServiceRecords/ServiceRecordService.cs`,
  `src/RentACar.Application/ServiceRecords/ServiceRecordInput.cs`,
  `src/RentACar.Web/Components/Pages/ServiceRecords/ServiceRecordList.razor`,
  `src/RentACar.Web/ServiceRecords/ServiceRecordEndpoints.cs`
- **efor:** 1 gün
- **bağımlılık:** yok

### servis_tanim_tablosu.aspx
- **desen:** D2
- **eylem:** `ServisTanim`'in bugünkü serbest-metin `AracTipi`+`Kod` anahtarı, filodaki GERÇEK
  (Marka,Tip,Yakıt,Vites) kombinasyonuna bağlanır: `Marka`/`Tip`/`Yakit`/`Vites` (nullable) kolonları
  eklenir (eski `Kod`/`AracTipi` KALIR — additive, geriye-uyum). Yeni bir "otomatik türet" akışı:
  `db.Vehicles`'tan DISTINCT (Marka,Tip,Yakıt,Vites) kombinasyonları çekilip her biri için (eşleşen
  `ServisTanim` yoksa) satır önerilir; `ServisTanimList.razor`'da KM alanı inline düzenlenir (kural:
  filoya bağlı otomatik matris — D2'nin "kuralı okuyan yol" testi bu türetme mantığı).
- **dokunulacak:** `src/RentACar.Domain/Entities/ServisTanim.cs`, yeni migration,
  `src/RentACar.Application/ServisTanimlari/ServisTanimFiles.cs` (repository arayüzü + servis burada),
  `src/RentACar.Infrastructure/Persistence/Repositories/ServisTanimRepository.cs`,
  `src/RentACar.Web/Components/Pages/ServisTanimlari/ServisTanimList.razor`,
  `src/RentACar.Web/ServisTanimlari/ServisTanimEndpoints.cs`
- **efor:** 1 gün
- **bağımlılık:** yok

---

TOPLAM: 25 ekran planlandı
