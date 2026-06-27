# TürevRent — Ekran Envanteri & Klonlama Referansı

> **Amaç:** Klonladığımız canlı TürevRent (turev2.turevrac.com) sisteminin GERÇEK ekran/alan/filtre
> yapısı. Otonom plan (gece çalışması) ve gelecek oturumlar için tek doğruluk kaynağı.
> **Kaynak:** Canlı sayfalar (ÜMİT YÜCE oturum çerezi ile curl), 3 Excel export (Araç/Kira/Müşteri),
> giriş formları (Yeni Kira/Araç/Cari, Araç Grubu Tanımı). 2026-06.
> **Durum işaretleri:** ✅ bizde var · 🟡 kısmi · ❌ yok.

## 0. Büyük resim
- Canlı sistemde **158 ekran** (.aspx). Bizde ~28 ekran + 8 REST kaynağı → **dürüst kapsam ~%35-40**.
- Veri modeli BİZİMKİNDEN KAT KAT zengin: **Araç ~50 alan / 65 kolon**, **Cari ~50 alan / 42 kolon**,
  **Kira ~50 form alanı / 170 kolon**. Bizde sırasıyla ~7 / ~15 / ~25.
- En büyük yapısal eksikler: **Araç Status** (filo: stok/havuz/tahsis/2.el — kira durumundan AYRI),
  **SIPP/ACRISS** birinci sınıf gruplama, **Araç Grubu = fiyat-kural master'ı** (sürücü yaşı/provizyon/
  muafiyet/KM limiti), **ek hizmet ekonomisi** (onlarca paketli hizmet), **kurumsal faturalama**
  (Müşteri/Firma Ödemeli + komisyon), **CRM** (risk/uyarı/İYS/doğum günü/temsilci), **global hızlı arama**.

## 1. Ekran envanteri (modül modül, 158 ekran)

### Araçlar — listeler/durum
- ✅🟡 **Araç (Güncel) Durum** `Arac_Guncel_Durum.Aspx` — operasyon kalbi; 16 filtre (aşağıda).
- ❌ Araç Günlük Durum `Arac_Gunluk_Durum.aspx`
- 🟡 Tüm Araçlar `Arac_Listesi.Aspx` (Grup/SIPP toggle, Araç Sahibi Bizim/Dış)
- ❌ Detaylı Araç Listesi `Detayli_Arac_Listesi.Aspx`
- ❌ Boş Araçlar `Bos_Arac_Listesi.Aspx` · KM Detay `Bos_Km_Detay.aspx` · Tarih Araç Raporu `Bos_Arac_Raporu.aspx`
- ❌ Araç Hareket Raporu `Arac_Durum_Takip.aspx` · Araç Park Durumu (grafik) `Arac_Genel_Durumu_Grafik.Aspx`
- ✅🟡 Yeni Araç `Arac_Kayit.Aspx` (50 alan — bizde 7)
- ❌ Araç Artır&Azalt `Arac_Plan_Yonetim.aspx` · Çalışma Takvimi `Arac_Rac_Takvim.aspx`
- ❌ Sipariş: Liste/Detay/Yeni `Arac_Siparis*` · Mobil Teslimat/Ödeme `Mobil_Teslimat/Odeme`

### Kira
- ✅🟡 Tüm Sözleşmeler `Kira_Listesi.Aspx` (19 filtre — aşağıda) · Bugün Çıkanlar (filtre)
- ✅ Yeni Kira `Kiralama.Aspx` (50 alan)
- ❌ Talep Listesi `Kira_Talep_Ara.aspx`
- ❌ Filo Sözleşmeleri `Filo_Kiralama_Listesi.Aspx` · Yeni Filo Kiralama `Filo_Arac_Kiralama.Aspx`

### Rezervasyon
- ✅ Rezervasyon Listesi `Rezervasyon_Listesi.aspx` · Yeni Rezervasyon `Rezervasyon.Aspx`
- ✅🟡 Müsaitlik-Rez Açma `Broker_Musaitlik_Listesi.Aspx` · Fiyat Verme `Musait_Arac_Listesi.Aspx` · Raporu `Musaitlik_Durum.Aspx`
- ❌ Web/Broker Rezervasyon `Web_Rezervasyon.aspx`

