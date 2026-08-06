# Ekleme Planı — 02-tanim-master

**Ekran sayısı (eksikler.json, bu modül):** 27
**Desen dağılımı:** D1=2 · D2=8 · D3=1 (D3 payı detayli_arac_listesi'nde D2 ile karışık) · D4=1 ·
D8=5 (bloke) · YAPILMAZ=3 · PARA—Opus (yapısal eylemli)=6
**Toplam efor (yapısal, bloke hariç):** ~22,5 gün → **14 PR** (10 kimliksiz + 4 PARA grubu)
**Bloke (efor dışı):** 5 ekran — 3 farklı dış-kimlik ailesi (XML/OTA broker, rakip-fiyat API, SMS sağlayıcı)
**YAPILMAZ (efor yok):** 3 ekran — 1 ölçüm-belirsizliği (gerçek fark yok), 2 mimari/kapsam-dışı karar

Bu modülde D3 "ucuz sınıf" azınlıkta — çoğu ekran gerçekten **eksik alan** taşıyan sözlük/master
(D1/D2) veya PARA. Kural 7 agresif uygulandı: 27 ekran → 14 PR (aynı entity/dosyayı paylaşanlar
veya aynı kavramsal aile — Lokasyon×Drop, Filo Kiralama form+liste, Toplu Gider+Tahsilat — birleşti).

---

## PR-A: Basit sözlük derinlik paketi (Araç Grubu + Döviz + Hesap No)

Üç ayrı entity ama AYNI reçete (CLAUDE.md §5: entity alan ekle → migration → form → grid). D1
kataloğunun "0,5 gün / 5 ekran tek PR" ilkesine uygun tek PR'da toplanır.

### arac_grubu.aspx
- **desen:** D2
- **eylem:** `VehicleGroup` entity'sine 7 alan eklenir: `ProvizyonDoviz`, `Provizyon2Doviz` (string,
  3-harf döviz kodu), `YakitTuru` (nullable `FuelType?`, grup-seviyesi varsayılan), `Vites` (nullable
  `Vites?`, grup-seviyesi varsayılan), `EntegrasyonKod1` (string), `WebId` (string), `ServisId`
  (string). `VehicleGroupList.razor`'daki create/edit formuna karşılık gelen input'lar eklenir. Liste
  grid'i (şu an 7 kolon: Kod/Ad/SIPP/Segment/GünlükKM/Provizyon/Durum) **Açıklama**, **Araç Sayısı**
  (mevcut `VehicleGroupService`'te zaten hesaplanan `_unmatched`/eşleşen-sayım mantığına benzer bir
  `COUNT(Vehicle WHERE Grup=g.Ad)` sorgusu), **Web ID**, **Servis ID**, **Sürücü Yaşı**
  (`SurucuMinYas`), **Ehliyet Yılı** (`EhliyetMinYil`) kolonlarıyla genişletilir — bu 4 alan zaten
  entity'de var, sadece grid'e taşınmamış (D3-benzeri ek iş, aynı PR'da).
- **dokunulacak:** `src/RentACar.Domain/Entities/VehicleGroup.cs`,
  `src/RentACar.Infrastructure/Persistence/Configurations/VehicleGroupConfig.cs` (veya ilgili config
  dosyası), yeni migration, `src/RentACar.Web/Components/Pages/VehicleGroups/VehicleGroupList.razor`,
  `src/RentACar.Application/VehicleGroups/VehicleGroupService.cs`
- **efor:** 1 gün
- **bağımlılık:** yok

### para_tanimlama.aspx
- **desen:** D1 (Ülke alanı) + **YAPILMAZ** (Kur alt-parçası, alt not aşağıda)
- **eylem:** `Currency` entity'sine `Ulke` (string, opsiyonel) alanı eklenir; `CurrencyList.razor`
  create/edit formuna ve grid'e (Kod/Ad/Sembol/Durum → +Ülke) eklenir.
  **Kur alanı BİLİNÇLİ eklenmez** — gerekçe: kur zaten AYRI ve tek kaynaktan yönetiliyor
  (`/kurlar`, TCMB otomatik + tenant sabit kur; bkz. TCMB kur sistemi kararı). `Currency`'ye statik/
  manuel bir `Kur` alanı eklemek çift-kaynak (iki yerde kur, hangisi geçerli belirsizliği) yaratır —
  bu para-doğruluğu riskini büyütür, mimari gerileme sayılır (kural 6).
- **dokunulacak:** `src/RentACar.Domain/Entities/Currency.cs`, ilgili config + migration,
  `src/RentACar.Web/Components/Pages/Currencies/CurrencyList.razor`
- **efor:** 0,25 gün (yalnız Ülke)
- **bağımlılık:** yok

### hesap_no_tanimlama.aspx
- **desen:** D2
- **eylem:** `FinancialAccount` entity'sine `HediyeCek` (bool), `OzelKod` (string?),
  `UyariMailListesi` (string?, virgülle ayrılmış eposta listesi — bakiye/vade uyarısı TETİKLEME
  mantığı bu PR'a girmez, yalnız alan eklenir) eklenir. Ayrıca entity'de **zaten var ama forma
  hiç bağlanmamış** `Sube` (banka şube adı) alanı `FinancialAccountList.razor`'daki create/edit
  formuna ve grid'e eklenir (wire-in — migration gerekmez, mevcut kolon).
- **dokunulacak:** `src/RentACar.Domain/Entities/FinancialAccount.cs`, ilgili config + migration
  (yalnız 3 yeni alan için), `src/RentACar.Web/Components/Pages/FinancialAccounts/FinancialAccountList.razor`
- **efor:** 0,5 gün
- **bağımlılık:** yok
- **gruplama:** PR-A — "Basit sözlük derinlik paketi (Araç Grubu + Döviz + Hesap No)"

### hesap_tanimalama.aspx
- **desen:** YAPILMAZ
- **gerekçe:** Alan bazında **tam örtüşme** zaten var (`HesapKodu.Kod/Ad/Aciklama/Aktif` ↔ canlı
  `Kod_Adı+Açıklama`). KISMİ işareti, canlı ekranın grid kolonu güvenilir çıkarılamadığı için
  (kolon kaynağı=yok) verilen bir **ölçüm belirsizliği** — gerçek bir fonksiyonel fark tespit
  edilmedi. Yapılacak bir iş yok; ilerideki bir tarama canlı grid kolonunu doğrularsa yeniden
  değerlendirilir.
- **efor:** yok
- **bağımlılık:** yok

---

## PR-B: Filo Kiralama derinlik + liste/arama

İki ekran AYNI rota (`/filo-kiralama`) ve AYNI dosya (`FiloKiralamaList.razor`) üzerinde — ayrı PR
anlamsız, zorunlu grup.

### filo_arac_kiralama.aspx
- **desen:** D2
- **eylem:** `FiloKiralama` entity'sine 11 alan eklenir: `SatisTemsilcisi` (string?), `FaturaTuru`
  (string?, "Dönem"/"Kırık"), `SozlesmeTarihi` (DateTimeOffset?, `BasTar`'dan ayrı — sözleşme imza
  tarihi), `MakbuzNo`/`DosyaNo` (string?), `ImzaTarih` (DateTimeOffset?), `SozlesmeNo` (string?),
  `VadeGun` (int?), `FiyatTuru` (string?, "Aylık"/"30 Gün Aylık"/"KDV Dahil"/"30 Gün Dahil"),
  `Kaynak` (string?, rez. kaynağı), `CikisKm` (int?), `ToplamKm` (int?). `FiloKiralamaList.razor`
  create/edit formuna eklenir.
  **Çok-araçlı sözleşme modeli (canlı: bir sözleşmeye ayrı ayrı araç/fiyat satırı) BU PR'A GİRMEZ** —
  bizim tek-araç/tek-sözleşme modelini çok-araçlı hale getirmek CLAUDE.md §2 mimari kararına
  (temiz, additive genişleme) dokunan bir yeniden-tasarım; açık bırakılır, kullanıcıya ayrı soru
  olarak sorulmalı (ileride roadmap kalemi).
- **dokunulacak:** `src/RentACar.Domain/Entities/FiloKiralama.cs`, ilgili config + migration,
  `src/RentACar.Web/Components/Pages/FiloKiralamalar/FiloKiralamaList.razor`,
  `src/RentACar.Application/FiloKiralamalar/FiloKiralamaService.cs` (dosya adı yaklaşık; servis
  gerçek adıyla teyit edilip güncellenir)
- **efor:** 1 gün
- **bağımlılık:** yok

### filo_kiralama_listesi.aspx
- **desen:** D3
- **eylem:** `FiloKiralamaList.razor`'a filtre formu eklenir: Cari Bilgi arama (No/Ad/Soyad —
  mevcut `CustomerService.ListAsync` üzerinden), Tarih (başlangıç/bitiş aralığı, önemli/önemsiz
  seçici canlı-özel bir ayrım — sade aralık filtresiyle karşılanır), Plaka arama, Araç arama.
  "Tablo Ayarlarını Kaydet" (kullanıcı bazlı grid düzeni) **eklenmez** — bizde kullanıcı bazlı grid
  kişiselleştirme altyapısı yok, kapsamı büyütür; sade filtre yeterli parite eşiği sağlar.
- **dokunulacak:** `src/RentACar.Web/Components/Pages/FiloKiralamalar/FiloKiralamaList.razor`
- **efor:** 0,5 gün
- **bağımlılık:** yok
- **gruplama:** PR-B — "Filo Kiralama derinlik + liste/arama" (toplam 1,5 gün)

---

## PR-C1: Lokasyon × Drop derinlik paketi

Lokasyon ve Drop-tanım aynı kavramsal çift (`DropTanim.Lokasyon`+`Sube` zaten Lokasyon×Şube
ikilisi) — Lokasyon'a Çıkış/Dönüş ayrımı eklenince Drop de ondan yararlanır, birlikte PR.

### lokasyonlar.aspx
- **desen:** D2 (büyük derinlik — kataloğun 1-2g tabanının üstünde, sapma nedeni: 24+ yeni alan)
- **eylem:** `Location` entity'sine eklenir: `IngilizceAd`, `BulusmaNoktasi` (string?, "Ofis
  Teslim"/"Havalimanı"/"Shuttle"…), `Iata` (string?), `WebdeGizle` (bool), `LokasyonTuru` (string?,
  "Havalimanı"/"Şehir Merkezi"/…), `BinaNo` (string?), `Tarif` (string?, yol tarifi metni), `Ulke`
  (string?), `PostaKodu` (string?), `MapsKonumu` (string?), `EkAciklama` (string?), `WebSira`
  (int?), günün 7 günü için açılış/kapanış saat çifti — **tek bir `List<GunSaat>`/JSONB kolon**
  olarak modellenir (`HaftalikCalismaSaatleri` — gün adı + açılış + kapanış, 7 satır), canlının
  14 ayrı sütununu birebir kopyalamak yerine (aynı veri, daha az kolon şişmesi) — `CalismaSaatleri`
  serbest-metin alanı GERİYE UYUM için korunur. `DropKarsilamaTuru`, `DropCalismaSekli`, `OzelMail`,
  `OzelTelefon` eklenir. `LocationList.razor` formuna ve grid'ine yansıtılır.
