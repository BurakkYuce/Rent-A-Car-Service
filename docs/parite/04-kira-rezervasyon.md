# Modül 04 — kira-rezervasyon

**11 ekran işlendi.** Durum dağılımı: PARA — OPUS'A DEVİR = 8 · 🟡 KISMİ = 2 · 🩹 CANLI BOZUK = 1 · ✅ TAM = 0 · ❌ YOK = 0 · ❓ DOĞRULANAMADI (bağımsız üst-statü olarak) = 0.

Bu modül canlının en ağır iki ekranını taşıyor: `kiralama.aspx` (400 gerçek form alanı, 13 sekme) ve
`rezervasyon.aspx` (400 gerçek form alanı, 9 sekme) — her ikisi de fiyat/KDV/komisyon/tahsilat üreten
ekranlar olduğu için kural 5 gereği **PARA — OPUS'A DEVİR** işaretlendi; K2/K3 kanıtı yine de tam
hesaplandı (alan-alan okuma + `KiraForm.razor` + 10 panel dosyası + `ReservationList.razor` içeriğiyle
karşılaştırılarak). Metodoloji notu: canlı profilin başlık satırındaki "alan=705/597" sayısı ile
`## Form alanları` bölümünde gerçekten enumere edilen alan sayısı (400/400) FARKLI — kanıtlanabilir
olan yalnız enumere edilen 400'dür; K2 payda olarak 400 kullanıldı (kanıtsız iddia yasağı).

---

## kira_listesi.aspx — Kira Listesi
- **Durum:** PARA — OPUS'A DEVİR
- **Bizde:** /kiralar (`src/RentACar.Web/Components/Pages/Bookings/RentalList.razor`)
- **kanit:** /kiralar · K2=%16 (10/61) · K3=%7 (11/163)
- **Canlı fazlası (filtre alanları, 51/61):** `Plaka_Ara` (KVKK-düşürülmüş), `Tarih_Listesi` (tarih türü
  seçici: Başlangıç/Bitiş/Kira Zamanı/İşlem/Taksit/Kapatma/Opsiyon/Vade), `Dosya_No2`, `Kira_Bakiye`
  (özet/detaylı), `Ofis_Durum` (İşlem/Çıkış/Dönüş/Otopark/Bölge ayrımı), `Sahip_Grup` (Bizim/Dış Araç),
  `Tablet_Kullanim`, `Evrak_Durum`, `Kiralama_Turu` filtresi, `Grid_Alan`/`Grid_Alan_Deger` (163 kolonluk
  kullanıcı-seçilebilir kolon sistemi — bizde YOK), `Ilk_4`/`Son_4` (kart no arama), `Kalan_Bedel`,
  `Kay_Filitre` (=/<>), `Rez_Kaynak` filtresi, `Arac_Grubu` filtresi, `Personel_Tip`/`Personel_Listesi`,
  9 checkbox (`Arac_Goster`, `Fatura_No_Goster`, `Uye_Puan`, `Cezali_Gecis`, `Web_Bilgileri`,
  `Baslik_Koy`, `Tahsilat_Adet`, `Sifir_Bedel`, `Hasarli`), `PuanYuklu`, tablo-boyutu/kaydet aksiyonları.
- **Canlı fazlası (kolon grupları, 152/163):** kart/risk (Kalan Döviz, Müşteri Bedel/Kalan, Risk); sigorta/ek
  hizmet dökümü (SCDW, Muafiyetli Sigorta, Mini Hasar, Navigasyon, Geçiş Bedeli/%10, Yakıt Bedeli, Hasar
  Bedeli, Drop Bedeli, Üyelik Bedeli, LCF Bedeli, Paket Hizmet1-6); komisyon (Alacağımız Komisyon, Fatura
  Komisyon, Ödenen Komisyon); vade/fatura dönemi (Vade Tar, Faturalanan, Fatura Kalan, Fatura No, Fat.
  Tarihi, Faturalan Tipi); üyelik/puan (Üyelik Tipi, Üye Numarası, Üye Puan, Dış Puan, Puan Türü/Tarih);
  web/OTA (Web Kupon, Web I.Kodu, Promosyon, Upcell Fiyat); personel/işlem (Kiraya Veren, Rez. Alan, Teslim
  Eden/Alan, Kapatan Pers.); diğer (Rac Tablet, Anket, Mutabakat, Onay Kodu, Proje Adı, Özel Şoför, Kış
  Lastiği, Çocuk Koltuğu, Assist Firma, Pasaport No, Vergi Numarası, Hesaplanan Gün/Günlük).
