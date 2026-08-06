# Modül: 02-tanim-master

**Ekran sayısı:** 33
**Durum dağılımı:** ✅ TAM=6 · 🟡 KISMİ=6 · ❌ YOK=15 · ⚠ ERİŞİLEMEZ=0 · 🩹 CANLI BOZUK=0 · ❓ DOĞRULANAMADI=0 · PARA—OPUS'A DEVİR=6

Not: Bu modülün çoğu ekranı basit sözlük/master olduğu için hızlı ilerledi, ama üç grup net ayrıştı:
(1) tek-alanlı sözlükler (marka/segment/sahibi/iptal-sebebi) → TAM; (2) TürevRent'in on-yıllık
eklenti-yığını taşıyan iki dev form (Lokasyon 58 alan, Şube 80 alan — haftanın 7 günü×2 mesai saati,
web/bayi/entegrasyon alanları) → bizim sade CRUD'a göre alan-bazında %30 eşiğinin altında kaldı, ❌ YOK;
(3) para hareketi/hesaplama üreten ekranlar (kasa raporu, para çekme, toplu gider/tahsilat, otomatik
tahsilat, XML fiyat aktarım) → kural 5 gereği PARA—OPUS'A DEVİR, tutar doğruluğuna karar verilmedi.

---

## arac_grubu.aspx — Araç Grupları
- **Durum:** 🟡 KISMİ
- **Bizde:** /arac-gruplari (`src/RentACar.Web/Components/Pages/VehicleGroups/VehicleGroupList.razor`)
- **kanit:** /arac-gruplari · K2=%81 (29/36) · K3=%33 (3/9)
- **Canlı fazlası:** Grid kolonları — Açıklama, Araç Sayısı, Web ID, Servis ID, Sürücü Yaşı, Ehliyet Yılı
  (bu alanlar bizde formda VAR ama liste tablosunda gösterilmiyor). Form alanları — Provizyon_Doviz,
  Provizyon2_Doviz (provizyon dövizi seçimi), Yakıt Türü, Vites (grup düzeyinde), Entegrasyon_Kod1,
  Web ID (TextBox3), Servis ID.
- **Bizde fazlası:** Bagaj Sayısı (toplam, ayrı alan), Genç Sürücü/Ek Sürücü günlük ücreti (canlıda
  grup düzeyinde yok).
- **Not:** Alan bazında iyi örtüşme (%81) ama liste tablosu çok dar (yalnız 7 gerçek kolon); asıl veri
  form alanlarında var, grid'e taşınmamış.

## arac_sahibi.aspx — Türevrent - Araç Sahip Grubu
- **Durum:** ✅ TAM
- **Bizde:** /arac-sahipleri (`src/RentACar.Web/Components/Pages/VehicleOwners/VehicleOwnerList.razor`)
- **kanit:** /arac-sahipleri · K2=%100 (1/1) · K3=%100 (1/1)
- **Canlı fazlası:** —
- **Bizde fazlası:** Kod (ayrı alan), Tür (Bizim/Dış serbest metin), Durum (aktif/pasif — canlıda yok).
- **Not:** Canlı tek alanlı basit sözlük (Arac_SAhibiX); bizim tarafta aynı işi görüp üstüne kod/tür/durum
  ekliyor.

## arac_segment.aspx — TürevRent - Araç Segmenti
- **Durum:** ✅ TAM
- **Bizde:** /segmentler (`src/RentACar.Web/Components/Pages/VehicleSegments/VehicleSegmentList.razor`)
- **kanit:** /segmentler · K2=%100 (1/1) · K3=%100 (1/1)
- **Canlı fazlası:** —
- **Bizde fazlası:** Kod, Açıklama, Durum (canlıda yalnız "Segment" adı var).
- **Not:** Basit tek-alanlı sözlük, tam eşleşme.

## arac_tipi_tanimlama.aspx — Araç Tipi Tanımı
- **Durum:** ✅ TAM
- **Bizde:** /arac-tipleri (`src/RentACar.Web/Components/Pages/VehicleTypes/VehicleTypeList.razor`)
- **kanit:** /arac-tipleri · K2=%100 (5/5) · K3=%100 (5/5)
- **Canlı fazlası:** Marka_Lst (ayrı filtre dropdown'u — bizde tek arama/filtre yok, ama Marka alanı
  ComboBox olarak zaten filtrelenebilir).
- **Bizde fazlası:** Kod (ayrı anahtar), Durum.
- **Not:** Marka/Vites/Grup/Yakıt Türü/Tip tam örtüşüyor; grid kolonları da bire bir.