- **dokunulacak:** `src/RentACar.Domain/Entities/Location.cs`, ilgili config + migration,
  `src/RentACar.Web/Components/Pages/Locations/LocationList.razor`,
  `src/RentACar.Application/Locations/LocationService.cs` (gerçek adıyla teyit edilir)
- **efor:** 2,5 gün
- **bağımlılık:** yok

### lokasyon_sube_ara.aspx
- **desen:** D2
- **eylem:** `DropTanim` entity'sindeki tek `Lokasyon` alanı **Çıkış**/**Dönüş** ayrımına
  bölünmez (mevcut `(TenantId, Lokasyon, Sube)` benzersizliğini bozar) — bunun yerine `CikisLokasyon`
  (yeni, mevcut `Lokasyon` alanının anlamı budur, isim netleştirilir ama kolon aynı kalır) +
  `DonusLokasyon` (yeni, nullable — null=tek yön/aynı lokasyon) eklenir. Ayrıca `ManSuresi` (int?,
  dakika), `MinGun` (int?), `Drop2` (decimal?, ikinci ücret kalemi — tek/çift yön ayrımı) eklenir.
  `DropTanimList.razor`'a filtre formu: İşlem Şube, Çıkış Lokasyon, Dönüş Lokasyon (dropdown'lar
  `LocationService`/`BranchService`'ten beslenir).
