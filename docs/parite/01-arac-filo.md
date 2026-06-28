# Parite — Modül 01: ARAÇ & FİLO

> Kaynak: canlı `turev2.turevrac.com` HTML dump'ları (2026-06-28). Clone: `/Users/burak/Desktop/demo-apps/demo/src/`.
> Yalnız ekran/alan **yapısı** belgelenir; müşteri/kimlik verisi yok.
> Durum lejantı: ✅ var · 🟡 kısmi (eksik alanlar yazılı) · ❌ yok.
> Form alan adları WebForms `name="ctl00$ContentPlaceHolder1$X"` → kısaltılmış `X`. Gridler DevExpress (`dxgvHeader`).

---

## 0. Clone'daki araç/filo modeli (referans)

**`Domain/Entities/Vehicle.cs`** (yalnız ~18 alan):
`Plaka, Marka, Tip, Grup, Segment, Sipp, Renk, ModelYili, Vites, SasiNo, MotorNo, Sube, Durum (VehicleStatus), FiloDurum (FiloStatus), Km, Yakit (FuelType)` + audit.

- **`VehicleStatus`** (operasyonel): Stokta, Musait, Kirada, Serviste, Pasif, Satildi.
- **`FiloStatus`** (filo yaşam döngüsü = TürevRent `Arac_Status`): SifirKmStok, Havuz, Tahsis, Usk, Ksk, IkinciElSatis, Siparis. → **Canlı `Arac_Status` opsiyonlarıyla birebir eşleşir** (0 KM STOK / HAVUZ / TAHSİS / USK / KSK / 2.EL SATIŞ / SİPARİŞ). ✅
- İlgili sayfalar: `/vehicles` (VehicleList), `/araclar/{id}` (VehicleEdit — rota `araclar`, dosya `Vehicles/`), `/arac-durum` (FleetStatus), `/musaitlik` (MusaitlikArama), `/satislar` (VehicleSaleList).
- Ayrı master varlıklar (araca FK ile **bağlı değil**): `VehicleOwner` (/arac-sahipleri), `CustomCode` (/ozel-kodlar), `VehicleGroup`, `VehicleType`, `VehicleSegment`, `Brand`, `VehicleColor`, `FuelKind`, `TransmissionType`.

