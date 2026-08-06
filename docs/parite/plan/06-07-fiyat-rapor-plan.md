# Ekleme Planı — Modül 06 (Fiyat & Sigorta) + Modül 07 (Raporlar)

**Kapsam:** 24 ekran (06: 13, 07: 11).

**Desen dağılımı (birincil etiket):** D2=8 · D3=7 · D4=5 · D7=1 · D8 (bloke)=1 · YAPILMAZ=2

**Toplam efor:** ~29 gün (bloke ve YAPILMAZ hariç) — Modül 06: ~17,5 gün (12 ekran) · Modül 07: ~11,5 gün (9 ekran)
**Bloke (efora dahil değil):** 1 ekran (`kabis_raporu.aspx` — D8, KABİS entegrasyon kimliği gerekir)
**YAPILMAZ (efora dahil değil):** 2 ekran (`fiyat_kampanya_yonetimi.aspx` — düşük-güven profil, `kampanya_ara.aspx` kapsamına birleşti; `genel_rapor.aspx` — serbest pivot rapor-builder, mimari gerileme)

**Önemli bulgu (araştırma sırasında):** Modül 06'daki "canlı fazlası" alanlarının büyük kısmı repoda ZATEN VAR
ama broker/müsaitlik ekranında JOIN edilmemiş: `VehicleGroup.Sipp/Provizyon/EhliyetMinYil/GunlukKmLimiti/
AsimKmUcreti/GencSurucuYas`, `DropTanim.Ucret`, `Vehicle.SasiNo/MotorNo/AracSahibi/ZIzni`, `Reservation.Kaynak/
Provizyon/DropUcreti`. Bu nedenle taramacının verdiği blanket "PARA — OPUS'A DEVİR" kararı yapısal eylemi
engellemez — çoğu ekranda asıl iş D3 (var olanı göster) + küçük D2 (birkaç gerçek eksik alan), D8/yeni-tablo
DEĞİL. PARA — Opus etiketi yalnız gerçekten YENİ bir tutar/formül kararı gerektiren noktalarda kullanıldı.

---

# MODÜL 06 — Fiyat & Sigorta

### broker_musaitlik_listesi.aspx
- **desen:** D3
- **eylem:** `/musaitlik` (`MusaitlikArama.razor`) broker/kanal-yöneticisi görünümüne genişletilir. Ekli
  kolonlar — hepsi MEVCUT master alanlardan JOIN, yeni tablo yok: SIPP (`VehicleGroup.Sipp`), Km Limiti
  (`VehicleGroup.GunlukKmLimiti`/`AylikMaxKm`), Yaş (`VehicleGroup.SurucuMinYas`/`GencSurucuYas`), Ehliyet
  (`VehicleGroup.EhliyetMinYil`/`GencEhliyetMinYil`), Provizyon (`VehicleGroup.Provizyon`), Drop Bedeli
  (`DropTanim.Ucret`, çıkış≠dönüş şubeyse), Şube Id (`Vehicle.SubeId`). Arama formuna Rez_Kaynak dropdown
  (`ReservationSource` listesi) + Döviz seçici eklenir; `AvailabilityService.FindAvailableAsync` imzasına
  `kaynak`/`doviz` parametresi eklenir. Gerçek YENİ alan yalnız "Analiz" (Evet/Hayır) bayrağı — küçük bool
  kolon, iş kuralı yok. Not: canlı ekran GRUP/SIPP bazlı TOPLU satır gösteriyor, bizimki araç-bazlı; bu PR
  grup-bazlı özet moduna DOKUNMAZ (ayrı küçük iş, gerekirse sonraki turda).
- **dokunulacak:** `src/RentACar.Web/Components/Pages/Availability/MusaitlikArama.razor`,
  `src/RentACar.Application/Availability/AvailabilityService.cs`,
  `src/RentACar.Application/Availability/IAvailabilityRepository.cs`
- **efor:** 1 gün
- **bağımlılık:** yok