- **dokunulacak:** `src/RentACar.Domain/Entities/DropTanim.cs`, ilgili config + migration,
  `src/RentACar.Web/Components/Pages/DropTanimlari/DropTanimList.razor`
- **efor:** 1 gün
- **bağımlılık:** `lokasyonlar.aspx` PR'ı sonra gelmeli değil ama Çıkış/Dönüş kavramı Lokasyon
  listesinden dropdown besleneceği için PR-C1 içinde SIRALI (lokasyonlar önce) uygulanır.
- **gruplama:** PR-C1 — "Lokasyon × Drop derinlik paketi" (toplam 3,5 gün)

---

## PR-C2: Şube derinlik paketi

Ayrı PR tutuldu — child-tablo ("Şube Özel Ücretsiz Hizmet") + şube-birleştirme aracı taşıyan en
riskli/geniş kalem; PR-C1 ile birleştirilirse gözden geçirme çok büyür (CLAUDE.md §3.4 "küçük,
gözden geçirilebilir PR" ilkesi).

### sube_tanimlama.aspx
- **desen:** D2 (büyük derinlik — sapma nedeni: 20+ yeni alan + 1 child-tablo + 1 veri-taşıma aracı)
- **eylem:** `Branch` entity'sine eklenir: `WebIsim`, `FirmaUnvani`, haftalık açılış/kapanış (Location
  ile aynı desen — `HaftalikCalismaSaatleri`), `WebRezOncesiSaat` (int?), `Enlem`/`Boylam`
  (decimal?), `HizmetKomisyonOran` (decimal?, mevcut `KomisyonOran`'dan AYRI — "hizmetten alınan"),
  `RezervasyonRengi` (string?, hex), `AlisSubesiDegilMi` (bool), `WebSira` (int?), `WebOtoparkId`
  (string?), `BayiCariKod`/`BayiOfisId` (bayi/aracı şube modeli), `KomisyonHesabi` (string?,
  "Satıştan"/"Maliyetten"), `OnlineRezId` (string?), `SozlesmeNoFormati` (string?), `NakitHesapId`/
  `BankaHesapId` (Guid?, `FinancialAccount` FK — default hesap ataması), `EntegrasyonKodu` (string?),
  `ResimDosyasi` (string?, dosya yolu). Yeni child-tablo `SubeUcretsizHizmet` (TenantId, SubeId FK,
  HizmetAdi, Aciklama — 10 satıra kadar, ayrı liste UI'da eklenir/silinir). "Eski Şube→Yeni Şube"
  birleştirme aracı: yeni bir endpoint (`/subeler/birlestir`) — kaynak şubeye bağlı `Vehicle.SubeId`/
  `Expense`/`User.AtanmisSube` gibi FK/metin referanslarını hedef şubeye toplu güncelleyip kaynağı
  pasife çeker (silme YOK — audit iziyle).
- **dokunulacak:** `src/RentACar.Domain/Entities/Branch.cs`, yeni
  `src/RentACar.Domain/Entities/SubeUcretsizHizmet.cs`, ilgili config'ler + migration,
  `src/RentACar.Web/Components/Pages/Branches/BranchList.razor`, yeni
  `src/RentACar.Web/Branches/BranchEndpoints.cs` (birleştirme ucu),
  `src/RentACar.Application/Branches/BranchService.cs` (gerçek adıyla teyit edilir)
- **efor:** 3 gün
- **bağımlılık:** yok

---

## PR-D: Rez. Kaynağı × Tedarikçi oranları

### xml_rez_kaynak_tedarikci.aspx
- **desen:** D2 (yapısal) — oranların hangi hesaplamaya nasıl yansıdığı **PARA — Opus**
- **eylem:** `ReservationSource` entity'sine `Tedarikci` (string?), `KiraOrani`/`HizmetOrani`/
  `DropOrani` (decimal?, %) eklenir. `ReservationSourceList.razor` formuna eklenir + "Aşağıya
  Yansıt" butonu: seçili kaynağın oranlarını tüm **aktif** kayıtlara toplu kopyalayan bir aksiyon
  (yapısal — hangi tabloya/hesaba yansıyacağı, komisyon/karlılık hesaplamasını değiştirip
  değiştirmeyeceği **Opus incelemesine bırakılır**, bu PR yalnız alan+buton iskeletini kurar,
  gerçek "yansıtma hedefi" boş/no-op bırakılabilir ya da Opus onayına kadar devre dışı bayrakla
  korunur).
- **dokunulacak:** `src/RentACar.Domain/Entities/ReservationSource.cs`, ilgili config + migration,
  `src/RentACar.Web/Components/Pages/ReservationSources/ReservationSourceList.razor`
- **efor:** 0,75 gün (yapısal iskelet; oran-yansıtma mantığı Opus onayı sonrası ayrı küçük ek)
- **bağımlılık:** yok

---

## PR-E: Rez Şartları (yeni küçük dikey)

### rezsartlar.aspx
- **desen:** D1
- **eylem:** Yeni tenant-owned tablo `RezSart` (CLAUDE.md §5 reçetesi tam uygulanır): `MusteriId`
  (Guid, Customer FK), `Grup` (string?), `Sart` (string, özel talep metni), `BasTar`/`BitTar`
  (DateTimeOffset?), `TalepTarihi` (DateTimeOffset), `KarsilamaTarihi` (DateTimeOffset?, null=henüz
  karşılanmadı), `TeslimEden` (string?, personel adı — PII değil, serbest metin). Opsiyonel
  `ReservationId`/`QuotationId` (Guid?, bağlama — FK YOK, additive) ile mevcut rezervasyon/teklife
  gevşek bağlanabilir. Liste sayfası: müşteri/tarih/durum (Karşılandı mı) filtreli.
- **dokunulacak:** yeni `src/RentACar.Domain/Entities/RezSart.cs`, yeni config + migration (RLS
  bloğu elle), yeni `src/RentACar.Application/RezSartlar/RezSartService.cs` + repository, yeni
  `src/RentACar.Web/Components/Pages/RezSartlar/RezSartList.razor` + endpoint + nav
- **efor:** 1 gün
- **bağımlılık:** yok

---

## PR-F: Otomatik Servisler günlük log

### otomatik_servisler.aspx
- **desen:** D2
- **eylem:** Yeni küçük tablo `JobCalismaLog` (TenantId, JobTuru [enum: HgsBasarili/HgsBasarisiz/
  KmGuncelleme/TaahhutUzatma/Supurme], Tarih, Detay string?, Basarili bool). Mevcut `Uretici`
  desenindeki sınıflara (`FiloBildirimUretici`, `VadeBildirimUretici`, `OperasyonOzetUretici`,
  `DonemFaturaUretici` ve HGS-yansıtma/KM-güncelleme akışlarının bulunduğu servisler) her koşuda bu
  tabloya bir log satırı yazan ince bir çağrı eklenir (yetki yüzeyi büyümez — `Uretici.RunAsync`
  paterni korunur). Yeni liste sayfası `/raporlar/otomatik-servisler`, Tarih filtresiyle.
- **dokunulacak:** yeni `src/RentACar.Domain/Entities/JobCalismaLog.cs`, ilgili config + migration,
  `src/RentACar.Infrastructure/Persistence/FiloBildirimUretici.cs`,
  `src/RentACar.Infrastructure/Persistence/VadeBildirimUretici.cs`,
  `src/RentACar.Infrastructure/Persistence/OperasyonOzetUretici.cs`,
  `src/RentACar.Infrastructure/Persistence/DonemFaturaUretici.cs` (her birine log-yazma çağrısı),
  yeni `src/RentACar.Web/Components/Pages/Reports/OtomatikServisler.razor` + nav
- **efor:** 1 gün
- **bağımlılık:** yok

---

## PR-G: Karşılaştırmalı Durum Analizi (yeni pivot rapor)

### karsilastirmali_durum_analizi.aspx
- **desen:** D4 (kataloğun 1g tabanının üstünde — sapma nedeni: çok-boyutlu pivot)
- **eylem:** `OrtakSorgular`'a yeni sorgu eklenir: Periyot (1-12 ay) × Tablo (Kira/Rezervasyon
  seçici) × VeriTürü (Adet/Gün) × Kaynak-kırılımı (Rez. Kaynağı/Araç Grubu/Çıkış Noktası — kullanıcı
  seçimli TEK kırılım boyutu, canlının serbest pivot-ızgarası yerine sabit 3 seçenekli dropdown ile
  sadeleştirilir) + İşlem Şube filtresi. Yeni `ReportService` metodu + `/raporlar/karsilastirmali-
  analiz` sayfası, aylık kolonlu grid + export. **P&L değil** — bu bir hacim/adet karşılaştırması,
  defter kısıtı (D4'ün "yalnız defterden" kuralı) burada uygulanmaz çünkü tutar üretmiyor.
- **dokunulacak:** `src/RentACar.Infrastructure/Persistence/OrtakSorgular.cs` (gerçek dosya adıyla
  teyit edilir), `src/RentACar.Application/Reports/ReportService.cs`, yeni
  `src/RentACar.Web/Components/Pages/Reports/KarsilastirmaliAnaliz.razor` + nav + export
- **efor:** 2 gün
- **bağımlılık:** yok

---

## PR-H: Detaylı Araç Listesi (konsolide grid)

### detayli_arac_listesi.aspx
- **desen:** D3 (konsolidasyon) + D1 (gerçekten eksik ~20 alan) — karışık, sapma nedeni: 52 eksik
  kolonun ~30'u MEVCUT tablolarda (Vehicle/VehicleSale/AracKredi/InsurancePolicy/InspectionRecord)
  dağınık duruyor (D3), ~20'si HİÇBİR yerde yok (D1 eki).
- **eylem:** `VehicleList.razor`'a (veya yeni bir "detaylı görünüm" sekmesine) mevcut verilerden
  join edilen kolonlar eklenir: Alım Firma/Tarihi/Bedeli (`Vehicle.AlimYapilanFirma/AlimTarihi/
  AlimBedeli` — zaten var), Hedef Satış Bedeli (`VehicleSale.HedefFiyat`), Kredi Firma
  (`AracKredi.BankaAdi`), Muayene Tar (`InspectionRecord.Bitis`, araç başına en yakın), Sig./Kasko
  Bit. Tar (`InsurancePolicy.Bitis`, Tip'e göre), Filo Gir./Çık. Tar (zaten var), Özel Kod1-5 (zaten
  var). Gerçekten YENİ alanlar `Vehicle`'a eklenir: `BelgeNo`, `RuhsatSahibi`, `SozNo`, `AraciAlan`,
  `SonTeslimKm`, `SonTeslimTarihi`, `Kiralayan`, `KiraGun`, `KiraFiyat`, `KiraBitTar`, `KiraBekTar`,
  `AssistanFirma`, `DisKmLimit`, `KiraMusteriId` (Guid?, Customer FK), `TsbKodu`, `TsbKaskoDegeri`,
  `OdemeSekli`, `PasifSebep`, `SonDurum`, `AlisEuro`, `AlisEuroFiyat`, `SatisEuroFiyat`, `HgsFirma`.
  `VehicleSale`'e `IhaleTarihi`/`IhaleFirmasi`/`NoterSatisTarihi` eklenir. "Rez. Müşteri" alanı
  DEPOLANMAZ — aktif rezervasyon/kira üzerinden CANLI çözülür (D3). Filtreye Ofis (Şube) eklenir.
- **dokunulacak:** `src/RentACar.Domain/Entities/Vehicle.cs`,
  `src/RentACar.Domain/Entities/VehicleSale.cs`, ilgili config'ler + migration,
  `src/RentACar.Web/Components/Pages/Vehicles/VehicleList.razor`,
  `src/RentACar.Application/Vehicles/VehicleService.cs`
- **efor:** 2 gün
- **bağımlılık:** yok

---

## PARA — Opus'a devir grupları (yapısal iskelet bu PR'larda kurulur, tutar/formül/idempotency doğruluğu Opus incelemesine bırakılır)

### genel_kasa.aspx
- **desen:** D5-bitişik rapor (yapısal kısmı D4) — tutar/bakiye **PARA — Opus**
- **eylem:** `Reports/KasaBankaDefteri.razor`'a filtreler eklenir: Araç Sahibi (dropdown,
  `VehicleOwnerService`'ten), Kasa Kodu (şu an tek Kasa/Banka seçici → çoklu-kasa checkbox listesi,
  `FinancialAccountService.ListAsync` üzerinden), İşlem Tipi (Depozito Hariç/Sadece Depozito —
  `SourceType` filtreleme), Döviz. Grid'e Cari Bilgi, Kasa Kodu, Döviz, Şube, Evrak No, Araç Sahibi,
  Plaka kolonları eklenir (satır-bazında — `ReportService.GetAccountLedgerAsync` genişletilir).
  **Personel filtresi EKLENMEZ** (PII nedeniyle bilinçli düşürülmüş — canlı fazlası olarak kabul
  edilir, kural 6 benzeri bilinçli fark).
- **dokunulacak:** `src/RentACar.Web/Components/Pages/Reports/KasaBankaDefteri.razor`,
  `src/RentACar.Application/Reports/ReportService.cs`
- **efor:** 1 gün (yapısal); tutar/bakiye doğruluğu ve çoklu-kasa/çoklu-döviz agregasyonu **PARA —
  Opus**
- **bağımlılık:** yok

### hesap_para_islem.aspx
- **desen:** D5-bitişik (Kasa↔Banka virman formunun genişletilmesi) — **PARA — Opus**
- **eylem:** `Finance/KasaHub.razor`'daki virman formuna IBAN/Hesap No seçimi (belirli
  `FinancialAccount` — şu an generic Kasa/Banka `<select>`, spesifik hesaba daraltılır), Banka
  Dövizi, Banka Kuru, Makbuz Numarası, İşlem Şube alanları eklenir. "İşlemi Yapan" alanı EKLENMEZ
  (oturum kullanıcısından geliyor, form alanı canlı fazlası ama bizde kasıtlı — audit zaten
  kullanıcıyı taşıyor).