**Vehicle'da clone'da OLMAYAN kritik alan grupları** (canlı `arac_kayit`'te var):
- **Özel Kod 1..5** (`Ozel_Kod1..5`, dropdown) — ❌. Master (`CustomCode`) var ama araca bağlı değil.
- **Alış/satın-alma + fatura vergi**: `Alim_Bedeli`, `Alim_Bedeli_Kur`, `Alim_Fat_Tarih`, `Alim_Yapilan_Firma`, `Fatura_No`, `Fatura_Tutari_Vergisiz`, `Fatura_Tutari_OTV`, `Fatura_Tutari_KDV` — ❌.
- **Filo maliyeti**: `AylikMaliyet` (+ döviz), `FiloYonetimMaliyeti` — ❌.
- **2.El / güncel değer**: `Arac_2_Fiyat` (+kur), `Arac_Satis_KM`, `SimdiKur`, `Alis_Euro` — ❌.
- **Filo giriş/çıkış tarihleri**: `Filo_Giris_Tarihi`, `Filo_Cikis_Tarihi`, `Cikmasi_Planan_Tarih`, `Kapatma_Tarih` — ❌.
- **Araç sahibi bağı**: `Arac_Sahibi`, `Arac_Sahibi2`, `Arac_Sahibi_No`, `Sahip_Grup` — ❌ (Vehicle'da owner yok).
- **HGS/OGS**: `HGS_Firmasi`, `HGS_Numarasi`, `HGS_OGS`, `HGS_Servis_Yolu` — ❌ (HGS yansıtma servisi var ama araç kartında alan yok).
- **Sigorta/kasko/trafik/muayene tarihleri araç kartında**: clone'da bu veriler ayrı `InsurancePolicy`/`MtvRecord`/`InspectionRecord` varlıklarında; araç kartında inline alan ❌.
- **Ruhsat/tescil**: `Ruhsat_Belge_No`, `Ruhsat_Tarihi`, `Tescil_Tarihi` — ❌.
- **Teknik detay**: `Motor_Gucu`, `Silindir_Hacmi`, `Kasa_Tipi`, `Detay_Tipi` (Sedan/SUV/Van…) — ❌.
- **Operasyon kilitleri/bayraklar**: `Arac_Pasif_Edemez`, `Sube_Degistiremez`, `Kll_Plaka_Degistiremez`, `Web_Rez_Kapat`, `Ofis_Rez_Kapat` (+sebep), `Kontak_Kapat`, `Z_izni`, `UTTS`, `Seyrusefer`, `Kar_Lastigi`, `Yedek_Anahtar`, `Temizlik`, `Rehin_Durumu`, `Entegrasyon_Kodu`, `Teyp_Kodu` — ❌.
- **Bakım/lastik/km takip**: `Son_Bakim_*`, `Son_Per_*`, `Lastik_Bilgisi`, `Lastik_Uyari_Km`, `Kar_Lastigi`, `Km_Tespit_*`, `Kira_Km_Limiti` — ❌.

`Sipp` (SIPP/ACRISS) clone'da **var** ✅ (Vehicle.Sipp, 4 harf).

---

## 1. Araç kayıt & listeler

### 1.1 `arac_kayit.aspx` — Araç Kartı (master form)
**Amaç:** Tek araç için tüm filo/teknik/mali/sigorta/operasyon verisini tutan ana kayıt formu (~100 alan, sekmeli).

**Form alanları (görünür etiket → name):**
- Kimlik: Plaka * (`Plaka_X`), Marka (`Marka`, select), Tip (`Arac_Tipi`, select), Model(Yılı) (`Model`), Araç Grubu (`Grubu`, select) + Alt Grup (`Alt_Grup_Adi`, select), Detay (`Detay_Tipi` Sedan/Hatchback/Crossover/SUV/Van…), Renk (`Renk`), Vites (`Vites`, select), Yakıt Türü (`Yakit`/`Yakit_Turu`, select), Şasi No (`TxtSasi_No`), Motor No (`TxtMotor_No`), Kasa Tipi (`Kasa_Tipi`, select), Motor Gücü (`Motor_Gucu`), Silindir Hacmi (`Silindir_Hacmi`), SIPP (TSB Marka/Tip: `TSRB_Marka_Kodu`/`TSRB_Tip_Kodu`).
- **Filo statü/durum:** **Araç Status** (`Arac_Status`, select: 0 KM STOK / HAVUZ / TAHSİS / USK / KSK / 2.EL SATIŞ / SİPARİŞ), Durum=Aktif/Pasif (`Arac_Durumu`, select False/True), Pasif Etme Sebebi (`Pasif_Sebep`, select), Filo Giriş Tarihi (`Filo_Giris_Tarihi`), Filo Çıkış Tarihi (`Filo_Cikis_Tarihi`), Çıkması Planan Tar. (`Cikmasi_Planan_Tarih`), Kapatma Tarihi (`Kapatma_Tarih`), Lokasyon (`Konum`, select), Şube (`Islem_Sube`, select).
- **Özel Kodlar:** Özel Kod1..5 (`Ozel_Kod1..5`, hepsi select).
- **Mali/alış:** Alım Bedeli Vergisiz/ÖTV/KDV/Toplam (`Fatura_Tutari_Vergisiz`/`_OTV`/`_KDV` + `Alim_Bedeli`), Alım Tarihi/Kur (`Alim_Fat_Tarih`/`Alim_Bedeli_Kur`), Alım Fatura No (`Fatura_No`), Alım Firması (`Alim_Yapilan_Firma`), Aylık Maliyet (`AylikMaliyet`+`AylikMaliyetDoviz`), Filo Yönetim Maliyeti (`FiloYonetimMaliyeti`), Araç 2.El Fiyatı (`Arac_2_Fiyat`+kur), Şimdi Değer/Kur (`SimdiKur`), Ek (İlave) Fiyat (`EkFiyat`), Satış/Satışa-Çıkış KM (`Arac_Satis_KM`).
- **Sahip/kredi:** Araç Sahibi (`Arac_Sahibi`), Araç Sahibi2 (`Arac_Sahibi2`), Araç Sahibi Firma/No (`Arac_Sahibi_No`), Sahip Grup (`Sahip_Grup`), Kredi Firması (`Kredi_Firma`), Rehin Durumu (`Rehin_Durumu`, select).
- **Ruhsat/tescil:** Ruhsat Belge No (`Ruhsat_Belge_No`), Ruhsat Tarihi (`Ruhsat_Tarihi`), Tescil Tarihi (`Tescil_Tarihi`).
- **Sigorta/kasko/trafik/muayene:** Kasko Firma/Acenta/Poliçe/Bedeli/Baş.-Bit.Tar (`Kasko_*`), Trafik Sig. Firma/Poliçe/Baş.-Bit. (`Trafik_*`), Sigorta Firması/Acenta (`Sigorta_*`), Muayene Baş.-Bit. (`Muayene_*`).
- **HGS/OGS:** OGS/HGS Varmı (`HGS_OGS`, select), HGS Firması (`HGS_Firmasi`, select), HGS/OGS Numarası (`HGS_Numarasi`), Servis Yolu (`HGS_Servis_Yolu`).
- **Bakım/lastik/km:** Son KM (`KM`/`Son_Tespit_Km`), Son Bakım No/Km/Tar (`Son_Bakim_*`), Son Per. No/Km/Tar (`Son_Per_*`), Lastik Bilgisi/Uyarı KM/Son Lastik (`Lastik_*`/`Takip_*`), Kar Lastiği (`Kar_Lastigi`, select), Km Tespit No/Tar (`Km_Tespit_*`), Dış Tedarik/Kira Km Limiti (`Kira_Km_Limiti`).
- **Operasyon bayrakları/kilitler:** Web/Ofis Rezervasyonu + Kapatma Sebebi (`Web_Rez_Kapat`/`Ofis_Rez_Kapat` + `_Sebep`), Otomatik Motor Durdur (`Kontak_Kapat`), Z-İzni (`Z_izni`), UTTS Varmı (`UTTS`, select), Seyrüsefer (`Seyrusefer`), Temizlik Durumu (`Temizlik`, select), Yedek Anahtar (`Yedek_Anahtar`, select), Teyp Kodu (`Teyp_Kodu`), Entegrasyon Kodu (`Entegrasyon_Kodu`), Hizmet Veren Assist Firma (`Hizmet_Assist_Firma`), Periyodik Servis/Sigorta Takibi Dışarda (`Servis_Takip_Disarda`/`Sigorta_Takip_Disarda`, select), Açıklama (`Aciklama`).
**Aksiyon butonları:** Kaydet (`Up_Button1`), Yeni Araç Kaydı (`Button4`), Araç Listesi (`Button3`).

**Clone durumu: 🟡 kısmi.** `VehicleEdit.razor`/`VehicleList.razor` yaklaşık 18 temel alan tutar (Plaka, Marka, Tip, Grup, Segment, SIPP, Renk, ModelYılı, Vites, Şasi/Motor No, Şube, Durum, **Filo Status ✅**, KM, Yakıt). **Eksik (büyük):** Özel Kod1..5, tüm alış/fatura vergi (Vergisiz/ÖTV/KDV), Aylık/Filo maliyeti, 2.el-güncel değer, filo giriş/çıkış tarihleri, araç sahibi/kredi bağı, ruhsat/tescil, inline sigorta/kasko/trafik/muayene, HGS/OGS, bakım/lastik/km-takip, tüm operasyon bayrak/kilitleri, Detay tipi, Motor gücü/silindir hacmi, Kasa tipi. Sekmeli yapı yok.

### 1.2 `arac_listesi.aspx` — Araç Listesi
**Amaç:** Tüm filonun tek tablo görünümü (mali + filo + sigorta kolonlarıyla).
**Grid kolonları (seçme):** Plaka, Marka, Tipi, Grup, Grup Açıklama, Yılı, Renk, Vites, Yakıt Turu, SIPP, **Statü** (filo status), Pasif, Son KM, Son Yakıt, Lokasyon, **Alış Bedeli, 2. El Değeri, Kasko Bedeli, Alınan Firma, Kredi Kuruluşu / Son Tarih**, Araç Sahibi / -2 / Sahip Grup, Araç Belge No, Ruhsat Tarihi, Motor No, **HGS Numarası / HGS-OGS**, Kar Lastiği, Lastik Bilgisi / Uyarı KM, Teyp Kodu, Yedek Anahtar, Z-İzni, Seyrüsefer, Takip No, Sözleşme No, Filo Gir./Çık. Tar, **Baf, Satış, Servis, Detay** (aksiyon linkleri), Açıklama.
**Clone durumu: 🟡 kısmi.** `/vehicles` listesi kolonları: Plaka, Marka, Grup, Şube, Durum, KM, Yakıt + düzenle. Filtre: q (plaka/marka), grup, durum. **Eksik:** tüm mali kolonlar (alış/2.el/kasko bedeli), sahip/kredi, HGS, ruhsat, filo giriş/çıkış, statü kolonu (filo durumu listede gösterilmez), aksiyon linkleri (Baf/Satış/Servis).

### 1.3 `detayli_arac_listesi.aspx` — Detaylı Araç Listesi
**Amaç:** Araç + son/aktif kira + mali + sigorta birleşik geniş rapor.
**Grid kolonları (seçme):** Plaka, Marka, Model, Grup, Renk, Vites, Durum, Son Durum, Detay Tipi, 2.El, Alım Bedeli/Firma/Tarihi, Alış/Satış Euro Fiyat, Hedef Satış Bedeli, Aylık Maliyet, Ek Fiyat, Kira Fiyat/Gün/Bek.Tar/Bit.Tar, Kiralayan, Kira Müş.ID, Rez. Müşteri, Aracı Alan, Noter Satış Tarihi, Araç Sahip / Ruhsat Sahibi, Kredi Firma, HGS Firma/Numara, Assistan Firma, Belge No, Motor No, Muayene/Kasko/Sig. Bit. Tar, Dış Km Limit, Lastik, Pasif Sebep, Filo Gir./Çık. Tar.
**Clone durumu: ❌ yok.** Karşılığı yok (en yakın `/raporlar/filo` çok daha dar).

---

## 2. Durum / izleme ekranları

### 2.1 `arac_guncel_durum.aspx` — Araç Durum Listesi
**Amaç:** Filtrelenebilir anlık filo durum tablosu (aktif kira + mali + sigorta + baf bilgisiyle).
**Filtreler:** Aranacak Plakalar/Gruplar, Araç Aktif/Pasif, Araç Durumları, **Araç Status**, Araç Pasif Sebebi, Araç Tipi, Marka, Vites Türü, Yakıt Türü, Şubeler, GPS Durumu, HGS/OGS Durumu, Kar Lastiği Durumu, Kira Bakiyesi, Ofis/Web Rez. Durumu, Tarih Filtreleri, Detaylar.
**Grid kolonları (seçme):** Plaka, Marka, Model, Araç Grubu, Grup Özel Kod, Araç Status, Durum, Araç Sahibi, Alım Bedeli/Tar., 2.El, Baf / Baf Türü / Baf Çıkış Tar, Bayi Firma/Fiyat, Kira Fiyat/Kalan, Kiralama Türü, Firma/Cep Tel, Dosya No, Baş.-Bit. Tar/Saat, Ofis / Dönüş Ofisi/Yeri, GPS No, Kasko/Muayene Bit. Tar., Filo Giriş/Çıkış Tar., Motor No, Kirala (aksiyon).
**Clone durumu: 🟡 kısmi.** `/arac-durum` (FleetStatus) yaklaşık eşdeğer fakat dar: kolonlar Plaka, Marka/Tip, Grup, Filo, Durum, Müşteri (aktif kira), Bitiş, Bakiye, KM, Şube; filtre q/durum/filo/grup/marka. **Eksik:** mali kolonlar, baf/bayi/GPS/HGS/kasko-muayene kolonları, çoğu filtre (status detay, pasif sebep, GPS/HGS/kar lastiği/rez durumu, tarih).

### 2.2 `arac_gunluk_durum.aspx` — Araç Günlük Listesi
**Amaç:** Belirli güne ait araç bazlı günlük kira/hizmet/toplam dökümü.
**Filtreler:** Plaka, Araç Grubu, Araç Sahibi, Şube.
**Grid kolonları:** Plaka, Marka/SIPP, Araç Grubu, Araç Sahibi/İsim, Müşteri, Ofis, Baş.-Bitiş Tar, Günlük Kira, Günlük Hizmet, Günlük Toplam.
**Clone durumu: ❌ yok.**

### 2.3 `arac_durum_takip.aspx` — Araç Hareket Raporu
**Amaç:** Araç başına gün dağılımı (dolu/boş/bakım/baf gün) ve potansiyel gelir analizi.
**Filtreler:** Plaka, Araç Grupları, Araç Sahibi, Çıkış Şubesi, RentTo Durumu, Gruplama Tipi, Excel'e Aktarım Modu.
**Grid kolonları:** Plaka, SIPP, Gün, Boş Gün, Bakım Gün, **Baf Gün**, Potansiyel.
**Clone durumu: ❌ yok.** (`/raporlar/filo` doluluk verir ama gün-türü kırılımı ve potansiyel yok.)

### 2.4 `arac_genel_durumu_grafik.aspx` — Otopark Durumu (grafik)
**Amaç:** Filonun durum/şube dağılımının grafik (otopark) görünümü.
**Clone durumu: ❌ yok.**

### 2.5 `karsilastirmali_durum_analizi.aspx` — Karşılaştırmalı Analiz Raporu
**Amaç:** Şube/dönem bazlı karşılaştırmalı filo durum analizi.
**Filtreler:** Çıkış Şube (+ grid pivot — "Drag a column here").
**Clone durumu: ❌ yok.**

### 2.6 `kabis_raporu.aspx` — Kabis Listesi
**Amaç:** KABİS (Emniyet kiralama bildirim sistemi) bildirim kayıt/sonuç listesi.
**Grid kolonları:** Id, Zaman, Plaka, Müşteri, RA (no), Sonuç, İşlem.
**Clone durumu: ❌ yok.** (KABİS entegrasyonu yok — stub bile değil.)

---

## 3. Müsaitlik / boş araç

### 3.1 `bos_arac_listesi.aspx` — Boş Araç Listesi
**Amaç:** Şu an boştaki (kirada olmayan) araçlar + boşta kalma süresi.
**Grid kolonları:** Plaka, Marka, Tipi, Grup, Grup Özel Kod, SIPP, Renk, Vites, Yakıt/Türü, Yılı, Şube, Lokasyon, Son Km, Ruhsat/Tescil Tar., Temizlik, Kar Lastiği, **Boştaki Süresi**, Baş. Tar., Müşteri, Rezerve, **Kirala** (aksiyon).
**Clone durumu: 🟡 kısmi.** Boşa özel ekran yok; `/arac-durum` (FleetStatus) durum filtresiyle yaklaşık. **Eksik:** "boştaki süresi", temizlik/kar lastiği/lokasyon kolonları, hızlı "Kirala" aksiyonu.

### 3.2 `musait_arac_listesi.aspx` — Müsait Araç Listesi (fiyatlı)
**Amaç:** Tarih aralığında kiralanabilir araçları **fiyat/provizyon/drop ile** listeler (rezervasyon başlatma).
**Grid kolonları:** Arac, Grup, SIPP, Vites, Yakıt Türü, Yaş, Boş, Dolu, Durum, Km Limiti, Ehliyet, **Fiyat, Drop Bedeli, Provizyon, Toplam Fiyat, Döviz**, Gün, Rez ID, Sube Adı.
**Clone durumu: 🟡 kısmi.** `/musaitlik` (MusaitlikArama) tarih+grup+şube ile müsait araç listeler ama **fiyatsız** (kolonlar: Plaka, Marka, Grup, Şube, Durum). **Eksik:** fiyat/drop/provizyon/toplam/döviz, km limiti, ehliyet, yaş, boş/dolu gün — yani fiyat motoru bağı (bilerek ertelenmiş).

### 3.3 `musaitlik_durum.aspx` — Müsaitlik Raporu
**Amaç:** Tarih aralığı/grup bazlı müsaitlik (matris/takvim tarzı) raporu.
**Clone durumu: 🟡 kısmi.** `/musaitlik` arama var; matris/rapor görünümü yok.

---

## 4. Plan / takvim

### 4.1 `arac_plan_yonetim.aspx` — Araç Artırma ve Azaltma İşlemleri
**Amaç:** Grup/SIPP bazında dönemsel kapasite (filo adedi) artırma-azaltma planlaması.
**Form alanları:** Araç Grubu, SIPP, Miktar, İşlem Baş-Bit. Tarih, İşlem Şube, İşlemi Yapan Personel, Açıklama.
**Clone durumu: ❌ yok.**

### 4.2 `arac_rac_takvim.aspx` — Araç Çalışma Takvimi
**Amaç:** Araç/grup bazlı çalışma (müsait/dolu) takvim görünümü.
**Filtreler:** Tarih, Araç Grupları, Şubeler.
**Clone durumu: ❌ yok.** (Clone'da `/takvim` = ReservationCalendar **rezervasyon** takvimi; araç çalışma takvimi değil — farklı kapsam.)

---

## 5. Araç satış

### 5.1 `arac_satis.aspx` — Araç Satış İşlemleri
**Amaç:** Filodan çıkan aracın satış kaydı (cari borçlanma, noter, ihale, satış aşaması).
**Bölümler/alanlar:** Araç Bilgileri (Plaka/Marka/Model/Tipi/Renk/Vites/Yakıt — readonly), **Satış Aşaması** (durum stage), Tarih, Satış Tarihi, Satış Fiyatı, **Satış KDV Oranı**, Hedef Fiyat, Satış KM, Satışa Çıkış KM, Cari Bilgi (alıcı), Noter Bilgisi, Yevmiye Bilgisi, **Devir İşlemi**, Satış Kanalı, Satış Noktası, Satış Kampanyası, İhale Firması/Sayısı/Tarihi, 1. & 2. Açıklama, Durum.
**Clone durumu: 🟡 kısmi.** `VehicleSale.cs` + `/satislar` (VehicleSaleList): No, Araç, Alıcı Cari, Satış Net, KDV Oranı/Tutar, Genel Toplam, Noter No, Döviz/Kur, Açıklama, Durum (Tamamlandi/Iptal). Çift-taraflı defter postlama var. **Eksik:** Hedef Fiyat, Satış KM/Satışa-Çıkış KM, Satış Kanalı/Noktası/Kampanyası, İhale Firması/Sayısı/Tarihi, Devir İşlemi, Satış Aşaması (stage akışı), Yevmiye bağı.

### 5.2 `arac_satis_ara.aspx` — Satıştaki Araçlar
**Amaç:** Satışta/satılan araçların arama listesi (mali + sigorta detayıyla).
**Filtreler:** Araç Plaka, Tarih, Şube, Durum, Devir, Page size.
**Grid kolonları (seçme):** Plaka, Marka, Model, Tip, Renk, Vites, Yakıt, Şasi, Motor No, Tescil Tarihi, KM, Araç 2.El, Detay Tipi, Araç Sahibi, Liste/Hedef/Satış Fiyatı, Alım Bedeli, Aracı Alan, Sat. Tar, Satıldı, Devir İşlemi, Geçen Süre, Kayıt No, Ofis, Kredi Firma, Kasko Poliçe/Tutar/Bit.Tar, Trafik Poliçe/Tutar/Bit.Tar, İhale Firması/Tarihi.
**Clone durumu: 🟡 kısmi.** VehicleSaleList'in tablo kısmı (No/Tarih/Araç/Alıcı/Net/KDV/Toplam/Durum) dar karşılık. **Eksik:** sigorta/kredi/ihale/devir/geçen-süre kolonları, plaka/tarih/şube/devir filtreleri.

### 5.3 `arac_satis_bedeli.aspx` — Araç Satış Hesaplama (kârlılık)
**Amaç:** Araç/grup bazında gelir-gider-kazanç + doluluk + potansiyel kârlılık hesaplama raporu.
**Filtreler:** Plaka, RentTo Durum, Excel'e Aktarım Modu.
**Grid kolonları:** Plaka/Marka/Tipi/SIPP/Vites/Yakıt, Araç Grubu/Türü, Araç Sayısı, Söz. Sayısı, Cari Bilgi, Baş.-Bit. Tar., Gün/Toplam Gün/Kira Gün, Doluluk, **Gelir, Gider, Kazanç, Bakiye, Ortalama, Potansiyel, Toplam Kira, Sonuç**.
**Clone durumu: ❌ yok.** (En yakın `/raporlar/filo` doluluk verir; araç-bazlı gelir/gider/kazanç/potansiyel yok.)

---

## 6. Araç sipariş (tedarik)

### 6.1 `arac_siparis.aspx` — Araç Sipariş
**Amaç:** Yeni araç tedarik/satınalma siparişi (bayi teklif → onay → temin akışı).
**Form alanları:** Dosya Numarası, Tarih, Teklif/Temin/İmza Tarihi, Cari Bilgi (tedarikçi), Arac Grubu/Tipi, Marka, Model, Versiyon, Renk / İç Renk, Vites, Yakıt Türü, Opsiyon, **Liste/Piyasa/Filo/Ops/Onay Fiyatı**, Öneri/Onay Adet, Kaynak Tip, Satış Tipi, Genel/Özel Temsilcisi, Şube, Durum.
**Grid kolonları:** Kayıt No, Tarih, Tek./Tem. Tarihi, Grup, Marka, Model, Tipi, Versiyon, Renk, Vites, Yakıt, Yılı, Liste/Onay Fiyat, Önerilen, Onay, Son KM, Şasi No, Şube, Durum, Detay.
**Clone durumu: ❌ yok.** (Sipariş/tedarik modülü hiç yok; `FiloStatus.Siparis` enum değeri var ama akış/ekran yok.)

### 6.2 `arac_siparis_listesi.aspx` — Araç Sipariş Listesi (özet)
**Amaç:** Sipariş dosyalarının özet listesi.
**Grid kolonları:** ID, Tarih, Dosya No, Cari Bilgi.
**Clone durumu: ❌ yok.**

### 6.3 `arac_siparis_detay_listesi.aspx` — Araç Sipariş Detay Listesi
**Amaç:** Sipariş satır (araç) bazlı detay liste.
**Grid kolonları:** ID, Tarih, Dosya No, Cari Bilgi, Marka, Model, Araç Tipi, Grubu, Versiyon, Renk, Vites, Yakıt, Opsiyon, Liste/Piyasa/Filo/Ops/Onay Fiyat, Önerilen, Onaylanan, Teklif/Temin Tarih.
**Clone durumu: ❌ yok.**

---

## 7. Araç kredi (finansman)

### 7.1 `arac_kredi.aspx` — Araç Kredi
**Amaç:** Araç alımına bağlı banka kredisi + taksit planı kaydı.
**Form alanları:** Dosya Numarası, Tarih, Cari Bilgi (banka/finans), Toplam Kredi Borcu, Ana Para Toplam, Faiz Toplam, Taksit Adeti/Sayısı, Taksit Tutarı/Toplam, Bu Ayki Toplam Taksit, Vade Günü, İlk Vade Tarih.
**Clone durumu: ❌ yok.**

### 7.2 `arac_kredi_listesi.aspx` — Araç Kredi Listesi
**Amaç:** Kredi dosyaları listesi.
**Filtreler:** Araç Ara / Cari Ara, Ad & Soyad, Araç Bilgisi.
**Grid kolonları:** Kredi ID, Tarih, Dosya No, Cari Bilgi, Kredi Tutarı.
**Clone durumu: ❌ yok.**

### 7.3 `kredi_takip_listesi.aspx` — Kredi Takip Listesi
**Amaç:** Aktif kredilerin taksit/vade/kalan bakiye takibi.
**Filtreler:** Plaka Ara, Cari Ara, Araç Bilgi.
**Grid kolonları:** Plaka, Cari Bilgi, Toplam Kredi Borç, Taksit Tutarı, Vade, Kalan Bedel.
**Clone durumu: ❌ yok.**

---

## 8. BAF işlemleri (⚠ adlandırma çakışması)

> **DİKKAT:** Clone'da "BAF" = **Hasar Dosyası** (`DamageFile`, No=`BAF-000001`, onay akışı, `/hasar`). TürevRent'in `baf_islemleri`'i ise **bambaşka** bir şey: personelin filo aracını alıp-iade ettiği **araç görevlendirme/çıkış-dönüş** kaydı. İkisi karışmamalı — clone'un BAF'ı bu ekranları KARŞILAMAZ.

### 8.1 `baf_islemleri.aspx` — BAF İşlemleri (araç çıkış/dönüş)
**Amaç:** Personele filo aracı tahsisi: çıkış (km/saat/tarih/yakıt/şube) → dönüş (km/saat/tarih/yakıt/şube) kaydı.
**Bölümler/alanlar:** Araç Bilgisi (Marka/Model/Tipi), Personel Bilgisi (Ad/Soyad/Personel No), Kullanım Amacı, Çıkış İşlemleri (Çıkış KM/Saati/Tarihi/Yakıt/Şube), Dönüş İşlemleri (Dönüş KM/Saati/Tarihi/Yakıt/Şube), Onaylayan, İşlemi Yapan, Tarih, Durum, Açıklama.
**Clone durumu: ❌ yok.** (Clone'un DamageFile/BAF'ı bu değil; personel araç çıkış/dönüş kaydı yok.)

### 8.2 `baf_ara.aspx` — Baf Listesi
**Amaç:** BAF (çıkış/dönüş) kayıtlarının arama listesi.
**Filtreler:** Plaka Ara, Personel Ara, Durum, Kullanım Amacı.
**Grid kolonları:** Kayıt No, Tarih, Plaka, Marka, Tipi, Kullanıcı, Kullanım Şekli, Baş. Tar/Saat, Tes. Tar/Saati, Yapılan KM, Çıkış Şube, Dönüş Şube, İşlem Yapan, Durum, Açıklama.
**Clone durumu: ❌ yok.**

---

## Özet tablo

| # | Ekran (canlı) | Amaç | Clone | Kritik eksik |
|---|---------------|------|-------|--------------|
| 1 | arac_kayit | Araç kartı (master ~100 alan) | 🟡 | Özel Kod1-5, alış/fatura vergi (Vergisiz/ÖTV/KDV), aylık+filo maliyeti, 2.el-güncel değer, filo giriş/çıkış tarihleri, sahip/kredi bağı, ruhsat/tescil, HGS/OGS, inline sigorta/kasko, bakım/lastik/km takip, operasyon kilitleri |
| 2 | arac_listesi | Filo tablo görünümü | 🟡 | Mali/sahip/kredi/HGS/statü kolonları, aksiyon linkleri |
| 3 | detayli_arac_listesi | Araç+kira+mali birleşik | ❌ | Tüm ekran |
| 4 | arac_guncel_durum | Anlık durum (filtreli) | 🟡 | Mali/baf/GPS/HGS kolonları, çoğu filtre |
| 5 | arac_gunluk_durum | Günlük kira/hizmet dökümü | ❌ | Tüm ekran |
| 6 | arac_durum_takip | Hareket raporu (gün kırılımı) | ❌ | Dolu/boş/bakım/baf gün, potansiyel |
| 7 | arac_genel_durumu_grafik | Otopark grafik | ❌ | Tüm ekran |
| 8 | karsilastirmali_durum_analizi | Karşılaştırmalı analiz | ❌ | Tüm ekran |
| 9 | kabis_raporu | KABİS bildirim listesi | ❌ | KABİS entegrasyonu yok |
| 10 | bos_arac_listesi | Boş araç + boşta süre | 🟡 | Boşta süresi, temizlik/lokasyon, hızlı kirala |
| 11 | musait_arac_listesi | Müsait araç (fiyatlı) | 🟡 | Fiyat/drop/provizyon/döviz, km limiti (fiyat motoru) |
| 12 | musaitlik_durum | Müsaitlik raporu (matris) | 🟡 | Matris/rapor görünümü |
| 13 | arac_plan_yonetim | Filo artır/azalt planı | ❌ | Tüm ekran |
| 14 | arac_rac_takvim | Araç çalışma takvimi | ❌ | (clone /takvim = rezervasyon takvimi, farklı) |
| 15 | arac_satis | Araç satış işlemi | 🟡 | Hedef fiyat, satış KM, kanal/nokta/kampanya, ihale, devir, satış aşaması |
| 16 | arac_satis_ara | Satıştaki araçlar arama | 🟡 | Sigorta/kredi/ihale/devir kolonları, filtreler |
| 17 | arac_satis_bedeli | Araç kârlılık hesaplama | ❌ | Gelir/gider/kazanç/potansiyel araç-bazlı |
| 18 | arac_siparis | Araç tedarik siparişi | ❌ | Sipariş/tedarik modülü yok |
| 19 | arac_siparis_listesi | Sipariş özet liste | ❌ | Tüm ekran |
| 20 | arac_siparis_detay_listesi | Sipariş detay liste | ❌ | Tüm ekran |
| 21 | arac_kredi | Araç kredi + taksit planı | ❌ | Kredi/finansman modülü yok |
| 22 | arac_kredi_listesi | Kredi dosya listesi | ❌ | Tüm ekran |
| 23 | kredi_takip_listesi | Kredi taksit/vade takip | ❌ | Tüm ekran |
| 24 | baf_islemleri | Personel araç çıkış/dönüş | ❌ | clone BAF=Hasar dosyası, farklı |
| 25 | baf_ara | BAF kayıt arama | ❌ | Tüm ekran |

**Dağılım (25 ekran):** ✅ 0 · 🟡 8 · ❌ 17.

**Filo statü notu:** TürevRent'in 3-katmanlı durum modeli — (a) Aktif/Pasif (`Arac_Durumu`), (b) Filo yaşam döngüsü (`Arac_Status`: 0KM STOK/HAVUZ/TAHSİS/USK/KSK/2.EL/SİPARİŞ), (c) operasyonel kira durumu. Clone (b)+(c)'yi `FiloStatus`+`VehicleStatus` ile **doğru ayırmış** ✅; (a) Aktif/Pasif ise `VehicleStatus.Pasif` içine gömülü (ayrı bayrak + pasif sebep yok).