### Cariler (Müşteri) — CRM ağırlıklı
- ✅🟡 Cari Listesi `Musteri_Genel_Liste.Aspx` (kolon: Ad/Tel/TC/Pasaport/Şehir/Cari Kart/**Kaynak/Adet/Ciro/son-kira**)
- 🟡 Müşteri Listesi `Musteri_Listesi.aspx`
- ✅🟡 Yeni Bireysel/Kurumsal Cari `Musteri_Kayit.Aspx` (50 alan — bizde 15)
- ❌ Müşteri Analiz (CRM) `Musteri_CRM.Aspx`
- 🟡 Cari Borçlandırma `Bakiye_Islem.Aspx` · Borçlandırma Listesi `Bakiye_Islem_Ara.Aspx`
- 🟡 Cari Virman `Cari_Virman.aspx` + Listesi · Hesap Extresi `Hesap_Extresi.aspx` · Extre Özeti `Extre_Ozeti.aspx`
- ❌ Genel Borç & Alacak `Genel_Borc_Alacak.aspx`

### Servis & Bakım
- ✅🟡 Servis & Bakım Listesi `Gider_Ara.Aspx` · Yeni Servis `Arac_Servis_Islemleri.Aspx`
- ❌ Periyodik Servis Raporu `Periyodik_Servis_Raporu.aspx` · Servis Rezervasyonu `Servis_Rezervasyon.aspx`
- ❌ Otomatik Servisler `Otomatik_Servisler.aspx`

### Baf & Satış
- ✅🟡 Baf Listesi `Baf_Ara.aspx` · Yeni Baf `Baf_Islemleri.aspx`
- ✅🟡 Satış Listesi `Arac_Satis_Ara.aspx` · Yeni Satış `Arac_Satis.aspx` · Detay `Fatura_Detay_Listesi.aspx`
- ❌ Araç Satış Hesaplama `Arac_Satis_Bedeli.aspx` · Maliyet `Maliyet_Hesaplama.Aspx` + Liste
- ❌ Yeni Araç Kredi `Arac_Kredi.Aspx` · Kredi/Takip Listesi

### Ceza & HGS
- ✅🟡 Ceza & Geçiş Listesi `Ceza_Listesi.aspx` · Yeni Ceza `Cezalar.aspx`
- 🟡 Hgs Geçiş Listesi `HGS_Gecis_Listesi.aspx` · Trafik Ceza `Ceza_Gecis_Listesi.aspx`
- ❌ Hukuk Birimi/Dosyaları `Hukuk_*`

### Sigorta / MTV / Muayene
- ✅🟡 Sigorta & Muayene `Sigorta_Muayene.Aspx` · Yeni Sigorta/MTV/Muayene Gideri `Arac_*_Islemleri.Aspx`
- ❌ Sigorta İşlemleri `Sigorta_Gider_Ara.Aspx` · Sigorta Tarifeleri `Sigorta_Tarife_Listesi.aspx`

### Gider
- ✅ Yeni Genel Gider `Gider_Islemleri.Aspx`
- ❌ Toplu Gider `Toplu_Gider.Aspx` · Araç Gelir `Arac_Gelir_Gider_Tablosu.Aspx`

### Kasa & Banka
- ✅🟡 Nakit Tahsilat `Nakit_Islem.Aspx` + Listesi · Nakit Kasa Durumu `Genel_Kasa.Aspx`
- 🟡 K.Kartı Tahsilatı `Banka_Islemleri.Aspx` + Listesi
- ✅🟡 Banka Virman `Banka_Virman.aspx` + Listesi · Kasa Virman `Kasa_Virman.aspx` · Hesap Hareketleri
- ❌ Para Giriş/Yatırma (+Listeleri) · Toplu/Otomatik Tahsilat · Banka Entegrasyonu `Dis_Banka_*`

### Fatura
- ✅🟡 Yeni Satış Faturası `Fatura.aspx` · Satış Fatura Listesi `Fatura_Islem_Listesi.aspx`
- ❌ Gelen E-Faturalar `Gelen_E_Fatura_Listesi.aspx` · Fatura Dönemleri `Fatura_Donem_Raporu.aspx`

### Tanımlamalar (master) — çoğu ❌
- ❌ **Araç Grubu Tanımı** `Arac_Grubu.Aspx` (fiyat-kural master; 30 alan, SIPP/segment/limit) — KRİTİK
- ❌ Araç Segment `Arac_Segment.Aspx` · Marka `Marka_Tanim.Aspx` · Tip `Arac_Tipi_Tanimlama.Aspx`
- ❌ Araç Sahip Grubu `Arac_Sahibi.aspx` · Özel Kod `Ozel_Kod_Tanim.Aspx`
- ✅ Şube `Sube_Tanimlama.Aspx` · ✅ Lokasyon `Lokasyonlar.Aspx`
- 🟡 Genel Tarife (NRM) `Tarifeler.Aspx` (bizde RateCard kısmi)
- ❌ Tarife Grubu `Fiyat_Grup_Tanimlama.aspx` · Döviz `Para_Tanimlama.Aspx` · Gider `Gider_Tanimlama.Aspx`
- ❌ Kasa/Banka Hesap/Kod Tanım · İptal Sebep `Iptal_Sebepleri.Aspx` · Rezervasyon Kaynağı
- ❌ Periyodik Km Sınırları `Servis_Tanim_Tablosu.Aspx` (Marka/Tip/Yakıt/Vites→Periyodik KM)
- ❌ Kiralama/Kurumsal Şartları · Kupon/Kampanya Yönetimi

### Raporlar — çoğu ❌ (bizde 5 rapor)
Genel Rapor, Günlük Faaliyet `GunRaporu`, Dönem Analizi, Doluluk `Doluluk_Grafik`, Gelir Tablosu,
KDV Listesi, Tahsilat Fatura, Ek Hizmet Raporu `Extralar_Raporu`, Rez Kaynak, Periyodik Servis,
Kabis, Araç Hareket, Genel Borç&Alacak, Hesap/Extre.

### Fiyatlama / Broker / Web (entegrasyon — ertelenmiş)
Dinamik Fiyatlama `Doluluk_Algoritma`, Fiyat Kampanya, Rakip Fiyat Analiz, XML Tarife, Broker (XML)
ayarları, Web Site Yönetimi, Stop Sell, Tablet Yönetimi, SMS, Anket, Şikayet, Assistans, Kabis.

### Personel / Kullanıcı / Sistem
- ❌ Personel Listesi/Yeni/Çalışma Tablosu (bizde personel YOK)
- ✅🟡 Yetki Grupları `Kullanicilar.Aspx` · 🟡 Log/Ayarlar/Şifre

## 2. En çok kullanılan sayfaların filtreleri (canlıdan)
- **Araç Güncel Durum (16):** Durum(Boş/Kirada/Bakımdaki+Talepler/Baftaki/Satıştakiler), **Araç Status**
  (0KM STOK·HAVUZ·TAHSİS·USK·KSK·2.EL SATIŞ·SİPARİŞ), Tarih boyutu(Çıkması Planan/Çıkış/Giriş/Çalışma/
  **Sigorta Bitiş/Kasko Bitiş**), Grup(segment ekli), Marka, Vites, Yakıt, Kar Lastiği, Pasif Sebep
  (Satış/Pert/Çalınma/Plaka Değ.), Ofis, Kira Bakiye göster.
- **Kira Listesi (19):** Kira Durum(Kirada·RentTo·Döndü·İptal), **Fatura Durum**(Faturalanmış/-mamış),
  **Kiralama Türü**(Kısa·Uzun·Ikame·Aylık), Tarih boyutu(Başlangıç/Bitiş/Taksit/Kapatma/Opsiyon), Ofis
  boyutu(İşlem/Çıkış/Dönüş/Bölge), Sahip(Bizim/Dış), Tablet, Evrak, **dinamik kolon seçici**.
- **Cari Listesi:** Cari Tipi(Bireysel/Kurumsal/**Uyarı Liste**/**İzinli Liste İYS**/Doğum Günü), Durum.
- **Global hızlı arama** (her sayfada): Müşteri·Plaka·RA No·TC·Dosya No.