- **dokunulacak:** `src/RentACar.Web/Components/Pages/Finance/KasaHub.razor`,
  `src/RentACar.Application/Finance/CashService.cs` (gerçek adıyla teyit edilir)
- **efor:** 1 gün (yapısal); kur/tutar doğruluğu **PARA — Opus**
- **bağımlılık:** yok

### toplu_gider.aspx
- **desen:** D5 — **PARA — Opus** (defter yazıyor, zorunlu adversarial inceleme)
- **eylem:** `Finance/TopluGider.razor`'daki serbest-metin satır formatına (`netTutar;açıklama`)
  Cari Bilgisi (opsiyonel cariId), Vade (tarih), Hesap No (IBAN — hangi `FinancialAccount`'tan
  ödeneceği) eklenir; "Plaka Ekle" için satır formatı `netTutar;açıklama;plaka(ops)` şeklinde
  genişletilir (birden çok araca aynı gider dağıtımı — her plaka için ayrı satır, tekil gider
  paylaştırma DEĞİL).
- **dokunulacak:** `src/RentACar.Web/Components/Pages/Finance/TopluGider.razor`,
  ilgili servis (`ExpenseService`)
- **efor:** 0,5 gün (yapısal); dengeli-defter/KDV/idempotency doğruluğu **PARA — Opus**
  (D5 zorunlu adversarial inceleme kapsamına girer)
