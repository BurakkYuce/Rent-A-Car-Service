# 03-cari-crm + 04-kira-rezervasyon — Ekleme Planı

**22 ekran** (12 cari-crm + 10 kira-rezervasyon).

**Desen dağılımı:** D1=3 · D2=4 · D3=9 · D4=1 · D6=1 · D7=4 (D8 ve YAPILMAZ alt-notlar bu sayıma
girmez — ilgili ekranların içinde ayrıca işaretli).

**TOPLAM YAPISAL EFOR (bloke hariç): ~35,25 gün** (~7 hafta / tek geliştirici). Ayrıca:
- **8 ekranda PARA — Opus kararı bekleyen alt-parça** var (yapısal iş yukarıdaki efora dahil;
  tutar/formül kararı hariç).
- **1 ekranda (rezervasyon_kaynagi) D8 bloke alt-parça** var — efor yazılmadı, kimlik gerekir.
- **3 yerde kısmi YAPILMAZ** kararı var (kiralama.aspx ×2, rezervasyon.aspx ×1, kira_listesi.aspx ×1) —
  gerekçeli, aşağıda.

Metodoloji notu: her ekran için önce ilgili entity/repository/razor dosyası okunup hangi alanların
**zaten var ama yüzeye çıkmadığı** (→ D3/D6-devamı) ile **gerçekten hiç yok** (→ D1/D2/D7) olduğu
ayrıştırıldı. Bu ayrım, modül raporlarındaki "canlı fazlası" listelerinin ilk okumada ima ettiğinden
önemli ölçüde daha küçük bir gerçek açığa işaret ediyor (örn. Reservation.Ota* 8 alanı, RentalContract'ın
~30 "bilgi amaçlı" kolonu zaten yazılmış).

---

# Modül 03 — cari-crm

## Gruplama: "Cari alan+kolon derinliği" PR

Üç ekran de aynı yığını paylaşıyor: `src/RentACar.Domain/Entities/Customer.cs`,
`src/RentACar.Application/Customers/{CustomerRow,CustomerFilter}.cs`,
`src/RentACar.Web/Components/Pages/Customers/{CustomerList,CustomerEdit}.razor`,
`src/RentACar.Infrastructure/Persistence/{Configurations/CustomerConfigs.cs,Repositories/CustomerRepository.cs}`.
Tek PR'da birlikte gider (üç ekranın kolon/alan boşlukları örtüşüyor, ayrı ayrı PR israf).

