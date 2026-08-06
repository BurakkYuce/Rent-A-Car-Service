# 08-sistem — ayarlar/kullanıcı/yetki/log/web yönetimi/entegrasyon (15 ekran)

**Durum dağılımı:** ✅ TAM=0 · 🟡 KISMİ=1 · ❌ YOK=6 · ⚠ ERİŞİLEMEZ=0 · 🩹 CANLI BOZUK=4 · ❓ DOĞRULANAMADI=3 · PARA=1

**Genel not:** Bu modülün canlı profillerinden 4'ü (`log_kayit`, `web_log_kayit`, `evrak_listesi`, `yetki_reset`)
canlıda HTTP 302 ile `Default.aspx`'e düşüyor — kapalı/boş özellik, "bizde yok" parite borcu ÜRETİLMEDİ,
`🩹 CANLI BOZUK` işaretlendi. `cikis.aspx` de içerik üretmiyor (redirect) ama bu ÇIKIŞ (logout) ekranının
normal davranışı — kodda karşılığı doğrulandı, `❓ DOĞRULANAMADI` (kıyaslanacak alan/kolon yok) + açıklayıcı not.

---

## ayarlar.aspx — Ayarlar
- **Durum:** ❌ YOK
- **Bizde:** /ayarlar (`Settings/Ayarlar.razor`) + /belge-sablonlari (`Settings/BelgeSablonList.razor`, Design/HTML/Preview sekmesinin karşılığı)
- **kanit:** /ayarlar · K2=%8 (12/148) · K3=%50 (1/2, "Dosya Listesi"≈şablon listesi; "Menü" eşleşmiyor)
- **Canlı fazlası:** Kapsamı çok geniş — kategorilere göre: (1) Görsel tema/renk kodları (Renk_Limit_Bakiye, Renk_Opsiyonlu, Renk_Alacakli, Renk_Bugun_Donecekler, Renk_Bugun_Cikacaklar, Renk_Rez_Atanan_Plaka, Renk_Gecikenler, Renk_Kiralanmayan); (2) SMS entegrasyonu (SMSTuru, SMS_Kll_Adi/Sifre, SMSTokenUsername/Password, Rez_Gun_Once, Kira_Bitis_Onc_Saat, 6 SMS şablon metni); (3) Fiyatlandırma/muhasebe kuralları (Fiyat_Turu, Yakit_Seviyesi, Sistem_Doviz, Tarife, Drop_Mesafe_Yok_Sifir, Saat_Farki, Iade_Islem_Saat); (4) Banka/Pos hesap seçimleri (Online_Hesap_No, Provizyon_Hesap_No, Sanal_Hesap_No, EURO_Hesap_No, Garanti_Pos_Hesap); (5) Ceza/Geçiş kod eşleme (Ceza_Kodu, Gecis_Kodu); (6) ~45 iş-kuralı checkbox'ı (Cari_Tek, TCMB_Otomatik, TC_Dogrula, Baf_Bakim_Cikart, Farkli_Nokta_Biralabilir, Fatura_Donem_Yaz, SCDW_Dahil/CDW_Dahil/TP_Dahil, vb.); (7) Sigorta/ek-hizmet açıklama+max-gün metinleri (PAI/IMM/Muafiyet/Genç Sürücü/Bebek Koltuğu/Navigasyon/Mini Hasar/SCDW/LCF/Ek Sürücü); (8) RTF şablon dosya yükleme (UcResimler) + Find/Replace editörü.
- **Bizde fazlası:** WhatsApp günlük özet (whatsAppNumarasi/whatsAppGunlukOzet), dönemsel faturalama job ayarları, PDF logo baskı-uygunluk kontrolü, halka açık site aç/özel domain ekleme — canlının bu ekranında karşılığı yok.
- **Not:** Canlı tek büyük "ayarlar" ekranında firma+SMS+fiyat+sigorta+muhasebe kurallarını topluyor; bizde bu firma/SMTP/entegrasyon-placeholder+public-site'a daralmış, KDV/pricing/sigorta kuralları ayrı modüllerde (TenantSettings, fiyat motoru) veri olarak var olabilir ama BU ekranda kullanıcı arayüzü yok — gerçek genişlik farkı.