### broker_yasaklari.aspx
- **desen:** D2
- **eylem:** `BrokerYasak.AracGrupKod` ve `Bolge` tekil-string alanları virgülle-ayrılmış çoklu-değere
  çevrilir (ör. `"EKO,STD,LUX"`) — geriye uyumlu (mevcut tekil değer otomatik tek-elemanlı liste sayılır).
  `BrokerYasakList.razor` formunda tekil `<input>` yerine `VehicleGroup`/şube listesinden çoklu-seçim
  (`<select multiple>` veya checkbox grid — repoda bugün HİÇ multi-select deseni yok, bu PR ilk örneği
  kurar) kullanılır. `BrokerYasakService`'teki kapsam-eşleşme kontrolü CSV listeyi `Contains` ile arar.
  **gruplama:** "Çoklu-Seçim Kapsam Alanları" PR (`tarifeler_xml.aspx` #13 ile birlikte — aynı UI bileşeni).
- **dokunulacak:** `src/RentACar.Domain/Entities/BrokerYasak.cs`,
  `src/RentACar.Application/BrokerYasaklari/BrokerYasakService.cs`,
  `src/RentACar.Application/BrokerYasaklari/BrokerYasakInput.cs`,
  `src/RentACar.Web/Components/Pages/BrokerYasaklari/BrokerYasakList.razor`,
  `src/RentACar.Web/BrokerYasaklari/BrokerYasakEndpoints.cs`
- **efor:** 1 gün
- **bağımlılık:** yok

### doluluk_algoritma.aspx
- **desen:** D2
- **eylem:** Canlının "TEK kayıtta 10 sabit doluluk-dilimi × oran" modeli ile bizim "çok-satırlı, her satır
  bir eşik" modelimiz (`DolulukFiyatKural`) FONKSİYONEL eşdeğer — 10 ayrı kayıt = 10 kademe. Entity/motor
  GÖÇÜ gerekmez. Yalnız: (a) `DolulukFiyatList.razor`'a "aynı grup için 10 kademeyi tek formda gir" toplu-
  giriş yardımcısı (UI ergonomisi, entity değişmez); (b) `Sadece_Kendi_Subeleri` bayrağı için additive
  `SadeceKendiSubeleri bool` kolonu + `RentalQuoteEngine`'deki surge uygulama noktasında şube eşleşme
  kontrolüne bağlanır.
- **dokunulacak:** `src/RentACar.Domain/Entities/DolulukFiyatKural.cs`,
  `src/RentACar.Application/DolulukFiyat/DolulukFiyatFiles.cs`,
  `src/RentACar.Web/Components/Pages/DolulukFiyat/DolulukFiyatList.razor`,
  `src/RentACar.Application/Pricing/RentalQuoteEngine.cs`
- **efor:** 1 gün
- **bağımlılık:** yok
- **not:** mevcut %50 tavan formülü DEĞİŞMİYOR → PARA — Opus GEREKMİYOR.

### fiyat_grup_tanimlama.aspx
- **desen:** D2 (master kaydı) — kimlik-doğrulama akışı için ayrıca not
- **eylem:** Yeni `TarifeGrubu` entity'si: `Ad`, `Oran` (decimal), `KullaniciAdi`, `SifreHash` (CLAUDE.md
  PII şifreleme desenine benzer — `ISecretProtector`/hash ile at-rest korumalı, düz metin YAZILMAZ) + CRUD
  sayfası `/tarife-grubu`. `RateMatrix.Kanal`/`RentalRule.Kanal` serbest-metin alanlarına bu grubun `Kod`'u
  opsiyonel referans olarak eklenir (additive, FK değil).
  **ÖNEMLİ SINIR:** bu kaydın gerçek canlı kullanım amacı (broker'ın bu kimlikle bize XML/feed üzerinden
  girişi/doğrulaması) BAŞKA ve daha büyük bir kapsam (D7 — yeni dikey: broker feed auth). Bu PR YALNIZ
  master kaydı (Ad+Oran+kimlik alanları) planlıyor; auth-gate akışı plan dışı.
- **dokunulacak:** yeni `src/RentACar.Domain/Entities/TarifeGrubu.cs`, yeni
  `src/RentACar.Application/TarifeGrubu/*` (Input/Service/IRepository), yeni
  `src/RentACar.Web/Components/Pages/TarifeGrubu/TarifeGrubuList.razor`, migration (RLS bloğu elle)
- **efor:** 1,5 gün
- **bağımlılık:** master kaydı için yok. Gerçek broker-auth kullanımı (feed doğrulama) **D8 — bloke, hangi
  broker/XML-feed kimliği kullanılacağı önce kullanıcıya sorulmalı.**

### fiyat_kampanya_yonetimi.aspx
- **desen:** YAPILMAZ
- **gerekçe:** Extractor profili düşük güvenli (alan=3, satır=0 — muhtemelen DevExpress callback'i statik
  HTML çıkarımına yakalanmamış). Gerçek işlevi `kampanya_ara.aspx` (aşağıda) ile örtüşüyor; "Kampanya
  Yenile" aksiyonu zaten `RentalRuleService`'teki `KampanyaKodu` REPLACE mekanizmasıyla (CLAUDE.md FAZ 3)
  karşılanıyor. Ayrı bir ekran/efor açmak yerine kapsam `kampanya_ara.aspx` planına birleştirildi.