- **bağımlılık:** yok
- **gruplama:** PR-P3 — "Toplu Gider + Toplu Tahsilat (tek-cari modu)"

### toplu_tahsilat.aspx
- **desen:** D5 — **PARA — Opus** (defter yazıyor, zorunlu adversarial inceleme)
- **eylem:** Canlının modeli TERS ("bir cari, çok açık kalem seç") — mevcut çok-cari modeli
  KORUNUR (bozulmaz), YANINA yeni bir mod eklenir: Cari arama (No/Ad/Soyad, TEK cari), Hesap No
  (IBAN) seçimi, "Carinin İşlem Listesini Getir" (o carinin açık/bakiyeli kalemlerini listeler —
  hangi kaynaktan okunacağı Opus'a: Cari ekstre/AccountLedgerEntry sorgusu), Seçili Toplam
  gösterimi, tek tıkla TOPLU kapatma. Bu ayrı bir sayfa/mod olarak eklenir (mevcut
  `/toplu-tahsilat` bozulmadan).
- **dokunulacak:** `src/RentACar.Web/Components/Pages/Finance/TopluTahsilat.razor`, yeni
  `src/RentACar.Web/Components/Pages/Finance/TekCariToplu.razor` (yeni "tek-cari-çok-kalem" modu),
  ilgili servis (`CashService`)
- **efor:** 1 gün (yapısal); dengeli-defter/idempotency doğruluğu **PARA — Opus**
  (D5 zorunlu adversarial inceleme kapsamına girer)
- **bağımlılık:** yok
- **gruplama:** PR-P3 — "Toplu Gider + Toplu Tahsilat (tek-cari modu)" (toplam 1,5 gün yapısal)

### otomatik_tahsilat.aspx
- **desen:** D5-bitişik (yeni arama+tetikleme ekranı) — **PARA — Opus**
- **eylem:** Yeni sayfa (mevcut `/ayarlar` togglesinden AYRI — o yalnız job-seviyesi açık/kapalı
  anahtarı, bu ekran SEÇİLİ sözleşmeler üzerinde MANUEL tetikleme): Sözleşme No filtresi, Bakiye
  durumu (Müşteri Bakiyeli/HGS Bakiyeliler/Sadece Otomatik Olanlar — `RentalContract`+`Bildirim`/
  `AccountLedgerEntry` sorgusu), İşlem Şube, Tarih aralığı → sonuç listesi + "Seçilenler için
  Tahsilatı Çalıştır" butonu (mevcut `DonemFaturaUretici`/otomatik-tahsilat job mantığını TEK
  seferlik, seçili-kapsamlı olarak yeniden kullanır — job'un içindeki tahsilat çekirdeği paylaşılır,
  kopyalanmaz).