- **Bizde fazlası:** satır-içi hızlı tahsilat mini-formu (deterministik idempotency anahtarıyla); sözleşme
  PDF görüntüle/yazdır/indir linki satır başına.
- **Not:** Sabit 9 kolonlu basit tablo vs. canlının 163 kolonluk kullanıcı-özelleştirilebilir grid + 61
  alanlık gelişmiş filtre paneli. Tutar/KDV/komisyon kolonlarının doğruluk kararı `kiralama.aspx`
  karşılaştırmasına devredilir (kural 5).

## kira_talep_ara.aspx — (canlı: Runtime Error)
- **Durum:** 🩹 CANLI BOZUK
- **Bizde:** — (doğrulanamadı)
- **kanit:** — · K2=❓ N/A · K3=❓ N/A (canlı profil `alan=0 kolon=0`, kolon kaynağı `yok`)
- **Canlı fazlası:** doğrulanamadı.
- **Bizde fazlası:** doğrulanamadı.
- **Not:** Canlı ekran 500/Runtime Error döndürüyor; hiçbir alan/kolon çıkarılamadı, karşılaştırma
  yapılamaz. Kural tablosuna göre parite borcu düşük öncelikli.

## kiralama.aspx — Kira Formu
- **Durum:** PARA — OPUS'A DEVİR
- **Bizde:** /kiralar/yeni + /kiralar/{Id:guid} (`src/RentACar.Web/Components/Pages/Bookings/KiraForm.razor`
  + `KiraFormPaneller/SekmeHizliGiris.razor`, `SekmeKiraBilgisi.razor`, `SekmeMusteri.razor`,
  `SekmeArac.razor`, `SekmeFiyat.razor`, `SekmeEkHizmet.razor`, `SekmeAyrintilar.razor`,
  `SekmeDonus.razor`, `StickyPanel.razor`, `YeniMusteriBlok.razor`)