### kampanya_ara.aspx
- **desen:** D3 (arama) + D2 (5-durum yaşam döngüsü)
- **eylem:** (a) `/kira-kurallari`'ne arama formu eklenir: Kural Adı (metin), Tarih Tipi (Talep/Rezervasyon
  — bugün yok, additive `TarihTipi` enum), tarih aralığı (`GecerlilikBas/Bit` üstünden filtre). (b)
  `RentalRule.Aktif bool` → 5-durumlu `KampanyaDurum` enum'una göç: Planlandı/Aktif/Pasif/Taslak/İptal.
  Additive enum kolonu eklenir; migration'da `Aktif=true→Aktif`, `Aktif=false→Pasif` backfill; eski
  `Aktif` bool kolonu bir süre senkron bırakılır (geriye uyum). **Wire-in completeness riski:** `Aktif`
  alanını okuyan TÜM noktalar (`RentalQuoteEngine`, `RentalRuleService.ListActiveAsync` vb.) grep'lenip
  `KampanyaDurum==Aktif` kontrolüne taşınmalı — tek bir tüketim noktası unutulursa "Planlandı" durumundaki
  bir kampanya sessizce aktif kabul edilebilir.
- **dokunulacak:** `src/RentACar.Domain/Entities/RentalRule.cs`,
  `src/RentACar.Application/RentalRules/RentalRuleService.cs`,
  `src/RentACar.Application/RentalRules/RentalRuleInput.cs`,
  `src/RentACar.Web/Components/Pages/RentalRules/RentalRuleList.razor`,
  `src/RentACar.Web/RentalRules/RentalRuleEndpoints.cs`,
  `src/RentACar.Application/Pricing/RentalQuoteEngine.cs`
- **efor:** 2 gün (5-durum migration + tüm tüketim noktalarının bulunup taşınması)
- **bağımlılık:** yok

### maliyet_hesaplama.aspx
- **desen:** D2
- **eylem:** `MaliyetHesapInput`'taki tek `AylikGider` alanı yerine kalem-bazlı alanlar eklenir: Kasko,
  Trafik Sigortası, MTV, Bakım(+birim), Lastik(+kış), Araç Takip, Tescil/Plaka, Muayene-Emisyon, Yedek
  Araç(+yıllık), Yönetim Gideri(+aylık), Enflasyon(+yıllık), Banka Dosya/Diğer Masraf — her biri kendi
  yıllık/birim alanıyla; `MaliyetHesapService.Hesapla` bu kalemlerin toplamı/12'sini kullanır (eski
  `AylikGider` "Diğer" kalemine map'lenerek geriye uyum korunur). `KrediHesaplamaSekli` enum
  (EsitTaksitli/Rotatif) eklenir. `CariId`(Müşteri) + `Hazirlayan`(User) + `AracSayisi` alanları eklenir.
  **gruplama:** "Maliyet Hesaplama Derinliği" PR (aşağıdaki `maliyet_hesaplama_ara.aspx` ile birlikte).
- **dokunulacak:** `src/RentACar.Application/Pricing/MaliyetHesapModels.cs`,
  `src/RentACar.Application/Pricing/MaliyetHesapService.cs`,
  `src/RentACar.Web/Components/Pages/Pricing/MaliyetHesaplama.razor`
- **efor:** 2 gün
- **bağımlılık:** yok — **PARA — Opus:** Rotatif kredi hesap yönteminin faiz/amortisman formülü ayrı bir
  finansal-model kararı gerektirir (Eşit Taksitli formülünün klonu değil); kalem-toplama yapısı bu karara
  bağlı değil ve yine de eklenir.

### maliyet_hesaplama_ara.aspx
- **desen:** D7
- **eylem:** Bugün `/maliyet-hesapla` durumsuz bir hesap makinesi (query-string girdi/çıktı, hiçbir kayıt
  persist edilmiyor). Yeni `MaliyetTeklifi` entity'si (tenant-owned+auditable; boşluksuz `KayitNo`
  `MT-000001`; madde-7'deki tüm girdi alanları + `MaliyetHesapSonuc` çıktısı SNAPSHOT olarak saklanır —
  girdi tanımları sonradan değişse geçmiş teklif kaymaz) + `IMaliyetTeklifiRepository` +
  `MaliyetTeklifiService` (CRUD + arama: başlık/tarih/plaka/fiyat) + `/maliyet-hesapla`'ya "Kaydet" aksiyonu
  + yeni `/maliyet-teklifleri` arama/liste sayfası.
  **gruplama:** "Maliyet Hesaplama Derinliği" PR (`maliyet_hesaplama.aspx` ile birlikte).
- **dokunulacak:** yeni `src/RentACar.Domain/Entities/MaliyetTeklifi.cs`, yeni
  `src/RentACar.Application/Pricing/MaliyetTeklifi*` (Input/Service/IRepository), yeni
  `src/RentACar.Web/Components/Pages/Pricing/MaliyetTeklifiList.razor`,
  `src/RentACar.Web/Components/Pages/Pricing/MaliyetHesaplama.razor` (Kaydet butonu), migration (RLS elle)
- **efor:** 2 gün
- **bağımlılık:** yok