## cikis.aspx — Object moved
- **Durum:** ❓ DOĞRULANAMADI
- **Bizde:** `POST /auth/logout` (form: `Components/Layout/MainLayout.razor:251`, uç: `Identity/AuthEndpoints.cs:45`) — ayrı `.razor` sayfası yok, katmanlı çıkış ucu.
- **kanit:** /auth/logout · K2=n/a (canlı alan=0) · K3=n/a (canlı kolon=0, kolon_kaynak=yok)
- **Canlı fazlası:** —
- **Bizde fazlası:** —
- **Not:** Canlı ekran içerik üretmiyor (302 redirect) — bu ÇIKIŞ ekranının normal davranışı, "canlı bozuk" değil. Bizde fonksiyonel karşılığı kodda doğrulandı (cookie sign-out + redirect) ama canlı tarafta kıyaslanacak alan/kolon olmadığından oran hesaplanamaz.

## default.aspx — TürevRent
- **Durum:** ❌ YOK
- **Bizde:** / (`Pages/Home.razor`)
- **kanit:** / · K2=%0 (0/9) · K3=%14 (3/21 — Plaka, Şube, Müşteri≈Ad Soyad eşleşiyor; `Shared/DcTable.razor` başlıkları: Tarih/Saat, Ad Soyad, Plaka, Şube)
- **Canlı fazlası:** 24 sekmeli BI/rapor süiti (Doluluk, Şube Karşılaştırma, Gelir Raporu, Hizmet Kalemleri, Şube Performans, Araç Grubu Analizi, Filo Yapısı, Rez. Kaynağı, İptal Analizi, Kaynak Performans, Lokasyon, Süre Analizi, Tek Yön, Dönemsel vb.) + Excel/Grup Excel/Şube Excel export + şube arama kutusu + tarih-aralığı kısayolları (Son 7/15/30/60 gün, Bu Ay, Geçen Ay).
- **Bizde fazlası:** Filo KPI şeridi (Doluluk/RevPACD/ADR, ömür-boyu havuz), 6-ay gelir mini-trend grafiği, vade uyarı rozetleri, site-talep kutusu, dönüş satırında hızlı tahsilat formu — canlının bu tek ekranında yok (muhtemelen başka ekranlarda).
- **Not:** Canlı Default.aspx bir BI-dashboard'u (çoğu sekme muhtemelen `/raporlar/*` altında AYRI ekranlarımızla — bu modülün kapsamı dışında — karşılık buluyor); bizim `/` sadece operasyonel KPI+dönüş/çıkış panosu, literal alan/kolon örtüşmesi düşük.

## evrak_listesi.aspx — Object moved
- **Durum:** 🩹 CANLI BOZUK
- **Bizde:** /belge-sablonlari (`Settings/BelgeSablonList.razor`) — ad benzerliğiyle en yakın aday, doğrulanamadı
- **kanit:** — · K2=n/a (canlı 302→Default.aspx, alan=0) · K3=n/a (kolon_kaynak=yok)
- **Canlı fazlası:** —
- **Bizde fazlası:** —
- **Not:** Canlıda kapalı/boş özellik (302→Default.aspx) — parite borcu üretilmedi. Ad benzerliği "evrak" (belge) ile bizim /belge-sablonlari arasında olabilir ama canlı içerik üretmediği için doğrulanamaz.

## fonksiyonlar.aspx — (başlık yok)
- **Durum:** ❓ DOĞRULANAMADI
- **Bizde:** — (aday bulunamadı)
- **kanit:** — · K2=n/a (canlı alan=0) · K3=n/a (kolon_kaynak=yok)
- **Canlı fazlası:** —
- **Bizde fazlası:** —
- **Not:** Profilde başlık/alan/kolon hiç yakalanamamış (tip=diğer, tüm sayımlar 0) — muhtemelen JS/AJAX tabanlı bir yardımcı bileşen ya da yetki gerektiren stub. Kanıt yok, tahmin yapılmadı.