- **kanit:** /kiralar/yeni · K2=%31 (123/400) · K3=❓ DOĞRULANAMADI (kolon kaynağı yok — form ekranı)
- **Canlı fazlası (277/400 alan, kategori bazlı):**
  - **En büyük tek blok (128 alan):** ~30 SABİT-KODLANMIŞ ek hizmet/sigorta tipi × Dahil/Miktar/Tutar/
    Bedava alt-alanları: `B_Koltuk_*`, `C_Koltuk_*`, `Navigasyon_*`, `Surucu(Ek Sürücü)_*`, `CDW_*`,
    `LCF_*`, `Mini_Hasar_*`, `Max_Guvence_*`, `Super_Mini_Hasar_Sigortasi_*`, `SCDW_*`, `YolYardim_*`,
    `Hirsizlik_Sigorta_*`, `Wifi_*`, `Genc_Surucu_*`, `IMM_*`, `Sarj_Cihazi_*`, `Ek_KM`, `Adres_Teslim`,
    `Drop_Manuel`/`Drop_Dosya_No`/`DropT`, `Kis_Lastik_*`, `Uyelik_*`, `Iptal_*`, `Ek_Hizmet1/2_*`,
    `Paket_Hizmet1-6_*`, `Ek_Bedel3/4_*`, `Kira_Farki`, `Upcell_Fiyat`, `Gonderici_Katki_Payi`. Bizde
    bunun yerine GENERİK `EkHizmetTanim` master-tablosu + tek satır (checkbox+miktar) var; tip-bazlı
    ayrı Dahil/Tutar alanı ve "Bedava/Özel/Ücretsiz" fiyat-tipi override'ı yok (satırda `disabled` TODO).
  - **Sürücü 1/2 serbest-metin (16 alan):** `Surucu1_TC/Ad/Soyad/Tel/Ehliyet_No/Ehliyet_Yer/Ehliyet_Tar/
    Dogum_Tar` + `Surucu2` eşleniği. Bizde 2. sürücü sadece mevcut `Customer` kaydından FK ile seçilir;
    serbest-metin sürücü detayı yok, 1. sürücü ayrı bir kayıt değil (müşterinin kendisi).
  - **Müşteri adres/kimlik derinliği (9 alan):** `Mst_Adres/Ilce/Sehir/Ulke/Pasaport_No/Pasaport_Yer/
    Ehliyet_Tar/Dogum_Yeri/Dog_Tar` — formda gösterilmiyor/düzenlenemiyor.
  - **Çok-taraflı bakiye/fatura bölünmesi (9 alan):** `Mst_Toplam/Bakiye`, `Firma_Toplam/Bakiye`,
    `RezKaynak_Toplam/Bakiye`, `Mst_Fatura`, `Firma_Fatura`, `RezKaynak_Fatura` — yok.
  - **B2B Hizmet Alımı görsel-ama-kayıtsız alanlar (9 alan):** `Firma_Adi`, `Bakiyeli_Dis_Cari_Ad`,
    `Komisyon_Min_Oran/Tutar`, `F_Komisyon_Turu/Oran/Tutar`, `Bayi_Fatura_No`, `Indirim_Turu`,
    `Kdv_Muafiyeti`, `Fatura_Firmaya` — ekranda görsel var ama `disabled`/TODO, kayıtlı değil (bizim
    `StickyPanel` Dış Hizmet Alımı formu farklı alan setiyle KISMEN örtüşüyor: alinanHizmet/
    hizmetAlinanFirma/hizmetBedeli/komisyonOran — gerçek deftere yazan).
  - **Fiyat başlığı/indirim ayrıntısı (12 alan):** `H_Liste_Fiyat/Liste_Indirim`, `Liste_Fiyat/
    Liste_Indirim`, `Indirim/Indirim_Dvz`, `Damga_Vergisi_Orani/Damga_Yansit`, `Saat_FarkiCkd/
    Saat_Fark_Var/Indirim2/Saat_Farki_Almama_Sebebi`, `Vade_Farki_Ay/Hizmet`.
  - **Diğer (kalan ~94 alan):** `Dosya_No/No2`, `Dosya_Tipi`, `Filo_Fatura_Turu`, `Sure_Ay`/`Aylik_Km`
    (aylık kira süre/km limiti), `Beyan_Adet/Turu/Limit_Turu/Limit`, `Vale/Lastik_Hakki`, `Islem_Sube`
    (gerçek FK — bizde "çıkış ofisinden türetilir" TODO notu), `Kiraya_Veren`, `Kira_Takip`,
    `Opsiyon_Tarih`, `Vade_Tar`, `Kira_Devam_Musteri_Kontrol`, `Fatura_Profili` (gerçek veri yok),
    `Teslimat_Turu`, `Tarife` (elle seçim yok — sadece otomatik), `Odeme_Sekli` (create formunda yok),
    `Rez_Grubu`/`Konum` (araçta ayrı alan yok — TODO), `Kira_Adet`, HGS/yakıt/hasar manuel alanları
    (`Hgs_Bit_Tar`, `HGS_Doviz`, `GecisT`, `HGS_Manuel`, `HGS8_Doviz`, `GecisT_8`, `Yakit_Manuel`,
    `YikamaT`, `Hasar_Nedeni`, `Hasar_Bedeli`), `Rez_Kay_Bakiye_*`, `Ozel_Sofor_ID`, `Teslim_Eden`
    (çıkışı yapan personel — bizde sadece dönüşte var), `Firma_Bilgisi`, `TextBoxCikis_Yeri/Donus_Yeri`
    (serbest metin ofis adresi), `Kazali_Plaka`/`Musteri_Arac_Detayi`.