- **dokunulacak:** yeni `src/RentACar.Web/Components/Pages/Finance/OtomatikTahsilat.razor` + endpoint,
  `src/RentACar.Infrastructure/Persistence/DonemFaturaUretici.cs` (tahsilat çekirdeğinin yeniden
  kullanılabilir hale getirilmesi)
- **efor:** 1,5 gün (yapısal arama+tetikleme iskeleti); hangi sözleşmelerin "otomatik tahsilat
  edilebilir" sayılacağı ve tahsilat tutarının doğruluğu **PARA — Opus**
- **bağımlılık:** yok

### xml_fiyat_aktar.aspx
- **desen:** D3 (canlı ızgara görüntüleme) + D5-bitişik (toplu silme) — **PARA — Opus**
- **eylem:** `/tarife-aktar` sayfasına (ya da `/tarife-matris`'e, hangisi RateMatrix'i listeliyorsa)
  Rezervasyon Kaynağı/Şube ön-filtreli CANLI fiyat ızgarası görünümü eklenir (mevcut RateMatrix
  repository'sinden okunur — yeni tablo YOK, D3) + "Sadece Seçili Rezervasyon Kaynağını Sil" toplu
  silme butonu (onaylı/beklemede tarifeleri kapsam-filtreli toplu kaldırma — hangi durumdaki
  (Beklemede/Onaylı) satırların silinebileceği ve fiyat motorunun o an aktif tarifeyi kaybetme
  riski **Opus incelemesine bırakılır**).
- **dokunulacak:** `src/RentACar.Web/Components/Pages/Import/TarifeAktar.razor` (veya
  `/tarife-matris` sayfası — gerçek dosya teyit edilir),
  `src/RentACar.Application/Pricing/RateMatrixService.cs` (gerçek adıyla teyit edilir)
- **efor:** 1 gün (yapısal); toplu-silme güvenliği/onay-akışı etkileşimi **PARA — Opus**
- **bağımlılık:** yok

---

## Bloke — D8 (kod yazılmaz, kimlik/credential gerekir; kullanıcıya sorulmadan açılmaz)

### rakip_fiyat_analizi.aspx
- **desen:** D8
- **bloke:** dış rakip-fiyat veri sağlayıcısı/API sözleşmesi + token gerekir. Ek not: canlı
  TürevRent'te de bu ekranın gerçek işlevinin sağlıklı çalıştığı doğrulanamadı (grid tek kolon
  "Araç", veri boş; kodda tek eşleşme salt-görünüm bir yorum satırı) — düşük öncelikli bloke.