## globalsearch.aspx — (başlık yok)
- **Durum:** ❓ DOĞRULANAMADI
- **Bizde:** /ara (`Search/Ara.razor`, menude=hayır) — ad/amaç benzerliğiyle güçlü aday, doğrulanamadı
- **kanit:** /ara · K2=n/a (canlı alan=0) · K3=n/a (kolon_kaynak=yok)
- **Canlı fazlası:** —
- **Bizde fazlası:** —
- **Not:** Canlı profilinde hiç içerik yakalanamamış (muhtemelen header'daki AJAX arama kutusu, statik crawl'a düşmüyor). Bizde plaka/cari/kira/rez/fatura no arayan `/ara` var ve amaçça örtüşüyor ama kanıtsız "✅" yazılamaz — kural 6 gereği "bilmiyorum".

## kullanicilar.aspx — Kullanıcılar
- **Durum:** ❌ YOK
- **Bizde:** /kullanicilar (`Users/UserList.razor`)
- **kanit:** /kullanicilar · K2=%5 (7/132) · K3=%29 (2/7 — Kullanıcı, Şube eşleşiyor; Kullanıcı Adı/Yetki Adı/Grup/"Kullanıcıya Git"/toplu-şube-işaretle eşleşmiyor)
- **Canlı fazlası:** Kullanıcı formunda gömülü **~100 kişiye-özel menü/izin checkbox'ı** (Dash_Doluluk, Tanimlar, Arac_Tanimla, Islemler, Yeni_Kiralama, Nakit_Islemler, Cari_Raporlar, Parametreler, Web_Yoneticisi vb. — her menü öğesi başına ayrı toggle); Digital_Imza_Mail, Sms_Onay/Sms_Telefon, Kasa_Kodu, Personel_Kodu, Entegrasyon_Kod1; şube/durum'a göre filtre; "Yetki Kopyalama" (kullanıcıdan kullanıcıya).
- **Bizde fazlası:** Aktif/Pasifleştir toggle'ı liste satırında doğrudan buton (canlıda ayrı bir akış olabilir, profilde görünmüyor).
- **Not:** Bizde temel kullanıcı CRUD (oluştur/listele/parola sıfırla/şube ata) var ve doğru örtüşüyor, ama canlının hacmini oluşturan per-KULLANICI ~100 menü-checkbox modeli bizde YOK — bilinçli mimari farkı (CLAUDE.md §4: rol+izin-matrisi + `/yetki` ekran-override, kullanıcı-bazlı değil). `/yetki` en yakın analog ama granülerlik modeli temelden farklı (rol+ekran vs kullanıcı+menü-öğesi) — doğrudan alan eşleşmesi yok.

## log_kayit.aspx — Object moved
- **Durum:** 🩹 CANLI BOZUK
- **Bizde:** /denetim (`Audit/AuditList.razor`) — isim benzerliğiyle en yakın aday
- **kanit:** — · K2=n/a (canlı 302→Default.aspx, alan=0) · K3=n/a (kolon_kaynak=yok)
- **Canlı fazlası:** —
- **Bizde fazlası:** —
- **Not:** Canlıda kapalı/boş özellik (302→Default.aspx) — parite borcu üretilmedi. Bizim `/denetim` (audit log) gerçek ve çalışan bir özellik, ama canlı log_kayit.aspx içerik üretmediğinden karşılaştırma yapılamaz.

## mobil_odeme.aspx — TürevRent
- **Durum:** PARA — OPUS'A DEVİR
- **Bizde:** /kasa (`Finance/KasaHub.razor`) — en yakın aday (genel kasa/banka hareket listesi)
- **kanit:** /kasa · K2=n/a (canlı alan=0, salt liste) · K3=%67 (4/6 — Kayıt No≈No, Tarih≈Tarih, Ad Soyad≈Cari, Tutar≈Tutar eşleşiyor; Açıklama≈Açıklama de eşleşiyor [5/6 sayılırsa %83]; Ödeme Kodu≈Tip TAM eşleşmiyor — farklı kavram)
- **Canlı fazlası:** Mobil/tablet kanalından yapılan tahsilatların ayrı görünümü, "Ödeme Kodu" (ödeme yöntemi kodu).
- **Bizde fazlası:** Hesap/Cari/Makbuz kolonları, tüm kasa/banka hareketleri (yalnız mobil değil).
- **Not:** Tutar üreten/gösteren bir tahsilat ekranı — kural 5 gereği karar verilmedi. `/kasa` genel ledger'ı kapsıyor ama mobil/tablet kanalına özel bir filtre/etiket bizde tespit edilemedi (Opus doğrulasın).