## detayli_arac_listesi.aspx — Detaylı Araç Listesi
- **Durum:** 🟡 KISMİ
- **Bizde:** /vehicles (`src/RentACar.Web/Components/Pages/Vehicles/VehicleList.razor`)
- **kanit:** /vehicles · K2=%75 (3/4) · K3=%22 (15/67)
- **Canlı fazlası (kolon, tek tek — 52 eksik):** Belge No, Ruhsat Sahibi, Özel Kod1-5, Söz. No,
  Rez. Müşteri, Ruhsat Tarihi, Açıklama, Sig. Bit. Tar, Kasko Bit. Tar, TSB Kodu, TSB Kasko Değeri,
  Alım Firma, Alım Tarihi, Alım Bedeli, İhale Tarihi, Hedef Satış Bedeli, İhale Firması, Noter Satış
  Tarihi, Aracı Alan, Son Tesl. Km, Son Tesl. Tarihi, Kredi Firma, Kiralayan, Kira Gün, Kira Fiyat,
  Muayene Tar, Kira Bit Tar, Çık. Planan Tar., Kira Bek. Tar., Aylık Maliyet, Yönetim Maliyeti, Ek
  Fiyat, Filo Gir. Tar., Ödeme Şekli, Assistan Firma, Dış Km Limit, Filo Çık. Tar., Lastik, 2.El, Pasif
  Sebep, Son Durum, Alis Euro, Alış Euro Fiyat, Satış Euro Fiyat, Kira Müş.ID, UTTS, HGS Firma, Satış
  Fiyat. Filtre — Ofis (Şube) filtresi de yok.
- **Bizde fazlası:** Segment, Filo Durumu, Motor Gücü, Kasa Tipi, Son Bakım Km (canlının bu ekranında yok
  ama başka canlı ekranlarda olabilir — kontrol edilmedi).
- **Not:** Bu ekran çoğu finansal/hukuki alanı TEK grid'de birleştiriyor; bizde aynı veriler (varsa) ayrı
  modüllere dağılmış (VehicleSale, AracKredi, Sigorta, Regulation) — konsolide görünüm yok. Fiyat/bedel
  kolonları var ama bu bir hesaplama ekranı değil, salt listeleme; PARA kuralı uygulanmadı.

## filo_arac_kiralama.aspx — TürevRent (Filo Araç Kiralama — sözleşmeye araç/fiyat ekleme)
- **Durum:** 🟡 KISMİ
- **Bizde:** /filo-kiralama (`src/RentACar.Web/Components/Pages/FiloKiralamalar/FiloKiralamaList.razor`)
- **kanit:** /filo-kiralama · K2=%44 (7/16) · K3=%27 (3/11)
- **Canlı fazlası:** Satış Temsilcisi, Fatura Türü (Dönem/Kırık), Tarih (sözleşme tarihi — Bas_Tarih'ten
  ayrı), Makbuz No (Dosya Numarası), İmza Tarih, Sözleşme No, Vade Gün, Fiyat Türü (Aylık/30 Gün
  Aylık/KDV Dahil/30 Gün Dahil), Kaynak (grid), Çıkış KM, Toplam KM (grid).
- **Bizde fazlası:** KDV Oranı (ayrı alan girişi), Açıklama.
- **Not:** Bizim ekran tek-araç/tek-sözleşme modeli; canlı ekran sözleşmeye ayrı ayrı araç/fiyat satırı
  ekleyip toplu rezervasyon/kira olarak kaydediyor (çok-araçlı sözleşme). Damga vergisi türü (kim öder)
  bizde sayısal tutar olarak modellenmiş — farklı temsil.

## filo_kiralama_listesi.aspx — TürevRent (Filo Kiralama Listesi — filtre/arama)
- **Durum:** ❌ YOK
- **Bizde:** /filo-kiralama (`src/RentACar.Web/Components/Pages/FiloKiralamalar/FiloKiralamaList.razor`)
- **kanit:** /filo-kiralama · K2=%0 (0/7) · K3=%50 (2/4)
- **Canlı fazlası:** Filtre alanları — Cari Bilgi arama, Ad Soyad, Tarih (önemli/önemsiz seçici +
  başlangıç/bitiş), Plaka arama, Araç arama. "Tablo Ayarlarını Kaydet" (kullanıcı bazlı grid düzeni).