### sigorta_gider_ara.aspx
- **desen:** D3
- **eylem:** `/giderler` (`ExpenseList.razor`, bugün SIFIR filtre) genişletilir: (a) Tip/tarih/plaka/Ofis
  filtre formu (Tip=Sigorta ön-seçili varyant), (b) Sözleşme No + Kira Müşteri kolonları —
  `Expense.VehicleId`+`Tarih` üzerinden aktif/en-yakın `RentalContract` atfı (Araç Karnesi'nde kurulmuş
  atıf desenine benzer, ledger değil doğrudan `Rentals` sorgusu), (c) Hazır Açıklama (canonik şablonlar,
  `datalist` seç-veya-yaz — mevcut ComboBox deseni), (d) Marka/Tipi/Yakıt/Vites/Model (Vehicle join), (e)
  "Sat_Aktif_Sigorta" checkbox filtresi. **Kalan (kısmi ödeme bakiyesi) kolonu YAPISAL olarak eksik** —
  `Expense` bugün kısmi-ödeme kavramı taşımıyor (her gider oluşturulduğunda tam ödenir); bu PR'da "Kalan=0"
  sabit not-sütunu gösterilir, gerçek kısmi-ödeme mekanizması (D5 sınıfı, defter etkili) PLAN DIŞI.
- **dokunulacak:** `src/RentACar.Application/Expenses/ExpenseService.cs`,
  `src/RentACar.Application/Expenses/IExpenseRepository.cs`,
  `src/RentACar.Web/Components/Pages/Expenses/ExpenseList.razor`,
  `src/RentACar.Web/Expenses/ExpenseEndpoints.cs`