- **Bizde fazlası:** şablon-bazlı sözleşme PDF + müşteriye paylaşım linki (WhatsApp/Gmail); Fatura
  Dönemleri (dönemsel faturalama planı+kes) paneli; canlı TCMB kur tablosu; provizyon YAŞAM DÖNGÜSÜ
  (Al/Kapat) ayrı akış; B2B Dış Hizmet Alımı TAM DEFTERLİ form (canlıda salt görsel placeholder); risk-
  limit aşımında Admin/Yönetici onay guard'ı.
- **Not:** Canlının 13 sekme adı (Açıklama/Finans-Uçuş/Sürücüler/Diğer Bilgiler/Hizmet Alımı/Web Api/
  Aksesuar/Ek Koşullar/Kredi Kart-Havale/Nakit/Faturalar/Kur Bilgileri/Ceza-Geçişler) BİRE-BİR bizim
  Ayrıntılar alt-sekmeleri + StickyPanel fin-sekmeleriyle örtüşüyor (13/13 — iskelet tam), ama alan
  derinliği ~%31. En büyük tekil açık: ~30 sabit-kodlanmış ek hizmet/sigorta tipi yerine tek generik satır
  tablosu — fiyat/KDV üreten alan olduğundan nihai karar Opus'a.

## kiralama_kurallari.aspx — Kiralama Kuralları
- **Durum:** PARA — OPUS'A DEVİR
- **Bizde:** /kira-kurallari (`src/RentACar.Web/Components/Pages/RentalRules/RentalRuleList.razor`)
- **kanit:** /kira-kurallari · K2=%34 (12/35) · K3=%33 (1/3)
- **Canlı fazlası:** `Bas_Tar`/`Bit_Tar` talep-tarihi ikinci aralığı (bizde tek Geçerlilik Baş/Bit var —
  kira-dönemi ile talep-dönemi ayrımı yok), `Promosyon_Turu` (Çoklu/Tek), `KuponGercerlilik` (Hepsi/
  Sadece İlk Bedel), `Hesaplama` (Oran/Serbest), `HesaplamaTuru` (24 banka kodu — taksit/sanal pos
  bankası), `Pazartesi..Pazar` (7 gün-bazlı checkbox), `Hizli_Islem`. Kolon: Şube Adı, Araç Grubu (create/
  edit alanı var ama liste kolonunda gösterilmiyor).
- **Bizde fazlası:** Müşteri Segmenti (segment-bazlı indirim), ayrı Kampanya mı/Kampanya Kodu bayrağı,
  Şart Metni alanı.
- **Not:** Temel İskonto/Sonra Öde %/Hediye Gün/Promosyon Kodu/Min-Max Gün örtüşüyor; banka-bazlı taksit
  kuralları ve haftanın-günü kısıtları hiç yok. Fiyat/indirim üreten ekran — tutar kararı Opus'a.

## kiralama_kurallari_basic.aspx — Kiralama Koşulları (Basit)
- **Durum:** PARA — OPUS'A DEVİR
- **Bizde:** /kira-kurallari (`src/RentACar.Web/Components/Pages/RentalRules/RentalRuleList.razor`)
- **kanit:** /kira-kurallari · K2=%43 (6/14) · K3=❓ DOĞRULANAMADI (kolon kaynağı yok — tip=diger)
- **Canlı fazlası:** talep-tarihi ikinci aralığı (`Bas_Tar`/`Bit_Tar`), `Button1`/`Hesap` (framework).
- **Bizde fazlası:** Kanal, Araç Grubu Kodu, Max Gün, Hafta Sonu Fark %, Sonra Öde %, Hediye Gün,
  Kampanya alanları, Müşteri Segmenti, Şart Metni (basit ekranın kapsamadığı ama bizim tek birleşik
  ekranımızda hep var olan alanlar).
- **Not:** Muhtemelen `kiralama_kurallari.aspx`'in sadeleştirilmiş/eski sürümü; bizde tek birleşik ekran
  olduğundan aynı gerekçeyle örtüşüyor.