## mobil_teslimat.aspx — Mobil Teslimatlar
- **Durum:** ❌ YOK
- **Bizde:** — (aday bulunamadı)
- **kanit:** — · K2=n/a (canlı alan=1, `Rac_Tablet_Say` hidden — tablet sayacı) · K3=%0 (0/4)
- **Canlı fazlası:** Kayıt No, Çıkış Zamanı, Teslim Zamanı, Çıkış Ofisi — mobil/tablet tabanlı teslimat oturumu takibi (saha ekibinin tablet üzerinden yaptığı teslimat kaydı).
- **Bizde fazlası:** —
- **Not:** TürevRent'in ayrı bir saha/tablet uygulaması ekosistemine ait (`Rac_Tablet_Say` alanı bunu doğruluyor); bizde ayrı bir mobil teslimat-oturumu listesi yok. Kira mega-formundaki dönüş/teslim alanları farklı bir şey (kontrat-içi, oturum-listesi değil) — gerçek bir genişlik farkı.

## sifre_degistir.aspx — Şifre Değişikliği
- **Durum:** ❌ YOK
- **Bizde:** /kullanicilar (`Users/UserList.razor`, `POST /kullanicilar/sifre` — admin parola sıfırlama)
- **kanit:** /kullanicilar · K2=%20 (1/5 — Yeni_Sifre≈password; Eski_Sifre ve Sifre_Tekrari eşleşmiyor) · K3=n/a (kolon_kaynak=yok, grid içermeyen form ekranı)
- **Canlı fazlası:** Eski Şifre (doğrulama) alanı, Yeni Şifre Tekrarı (confirm) alanı — kullanıcının KENDİ oturumunda kendi parolasını eski parolayı doğrulayarak değiştirdiği self-service akış.
- **Bizde fazlası:** —
- **Not:** Bizde yalnız ADMİN'in başka bir kullanıcının parolasını sıfırladığı akış var (`/kullanicilar`, eski parola sorgusu YOK); kullanıcının kendi oturumunda kendi parolasını değiştirdiği self-service ekran yok — farklı iş akışı, gerçek eksik.

## web_log_kayit.aspx — Object moved
- **Durum:** 🩹 CANLI BOZUK
- **Bizde:** — (aday belirsiz — muhtemelen web sitesi erişim logu)
- **kanit:** — · K2=n/a (canlı 302→Default.aspx, alan=0) · K3=n/a (kolon_kaynak=yok)
- **Canlı fazlası:** —
- **Bizde fazlası:** —
- **Not:** Canlıda kapalı/boş özellik (302→Default.aspx) — parite borcu üretilmedi.

## web_rezervasyon.aspx — Web (Acente) Rezervasyonları
- **Durum:** 🟡 KISMİ
- **Bizde:** /rezervasyonlar (`Bookings/ReservationList.razor`)
- **kanit:** /rezervasyonlar · K2=n/a (canlı alan=0, salt liste — aksiyonlar yalnız genel UI kromu: Vazgeç/boyut/tema düğmeleri) · K3=%33 (3/9 — Kayıt No≈No, Durum≈Durum, Tarih≈Tarih eşleşiyor)
- **Canlı fazlası:** Teslim Tarihi (ayrı kolon — bizde Tarih hücresine gömülü), Ad + Soyad (ayrı kolonlar — bizde birleşik "Müşteri"), Cep Telefonu (kolon olarak YOK), Alış Şube (kolon olarak YOK — entity'de `CikisOfisi` var ama grid'e yansımıyor), Rezervasyon Kaynağı (kolon olarak YOK — formda `Kaynak` seçimi VAR ama listeye yansımıyor); ayrıca canlı ekran özel olarak Web/Acente KAYNAKLI rezervasyonlara filtrelenmiş, bizde böyle bir alt-liste/filtre yok (tüm rezervasyonlar karışık).
- **Bizde fazlası:** Araç/Plaka kolonu, Gün, Tutar, Onayla/Kiraya Çevir/İptal/Düzenle aksiyonları, Excel/CSV/PDF export — canlının bu ekranında yok.
- **Not:** Aynı varlık (Reservation) üzerinde çalışıyoruz ama canlı salt-okunur bir kanal-filtreli izleme ekranı, bizimki tam CRUD'lu genel liste; Kaynak/Ofis/Telefon bilgisi entity'de var ama bu grid'e kolon olarak eklenmemiş.