- **efor:** 1 gün
- **bağımlılık:** yok (kısmi-ödeme/Kalan hariç — ayrı D5 planı gerekir, bu PR'da yok)

### sigorta_muayene.aspx
- **desen:** D4 (rapor) + D1 (Vehicle additive alanlar)
- **eylem:** (a) `Vehicle`'a additive alanlar: `BelgeNo`, `Kimde` (araç şu an kimde), `SeyrusiferBitis
  DateTimeOffset?`, `ZIzniBitis DateTimeOffset?` (bugün `ZIzni bool` var ama bitiş tarihi yok — bool
  geriye-uyum için kalır). `SasiNo`/`MotorNo`/`AracSahibi` ZATEN VAR (sadece join eksik). (b) Yeni
  `/raporlar/sigorta-muayene` sayfası: `InsurancePolicy`(Trafik/Kasko — PoliceNo+Firma+Acenta ZATEN ayrı
  izleniyor, entity'de gap yok) + `MtvRecord` + `InspectionRecord` + Vehicle master (Marka/Tip/Yıl/Yakıt/
  Vites/Şube/Grup) TEK satırda birleştirilir; filtreler: Arac_Sahibi (Bizim/Dış), Turu
  (Muayene/Trafik/Kasko/Z-İzni/Seyrüsefer). Mevcut `/vade` ve `/regulasyon` (`RegulationList.razor`)
  DEĞİŞMEZ — bu yeni birleşik rapor onların ÜSTÜNE kurulur. D4 kuralı bu ekranda uygulanmaz (envanter/vade
  raporu, defter toplaması yok — PARA değil).
- **dokunulacak:** `src/RentACar.Domain/Entities/Vehicle.cs`,
  `src/RentACar.Infrastructure/Persistence/OrtakSorgular.cs`,
  `src/RentACar.Application/Reporting/ReportDtos.cs`, `src/RentACar.Application/Reporting/ReportService.cs`,
  `src/RentACar.Infrastructure/Persistence/Repositories/ReportRepository.cs`, yeni
  `src/RentACar.Web/Components/Pages/Reports/SigortaMuayeneRaporu.razor`, migration (Vehicle additive
  kolonlar — tablo zaten RLS'li, yeni kolon eklemek RLS bloğu gerektirmez)
- **efor:** 1,5 gün
- **bağımlılık:** yok

### sigorta_tarife_listesi.aspx
- **desen:** D2
- **eylem:** Canlının Bebek/Çocuk Koltuğu, Navigasyon, Wifi, Şarj Cihazı, Adrese Teslim, Kış Lastiği,
  Üyelik/İptal Bedeli gibi kalemleri `CoverageProductType` enum'una YENİ değerler eklenerek modellenir
  (entity şeması değişmez — her biri kendi `CoverageProduct` satırı olur, tek-tip-tek-satır deseni zaten
  örtüşüyor). GERÇEK yapısal eksik: **Km Paket 1-4** (kilometre-bazlı kademe) — yeni `KmPaket` alt-tablosu
  (bir `CoverageProduct` "KM_PAKET" tipinde birden çok kademe satırı: `KmMin`/`KmMax`/`Ucret`) eklenir;
  "Listeleme Yöntemi: Sadece Parktakiler/Tüm Gruplar" için additive `ListelemeYontemi` enum kolonu.
- **dokunulacak:** `src/RentACar.Domain/Entities/CoverageProduct.cs`,
  `src/RentACar.Domain/Enums/CoverageProductType.cs`, yeni `src/RentACar.Domain/Entities/KmPaket.cs`,
  `src/RentACar.Application/CoverageProducts/*` (Input/Service/IRepository),
  `src/RentACar.Web/Components/Pages/CoverageProducts/CoverageProductList.razor`
- **efor:** 1,5 gün
- **bağımlılık:** yok

### tarifeler.aspx
- **desen:** D2
- **eylem:** Canlıda her gün-kademesinin (7 kademe) KENDİ km limiti + km-aşım-ücreti var. Bizim `RateCard`
  zaten kademe-bazlı (her `MinGun/MaxGun` aralığı = ayrı satır) — YAPISAL göç GEREKMEZ, yalnız her satıra
  `KmLimit`+`KmAsimUcreti` additive kolonları eklenir. `SCDW_Dahil`/`MiniHasarDahil`/`HirsizlikDahil`/
  `SCDW_Zorunlu` bool bayrakları tarife satırına eklenir (bugün `/sigorta-urunleri` + kira formunda ayrı;
  additive, deftere yazmaz). `Gosterme` (gizle) bool + `TarifeGrubu` seçici (yukarıdaki #4'ün FK'si,
  opsiyonel) eklenir.
- **dokunulacak:** `src/RentACar.Domain/Entities/RateCard.cs`,
  `src/RentACar.Application/Pricing/RateCardInput.cs`, `src/RentACar.Application/Pricing/RateCardService.cs`,
  `src/RentACar.Web/Components/Pages/Pricing/RateCardList.razor`,
  `src/RentACar.Web/Pricing/RateCardEndpoints.cs`, `src/RentACar.Application/Pricing/RentalQuoteEngine.cs`
  (km-aşım + teminat-dahil okuma noktası eklenir)
- **efor:** 2 gün
- **bağımlılık:** yok — **PARA — Opus:** km-aşım ücretinin fiyat motorunda TAM olarak nasıl uygulanacağı
  (kira toplamına ne zaman/nasıl eklenir — mevcut `RentalContract.FazlaKmBedeli` ile ilişkisi) formül
  kararı gerektirir; kolon ekleme kendisi bu karara bağlı değil.

### tarifeler_xml.aspx
- **desen:** D2
- **eylem:** `RateMatrix`'e iki additive alan: `Turu` enum (Fiyat/Kampanya — salt-etiket, motor davranışı
  DEĞİŞMEZ) + `KiraSuresi int?` (Max Kira Kapsamı — `RentalQuoteEngine`'de gün sayısı bu sınırı aşarsa
  matris satırı ELENİR, `RateCard.Covers` benzeri guard). `Ozel_Lkasyon` çoklu-seçim: mevcut tekil
  `Lokasyon` alanı virgülle-ayrılmış çoklu-değere çevrilir (`broker_yasaklari.aspx` #2 ile AYNI desen/UI
  bileşeni).
  **gruplama:** "Çoklu-Seçim Kapsam Alanları" PR (`broker_yasaklari.aspx` ile birlikte).
- **dokunulacak:** `src/RentACar.Domain/Entities/RateMatrix.cs`,
  `src/RentACar.Application/RateMatrices/RateMatrixInput.cs`,
  `src/RentACar.Application/RateMatrices/RateMatrixService.cs`,
  `src/RentACar.Application/RateMatrices/RateMatrisCozumleme.cs`,
  `src/RentACar.Web/Components/Pages/RateMatrices/RateMatrixList.razor`,
  `src/RentACar.Web/RateMatrices/RateMatrixEndpoints.cs`
- **efor:** 1 gün
- **bağımlılık:** yok

---

# MODÜL 07 — Raporlar

**Not:** `/raporlar/kasa-banka` ve `/raporlar/servis-ozet` D9 zaten KAPANDI (menüye eklendi) — bu modülün
11 ekranından hiçbirine karşılık gelmediği için burada tekrar planlanmadı.

### arac_genel_durumu_grafik.aspx
- **desen:** D4
- **eylem:** `/raporlar/filo` (`FiloDoluluk.razor`, bugün TEK satır tenant-geneli özet, filtresiz) şube
  kırılımlı hale getirilir: her şube için Filo/Boş/Kirada/Bakımda/Doluluk% + Dönecekler/Çıkışlar/Çıkacaklar/
  Giden Rez./Dönüşler/Baf/Satılık kolonları. `GetFleetUtilizationAsync` yanına yeni
  `GetFleetUtilizationBySubeAsync` eklenir (mevcut tek-satır API GERİYE UYUMLU kalır). "Baf" sayısı
  `Baf.Durum==Acik` üzerinden, "Satılık" `Vehicle.FiloDurum==IkinciElSatis` üzerinden sayılır (ikisi de
  MEVCUT alan — yeni tablo/kolon gerekmez). "Dönecekler/Çıkacaklar" = bugün+N gün penceresinde
  BasTar/BitTar'ı olan `Rentals` sayımı.
- **dokunulacak:** `src/RentACar.Application/Reporting/ReportDtos.cs`,
  `src/RentACar.Application/Reporting/ReportService.cs`,
  `src/RentACar.Infrastructure/Persistence/Repositories/ReportRepository.cs`,
  `src/RentACar.Web/Components/Pages/Reports/FiloDoluluk.razor`
- **efor:** 1,5 gün
- **bağımlılık:** yok

### bos_arac_raporu.aspx
- **desen:** D4 (mevcut rapor; eklenen kısım D3-nitelikli — filtre+kolon)
- **eylem:** `/raporlar/arac-durum-takip` (`AracDurumTakip.razor`) Ofis (şube) filtresi + Tarih_Listesi
  kapsam seçici (bugün sabit from/to) + Toplam Baf kolonu ile genişletilir. "Toplam Kira" kolonu zaten
  "Dolu" adıyla mevcut (yeniden adlandırma değil, dokümantasyon notu yeterli). `GetAracDurumTakipRowsAsync`
  `Vehicle.SubeId` üzerinden şube filtresi parametresi alır.
  **gruplama:** "Rapor Filtre Derinliği Serisi" PR (aşağıdaki `bos_km_detay`/`gunraporu`/
  `periyodik_servis_raporu`/`rezervasyon_kaynak_raporu` ile birlikte — hepsi aynı desen: mevcut tek rapor
  sayfasına şube/tarih filtresi + join kolonu eklemek).
- **dokunulacak:** `src/RentACar.Application/Reporting/ReportDtos.cs`,
  `src/RentACar.Application/Reporting/ReportService.cs`,
  `src/RentACar.Infrastructure/Persistence/Repositories/ReportRepository.cs`,
  `src/RentACar.Web/Components/Pages/Reports/AracDurumTakip.razor`
- **efor:** 1 gün
- **bağımlılık:** yok

### bos_km_detay.aspx
- **desen:** D3 (düşük güvenle — bkz. not)
- **eylem:** `/raporlar/km-detay` (`KmDetay.razor`) kolonlarına Vehicle join'i (Marka/Vites/Yakıt) + Rental
  (BasTar/BitTar/İşlem Türü) join'i eklenir — `GetKmDetayRowsAsync` zaten Vehicle-plaka join'i yapıyor,
  genişletilir. **UYARI:** canlının gerçek amacı (boşta-geçen-sürede km/fraud kontrolü) bizim ekranın
  konusuyla (kira KM aşım faturalama satırı) FARKLI OLABİLİR — K3 %29 sınırda, ad benzerliği tuzağı
  şüphesi taşıyor. Bu PR öncesi bir canlı-tarama turunda ekranın gerçek amacı doğrulanmalı; farklıysa bu
  madde D7 (yeni dikey: park-halinde-km-takip) olarak yeniden açılmalı.
  **gruplama:** "Rapor Filtre Derinliği Serisi" PR.
- **dokunulacak:** `src/RentACar.Application/Reporting/ReportDtos.cs`,
  `src/RentACar.Infrastructure/Persistence/Repositories/ReportRepository.cs`,
  `src/RentACar.Web/Components/Pages/Reports/KmDetay.razor`
- **efor:** 0,5 gün (doğrulama sonrası artabilir)
- **bağımlılık:** yok (önce canlı-tarama doğrulaması ÖNERİLİR)

### doluluk_grafik.aspx
- **desen:** D4
- **eylem:** `/raporlar/doluluk` (`DolulukRaporu.razor`, bugün tek dönem/tek yüzde kartı) gün-kırılımlı
  satır tabloya + Kira Doluluk/Rez Doluluk ayrımına + Şube/Araç Grubu/Rezervasyon Kaynağı "Karsilastir"
  karşılaştırma moduna genişletilir. Yeni `GetDolulukGunlukAsync(from,to,boyut)` eklenir (mevcut
  `GetDolulukAsync` API'si korunur); gün-bazlı `OverlapDays` mantığı ortak private helper'a çıkarılır (kod
  tekrarı yasak).
- **dokunulacak:** `src/RentACar.Application/Reporting/ReportDtos.cs`,
  `src/RentACar.Application/Reporting/ReportService.cs`,
  `src/RentACar.Infrastructure/Persistence/Repositories/ReportRepository.cs`,
  `src/RentACar.Web/Components/Pages/Reports/DolulukRaporu.razor`
- **efor:** 1,5 gün
- **bağımlılık:** yok

### extralar_raporu.aspx
- **desen:** D3
- **eylem:** `/raporlar/ek-hizmet` (`EkHizmetRaporu.razor`, bugün Ad'a göre TOPLU özet) satır-bazlı detay
  listesine genişletilir: Kayit No, Baş./Bit. Zaman, Plaka, RA No (`Rental.SozlesmeNo`), Müşteri, Rez.
  Kaynağı, Ç. Ofisi, İlk Tahsilat + Rapor_Turu(Kira/Rezervasyon)/Icerik/Kime_Ait/Islem_Sube filtreleri.
  "Kiraya Veren/Teslim Eden/Ek Hizmet Satan" için `RentalAddOn`'a additive `PersonelId` kolonu eklenir
  (bugün satan personel hiç izlenmiyor). Yeni gruplamasız-ham-satır repo metodu eklenir (mevcut özet API
  `GetEkHizmetSalesRowsAsync` korunur). NOT: "Döviz/TL Fiyat" kolonları — `RentalAddOn` tutarları zaten
  TRY-baz saklanıyor (Kur alanı yok, tasarım kararı); canlının döviz gösterimini birebir taklit etmek bu
  PR kapsamında değil, "TL Fiyat" zaten karşılanıyor.
- **dokunulacak:** `src/RentACar.Domain/Entities/RentalAddOn.cs` (additive `PersonelId`),
  `src/RentACar.Application/Reporting/ReportDtos.cs`, `src/RentACar.Application/Reporting/ReportService.cs`,
  `src/RentACar.Infrastructure/Persistence/Repositories/ReportRepository.cs`,
  `src/RentACar.Web/Components/Pages/Reports/EkHizmetRaporu.razor`
- **efor:** 1,5 gün
- **bağımlılık:** yok

### gelir_tablosu.aspx
- **desen:** D4
- **eylem:** `/raporlar/karlilik` (`Karlilik.razor`/`GetKarlilikAsync`) çok-boyutlu genişletilir: Doluluk/
  Araç Başı Gelir/Ortalama Fiyat (mevcut `AracKpiDto` alanları zaten var, `KarlilikSatirDto`'ya bağlanır),
  RentTo ayrımı (Rental/Reservation Durum filtresi), SIPP kodu (`VehicleGroup.Sipp` join), Rezervasyon
  Kaynağı kırılımı, Cari Bilgi/Bakiye (`GetCariBalancesAsync` ile join), Otopark (`Vehicle.SubeId`). Kdv
  Durum modu (Kdvsiz/Kdv Dahil) SALT GÖSTERİM anahtarı — tutar formülü değişmez. Maliyet kırılımı (Ana/
  Yönetim Maliyeti) ve Potansiyel gelir kolonları **referans/karşılaştırma niteliğinde ayrıca gösterilir,
  P&L toplamına KATILMAZ** — D4 kuralı (P&L yalnız defterden okunur, kaynak-varlık tutarı asla toplanmaz)
  bu kolonlarda da korunur; `Vehicle.AylikMaliyet`/`FiloYonetimMaliyeti` master-alan bazlı ayrı bir
  "referans maliyet" satırı olarak sunulur, defter Gider toplamıyla KARIŞTIRILMAZ.
- **dokunulacak:** `src/RentACar.Application/Reporting/ReportDtos.cs`,
  `src/RentACar.Application/Reporting/ReportService.cs`,
  `src/RentACar.Infrastructure/Persistence/Repositories/ReportRepository.cs`,
  `src/RentACar.Web/Components/Pages/Reports/Karlilik.razor`
- **efor:** 3 gün (40 kolonun ~15'i eklenir — modülün en büyük tek genişlemesi)
- **bağımlılık:** yok yapısal olarak — **PARA — Opus:** "Ana Maliyet/Yönetim Maliyeti"nin defter P&L'iyle
  nasıl YAN YANA gösterileceği (mutabık kalması BEKLENMEDİĞİ açıkça mı işaretlenecek) ve "Potansiyel gelir"
  formülünün hangi tarife kaynağından (RateCard mı RateMatrix mi) türetileceği ayrı kararlar; kolon iskeleti
  bu kararlara bağlı değil ve yine planlanabilir.

### genel_rapor.aspx
- **desen:** YAPILMAZ
- **gerekçe:** Kullanıcının kendi alan/pivot tanımlayabildiği genel bir rapor-oluşturucu (custom report
  builder — "Alan Ekle/Düzenle", pivot tablo `DataTableJson`, kayıtlı rapor, "Tümünü Sil/Aktar"). Bizim
  mimarimiz sabit-şema tenant-owned tablolar + sabit rapor sayfaları üzerine kurulu (CLAUDE.md §2 "temiz
  mimari, katmanlı"); kullanıcının serbestçe alan/pivot tanımlayabildiği bir BI-motoru inşa etmek kendi
  başına haftalar sürecek ayrı bir kategori (D6/D7'den daha büyük), ROI düşük — repo zaten 20+ sabit rapor
  sunuyor. Taklit edilmesi mimari gerileme olur (talimat madde 6 örneğiyle birebir örtüşen durum). Özel bir
  kırılım isteği gelirse mevcut raporlardan birine (örn. Karlılık) yeni boyut eklemek (D4) yeterli.

### gunraporu.aspx
- **desen:** D3
- **eylem:** `/raporlar/gunluk` (`GunlukFaaliyet.razor`) Islem_Sube (şube) filtresi eklenir —
  `GetGunlukFaaliyetAsync` bugün şubesiz; Rentals/Reservations sorgularına `SubeId` filtresi eklenir
  (`BranchScope.Effective` ile aynı desen). Canlı kolon/kart içeriği doğrulanamadığından (kural 4:
  DOĞRULANAMADI), bu ekleme mevcut kartların (yeniRez/yeniKira/cikis/donus/tahsilat/fatura) şube-filtreli
  halidir; içerik canlıyla örtüşmüyorsa sonraki canlı-tarama turunda yeniden değerlendirilmeli.
  **gruplama:** "Rapor Filtre Derinliği Serisi" PR.
- **dokunulacak:** `src/RentACar.Application/Reporting/ReportDtos.cs`,
  `src/RentACar.Application/Reporting/ReportService.cs`,
  `src/RentACar.Infrastructure/Persistence/Repositories/ReportRepository.cs`,
  `src/RentACar.Web/Components/Pages/Reports/GunlukFaaliyet.razor`
- **efor:** 0,5 gün
- **bağımlılık:** yok

### kabis_raporu.aspx
- **desen:** D8
- **eylem:** yok (kod yazılmaz)
- **bağımlılık:** **bloke — KABİS (kayıp/çalıntı araç sorgu sistemi) entegrasyon kimliği/API erişimi
  gerekir.** Repoda "Kabis"/"KABİS" terimi hiçbir dosyada geçmiyor (sıfır iz). Açmadan önce kullanıcıya
  sorulmalı.

### periyodik_servis_raporu.aspx
- **desen:** D3
- **eylem:** `/raporlar/periyodik-servis` (`PeriyodikServis.razor`, bugün SIFIR filtre) araç arama (plaka),
  Ofis (şube) filtresi, Durum (aktif/pasif araç), Uyarı eşiği seçimi (kalan-km eşiği kullanıcı seçebilir,
  bugün sabit) eklenir + Marka/Tipi/Model/Yakıt Türü/Vites/Şube/İşlem Tarihi/İşlem KM kolonları.
  **DİKKAT:** `OrtakSorgular.PeriyodikServisAsync` `FiloBildirimUretici` (bakım-km bildirimi) ile
  PAYLAŞILAN sorgu (O12a deseni) — değişiklik dikkatli yapılmalı, bildirim üretimi regresyona uğramamalı
  (ayrı test: bildirim sayısı değişmemeli, yalnız rapor sayfası filtre/kolon kazanmalı).
  **gruplama:** "Rapor Filtre Derinliği Serisi" PR.
- **dokunulacak:** `src/RentACar.Infrastructure/Persistence/OrtakSorgular.cs`,
  `src/RentACar.Application/Reporting/ReportDtos.cs`, `src/RentACar.Application/Reporting/ReportService.cs`,
  `src/RentACar.Web/Components/Pages/Reports/PeriyodikServis.razor`
- **efor:** 1 gün (paylaşılan sorgu değişikliği — ekstra regresyon dikkati)
- **bağımlılık:** yok

### rezervasyon_kaynak_raporu.aspx
- **desen:** D3
- **eylem:** `/raporlar/rezervasyon-kaynak` (`RezervasyonKaynak.razor`) Ofis (şube) + Gruplar (araç grubu)
  filtresi + Tarih_Listesi (Kayıt/Çıkış/Dönüş tarihine göre seçim — bugün sabit BasTar) eklenir. Döviz
  kırılımı: `Reservation` entity'sinde HİÇ döviz/kur alanı yok (grep doğrulandı) — additive `Doviz string?`
  kolonu eklenir, `GetRezervasyonKaynakRowsAsync` döviz bazında GROUP BY genişletilir. **Toplamlar HÂLÂ tek
  baz para (₺) kalır** — döviz kırılımı EK bilgi satırı, mevcut Genel Toplam formülü DEĞİŞMEZ → PARA — Opus
  gerekmiyor.
  **gruplama:** "Rapor Filtre Derinliği Serisi" PR.
- **dokunulacak:** `src/RentACar.Domain/Entities/Reservation.cs` (additive `Doviz`),
  `src/RentACar.Application/Reporting/ReportDtos.cs`, `src/RentACar.Application/Reporting/ReportService.cs`,
  `src/RentACar.Infrastructure/Persistence/Repositories/ReportRepository.cs`,
  `src/RentACar.Web/Components/Pages/Reports/RezervasyonKaynak.razor`
- **efor:** 1 gün
- **bağımlılık:** yok

---

TOPLAM: 24 ekran planlandı