## kiralama_sartlari.aspx — TürevRent - Kiralama Şartları
- **Durum:** 🟡 KISMİ
- **Bizde:** /kira-kurallari (`src/RentACar.Web/Components/Pages/RentalRules/RentalRuleList.razor`)
- **kanit:** /kira-kurallari · K2=%40 (4/10) · K3=❓ DOĞRULANAMADI (kolon kaynağı yok — tip=diger)
- **Canlı fazlası:** `Hafta_Gun` (Farketmez/Pazartesi..Pazar — gün-bazlı min-gün kuralı), `Up_Button1`/
  `Button1`/`Hesap` (framework/aksiyon).
- **Bizde fazlası:** İskonto, Promosyon, Max Gün, Hediye Gün, Kampanya, Segment, Şart Metni — bu ekranın
  kapsamı dışında ama bizim tek ekranımızda var.
- **Not:** Para/oran alanı içermiyor (yalnız Şube/Min Gün/tarih aralığı) — bu yüzden PARA yerine normal
  statü verildi. Şube+Min Gün+tarih aralığı örtüşüyor; haftanın-günü bazlı minimum-gün kısıtı bizde yok.

## musait_arac_listesi.aspx — Müsait Araç Listesi
- **Durum:** PARA — OPUS'A DEVİR
- **Bizde:** /musaitlik (`src/RentACar.Web/Components/Pages/Availability/MusaitlikArama.razor`)
- **kanit:** /musaitlik · K2=%13 (3/23) · K3=%32 (6/19)
- **Canlı fazlası (alan):** `Doviz` seçici (bizde para birimi seçilemiyor — motorun döndürdüğü döviz
  sabit), `Donus` (ayrı dönüş ofisi — bizde tek Şube filtresi), `Rez_Kaynak` filtresi, `Kira_Gun` (elle
  gün girişi), `Bas_Saat`/`Bit_Saat` (saat hassasiyeti — bizde sadece tarih), tablo düzeni/Excel
  aksiyonları (`btnSaveLayout`, ayrı Excel butonu — bizde export yok).
- **Canlı fazlası (kolon, 13/19):** Drop Bedeli, SIPP, Provizyon, Km Limiti, Yakıt Türü, Vites, Dolu, Boş,
  Gün, Durum, Yaş, Ehliyet, Rez ID.
- **Bizde fazlası:** —
- **Not:** Temel arama (tarih aralığı+grup+şube) ve fiyat/toplam-fiyat kolonu örtüşüyor; SIPP/Provizyon/
  Km-Limiti gibi rezervasyon kararı için önemli kolonlar yok. Fiyat kolonu fiyat motorundan ÜRETİLİYOR —
  tutar kararı Opus'a.

## musaitlik_durum.aspx — Müsaitlik Raporu
- **Durum:** 🟡 KISMİ
- **Bizde:** /musaitlik (`src/RentACar.Web/Components/Pages/Availability/MusaitlikArama.razor`)
- **kanit:** /musaitlik · K2=%50 (2/4) · K3=❓ DOĞRULANAMADI (kolon kaynağı yok — canlı grid DOM'da
  yakalanamadı)
- **Canlı fazlası:** `SIPP` checkbox (SIPP koduna göre görünüm), tek-günlük anlık-durum sorgusu (bizde
  yalnız başlangıç-bitiş ARALIĞI araması var, tek-tarihli anlık müsaitlik-matrisi yok).
- **Bizde fazlası:** Grup/Şube ayrı filtre kutuları; sonuç tablosunda fiyat/günlük/toplam kolonları
  (canlı "Müsaitlik Raporu" büyük olasılıkla salt durum matrisi — fiyat göstermiyor).
- **Not:** Canlı ekranın gerçek çıktısı (muhtemelen tarih×SIPP matris raporu) DOM'dan yakalanamadığı için
  düşük güvenle eşleştirildi; ekran tipi kökten farklı olabilir (arama/booking aracı vs. salt-okunur
  matris rapor) — K3 doğrulanamaz, K2 de düşük güvenle verildi.