### serbest_sms.aspx
- **desen:** D8
- **bloke:** SMS sağlayıcı API kimliği gerekir. `TenantSettings.SmsApiKey`/`SmsBaslik` alanları VAR
  ama hiçbir gönderim servisi/entegrasyonu bağlı değil — Twilio altyapısı bu repoda yalnız WhatsApp
  için kurulu (`TwilioWhatsAppService`), serbest-metin/zamanlı SMS için ayrı bir sağlayıcı sözleşmesi
  (veya aynı Twilio hesabına SMS kanalı ekleme kararı) kullanıcıya sorulmalı.

### xml_disardan_arac.aspx
- **desen:** D8 (gruplu bloke notu — bkz. xml_disardan_sube.aspx + xml_firma_tanim.aspx, üçü de
  aynı eksik altyapıya bağlı)
- **bloke:** harici XML/OTA broker feed'i — hangi firma(lar), feed URL/format, kimlik/credential
  kullanıcıdan gelmeden "araç eşleştirme/mutabakat" ekranı anlamsız kalır.

### xml_disardan_sube.aspx
- **desen:** D8 (gruplu bloke notu — bkz. xml_disardan_arac.aspx)
- **bloke:** aynı XML/OTA broker feed kimliği, bu kez şube/lokasyon kodları için.