### musteri_kayit.aspx
- **desen:** D1
- **eylem:** `Customer` entity'sine 39 yeni nullable alan eklenir (migration `AddCustomerFormDerinlik2`):
  `TcDogrulama` (bool?), `Ulke`, `Tel2`, `OzelKod`, `EntegrasyonKodu`, `Aciklama`,
  `FaturaAdresFarkli` (bool), `RiskIzin`, `BayiKomisyon` (decimal?), `FaturaTekSatir` (bool),
  `DogumGunuTakip` (bool), 6× KVKK-anonimleştirme bayrağı (`MusteriKartiAnonim`, `KiraAnonim`,
  `RezervasyonAnonim`, `FaturaAnonim`, `CariAnonim`, `CezaAnonim` — bool), `DogumYeri`,
  `PasaportTarihi`, `PasaportYeri`, `KurumsalNo`, `Sifre` (portal şifresi — hash'lenir, düz metin
  YAZILMAZ), `UyariSerbest` (mevcut `UyariNedeni`'nden ayrı serbest metin), `WebIndirim` (decimal?),
  `KaraZamani` (DateTimeOffset?), `IslemSubeId` (Guid? — Branch FK), `BakiyeGor` (bool),
  `TevkifatKodu`, `AracVerilmez` (bool), `YasEhliyetSerbest` (bool), `FaturaKiralayanIsim`,
  `MerkezKurumsal` (bool), `Broker` (bool), `FindexZorunlu` (bool), `IsAdresi`, `IsTelefonu`,
  `FirmaId` (Guid? — bağlı firma cari'ye self-FK), `KayitliIl`, `KayitliIlce`, `MahalleKoy`,
  `SeriNo`, `CiltNo`, `AileSira`, `SiraNo`. **Ayrıca:** var olan `Adres` alanı `CustomerEdit.razor`
  formunda hiç render edilmiyor — `<label>Adres<input name="adres" value="@_c.Adres" /></label>`
  eklenir (tek satır, D3 karakterinde ama aynı PR'da).
- **dokunulacak:** `src/RentACar.Domain/Entities/Customer.cs`,
  `src/RentACar.Infrastructure/Persistence/Configurations/CustomerConfigs.cs`,
  `src/RentACar.Application/Customers/CustomerInput.cs`,
  `src/RentACar.Web/Components/Pages/Customers/CustomerEdit.razor`, yeni migration
  `src/RentACar.Infrastructure/Migrations/`
- **efor:** 1,5 gün (39 basit alan — iş kuralı yok, D1 tabanı ~0,5g/5 alan yerine field-yoğun
  form nedeniyle sapıldı)
- **bağımlılık:** yok
- **gruplama:** Cari alan+kolon derinliği PR

### musteri_genel_liste.aspx
- **desen:** D3
- **eylem:** `CustomerRow` projeksiyonuna **zaten Customer entity'sinde var olan** alanlar eklenir:
  `MusteriTemsilcisi`, `DogumTarihi`, `PasaportNo` (maskeli — `PasaportNoEnc`'ten decrypt, KVKK-gate),
  `VadeGun`, `UyariNedeni`, `Gsm2` (Tel2), `Adres`, `Il`/`Ilce` (zaten var, gösterilmiyor); repo
  `CustomerRepository.SearchRowsAsync` JOIN'i genişletilir; `CustomerList.razor` grid'ine bu kolonlar
  + `İşlem Tarihi` aralık filtresi + ayrı `Soyad` arama alanı eklenir. `Bakiye` kolonu için
  `/cariler/{id}/detay`'daki mevcut bakiye hesaplama sorgusu (defterden) satır-bazlı JOIN'e taşınır
  (N+1 riskine karşı tek toplu sorgu — `AccountLedgerEntry` GROUP BY CariId).
- **dokunulacak:** `src/RentACar.Application/Customers/CustomerRow.cs`,
  `src/RentACar.Application/Customers/CustomerFilter.cs`,
  `src/RentACar.Infrastructure/Persistence/Repositories/CustomerRepository.cs`,
  `src/RentACar.Web/Components/Pages/Customers/CustomerList.razor`
- **efor:** 0,5 gün
- **bağımlılık:** yok (Entegrasyon Kodu/Özel Kod/A-Müşteri/Uyarı Tarih/Bakiye Şube kolonları
  musteri_kayit.aspx'teki yeni alanlara bağlı — o PR ile aynı anda gider)
- **gruplama:** Cari alan+kolon derinliği PR

### musteri_listesi.aspx
- **desen:** D3
- **eylem:** `CustomerFilter`'a `Pasif` (bool?) alanı eklenir (entity'de zaten var, filtre modelinde
  YOK — kontrol edildi) + `CustomerList.razor` filtre barına "Durum (Aktif/Pasif/Hepsi)" select
  eklenir. Kolon: Tel2(Gsm2)/Mail/Pasaport No/Ülke (musteri_genel_liste ile aynı JOIN'den), Toplam
  Gün/Ortalama Günlük/Ortalama Km/Toplam Hizmet (RentalContract GROUP BY MusteriId agregaları —
  mevcut Ciro/KiraAdet hesaplamasının yanına eklenir), Önem (Sinif alanı zaten var, kolon eksik),
  Özel Kod (musteri_kayit PR'ındaki yeni alan), Bakiye (yukarıdaki JOIN paylaşılır).
- **dokunulacak:** `src/RentACar.Application/Customers/CustomerFilter.cs`,
  `src/RentACar.Application/Customers/CustomerRow.cs`,
  `src/RentACar.Infrastructure/Persistence/Repositories/CustomerRepository.cs`,
  `src/RentACar.Web/Components/Pages/Customers/CustomerList.razor`
- **efor:** 0,5 gün (musteri_genel_liste ile aynı repo/liste dosyasında, ortak JOIN paylaşılır)
- **bağımlılık:** yok
- **gruplama:** Cari alan+kolon derinliği PR

---

## Gruplama: "Personel alan+liste derinliği" PR

### personel_kayit.aspx
- **desen:** D1
- **eylem:** `Personel` entity'sine (migration `AddPersonelDerinlik`) eklenir: `Adres` (ev adresi),
  `EvTelefonu`, `IsTelefonu`, `CepTel` (SurucuBelgeNo dışında ayrı — canlıda `Cep_Tel` var),
  `Referans`, `Aciklama`, `SSinifi` (sürücü sınıfı), `SVerilisTarihi`, `SVerilisYeri`, `MailAdresi`,
  `DogumTarihi`, `DogumYeri`, `BabaAdi`, `AnaAdi`, `Il`, `Ilce`, `Mahalle`, `CiltNo`, `AileSiraNo`,
  `SiraNo`, `KanGrubu`, **`GorevTanimi`** (İç Ekip/Yönetici/Operasyon — en büyük iş-değeri taşıyan
  alan, personelin unvan/departman ayrımı bugün hiç yok), `RacTabletNo`. `PersonelInput`/
  `PersonelDetail` bu alanlarla genişletilir; form (`PersonelList.razor`) alanları eklenir.
- **dokunulacak:** `src/RentACar.Domain/Entities/Personel.cs`,
  `src/RentACar.Application/Personnel/PersonelModels.cs`,
  `src/RentACar.Application/Personnel/PersonelService.cs`,
  `src/RentACar.Infrastructure/Persistence/Repositories/PersonelRepository.cs`,
  `src/RentACar.Web/Components/Pages/Personnel/PersonelList.razor`, yeni migration
- **efor:** 1 gün
- **bağımlılık:** yok (`Kimlik_No`/TC zaten `TcKimlikEnc` şifreli var — çakışma yok; `Maas` zaten var)
- **gruplama:** Personel alan+liste derinliği PR

### personel_listesi.aspx
- **desen:** D3
- **eylem:** `PersonelList.razor`'a arama kutusu (Ad/Soyad/Kod contains) + "Aktif" checkbox filtresi
  eklenir (şu an `Personel.ListAsync()` filtresiz çağrılıyor); liste kolonlarına Cep Tel, Mail Adresi,
  TC Kimlik (maskeli — `***-**-1234` formatında, PII gate Admin-only zaten mevcut), Rac Tablet No
  eklenir (yukarıdaki D1 PR'ının alanları).
- **dokunulacak:** `src/RentACar.Application/Personnel/IPersonelRepository.cs`,
  `src/RentACar.Infrastructure/Persistence/Repositories/PersonelRepository.cs`,
  `src/RentACar.Web/Components/Pages/Personnel/PersonelList.razor`
- **efor:** 0,5 gün
- **bağımlılık:** yok
- **gruplama:** Personel alan+liste derinliği PR

---

## Gruplama: "Hukuk dosyası derinliği" PR

### hukuk_birimi.aspx
- **desen:** D1
- **eylem:** `HukukDosya` entity'sine `FaturaNoTemp`, `Avukat2Ad`, `Avukat2Tel`, `Avukat2Mail`,
  `AvukatTel` (Avukat-1 telefon — canlıda 1. avukatın da iletişimi yok), `AvukatMail` eklenir
  (migration `AddHukukDosyaDerinlik`); form (`HukukList.razor`) alanları eklenir.
- **para-karar: PARA — Opus.** Canlıda **Tahsilat** (kısmi ödeme) ve türetilmiş **Kalan** bakiyesi
  var; bizde `HukukDosya.Tutar` tek alan (kod yorumunda "bilgilendirme amaçlı, deftere postlamaz").
  Yapısal seçenek iki koldan biri: (a) `Tahsilat` alanı BİLGİ amaçlı eklenir (Tutar gibi deftere
  yazmaz, `Kalan = Tutar - Tahsilat` hesaplanır) — düşük risk, mevcut "postlamaz" tasarımıyla tutarlı;
  (b) Tahsilat gerçek cari tahsilatına bağlanır (defter postlar) — bu durumda D5'e yükselir,
  zorunlu adversarial inceleme gerekir. Hangisi seçilirse seçilsin alan EKLENMESİ gerektiği kesin;
  postlama kararı Opus'a.
- **dokunulacak:** `src/RentACar.Domain/Entities/HukukDosya.cs`,
  `src/RentACar.Application/Legal/HukukDosyaInput.cs`,
  `src/RentACar.Application/Legal/HukukDosyaService.cs`,
  `src/RentACar.Web/Components/Pages/Legal/HukukList.razor`, yeni migration
- **efor:** 0,75 gün (yapısal alan ekleme; Tahsilat/Kalan kararına göre +0,5-2 gün ek — Opus sonrası)
- **bağımlılık:** yok (Tahsilat/Kalan'ın defter-postlama biçimi Opus kararına bağlı)
- **gruplama:** Hukuk dosyası derinliği PR

### hukuk_islem_listesi.aspx
- **desen:** D3
- **eylem:** `HukukList.razor` liste bölümüne filtre barı eklenir: Ad Soyad (CariId üzerinden JOIN),
  Tarih aralığı, Fatura No (yukarıdaki `FaturaNoTemp`), Dosya No (contains arama — şu an tam liste,
  arama yok). Liste kolonuna Müşteri adı eklenir (CariId var, isim JOIN edilip gösterilmiyor).
  `/hukuk` için Excel/CSV/PDF export ucu eklenir (envanterde `export=hayır`).
- **para-karar: PARA — Opus.** Tahsilat/Kalan kolonu hukuk_birimi'ndeki karara bağlı.
- **dokunulacak:** `src/RentACar.Application/Legal/IHukukDosyaRepository.cs`,
  `src/RentACar.Infrastructure/Persistence/Repositories/HukukDosyaRepository.cs`,
  `src/RentACar.Web/Components/Pages/Legal/HukukList.razor`,
  `src/RentACar.Web/Reports/ListExportCatalog.cs` (yeni `HukukDosyalari` export tablosu)
- **efor:** 0,5 gün
- **bağımlılık:** yok
- **gruplama:** Hukuk dosyası derinliği PR

---

## Bağımsız (D4 rapor)

### musteri_crm.aspx
- **desen:** D4
- **eylem:** `CrmAnaliz.razor`'daki "Müşteri Segment" tablosuna filtre barı eklenir: Tarih Listesi
  (Baş./Bit. — kira BasTar aralığı), Kiralama Adeti eşiği (`KiraSayisi >= N`), Rez. Kaynağı (Kaynak
  eşleşmesi), Çıkış Şube (`CikisSubeId` — zaten `BranchScope` altyapısı var, filtre olarak bağlanır).
  `MusteriSegmentRow`'a Müşteri Mail/Tel (Customer.Email/CepTel — zaten entity'de var, projeksiyona
  eklenmemiş), Ortalama Kira Bedeli (`ToplamCiro / KiraSayisi`), Ortalama KM (RentalContract
  DonusKm-CikisKm ortalaması), Doğ.Tar (Customer.DogumTarihi), İlk Kira Zamanı (MIN BasTar), Hizmet
  Bedeli (RentalAddOn toplamı, kira dışı) eklenir. `GetMusteriSegmentRowsAsync` sorgusu genişletilir.
- **dokunulacak:** `src/RentACar.Application/Reporting/ReportDtos.cs` (`MusteriSegmentRow`),
  `src/RentACar.Application/Reporting/IReportRepository.cs`,
  `src/RentACar.Infrastructure/Persistence/Repositories/ReportRepository.cs`,
  `src/RentACar.Web/Components/Pages/Crm/CrmAnaliz.razor`
- **efor:** 1 gün
- **bağımlılık:** yok

---

## Bağımsız (D7 yeni dikeyler)

### anket_listesi.aspx
- **desen:** D7
- **eylem:** Canlı ekran **sözleşmeye bağlı 8-soru yapılandırılmış** çıkış/dönüş anketi; bizim
  `Anket` entity'si (Id/CariId/Puan/Yorum/Tarih/Kaynak) sözleşmeden bağımsız genel geri bildirim.
  Ayrı bir dikey gerekir: `AnketV2` (veya `Anket`'e additive alan seti) — `RentalId` (Guid? FK),
  `AnketTuru` enum (Cikis/Donus), `Durum` enum (Yapildi/Yapilmadi), `CikisOfisi`, ve **8 soru**
  için `List<AnketCevap>` child-tablo (SoruNo int, Soru string, Cevap string, Aciklama string?) —
  düz 24 kolon yerine normalize edilmiş child (CLAUDE.md §5 desenine daha uygun, gelecekte soru
  sayısı değişirse migration gerekmez). Yeni `/anketler` filtre barı: Cari, Anket Türü, Durum, Tarih
  aralığı, Çıkış Ofisi.
- **dokunulacak:** `src/RentACar.Domain/Entities/Anket.cs` (+yeni `AnketCevap.cs`),
  `src/RentACar.Application/Crm/AnketService.cs`, yeni `IAnketRepository`/`AnketRepository`,
  `src/RentACar.Web/Components/Pages/Crm/AnketList.razor`, yeni migration (RLS bloğu dahil)
- **efor:** 4 gün
- **bağımlılık:** yok

### sikayet_listesi.aspx
- **desen:** D7
- **eylem:** Canlı ekran araç **teslim/dönüş** sürecine bağlı şikayet-değerlendirme; bizim `Sikayet`
  sözleşmeden bağımsız genel şikayet-bileti. `Sikayet` entity'sine additive alanlar: `RentalId`
  (Guid? FK — `RentalContract.Plaka`/`SozlesmeNo` buradan türer), `TeslimAlanPersonelId`,
  `TeslimEdenPersonelId` (Personel FK — teslim akışında zaten `TeslimAlanPersonelId` kavramı
  `RentalContract`'ta var, aynı deseni burada tekrar kullan), `Puan` (int), `SikayetKanali`
  (Telefon/Web/Yüz Yüze — seç-veya-yaz), `SikayetYeri` enum (Kira/Rezervasyon), `CikisOfisi`.
  `/sikayetler`'e filtre: Müşteri, Ofis, Şikayet Yeri, Şikayet Kanalı. Liste kolonu: Kayıt No,
  Plaka, Türü, Cep Tel (Customer'dan), Belge No, Puan, Teslim Alan/Eden, Özet, Çıkış Ofisi.
- **dokunulacak:** `src/RentACar.Domain/Entities/Sikayet.cs`,
  `src/RentACar.Application/Crm/SikayetService.cs`,
  `src/RentACar.Web/Components/Pages/Crm/SikayetList.razor`, yeni migration
- **efor:** 3 gün
- **bağımlılık:** yok

### musterigelenmesajlar.aspx
- **desen:** D7
- **eylem:** Sistemde bu işlevin hiçbir izi yok (grep sıfır isabet). Yeni entity `AssistansTalep`
  (kirada olan araçtan/müşteriden gelen yol-yardım tipi talep-mesaj takibi): `RentalId` (Guid? FK),
  `Plaka` (snapshot), `AdSoyad`/`CepTel` (Customer'dan snapshot), `Zaman` (DateTimeOffset), `Mesaj`,
  `Sebep`, `YedekLastikMi` (bool), `AracHareketMi` (bool). Yeni `/assistans` liste sayfası (Plaka +
  Tarih filtresi), CLAUDE.md §5 tam reçetesi (entity→config→migration+RLS→repo→servis→DI→liste+nav).
- **dokunulacak:** yeni `src/RentACar.Domain/Entities/AssistansTalep.cs`, yeni
  `src/RentACar.Application/Crm/AssistansTalepService.cs` + `IAssistansTalepRepository`, yeni
  `src/RentACar.Infrastructure/Persistence/Repositories/AssistansTalepRepository.cs`, yeni
  `src/RentACar.Web/Components/Pages/Crm/AssistansTalepList.razor`, yeni
  `src/RentACar.Web/Crm/AssistansTalepEndpoints.cs`, `Program.cs` (`MapAssistansTalepEndpoints`),
  `MainLayout.razor` (nav), yeni migration
- **efor:** 3,5 gün
- **bağımlılık:** yok

### personel_calisma_grafigi.aspx
- **desen:** D7
- **eylem:** İsim benzerliği var ("Personel Çalışma") ama iş tamamen farklı: canlı = şube+tarih
  bazlı personel çalışma/vardiya grafiği; bizimki (`/crm`'deki "Personel Çalışma (BAF)") personelin
  araç tahsis sayısı — ilgisiz metrik, dokunulmaz. Yeni dikey: entity `PersonelVardiya`
  (`PersonelId` FK, `Tarih`, `SubeId`/`Sube`, `BaslangicSaat`, `BitisSaat`, `Aciklama`). Yeni rapor
  sayfası `/raporlar/personel-calisma` — Şube + Tarih filtresi, personel×gün matris görünümü.
- **dokunulacak:** yeni `src/RentACar.Domain/Entities/PersonelVardiya.cs`, yeni
  `src/RentACar.Application/Personnel/PersonelVardiyaService.cs` + repository arayüz/impl, yeni
  `src/RentACar.Web/Components/Pages/Reports/PersonelCalismaTablosu.razor`, nav, yeni migration
- **efor:** 3 gün
- **bağımlılık:** yok

---

# Modül 04 — kira-rezervasyon

### kira_listesi.aspx
- **desen:** D3
- **eylem:** `RentalRow`'a **zaten `RentalContract`'ta var olan** ~20 alan eklenir: `Kaynak`,
  `Provizyon`, `Depozito`, `KomisyonOran`/`KomisyonTutar`, `VadeTar` (yok — eklenecek, bkz. not),
  `OnayKodu`, `ProjeAdi`, `AssistFirma`, `OzelSoforBilgisi`, `HediyeGun`/`FaturalananGun`,
  `Doviz`. `RentalFilter`'a mevcut alanlara karşılık gelen filtreler eklenir: `Tarih_Listesi` türü
  seçici (Başlangıç/Bitiş/İşlem/Vade — enum), `Ofis_Durum` (Çıkış/Dönüş ayrımı — `CikisOfisi` vs
  `DonusOfisi`), `SahipGrup` (Vehicle.AracSahibi ile filtre), `RezKaynak`, `AracGrubu`
  (Vehicle.Grup), `PersonelTip`/`PersonelListesi`. `RentalList.razor` grid'ine bu kolonlar eklenir.
- **para-karar: PARA — Opus.** Komisyon/Provizyon/Depozito kolonlarının GÖRÜNTÜLENMESİ salt-okur
  (zaten hesaplanıp saklanmış), risk yok; ancak "Vade Tar/Faturalanan/Fatura Kalan/Faturalan Tipi"
  grubu **dönemsel faturalama** (`FaturaDonemi`) ile ilişkili — bu kolonların hangi kaynaktan
  (Invoice mi, FaturaDonemi planı mı) okunacağı tutar-doğruluğu kararı, Opus'a.
- **desen (alt-not): YAPILMAZ** — canlının 163-kolonluk **kullanıcı-bazlı özelleştirilebilir grid**
  sistemi (`Grid_Alan`/`Grid_Alan_Deger`, tablo boyutu kaydet). **Gerekçe:** kullanıcı-başına
  persisted grid-config UI'ı yüksek karmaşıklık/düşük iş değeri; rolümüzde zaten yetki+şube kapsamı
  var, kullanıcı-bazlı kolon seçimi ek bir yetki yüzeyi açar. Onun yerine sabit-ama-geniş kolon seti
  (yukarıdaki D3 eklemeleri) + mevcut Excel/CSV/PDF export (tüm kolonlara zaten erişim veriyor)
  yeterli parite sağlar.
- **dokunulacak:** `src/RentACar.Application/Bookings/RentalRow.cs`,
  `src/RentACar.Application/Bookings/RentalFilter.cs`,
  `src/RentACar.Application/Bookings/RentalService.cs`,
  `src/RentACar.Web/Components/Pages/Bookings/RentalList.razor`
- **efor:** 2 gün
- **bağımlılık:** yok (Vade/Faturalanan kolon kaynağı Opus kararına bağlı)

### kiralama.aspx
- **desen:** D6 (mega-form devamı)
- **eylem (yapısal, gerçek boşluklar):**
  1. **İşlem Şube gerçek FK** — `SekmeKiraBilgisi.razor:57`'deki `readonly` TODO alanı
     (`title="TODO: şube FK yetki katmanı (flagged iş)"`) kapatılır: `RentalContract.CikisSubeId`
     zaten var, salt-okunur değil düzenlenebilir select'e çevrilir (kullanıcı override edebilsin,
     varsayılan çıkış ofisinden türetilen kalsın).
  2. **Teslim Eden (çıkışta)** — şu an `RentalContract.TeslimAlanPersonelId` yalnız DÖNÜŞTE var;
     çıkış anını yakalayan ayrı `TeslimEdenPersonelId` (Guid?) eklenir.
  3. **Ödeme Şekli** create formunda yok — `KiraFormPaneller/SekmeFiyat.razor`'a eklenir
     (entity'de zaten `FaturalamaTipi` benzeri alanlar var, ayrı `OdemeSekli` string? eklenir).
  4. **Rez Grubu/Konum** — araçta ayrı alan yok (TODO) — `Vehicle.Grup` zaten var, ek "Konum"
     (park yeri) alanı `Vehicle`'a eklenir.
  5. **Çok-taraflı bakiye bölünmesi** (Mst/Firma/RezKaynak Toplam/Bakiye/Fatura) — `RentalContract`'a
     yapısal alanlar eklenir (`MstToplam`, `MstBakiye`, `FirmaToplam`, `FirmaBakiye`,
     `RezKaynakToplam`, `RezKaynakBakiye`, `MstFatura`, `FirmaFatura`, `RezKaynakFatura`); GERÇEK
     üç-taraflı tahsilat/fatura DAĞITIMI mantığı (kim öder, kim fatura alır) tutar kararı — Opus'a.
  6. Müşteri adres/pasaport/ehliyet detayı — `Customer`'da zaten şifreli var, formda salt-okunur
     bilgi paneli olarak decrypt edilip gösterilir (düzenleme `/cariler/{id}` formunda kalır).
  7. 2. sürücü serbest-metin (TC/Ad/Soyad/Tel/Ehliyet) — FK'li 2. sürücü (`IkinciSurucuId`) zaten
     var; canlıdaki gibi kayıtlı olmayan (misafir) 2. sürücü için OPSİYONEL serbest-metin katmanı
     `SekmeMusteri.razor`'a eklenir (yeni alanlar `RentalContract.IkinciSurucuSerbest*`).
- **para-karar: PARA — Opus.** Fiyat başlığı/indirim ayrıntısı (`Liste_Fiyat`/`Liste_Indirim`,
  `Saat_Farki`/`Saat_Farki_Almama_Sebebi`, `Vade_Farki_Ay/Hizmet`, `Damga_Vergisi_Orani`/
  `Damga_Yansit`) — bu kalemlerin `GenelToplam`'a NASIL katılacağı (zaten var olan `IskontoTutar`/
  `HaftaSonuFark`/`DamgaVergisi` alanlarıyla çakışma riski var — KURAL A/B çift-sayım disiplini)
  tutar kararı.
- **desen (alt-not #1): YAPILMAZ** — canlının ~30 SABİT-KODLANMIŞ ek hizmet/sigorta tipi
  (128 alan: `B_Koltuk_*`, `CDW_*`, `LCF_*`, `SCDW_*`, `Genc_Surucu_*`, vb. × Dahil/Miktar/Tutar/
  Bedava). **Gerekçe:** `EkHizmetTanim` generic master + `RentalAddOn` snapshot satır modeli
  ZATEN VAR ve tam çalışıyor; 30 sabit kolonu ayrıca kodlamak — her yeni ek hizmet tipi için
  migration gerektiren, "Bedava/Özel/Ücretsiz" gibi fiyat-tipi override'larını her kolonda tekrar
  eden — mimari gerileme olur. Generic modelde yeni tip = yalnız veri satırı (`EkHizmetTanim` INSERT).
  Gerçek eksik: fiyat-tipi override (satırda "Bedava" seçeneği, şu an `disabled` TODO) — bu KÜÇÜK
  parça D2 olarak ayrı ele alınabilir (RentalAddOn'a `FiyatTipiOverride` enum eklenir), 0,5 gün.
- **desen (alt-not #2): YAPILMAZ** — B2B Hizmet Alımı "görsel ama disabled" 9 alan
  (`Firma_Adi`, `Komisyon_Min_Oran/Tutar`, vb.). **Gerekçe:** bizim `StickyPanel` Dış Hizmet Alımı
  formu (`DisHizmetAlimi` entity, FAZ 4-B2) AYNI işi TAM DEFTERLİ yapıyor; canlının kendisi de bu
  alanları placeholder/disabled tutmuş (gerçek özelliği değil). Placeholder'ı taklit etmek geriye
  gidiş olurdu.
- **dokunulacak:**
  `src/RentACar.Web/Components/Pages/Bookings/KiraFormPaneller/{SekmeKiraBilgisi,SekmeFiyat,SekmeMusteri,SekmeDonus}.razor`,
  `src/RentACar.Domain/Entities/RentalContract.cs`, `src/RentACar.Domain/Entities/Vehicle.cs`,
  `src/RentACar.Application/Bookings/{RentalService.cs,RentalUpdateInput.cs}`, yeni migration
- **efor:** 4 gün (yapısal maddeler 1-7; PARA-Opus ve alt-not #1'in küçük parçası hariç)
- **bağımlılık:** madde 5 ve para-karar Opus kararına bağlı; alt-not #1/#2 için onay gerekmez
  (mimari karar, kod incelemesiyle savunulabilir)

---

## Gruplama: "Kiralama kuralları derinliği" PR

Üçü de `src/RentACar.Domain/Entities/RentalRule.cs` +
`src/RentACar.Web/Components/Pages/RentalRules/RentalRuleList.razor` üzerinde — tek PR.

### kiralama_kurallari.aspx
- **desen:** D2
- **eylem:** `RentalRule`'a eklenir: `TalepBas`/`TalepBit` (DateTimeOffset? — canlıdaki `Bas_Tar`/
  `Bit_Tar` TALEP tarihi aralığı; mevcut `GecerlilikBas`/`GecerlilikBit` KURAL geçerlilik aralığından
  AYRI kavram, ikisi de kalır), `PromosyonTuru` enum (Coklu/Tek), `KuponGecerlilik` enum
  (Hepsi/SadeceIlkBedel), `HesaplamaTipi` enum (Oran/Serbest), `HizliIslem` (bool). Liste tablosuna
  Şube Adı + Araç Grubu kolonları eklenir (form alanı zaten var, `RentalRuleList.razor`'ın liste
  `<table>`'ında gösterilmiyor — tek satır ekleme).
- **para-karar: PARA — Opus.** `HesaplamaTuru` — 24 banka kodu bazlı taksit/sanal pos markup
  matrisi. Bu, fiyat motoruna yeni bir girdi katmanı (banka bazlı ek maliyet %) — formül/oran kararı
  ve fiyat motorundaki tüketim noktası (`RentalQuoteEngine`) Opus'a.
- **dokunulacak:** `src/RentACar.Domain/Entities/RentalRule.cs`,
  `src/RentACar.Application/RentalRules/RentalRuleInput.cs`,
  `src/RentACar.Web/Components/Pages/RentalRules/RentalRuleList.razor`, yeni migration
- **efor:** 1,5 gün yapısal (banka matrisi hariç — Opus kararı sonrası ayrı efor)
- **bağımlılık:** banka matrisi Opus kararına bağlı
- **gruplama:** Kiralama kuralları derinliği PR

### kiralama_kurallari_basic.aspx
- **desen:** D2
- **eylem:** Muhtemelen `kiralama_kurallari.aspx`'in sadeleştirilmiş eski sürümü — bizde tek
  birleşik `/kira-kurallari` ekranı zaten bu ekranın kapsadığı her şeyi (Kanal/Araç Grubu/Max Gün/
  Hafta Sonu Fark/Sonra Öde/Hediye Gün/Kampanya/Segment/Şart Metni) karşılıyor. Ek eylem gerekmiyor
  — `kiralama_kurallari.aspx` PR'ı otomatik olarak bu ekranı da kapatıyor (`TalepBas`/`TalepBit`
  ortak).
- **dokunulacak:** (yok — kiralama_kurallari.aspx PR'ı ile aynı dosyalar)
- **efor:** 0 gün (yukarıdaki PR'a dahil)
- **bağımlılık:** yok
- **gruplama:** Kiralama kuralları derinliği PR

### kiralama_sartlari.aspx
- **desen:** D2
- **eylem:** `RentalRule`'a `HaftaGunKisiti` eklenir — 7 günün her biri için bağımsız min-gün
  kısıtı (canlıda `Hafta_Gun`: Farketmez/Pazartesi..Pazar). Basit modelleme: `List<int>`
  (0-6, hangi günler kısıtlı) + mevcut `MinGun` o günler için geçerli olur; veya yeni child-tablo
  `RentalRuleGunKisiti` (Gun int, MinGun int) — 7'den fazla farklı min-gün gerekirse child tercih
  edilir. Para/oran alanı İÇERMİYOR — bu yüzden PARA değil normal D2.
- **dokunulacak:** `src/RentACar.Domain/Entities/RentalRule.cs` (veya yeni child entity),
  `src/RentACar.Application/RentalRules/RentalRuleService.cs`,
  `src/RentACar.Web/Components/Pages/RentalRules/RentalRuleList.razor`, yeni migration
- **efor:** 1 gün
- **bağımlılık:** yok
- **gruplama:** Kiralama kuralları derinliği PR

---

## Gruplama: "Müsaitlik kolon+filtre derinliği" PR

### musait_arac_listesi.aspx
- **desen:** D3
- **eylem:** `MusaitlikArama.razor` sonuç tablosuna **Vehicle entity'sinde zaten var olan** kolonlar
  eklenir: SIPP (`Vehicle.Sipp`), Km Limiti (`Vehicle.KiraKmLimiti`), Yakıt Türü (`Vehicle.Yakit`),
  Vites (`Vehicle.Vites`), Yaş (`Vehicle.ModelYili`'nden hesaplanır), Ehliyet (min sürücü yaşı —
  `VehicleGroup.SurucuMinYas`, zaten var). Filtre: `Doviz` seçici (`RentalQuoteEngine`'in
  döndürdüğü `ParaBirimi` zaten var, seçilebilir hale getirilir), `Donus` (ayrı dönüş ofisi —
  `AvailabilityService.FindAvailableAsync`'e ek parametre), `RezKaynak`, `KiraGun` (elle gün girişi
  — tarih aralığı yerine), `BasSaat`/`BitSaat` (saat hassasiyeti). Excel export ucu eklenir
  (`ListExportCatalog`'a yeni tablo).
- **para-karar: not gerekmez** — fiyat kolonu zaten `RentalQuoteEngine.QuoteAsync`'ten geliyor,
  mevcut motor davranışı değişmiyor, yalnız daha fazla girdi parametresi (Doviz/Donus/Saat) iletiliyor.
  "Dolu/Boş/Gün/Durum" kolonları bu ekranın (Müsait Araç ARAMA) kapsamında zaten örtük (sonuç
  listesi=müsait araçlar); ayrı kolon olarak eklenmez, gerekirse musaitlik_durum'a taşınır.
- **dokunulacak:** `src/RentACar.Web/Components/Pages/Availability/MusaitlikArama.razor`,
  `src/RentACar.Application/Availability/{AvailabilityService.cs,IAvailabilityRepository.cs}`,
  `src/RentACar.Infrastructure/Persistence/Repositories/AvailabilityRepository.cs`,
  `src/RentACar.Web/Reports/ListExportCatalog.cs`
- **efor:** 1 gün
- **bağımlılık:** yok

### musaitlik_durum.aspx
- **desen:** D3
- **eylem:** Canlı ekranın gerçek çıktısı DOM'dan yakalanamadığı için **düşük güvenle** eşleştirildi
  (K3 doğrulanamadı). Muhtemel yorum: tarih×SIPP anlık-durum matrisi (tek günlük, aralık değil).
  Aynı `/musaitlik` sayfasına "SIPP" checkbox + tek-tarihli anlık-durum modu eklenir (mevcut
  başlangıç-bitiş aralık aramasının yanına, `To == From` özel durumu olarak). **Bu ekranın kendi
  başına ayrı bir sayfa mı olması gerektiği belirsiz** — eğer canlı gerçekten SIPP×gün matrisi
  (transpoze rapor) ise D4'e kayabilir; şu anki kanıt seviyesiyle D3 öneriliyor, ekip canlıyı tekrar
  erişip DOM yakalarsa gözden geçirilmeli.
- **dokunulacak:** `src/RentACar.Web/Components/Pages/Availability/MusaitlikArama.razor`
- **efor:** 0,5 gün
- **bağımlılık:** kanıt netleşmeden düşük güven — canlı erişimi tekrarlanırsa efor değişebilir

---

## Gruplama: "Rezervasyon filtre+alan derinliği" PR

İkisi de `src/RentACar.Domain/Entities/Reservation.cs` +
`src/RentACar.Web/Components/Pages/Bookings/ReservationList.razor` üzerinde.

### rezervasyon.aspx
- **desen:** D3
- **eylem (gerçek boşluk — D3, veri zaten var):** "**Brokerden Gelen Bilgisi**" sekmesi —
  `Reservation.Ota*` 8 alanı (`OtaKiraBedeli`, `OtaDropBedeli`, `OtaBebekKoltugu`, `OtaNavigasyon`,
  `OtaLcf`, `OtaCdw`, `OtaScdw`, `OtaEkSurucu`) **ZATEN ENTITY'DE VAR** (FAZ 4.5, CLAUDE.md §6) —
  yalnız `ReservationList.razor`'ın create/edit formunda görünmüyor. Bir `<details>` bloğu olarak
  (mevcut "Ödeme/komisyon" bloğunun deseniyle) eklenir.
- **eylem (yeni alan — D1 parçası):** `Reservation`'a `TalepTuru`, `GeldigiBirim`, `OnayKodu`,
  `ProjeAdi` eklenir (bunlar `RentalContract`'ta zaten var, `Reservation`'da YOK — "Kiraya Çevir"
  dönüşümünde kiraya taşınacak şekilde).
- **para-karar: PARA — Opus.** Çok-taraflı bakiye (Rez.Ver.Komisyonu / Dış Bakiye-Hesaplanan /
  Müşteri Bakiye / Firma Bakiye) — `kiralama.aspx`'teki AYNI karara bağlı (tutarlılık şart:
  rezervasyon ve kira aynı üç-taraflı modeli paylaşmalı).
- **desen (alt-not): YAPILMAZ (kısmi).** Sürücü serbest-metin (16 alan), ~30 sabit-kodlanmış ek
  hizmet/sigorta tipi, B2B "görsel-ama-kayıtsız" alanlar, HGS/hasar manuel alanları, kart/provizyon
  erken-aşama detayları — **AYNI gerekçeyle** (`kiralama.aspx` alt-not #1/#2) rezervasyon
  aşamasında da eklenmez. **Ek gerekçe (bu ekrana özel):** bu derinlik rezervasyon aşamasında
  toplanırsa "Kiraya Çevir" ile kira mega-formuna geçişte İKİ KOPYA veri kaynağı oluşur (rezervasyon
  girişi + kira girişi) — senkron/çakışma riski. Ürün kararı olarak bilinçli minimal tutulmuş
  (CLAUDE.md geçmişi bunu doğruluyor); "Kiraya Çevir"den sonra derinlik zaten mega-formda geliyor.
  Bu, Kural 6'nın tam örneği — körlemesine 400 alan eklenmez.
- **dokunulacak:** `src/RentACar.Domain/Entities/Reservation.cs`,
  `src/RentACar.Application/Bookings/ReservationService.cs`,
  `src/RentACar.Web/Components/Pages/Bookings/ReservationList.razor`, yeni migration
- **efor:** 1,5 gün (Ota* sekmesi + 4 yeni alan; çok-taraflı bakiye Opus kararı sonrası ayrı efor)
- **bağımlılık:** çok-taraflı bakiye Opus kararına bağlı; YAPILMAZ kısmı onay gerekmez
- **gruplama:** Rezervasyon filtre+alan derinliği PR

### rezervasyon_listesi.aspx
- **desen:** D3
- **eylem:** `ReservationList.razor`'a arama/filtre barı eklenir (şu an SIFIR — `Reservations.ListAsync()`
  filtresiz): Ad Soyad (Customer JOIN), Dosya No (ReservationNo contains), Plaka (Vehicle JOIN),
  Rezervasyon Durum (`ReservationStatus` — entity'de zaten var), Tarih aralığı, Rez Kaynak. Yeni
  `ReservationFilter` sınıfı `RentalFilter`'ın deseniyle oluşturulur + `IReservationRepository`'ye
  `SearchAsync` eklenir (şu an yalnız `ListAsync`). Liste kolonlarına Talep Türü/Geldiği Birim/Proje
  Adı/Onay Kodu (yukarıdaki D1 alanları) eklenir.
- **para-karar: PARA — Opus.** Komisyon/bakiye kolonları (Alacağımız Komisyon, Dış Bakiye/
  Hesaplanan, Müşteri/Firma Bakiye) — rezervasyon.aspx'teki AYNI karara bağlı.
- **dokunulacak:** yeni `src/RentACar.Application/Bookings/ReservationFilter.cs`,
  `src/RentACar.Application/Bookings/{IReservationRepository.cs,ReservationService.cs}`,
  `src/RentACar.Infrastructure/Persistence/Repositories/ReservationRepository.cs`,
  `src/RentACar.Web/Components/Pages/Bookings/ReservationList.razor`
- **efor:** 1 gün
- **bağımlılık:** yok
- **gruplama:** Rezervasyon filtre+alan derinliği PR

---

### rezervasyon_kaynagi.aspx
- **desen:** D2 (+ D8 alt-parça)
- **eylem (yapısal, D2 — iş kuralı matrisi):** `ReservationSource`'a eklenir: `KaynakGrubu` enum
  (OfisSatis/Broker/Acente/RentACar/Otel/Diger), iş kuralı bool matrisi (`Uzatamaz`,
  `RezTarihleriDegisemez`, `ProvizyonYok`, `KmSinirsiz`, `MaliyetYansitma`, ve erken/gecikme/iptal/
  no-show/uzatma için ayrı davranış bayrakları `MatrisErken`, `MatrisGecikme`, `MatrisIptal`,
  `MatrisNoShow`, `MatrisUzatma`), sigorta/tedarik varsayılan seçenekleri (`SigortaKaynakNo`,
  `DropKaynakNo`, `ProvizyonSecenek`, `MuafiyatSecenek`, `ScdwDahil`/`CdwDahil`/`LcfDahil`/
  `PaiDahil` — bool), ek hizmet varsayılan tutarları (`BebekKoltugu`, `Navigasyon`, `EkSurucu`,
  `Wifi` — decimal), `MaxGun`, `MailAdres`, `OtomatikMailGitme`, `RiskAnalizYapma`, `SubeGor`,
  `AyniYonDrop`, `AcenteFiyatDegistir`, `Gizle`, `SadeceMusteriOdeme`. Bu kural matrisi rezervasyon/
  kira akışında (`ReservationService`/`RentalService`) TÜKETİLECEK şekilde bağlanır (D2'nin özü:
  alan eklemek yetmez, kuralın okunduğu yer de test edilir) — örn. `Uzatamaz=true` ise
  `RentalService.UpdateOpenAsync`'te uzatma reddedilir. Liste kolonuna Cari Bilgi, Tarife, Özel Kod
  eklenir.
- **para-karar: PARA — Opus.** `FKomisyonOran`/`Tutar`, `Bakiyelendirme`, `RezKayBakiyeDvz`,
  `XmlKatsayi`, `OnOdemeOrani`, `Indirim`, `PuanOrani` — komisyon/bakiye hesaplama formülü ve bu
  oranların fiyat motoruna/deftere nasıl bağlanacağı.
- **desen (alt-not): D8 BLOKE — efor yazılmadı.** `FrameKod`, `CalisilacakDoviz`,
  `Faz1Timeout`/`Faz2Timeout`, `PaymentTuru`, `XmlHizmetOzel`, `XmlMailGitme`, acente paneli ödeme
  entegrasyonu, `RezLogo` yükleme (gerçek dosya depolama + broker paneli) — bunlar XML broker/acente
  entegratör kimliği gerektirir (canlı sistemin B2B ortak entegrasyon katmanı). **Açmadan önce
  kullanıcıya sorulur** — port/stub hazırlanabilir ama kod yazılmaz.
- **dokunulacak:** `src/RentACar.Domain/Entities/ReservationSource.cs`,
  `src/RentACar.Application/ReservationSources/{ReservationSourceInput.cs,ReservationSourceService.cs}`,
  `src/RentACar.Application/Bookings/{ReservationService.cs,RentalService.cs}` (kural tüketimi),
  `src/RentACar.Web/Components/Pages/ReservationSources/ReservationSourceList.razor`, yeni migration
- **efor:** 3 gün (D2 yapısal + kural-tüketim testleri; komisyon formülü Opus sonrası ayrı efor;
  D8 parçası hariç)
- **bağımlılık:** komisyon formülü Opus kararına bağlı; D8 parçası kimlik/credential + kullanıcı onayı

---

TOPLAM: 22 ekran planlandı