## web_site_yonetimi.aspx — Web Sayfanızın Yönetimi
- **Durum:** ❌ YOK
- **Bizde:** /web-sitesi (`WebSite/WebSiteHub.razor`) + /site-icerik (`WebSite/SiteIcerikYonetim.razor`) + /blog-yonetim (`Blog/BlogList.razor`)
- **kanit:** /web-sitesi · K2=%1 (2/181 — `site-icerik`'in `metaAciklama` alanı Web_Seo_Description'a gevşek karşılık geliyor; net 2. eşleşme yok) · K3=%3 (1/29 — "Durum" kavramı genel olarak tekrar ediyor ama canlının somut kolonlarıyla [TR/EN/DE/FR/RU, Puan, A.Kelimeler, Link, Gsm, Konu vb.] doğrudan eşleşme yok)
- **Canlı fazlası:** Çok-dilli site yönetimi (Web_Language_*, 5 dil), anasayfa slider/görsel yönetimi (Web_Slider_*), "Neden Biz" bölümü (Web_WhyWe_*), müşteri yorumları/testimonial (Web_Comment_*), site iletişim/firma bilgileri (Web_Ayar_Unvan/Adres/Telefon/Whatsapp/Harita/Telif/sosyal medya x5), tema renkleri (Web_Ayar_Color_*), logo/favicon yükleme, ödeme/pos görünürlük ayarları (Ofiste Öde, Sanal Pos), arama kutusu boyutu, lokasyon fırsatları + zamanlama kampanyaları, sayfa-bazlı SEO (Web_Seo_* — başlık/açıklama/anahtar kelime/görsel/dil), özel menü oluşturucu (Web_Menu_* — 12 tip, 301 yönlendirme dahil), çeviri sözlüğü (Web_Translate_*), sigorta/ek-hizmet pazarlama blurb'ları (Web_Fuse_*, Web_AddService_*), iletişim formu mesaj kutusu.
- **Bizde fazlası:** Araç ilanı bazlı vitrin yönetimi (fiyat/özellik/foto başına ilan), statik CMS sayfa+SSS editörü, blog yazı editörü (kapak görseli, taslak/yayın) — canlının bu ekranında karşılığı görünmüyor (muhtemelen ayrı ekranlarda).
- **Not:** Canlı, çok-dilli tam bir tema/CMS/SEO/menü yönetim paneli; bizim web-sitesi modülü araç-vitrini + basit statik-sayfa/SSS + blog'a odaklı — tek-dilli, tema/renk/slider/testimonial/SEO-per-page/menu-builder hiçbiri yok. Gerçek ve büyük bir genişlik farkı.

## yetki_reset.aspx — Object moved
- **Durum:** 🩹 CANLI BOZUK
- **Bizde:** /yetki (`Authorization/Yetki.razor`) — isim benzerliğiyle en yakın aday
- **kanit:** — · K2=n/a (canlı 302→Default.aspx, alan=0) · K3=n/a (kolon_kaynak=yok)
- **Canlı fazlası:** —
- **Bizde fazlası:** —
- **Not:** Canlıda kapalı/boş özellik (302→Default.aspx) — parite borcu üretilmedi. Bizim `/yetki` ekranı (rol+ekran override matrisi) gerçek ve çalışıyor, ama canlı yetki_reset.aspx içerik üretmediğinden karşılaştırma yapılamaz.

---

TOPLAM: 15 ekran işlendi