## 3. Gerçek değer listeleri (enum kaynağı)
- **Araç Status:** 0 KM STOK · 2.EL SATIŞ · HAVUZ · KSK · SİPARİŞ · TAHSİS · USK
- **Pasif Sebep:** Satış · Pert · Çalınma · Plaka Değişikliği · Diğer
- **Kira Durum:** Kirada · RentTo · Döndü · İptal · (RentTo = kiraya çevrilmiş rez?)
- **Kiralama Türü:** Kısa Kiralama · Uzun Kiralama · Ikame · Aylık
- **Fiyat Türü:** KDV Dahil Günlük · (Dosya Tipi: Net)
- **Ödeme Şekli:** Kredi Kartı · Nakit · Açık Hesap …
- **Cari Tipi:** Bireysel · Kurumsal · Uyarı Liste · İzinli Liste (İYS) · Doğum Günü Liste/Uyarı
- **Araç Grubu örneği:** `FİAT-EGEA-MANUEL-DİZEL-Ekonomik` (Marka-Tip-Vites-Yakıt-**Segment**)
- **SIPP örneği:** `CDMD` (ACRISS 4-harf), **Vites:** Otomatik/Düz, **Temizlik:** Temiz/Kirli

## 4. Kritik entity alan açıkları (form'lardan)
- **Araç** (50 alan): + Tip, Alt Grup, Kasa Tipi, Segment, Renk, Şasi/Motor No, Ruhsat/Tescil Tarih,
  **Araç Status**, Web/Ofis Rezervasyon kapatma+sebep, OGS/HGS No+Firma, Teyp/Motor Gücü/Silindir,
  UTTS, Periyodik Servis Takibi, **gömülü sigorta/muayene/kasko Baş-Bit tarihleri + poliçe/firma/acenta**.