- **Bizde fazlası:** Süre Ay, Aylık, Genel Toplam, Durum kolonları (canlının bu listesinde yok, o
  bilgiler filo_arac_kiralama.aspx'te).
- **Not:** Aynı /filo-kiralama rotası altındaki liste tablosu ID/Cari Bilgi/Dosya No kavramlarını
  (kısmen) taşıyor ama HİÇBİR arama/filtre alanı yok — canlının bu ekranının temel işi (arama) bizde
  karşılıksız; bu yüzden alan bazında YOK.

## genel_kasa.aspx — Nakit Kasa Raporu
- **Durum:** PARA — OPUS'A DEVİR
- **Bizde:** /raporlar/kasa-banka (`src/RentACar.Web/Components/Pages/Reports/KasaBankaDefteri.razor`)
- **kanit:** /raporlar/kasa-banka · K2=%38 (3/8) · K3=%38 (5/13)
- **Canlı fazlası:** Filtre — Araç Sahibi, Kasa Kodu (çoklu kasa seçimi — bizde Kasa/Banka tek seçici),
  İşlem Tipi (Depozito Hariç/Sadece Depozito), Döviz, Personel (PII nedeniyle bu envanterde düşürülmüş).
  Grid — Cari Bilgi, Kasa Kodu, Döviz, Şube, Evrak No, Personel, Araç Sahibi, Plaka (satır bazında).
- **Bizde fazlası:** Bakiye (yürüyen bakiye) — canlı grid'de yok, bizde satır satır gösteriliyor.
- **Not:** Kural 5 — bu bir Borç/Alacak kasa defteri raporu; tutar/bakiye doğruluğu ve çoklu-kasa/çoklu-
  döviz kapsamı Opus incelemesine bırakıldı.

## hesap_no_tanimlama.aspx — Banka Hesap No
- **Durum:** 🟡 KISMİ
- **Bizde:** /hesaplar (`src/RentACar.Web/Components/Pages/FinancialAccounts/FinancialAccountList.razor`)
- **kanit:** /hesaplar · K2=%56 (5/9) · K3=%67 (4/6)
- **Canlı fazlası:** Hediye Çek (checkbox), Özel Kod, Uyarı Mail Listesi (Islem_Mail — bakiye/vade uyarı
  epostası), Banka Şube Adı (Sube_Adi — İşlem Şube'den ayrı, bankanın kendi şube adı).
- **Bizde fazlası:** Kod (ayrı anahtar), Hesap No (IBAN'dan ayrı bir "hesap no" alanı), Tür (Kasa/Banka
  serbest metin).
- **Not:** Temel IBAN/Banka/Şube/Döviz örtüşüyor; hediye çek ve uyarı-eposta özellikleri bizde yok.

## hesap_para_islem.aspx — TürevRent (Hesaptan Para Çekme)
- **Durum:** PARA — OPUS'A DEVİR
- **Bizde:** /kasa — Virman formu (`src/RentACar.Web/Components/Pages/Finance/KasaHub.razor`)
- **kanit:** /kasa · K2=%25 (3/12) · K3=❓ DOĞRULANAMADI (kolon kaynağı yok)
- **Canlı fazlası:** IBAN/Hesap No seçimi (belirli banka hesabından çekim), Banka Dövizi, Banka Kuru,
  Makbuz Numarası, İşlem Şube, İşlemi Yapan (serbest metin — bizde oturum kullanıcısından geliyor,
  form alanı değil).
- **Bizde fazlası:** İşlem anahtarı (çift-submit idempotency) — canlıda yok.
- **Not:** Kural 5 — banka hesabından nakit çekme, bizde en yakın karşılığı genel Kasa↔Banka virmanı;
  belirli-IBAN seçimi ve kur/makbuz detayı yok. Tutar/kur doğruluğu Opus'a.

## hesap_tanimalama.aspx — TürevRent - Hesap Tanımlama
- **Durum:** 🟡 KISMİ
- **Bizde:** /hesap-kodlari (`src/RentACar.Web/Components/Pages/HesapKodlari/HesapKoduList.razor`)
- **kanit:** /hesap-kodlari · K2=%100 (2/2) · K3=❓ DOĞRULANAMADI (kolon kaynağı yok)
- **Canlı fazlası:** —
- **Bizde fazlası:** Kod/Ad ayrımı (canlı tek "Kod_Adı" alanı kullanıyor), Aktif durumu.
- **Not:** Alan bazında tam örtüşme (Kod_Adı+Açıklama ↔ Kod/Ad+Açıklama) ama canlı profilinde grid
  kolonu güvenilir çıkarılamadığından (kolon kaynağı=yok) ✅ TAM verilemiyor, KISMİ ile sınırlandı.

## iptal_sebepleri.aspx — İptal Sebepleri
- **Durum:** ✅ TAM
- **Bizde:** /iptal-sebepleri (`src/RentACar.Web/Components/Pages/CancelReasons/CancelReasonList.razor`)
- **kanit:** /iptal-sebepleri · K2=%100 (1/1) · K3=%100 (1/1)
- **Canlı fazlası:** —
- **Bizde fazlası:** Kod (ayrı anahtar), Durum (aktif/pasif — canlıda sebepler pasife alınamıyor,
  sadece eklenir).
- **Not:** Basit tek-alanlı sözlük, tam eşleşme.

## karsilastirmali_durum_analizi.aspx — Karşılaştırmalı Analiz Listesi
- **Durum:** ❌ YOK
- **Bizde:** —
- **kanit:** — · K2=%0 (0/5) · K3=DOĞRULANAMADI (canlı grid'i "Drag a column here..." — boş pivot,
  kaynak veri yok)
- **Canlı fazlası:** Periyot (1-12 ay karşılaştırma), Tablo (Kira/Rezervasyon seçici), Veri Türü
  (Adet/Gün), Kaynak (Rez. Kaynağı/Araç Grubu/Çıkış Noktası kırılımı), İşlem Şube — tamamı pivot-tablo
  esnek karşılaştırma motoru.
- **Bizde fazlası:** —
- **Not:** En yakın (ama farklı iş yapan) ekran /raporlar/rezervasyon-kaynak — o sabit tarih aralığına
  göre TEK kırılım (Kaynak) verirken canlı ekran ay-bazlı, çok-boyutlu (Tablo×VeriTürü×Kaynak) pivot
  karşılaştırma sunuyor. Ad benzerliği var, iş farklı (kural 3) — YOK.

## lokasyon_sube_ara.aspx — Drop Durumları
- **Durum:** ❌ YOK
- **Bizde:** /drop-tanimlari (`src/RentACar.Web/Components/Pages/DropTanimlari/DropTanimList.razor`)
- **kanit:** /drop-tanimlari · K2=%0 (0/3) · K3=%43 (3/7)
- **Canlı fazlası:** Filtre — İşlem Şube, Çıkış Lokasyon, Dönüş Lokasyon (üçü de bizde yok). Grid — Dönüş
  Lokasyon (bizde tek "Lokasyon" var, çıkış/dönüş ayrımı yok), Man. Süresi, Min. Gün, Drop-2 (ikinci bir
  ücret kalemi — tek yön/çift yön ayrımı olabilir).
- **Bizde fazlası:** Karşılama Şekli, Çalışma Şekli, Özel İletişim (canlının bu ekranında yok, muhtemelen
  lokasyonlar.aspx'in "Drop Ayarları" kolonunda).
- **Not:** Veri kavramı (Lokasyon×Şube→ücret) kısmen örtüşüyor (K3=%43) ama hiç filtre/arama alanı yok
  ve çıkış/dönüş lokasyon ayrımı, min. gün, man. süresi bizde modellenmemiş; alan eşiğinin altında.

## lokasyonlar.aspx — Lokasyonlar
- **Durum:** ❌ YOK
- **Bizde:** /lokasyonlar (`src/RentACar.Web/Components/Pages/Locations/LocationList.razor`)
- **kanit:** /lokasyonlar · K2=%18 (7/38) · K3=%57 (4/7)
- **Canlı fazlası (alan, tek tek):** İngilizce Ad, Buluşma Noktası (Ofis Teslim/Havalimanı/Shuttle/…),
  IATA, Web'de Gizle, Lokasyon Türü (Havalimanı/Şehir Merkezi/…), Bina No, Tarif (yol tarifi metni),
  Ülke, Posta Kodu, Maps Konumu, Ek Açıklama, Web Sıralama, günün 7 günü için AYRI açılış/kapanış
  saati (14 alan — bizde tek "Çalışma Saatleri" metin alanı), Drop Karşılama Türü, Drop Çalışma Şekli,
  Özel Mail, Özel Telefon.
- **Bizde fazlası:** Teslim Ücreti (canlıda bu ekranda yok — Drop matrisinde ayrı ücret var).
- **Not:** Rota kesinlikle aynı işi yapıyor (alış/dönüş lokasyon yönetimi) ama TürevRent'in on-yıllık
  derinliği (i18n ad, IATA, web görünürlük, gün-bazlı mesai, drop karşılama alt-ayarları) karşısında
  bizim 8 alanlı sade form %18'de kalıyor — kural 3 eşiğinin (%30) altında, YOK olarak işaretlendi;
  ancak temel CRUD (Ad/Adres/Telefon/Eposta/Şube/Durum) fiilen çalışıyor, tamamen "yok" değil.

## marka_tanim.aspx — Markalar
- **Durum:** ✅ TAM
- **Bizde:** /markalar (`src/RentACar.Web/Components/Pages/Brands/BrandList.razor`)
- **kanit:** /markalar · K2=%100 (1/1) · K3=%100 (1/1)
- **Canlı fazlası:** —
- **Bizde fazlası:** Kod (ayrı anahtar), Durum (aktif/pasif).
- **Not:** Basit tek-alanlı sözlük, tam eşleşme.

## otomatik_servisler.aspx — Günlük Servis Raporları
- **Durum:** ❌ YOK
- **Bizde:** —
- **kanit:** — · K2=%0 (0/2) · K3=DOĞRULANAMADI (kolon kaynağı yok)
- **Canlı fazlası:** İşlem Türü (HGS Başarılı, HGS Başarısız, KM Güncelleme, Taahhüt Uzatma, Süpürme —
  otomatik arka-plan job'larının günlük çalışma logu), Tarih filtresi.
- **Bizde fazlası:** —
- **Not:** Arka-plan job'larının başarı/başarısız günlük dökümünü gösteren bir log ekranı; bizde HGS
  yansıtma (Ceza modülünde) ve km log (VehicleKmLog) VAR ama bunları "günlük iş çalıştı/çalışmadı"
  şeklinde özetleyen bir rapor ekranı yok.

## otomatik_tahsilat.aspx — TürevRent (Otomatik Tahsilat)
- **Durum:** PARA — OPUS'A DEVİR
- **Bizde:** /ayarlar — `donemselOtomatikTahsilat` anahtarı (`src/RentACar.Web/Components/Pages/Settings/Ayarlar.razor`)
- **kanit:** /ayarlar · K2=%0 (0/6) · K3=DOĞRULANAMADI (kolon kaynağı yok)
- **Canlı fazlası:** Sözleşme No filtresi, Bakiye durumu (Müşteri Bakiyeli/HGS Bakiyeliler/Sadece
  Otomatik Olanlar), İşlem Şube, Tarih (Başlangıç/Bitiş) — seçilen sözleşmeler üzerinde toplu otomatik
  tahsilat TETİKLEME ekranı.
- **Bizde fazlası:** —
- **Not:** Kural 5 — canlı ekran seçili sözleşmeler için manuel/otomatik tahsilatı TETİKLEYEN bir arama+
  aksiyon ekranı; bizde yalnız tenant ayarında "job kesiminde otomatik kasa tahsilatı" açık/kapalı
  anahtarı var, sözleşme/bakiye bazlı seçim-çalıştırma arayüzü yok. Kapsam/doğruluk Opus'a.

## ozel_kod_tanim.aspx — TürevRent - Kod Tanımlama
- **Durum:** ✅ TAM
- **Bizde:** /ozel-kodlar (`src/RentACar.Web/Components/Pages/CustomCodes/CustomCodeList.razor`)
- **kanit:** /ozel-kodlar · K2=%100 (2/2) · K3=%100 (2/2)
- **Canlı fazlası:** —
- **Bizde fazlası:** Açıklama, Durum (canlıda yok).
- **Not:** Alan/kolon düzeyinde tam eşleşme (Değer↔Ad, Türü↔Tür) ama canlıda "Tür" KAPALI 15 değerlik
  bir taksonomi (Özel Kod 1-6, Noter, Satış Kanalı, İhale Yeri/Firması, Statü, Vade, Kira Takip, Uyarı
  Sebepleri — bunların bazıları canlıda BAŞKA ekranların dropdown kaynağı); bizde "Tür" serbest metin.
  Bu, alan var ama davranış (kapalı liste + çapraz-ekran besleme) farklı — wire-in kontrolü ayrı konu.

## para_tanimlama.aspx — TürevRent (Döviz Tanımlama)
- **Durum:** 🟡 KISMİ
- **Bizde:** /dovizler (`src/RentACar.Web/Components/Pages/Currencies/CurrencyList.razor`)
- **kanit:** /dovizler · K2=%33 (1/3) · K3=%33 (1/3)
- **Canlı fazlası:** Kur (döviz tanımına bağlı statik/manuel kur değeri + "Kur Bilgilerini Al" butonu),
  Ülke (Ulke alanı — hiçbir ekranımızda yok).
- **Bizde fazlası:** Sembol (₺/$/€ gibi gösterim sembolü — canlıda yok).
- **Not:** Kur bizde AYRI bir rotada (/kurlar — TCMB otomatik + tenant sabit kur) yönetiliyor, bu ekranın
  (para_tanimlama) kendisinde yok; Ülke alanı sistemde hiçbir yerde bulunamadı.

## rakip_fiyat_analizi.aspx — Rakip Fiyat Analiz Listesi
- **Durum:** ❌ YOK
- **Bizde:** —
- **kanit:** — · K2=%0 (0/2) · K3=DOĞRULANAMADI (grid tek kolon "Arac", veri boş)
- **Canlı fazlası:** Çıkış/Dönüş Ofisi (924 lokasyon kodlu canlı seçici) + tarih/saat aralığı ile CANLI
  rakip firma fiyat karşılaştırması (harici API/token tabanlı sorgu).
- **Bizde fazlası:** —
- **Not:** Kodda tek "rakip" eşleşmesi `RateMatrix.cs`'te bir yorum satırı ("A6 KARARI: fiyat GİRDİSİ
  DEĞİLDİR... salt-görünüm rakip-kıyas notu") — gerçek bir canlı rakip-fiyat sorgulama/analiz özelliği
  değil, sadece kolon-parite notu. Fonksiyon karşılıksız.

## rezsartlar.aspx — Rez Şartları Listesi
- **Durum:** ❌ YOK
- **Bizde:** —
- **kanit:** — · K2=%0 (0/2) · K3=DOĞRULANAMADI (kolon kaynağı dom_dx ama karşılık bulunamadı)
- **Canlı fazlası:** Müşteri (Ad/Soyad), Grup, Şart (özel talep metni), Başlangıç/Bitiş Tarihi, Talep
  Tarihi, Karşılama Tarihi, Teslim Eden — rezervasyona bağlı özel talep/şart TAKİP ekranı (talep→
  karşılama yaşam döngüsü).
- **Bizde fazlası:** —
- **Not:** RentalRuleList ve QuotationList içinde "şart"/"talep" alanı aranmadı bulunamadı; rezervasyon/
  kira üzerinde özel talebin ayrı bir yaşam-döngüsü (talep tarihi→karşılama tarihi→teslim eden) izleyen
  bir ekran yok.

## serbest_sms.aspx — SMS Gönderim İşlemi
- **Durum:** ❌ YOK
- **Bizde:** —
- **kanit:** — · K2=%0 (0/9) · K3=DOĞRULANAMADI (kolon kaynağı yok)
- **Canlı fazlası:** Cari arama (No/Ad/Soyad), Cep Telefonu, Gönderim Tarihi/Saati (zamanlanmış), SMS
  İçeriği (serbest metin) — manuel/serbest tek-seferlik SMS kompozisyon ekranı.
- **Bizde fazlası:** SmsIzin (Customer'da izin bayrağı), SmsBaslik/SmsApiKey (Ayarlar'da Twilio/SMS
  sağlayıcı yapılandırması) — bunlar KONFİG/İZİN alanları, gönderim arayüzü değil.
- **Not:** Twilio WhatsApp entegrasyonu (TwilioWhatsAppService) otomatik/şablon bildirimler için var
  (ör. günlük operasyon özeti); serbest metinli, zamanlanmış, tek-cariye manuel SMS gönderen bir ekran
  bulunamadı.

## sube_tanimlama.aspx — Şube İşlemleri
- **Durum:** ❌ YOK
- **Bizde:** /subeler (`src/RentACar.Web/Components/Pages/Branches/BranchList.razor`)
- **kanit:** /subeler · K2=%18 (10/56) · K3=%40 (2/5)
- **Canlı fazlası (öne çıkanlar):** Web İsim, Firma Ünvanı, günün 7 günü için AYRI açılış/kapanış saati
  (14 alan — bizde tek "Çalışma Saatleri"), Web Rez. Öncesi Saat, Enlem/Boylam, Hizmetten Alınan Komisyon
  % (ayrı, bizde tek komisyon oranı), Rezervasyon Rengi, "Alış Şubesi Değil" bayrağı, Web Sıralama, Web
  Otopark ID, Bayi Cari Kod + Bayi Ofis (bayi/aracı şube modeli), Komisyon Hesabı (Satıştan/Maliyetten),
  Online Rez ID, Sözleşme No Formatı, Nakit/Banka No (default hesap ataması), Entegrasyon Kodu, 10 adet
  "Şube Özel Ücretsiz Hizmet" satırı, Resim Dosyası, Eski Şube→Yeni Şube (şube birleştirme aracı).
- **Bizde fazlası:** İl/İlçe (ayrı alanlar — canlı tek "Şehir" metni + ayrı "İlçe"), Evrak No Öneki.
- **Not:** Temel şube CRUD'u (Kod/Ad/Adres/Telefon/Eposta/İl-İlçe/Komisyon/Durum) çalışıyor ama canlının
  web-entegrasyonu, bayi/komisyon modeli, ücretsiz hizmet listesi ve şube-birleştirme aracı gibi derin
  TürevRent-özel katmanları hiç yok; %18 ile eşik altında.

## tabletyonetim.aspx — Tablet Yönetim
- **Durum:** ❌ YOK
- **Bizde:** —
- **kanit:** — · K2=%0 (0/~30) · K3=DOĞRULANAMADI
- **Canlı fazlası:** Saha tableti kullanıcı yönetimi, imza-tableti varsayılan ayarları (Rezervasyon/Çıkış/
  Dönüş/Değişim için "imzaya hazırla" varsayılanı), aksesuar/lastik fotoğraf şablonları, müşteri epostası
  şablon editörü, RTF sözleşme şablon yükleme, sözleşme/rezervasyon arama (tablet operasyon ekranı).
- **Bizde fazlası:** —
- **Not:** Saha tableti / dijital imza toplama donanım-entegrasyonu; bizim sistemde eşdeğer bir saha-
  tablet operasyon modülü yok.

## toplu_gider.aspx — Toplu Gider İşlemleri
- **Durum:** PARA — OPUS'A DEVİR
- **Bizde:** /toplu-gider (`src/RentACar.Web/Components/Pages/Finance/TopluGider.razor`)
- **kanit:** /toplu-gider · K2=%56 (5/9) · K3=%67 (2/3)
- **Canlı fazlası:** Cari Bilgisi (gider bir cariye bağlanabiliyor), Vade, Hesap No (IBAN — hangi banka
  hesabından ödeneceği), Plaka Ekle (birden çok araca aynı anda gider dağıtımı — bizde tek satır=tek
  gider, plaka bilgisi açıklamaya gömülü olabilir).
- **Bizde fazlası:** Satır-bazlı toplu giriş (netTutar;açıklama serbest metin listesi) — canlı tek
  seferde tek gider+opsiyonel plaka listesi girerken bizde çok satırlı serbest metin toplu giriş var.
- **Not:** Kural 5 — atomik/dengeli defter iddiası var (bizim tarafta açıkça belirtilmiş); tutar/KDV/
  idempotency doğruluğu Opus'a.

## toplu_tahsilat.aspx — Toplu Tahsilat İşlemi
- **Durum:** PARA — OPUS'A DEVİR
- **Bizde:** /toplu-tahsilat (`src/RentACar.Web/Components/Pages/Finance/TopluTahsilat.razor`)
- **kanit:** /toplu-tahsilat · K2=%38 (5/13) · K3=DOĞRULANAMADI (kolon kaynağı yok)
- **Canlı fazlası:** Cari arama (No/Ad/Soyad tek cari), Hesap No (IBAN seçimi), Seçili Toplam (carinin
  açık işlemlerinden seçilenlerin toplamı — "Carinin İşlem Listesini Getir" ile).
- **Bizde fazlası:** Çok-cari serbest metin satır girişi (cariId;tutar;açıklama) — canlı TEK cari için
  onun açık kalemlerinden seçim yaparken bizde farklı caRİlere tek seferde dağıtılan liste var (ters
  model: biri "bir cari, çok kalem", diğeri "çok cari, bir kalem").
- **Not:** Kural 5 — iki taraf da "toplu tahsilat" adını taşıyor ama iş modeli TERS (canlı: bir carinin
  birden çok açık işlemini kapat; biz: birden çok cariye tek tahsilat işle). Doğruluk/kapsam Opus'a.

## turevuzak.aspx — Türev Uzak Erişim
- **Durum:** ❌ YOK
- **Bizde:** —
- **kanit:** — · K2=%0 (0/0) · K3=DOĞRULANAMADI (alan yok, kolon yok)
- **Canlı fazlası:** Ekran paylaşma/uzak erişim destek uygulaması indirme/başlatma sayfası (TeamViewer
  benzeri destek aracı bağlantısı).
- **Bizde fazlası:** —
- **Not:** İş kuralı içermeyen, üçüncü-parti uzak-destek aracına yönlendiren bir sayfa; RentACar iş
  kapsamı dışında, karşılığı da hiç gerekmiyor.

## xml_disardan_arac.aspx — TürevRent (XML Dışarıdan Araç Eşleştirme)
- **Durum:** ❌ YOK
- **Bizde:** /ice-aktar (`src/RentACar.Web/Components/Pages/Import/IceAktar.razor`)
- **kanit:** /ice-aktar · K2=%0 (0/6) · K3=DOĞRULANAMADI (kolon kaynağı yok)
- **Canlı fazlası:** XML Firma seçimi (hangi harici besleme), Eşleşmeyenler/Değişenler filtresi, Otomatik
  Eşleştir toggle — harici XML besleme ile mevcut araç filosu arasında SÜREKLİ mutabakat/eşleştirme
  aracı.
- **Bizde fazlası:** —
- **Not:** /ice-aktar tek-seferlik CSV/Excel GÖÇ aracı (eski TürevRent export'undan bir kez veri taşıma);
  canlının bu ekranı sürekli çalışan bir XML-besleme mutabakat/eşleştirme aracı — farklı iş (kural 3).

## xml_disardan_sube.aspx — TürevRent (XML Dışarıdan Şube Eşleştirme)
- **Durum:** ❌ YOK
- **Bizde:** —
- **kanit:** — · K2=%0 (0/5) · K3=DOĞRULANAMADI (kolon kaynağı yok)
- **Canlı fazlası:** XML Firma seçimi, Eşleşmeyenler filtresi, Otomatik Eşleştir toggle — harici XML
  şube/lokasyon kodlarıyla bizim şube listemiz arasında mutabakat aracı.
- **Bizde fazlası:** —
- **Not:** xml_disardan_arac.aspx ile aynı desen, bu kez şube için; karşılığı yok.

## xml_firma_tanim.aspx — TürevRent (XML Firma Tanım)
- **Durum:** ❌ YOK
- **Bizde:** —
- **kanit:** — · K2=%0 (0/16) · K3=DOĞRULANAMADI (kolon kaynağı yok)
- **Canlı fazlası:** Harici XML/OTA firması tanımı — Kira/Drop/Hizmet XML katsayıları, maliyet-kiralama/
  drop/hizmet oranları, muafiyet/km/depozito modu, komisyon oranları (kiradan/hizmetten), avantaj
  katsayısı, çalışma şekli, özel fiyat gönderme anahtarı + firmaya özel HTML açıklama şablonu editörü
  (Design/HTML/Preview sekmeleri).
- **Bizde fazlası:** —
- **Not:** /broker-yasaklari farklı bir iş yapıyor (belirli broker'ları YASAKLAMA), bu ekranın işi
  (XML/OTA firmasının komisyon+katsayı+şablon YAPILANDIRMASI) değil — isim benzerliği yanıltıcı olabilir,
  gerçek karşılık bulunamadı.

## xml_fiyat_aktar.aspx — TürevRent (XML Fiyat Aktar)
- **Durum:** PARA — OPUS'A DEVİR
- **Bizde:** /tarife-aktar (`src/RentACar.Web/Components/Pages/Import/TarifeAktar.razor`)
- **kanit:** /tarife-aktar · K2=%75 (3/4) · K3=DOĞRULANAMADI (kolon kaynağı yok)
- **Canlı fazlası:** Rezervasyon Kaynağı/Şube ön-filtresiyle CANLI fiyat ızgarası görüntüleme + "Sadece
  Seçili Rezervasyon Kaynağını Sil" (mevcut fiyatları toplu silme) — bizde yalnız EKLEME/İÇE AKTARMA
  var, canlı ızgara görüntüleme+silme de sunuyor.
- **Bizde fazlası:** Beklemede-onay çiti (fiyat motoru onaysız tarifeyi kullanmaz) — canlıda yok, bizim
  ekstra güvenlik katmanı.
- **Not:** Kural 5 — her ikisi de dosyadan toplu fiyat aktarımı yapıyor (kanal/şube/araç grubu
  kapsamıyla); tutar/oran doğruluğu ve onay-akışı farkının yeterliliği Opus'a bırakıldı.

## xml_rez_kaynak_tedarikci.aspx — TürevRent (XML Rez. Kaynağı Tedarikçi Oranları)
- **Durum:** ❌ YOK
- **Bizde:** /rezervasyon-kaynaklari (`src/RentACar.Web/Components/Pages/ReservationSources/ReservationSourceList.razor`)
- **kanit:** /rezervasyon-kaynaklari · K2=%0 (0/5) · K3=DOĞRULANAMADI (kolon kaynağı yok)
- **Canlı fazlası:** Tedarikçi seçimi, Kira Oranı/Hizmet Oranı/Drop Oranı (kaynağa özel oran yansıtma,
  "Aşağıya Yansıt" ile tüm aktif kayıtlara toplu uygulama).
- **Bizde fazlası:** —
- **Not:** Bizim /rezervasyon-kaynaklari salt Kod/Ad/Durum sözlüğü; kaynak-bazlı oran/tedarikçi
  yapılandırması hiç yok — alan örtüşmesi sıfır, gerçek bir eşleşme yok (PARA kararı için bile bir
  temel bulunamadı).

---

TOPLAM: 33 ekran işlendi