### xml_firma_tanim.aspx
- **desen:** D8 (gruplu bloke notu — bkz. xml_disardan_arac.aspx)
- **bloke:** harici XML/OTA broker feed'i — hangi firma(lar), komisyon+katsayı sözleşme
  parametreleri kullanıcıdan gelmeden "firma tanımı" ekranı anlamsız kalır. Üçü (bu + araç + şube
  eşleştirme) BİRLİKTE aynı entegrasyon kararını bekler. D8 kataloğunun "XML broker" örneği tam bu
  üçlüye karşılık geliyor.

---

## YAPILMAZ (bilinçli tasarım farkı / kapsam dışı — efor yok)

### tabletyonetim.aspx
- **desen:** YAPILMAZ
- **gerekçe:** Saha tableti / dijital imza toplama DONANIM entegrasyonu (imza-tableti varsayılanları,
  RTF sözleşme şablon yükleme, aksesuar-fotoğraf şablonları). RentACar bu sürümde bulut-SaaS, saha
  donanımı filosu yönetmiyor; taklit edilmesi var olmayan bir donanım katmanını simüle eder — mimari
  gerileme. Kullanıcı ileride fiziksel saha-tablet operasyonu isterse ayrı bir roadmap kararı olarak
  açılmalı.

### turevuzak.aspx
- **desen:** YAPILMAZ
- **gerekçe:** İş kuralı içermeyen, 3. parti uzak-erişim destek aracına (TeamViewer benzeri)
  yönlendiren indirme sayfası. RentACar iş kapsamı dışı; hiçbir karşılık gerekmiyor.

*(hesap_tanimalama.aspx için de YAPILMAZ — yukarıda PR-A grubunda işlendi, tekrar sayılmadı.)*

---

## Özet tablo

| PR | Ekran sayısı | Efor | Not |
|---|---|---|---|
| PR-A (Araç Grubu+Döviz+Hesap No) | 3 (+1 alt-YAPILMAZ: hesap_tanimalama) | 1,75g | D1/D2 |
| PR-B (Filo Kiralama form+liste) | 2 | 1,5g | D2+D3 |
| PR-C1 (Lokasyon×Drop) | 2 | 3,5g | D2 |
| PR-C2 (Şube) | 1 | 3g | D2, child-tablo+birleştirme |
| PR-D (Rez Kaynağı×Tedarikçi) | 1 | 0,75g | D2, oran-mantığı Opus |
| PR-E (Rez Şartları) | 1 | 1g | D1, yeni tablo |
| PR-F (Otomatik Servisler log) | 1 | 1g | D2, yeni tablo |
| PR-G (Karşılaştırmalı Analiz) | 1 | 2g | D4 |
| PR-H (Detaylı Araç Listesi) | 1 | 2g | D3+D1 |
| PR-P1 (Genel Kasa) | 1 | 1g yapısal | PARA — Opus |
| PR-P2 (Hesaptan Para Çekme) | 1 | 1g yapısal | PARA — Opus |
| PR-P3 (Toplu Gider+Tahsilat) | 2 | 1,5g yapısal | PARA — Opus, D5 zorunlu adversarial |
| PR-P4 (Otomatik Tahsilat) | 1 | 1,5g yapısal | PARA — Opus |
| PR-P5 (XML Fiyat Aktar canlı ızgara) | 1 | 1g yapısal | PARA — Opus |
| Bloke (D8) | 5 | — | rakip-fiyat / SMS / XML-broker×3 |
| YAPILMAZ | 3 | — | hesap_tanimalama / tabletyonetim / turevuzak |

**Toplam yapısal efor (bloke hariç):** ~22,5 gün / **14 PR**
**Bloke (efor dışı, kullanıcı onayı gerekir):** 5 ekran
**YAPILMAZ:** 3 ekran

TOPLAM: 27 ekran planlandı