## rezervasyon.aspx — Rezervasyon
- **Durum:** PARA — OPUS'A DEVİR
- **Bizde:** /rezervasyonlar (`src/RentACar.Web/Components/Pages/Bookings/ReservationList.razor` — satır-
  içi oluştur/düzenle formu; ayrı bir `/rezervasyonlar/yeni` sayfası yok)
- **kanit:** /rezervasyonlar · K2=%5 (20/400) · K3=❓ DOĞRULANAMADI (kolon kaynağı yok — form ekranı)
- **Canlı fazlası:** Canlının 9 sekmesinin (Açıklama/Finans-Uçuş/Sürücüler/Diğer Bilgiler/Hizmet Alımı/
  **Brokerden Gelen Bilgisi**/Web Api Bilgisi/Ek Koşullar/SMM-Mail Log) TAMAMI bizde YOK — `/rezervasyonlar`
  sekmesiz, düz 16 alanlık satır-içi form. `kiralama.aspx` ile ~%95 aynı alan kümesini taşıyan bu ekranın
  derinliği (sürücü1/2 serbest metin, müşteri adres/pasaport, ~30 sabit ek hizmet/sigorta tipi, çok-taraflı
  bakiye, B2B hizmet alımı, HGS/hasar, kart/provizyon, risk/kefil, talep/onay kodları) hiçbiri bizde yok.
  Rezervasyona ÖZEL ekstra fazlalık: "Brokerden Gelen Bilgisi" sekmesi alanları (`Gelen_Kira`,
  `Gelen_Extra`, `Gelen_Drop`, `Gelen_Payment`) ve maliyet-karşılığı `Orj_Web_Kira_Bedeli/Drop_Bedeli/
  Bebek_Koltugu/Navigasyon/LCF/CDW/SCDW/Ek_Surucu` (×8 — web satış fiyatı vs. bizim maliyetimiz ayrımı).
- **Bizde fazlası:** Onayla/Kiraya Çevir/İptal iş akışı ayrı butonlarla (canlıda tek `Durum`/`RezStatus`
  select ile yönetiliyor); Depozito alanı formda var — canlı `rezervasyon.aspx`'te bu alan HİÇ yok (sadece
  `kiralama.aspx`'te var), yani bizim rezervasyon formumuz canlının kapsamadığı bir alanı taşıyor.
- **Not:** Rota/amaç eşleşmesi doğru (Rezervasyon ↔ `/rezervasyonlar`) ama derinlik uçurumu var: canlıda
  rezervasyon, kiralamayla NEREDEYSE aynı ~400 alanlık formu paylaşıyor; bizde rezervasyon aşaması
  bilinçli minimal tutulmuş — derinlik "Kiraya Çevir" ile `KiraForm`'a geçildiğinde geliyor. Ürün kararı
  gerekebilir (rezervasyon aşamasına derinlik eklensin mi); K2 çok düşük olduğundan mesele salt tutar
  doğruluğu değil — yine de PARA — OPUS'A DEVİR (komisyon/fiyat alanları var).