- **Cari** (50 alan): + Gsm2, **Risk Mesajı/Tarihi/İzinli**, **HGS Yansıtma Türü**, Ehliyet(Tür/Ülke/No/
  Tarih/Yer/Sınıf), Bayi Komisyon Oranı, Kurumsal No/Şifre, **Uyarı/Nedeni/Zamanı**, Bakiye Görebilir,
  Müşteri Temsilcisi, Müşteri Sınıfı/Türü, Tevkifat, Konuşma Dili, Fatura Adresi Farklı, İşlem Şube.
- **Kira** (50 form + 170 kolon): + Faturalama Türü(Müşteri/Firma Ödemeli), Müşterinin Firması, Hesaplama
  Tipi/Döviz, İndirim, Opsiyon, Vade/Fatura Dönem+Profili, Beyan Bilgisi/Limiti, Vale/Lastik, Hediye Gün,
  **onlarca ek-hizmet** (Bebek/Çocuk Koltuğu, Navigasyon, Wifi, Ek Sürücü, SCDW/Muafiyetli/Mini Hasar/IMM,
  Genç Sürücü, Kış Lastiği, Geçiş/HGS, Drop, Üyelik, LCF, **Paket Hizmet1-6**), komisyon (Alacağımız/
  Fatura/Ödenen), Teslim Eden/Alan, Gerçek/Planan Teslimat, Anket, Üyelik/Puan.
- **Araç Grubu Tanımı** = fiyat-kural master: Sürücü Yaşı/Genç, Ehliyet Yılı, **Provizyon/Muafiyet Ücreti
  (×2)**, Yakıt Fiyatı, **Günlük/Max KM Limiti + Aşım KM Fiyatı**, SIPP, Koltuk/Kapı/Bagaj, Segment, Kasa
  Türü, Web/Servis ID, Grup Kodu. → Bizim RateCard'tan çok daha zengin; KM/muafiyet/yaş kuralları burada.

## 5. Önerilen klonlama fazları (otonom plan için)
Backend-first çizgisinde, her biri API + test (para-dokunan → adversarial):
1. **Model zenginleştirme** (en yüksek değer, çoğunu açar): Vehicle (Status/Segment/SIPP/Tip/sigorta-tarih
   alanları), Customer (risk/uyarı/ehliyet/temsilci/kurumsal), Araç Grubu master (kural seti).
2. **Araç Güncel Durum birleşik grid + 16 filtre** (API) + dashboard Dönüşler/Çıkışlar kovaları.
3. **Ek hizmet ekonomisi** (PR #40 + paket hizmetler + kurumsal faturalama bölme + komisyon).
4. **Kira Listesi filtreli (19) + dinamik kolon** + Cari CRM agrega/İYS/uyarı.
5. **Tanım master'ları:** Marka/Tip/Segment/Sahip/İptal-Sebep/Rez-Kaynak/Periyodik-KM/Döviz.
6. **Raporlar** (Günlük Faaliyet, Doluluk, KDV, Ek Hizmet, Dönem Analizi…).
7. **Ertelenmiş (kimlik/parite gerek):** e-Fatura/GİB, banka entegrasyon, broker/XML, web rez, SMS, KABİS.

> NOT: Para kuruş yuvarlaması ve Gün_Hesapla parite kalibrasyonu canlı TürevRent'le yapılmadı (403/erişim);
> "makul varsayılan". Bu erişim çerezi süreliyken (curl) ek sayfa/değer çekilebilir.