## rezervasyon_kaynagi.aspx — Rezervasyon Kaynakları
- **Durum:** PARA — OPUS'A DEVİR
- **Bizde:** /rezervasyon-kaynaklari (`src/RentACar.Web/Components/Pages/ReservationSources/ReservationSourceList.razor`)
- **kanit:** /rezervasyon-kaynaklari · K2=%2 (2/90) · K3=%40 (2/5)
- **Canlı fazlası:** komisyon/bakiye/oran (`Bakiyelendirme`, `Bakiyelendirme_Str`, `Rez_Kay_Bakiye_Dvz_Y/
  Y`, `F_Komisyon_Oran`, `XML_Katsayi`, `XML_Hizmet`, `On_Odeme_Orani`, `Zorunlu_Hizmet_Bedeli`,
  `Indirim`, `Puan_Orani`); sigorta/tedarik varsayılanları (`Sigorta_Kaynak_No`, `Drop_Kaynak_No`,
  `Provizyon_Secenek`, `Muafiyat_Secenek`, `SCDW/CDW/LCF/PAI_Dahil`); XML/entegrasyon (`Frame_Kod`,
  `Calisilacak_Doviz`, `Faz_1/2_Timout`, `Min_Man_Suresi`, `Payment_Turu`, `XML_Hizmet_Ozel`,
  `XML_Mail_Gitme`); iş kuralı matrisi (`Uzatamaz`, `Rez_Tarihler_Degisemez`, `Provizyon_Yok`,
  `Km_Sinirsiz`, `Maliyet_Yansitma`, `Matris_Erken/Gec/Iptal/NoShow/Uzatma`); kanal/tür `Kaynak_Grubu`
  (Ofis Satış/Broker/Acente/RentACar/Otel/Diğer); ek hizmet varsayılan tutarları (`Bebek_Koltugu`,
  `Navigasyon`, `Ek_Surucu`, `Wifi`); logo yükleme (`RezLogo`); acente paneli ödeme entegrasyonu; İşlem
  Şube kapsamı; `Mail_Adres`, `Max_Gun`, `Otomatik_Mail_Gitme`, `Risk_Analiz_Yapma`, `Sube_Gor`,
  `Ayni_Yon_Drop`, `Acente_Fiyat_Degistir`, `Gizle`, `Tarife`/`Sadece_Musteri_Odeme`. Kolon: Cari Bilgi,
  Tarife, Özel Kod (3/5 kolon yok).
- **Bizde fazlası:** —
- **Not:** Bizde bu ekran sadece Kod+Ad+Durum sözlüğü; canlı ekran B2B ortak/kanal komisyon-fiyat-
  entegrasyon konsolu (~90 alan). En büyük tekil fark komisyon/bakiye/tarife alanları — para üreten
  katsayılar, Opus kararı gerekir.

## rezervasyon_listesi.aspx — Rezervasyon Listesi
- **Durum:** PARA — OPUS'A DEVİR
- **Bizde:** /rezervasyonlar (`src/RentACar.Web/Components/Pages/Bookings/ReservationList.razor`)
- **kanit:** /rezervasyonlar · K2=%0 (0/40) · K3=%9 (9/102)
- **Canlı fazlası (alan/filtre — 40/40):** `/rezervasyonlar` sayfasında hiçbir arama/filtre alanı yok:
  `Ad_Soyad`, `Dosya_No`, `Plaka`, `Rezervasyon_Durum`, `Tarih_Listesi`+`Tarih1/2`, `Musteri_No`,
  `Ad_Soyadx`, `Toplam_Al`, `Upgrate_Dosya`, `RezStatus`, `Ofis_Durum`/`Ofis`, `Arac_Grubu`,
  `Plaka_Durum`, `Grid_Alan`/`Deger`, `Rezerv_Tip`, `Rez_Kaynak`, `Grup_Goster`, `Baslik_Koy`,
  `Ayri_Ofis`, `Web_Maliyet_Goster` vb.
- **Canlı fazlası (kolon, 93/102):** komisyon/bakiye grubu (Alacağımız Komisyon, Rez. Ver. Kom., Dış
  Bakiye/Hesaplanan, Müşteri Bakiye, Firma Bakiye); web/OTA (Web Ind. Kodu, Web Status, Web Sebep,
  utm_source/medium/campaign); Taksit/Vade Farkı; Upcell Fiyat; Promosyon; Paket Hizmet1-6; Sürücü
  Bilgisi/Telefon; Talep Türü/Geldiği Birim/Firma Kodu/Onay Kodu/Proje Adı.
- **Bizde fazlası:** satır-içi Onayla/Kiraya Çevir/İptal/Düzenle aksiyonları (canlıda muhtemelen
  `rezervasyon.aspx`'ten yönetiliyor, liste ekranından değil).
- **Not:** Rota/amaç doğru ama liste bir filtre BARI içermiyor — canlının arama yeteneğinin hiçbiri yok;
  kolon derinliği de çok düşük. Komisyon/bakiye kolonları para-üreten alanlar olduğundan Opus'a.

---

TOPLAM: 11 ekran işlendi
