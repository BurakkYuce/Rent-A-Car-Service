# Parite — Sistem / Entegrasyon / Web / Mobil / Otomasyon

> Kaynak: canlı `turev2.turevrac.com` HTML'leri (2026-06-28 taraması), `parite_html/`.
> Karşılaştırma hedefi: `src/` (RentACar clone).
> NOT: Yalnız ekran/alan **yapısı** belgelenir; kimlik/müşteri verisi yazılmaz.
> Form alan adları gerçek `id`/`name` (ASP.NET: `ctl00$ContentPlaceHolder1$X`, kısaltıldı → `X`).

Bu modül = TürevRent'in **çevresi**: kullanıcı/yetki, sistem ayarları, dış dünyaya açılan
tüm entegrasyon uçları (XML/broker/tedarikçi, e-Fatura, SMS, sanal POS/banka, mobil tablet,
web sitesi CMS, uzak erişim) ve otomasyon (zamanlanmış servisler). Clone'da çekirdek
(kullanıcı/yetki/denetim) **var**; entegrasyonların tamamı **stub**; web/mobil/CMS/global
arama/ayarlar ekranı **yok**.

---

## Özet tablo

| # | Ekran | Tür | Clone | Not |
|---|-------|-----|:-----:|-----|
| 1 | `kullanicilar.aspx` | Sistem — kullanıcı + ~110 checkbox yetki matrisi | 🟡 | Bizde 4 rol × 4 izin; onlarda ekran-bazlı granüler + yetki grubu + kopyalama |
| 2 | `ayarlar.aspx` | Sistem — firma/sistem ayarları (170 alan) + tüm entegrasyon kimlikleri | ❌ | Clone'da ayar ekranı/entity yok |
| 3 | `globalsearch.aspx` | Sistem — global hızlı arama (JSON API) | ❌ | Clone'da global arama yok |
| 4 | `web_site_yonetimi.aspx` | Web — çok-dilli site CMS (turevweb.com) | ❌ | Clone'da web sitesi/CMS yok |
| 5 | `tabletyonetim.aspx` | Mobil — teslim/dönüş tablet uygulaması yönetimi | ❌ | Clone'da tablet/mobil yok |
| 6 | `turevuzak.aspx` | Sistem — uzak erişim (AnyDesk) | ❌ | Clone'da yok |
| 7 | `serbest_sms.aspx` | Entegrasyon — serbest (manuel) SMS gönderimi | 🟡 | `ISmsService` stub var, ekran yok |
| 8 | `otomatik_servisler.aspx` | Otomasyon — zamanlanmış servis çalıştırma | ❌ | Clone'da scheduler yok |
| 9 | `xml_firma_tanim.aspx` | Entegrasyon — XML/broker partner firma tanımı + katsayılar | ❌ | Clone'da yok |
| 10 | `xml_disardan_arac.aspx` | Entegrasyon — dış XML aracını yerel araca eşleme | ❌ | Clone'da yok |
| 11 | `xml_disardan_sube.aspx` | Entegrasyon — dış XML şubesini yerel şubeye eşleme | ❌ | Clone'da yok |
| 12 | `xml_fiyat_aktar.aspx` | Entegrasyon — dosyadan toplu fiyat içe aktar | ❌ | Clone'da yok |
| 13 | `xml_rez_kaynak_tedarikci.aspx` | Entegrasyon — rez kaynak → tedarikçi + fiyat oranı | ❌ | Clone'da yok |
| 14 | `dis_banka_entegrasyon_listesi.aspx` | Entegrasyon — dış banka hareket entegrasyonu | ❌ | Canlıda **Runtime Error** döndü; ekran var, içerik alınamadı |
| 15 | `log_kayit.aspx` | Sistem — kullanım/işlem log kaydı | 🟡 | Canlıda **302** (erişim engelli); clone'da AuditLog var |
| 16 | `web_log_kayit.aspx` | Sistem — web (site) log kaydı | ❌ | Canlıda **302**; clone'da web log yok |

Durum: ✅ tam · 🟡 kısmi/altyapı var · ❌ yok.

---

## 1. `kullanicilar.aspx` — Kullanıcı + Yetki Matrisi

**Amaç:** Program kullanıcılarını tanımlamak ve **ekran/aksiyon bazında granüler yetki**
vermek. Yetki grubu (template), kullanıcı bazlı override ve "başka kullanıcının yetkisini
kopyala" mekanizması var.

**Kullanıcı alanları (form):**

| Etiket | name (`...$X`) |
|--------|------|
| Kullanıcı Adı | `Kullanici_Adi` |
| Şifre | `Sifre` (+ gizli `Sifre_Asil`) |
| Ad Soyad Bilgileri | `Kullanici_Bilgisi` |
| Dijital İmza Eposta | `Digital_Imza_Mail` |
| SMS Onay (checkbox) | `Sms_Onay` |
| SMS Telefon | `Sms_Telefon` |
| Mail Adresi | `Mail_Adresi` |
| Yetki Grubu | `Grup_ID` (+ `Grup`) |
| İşlem Şube | `Islem_Sube` |
| Kasa Kodu | `Kasa_Kodu` |
| Personel (Kodu) | `Personel_Kodu` |
| Entegrasyon Kod1 | `Entegrasyon_Kod1` |
| Web Yöneticisi (checkbox) | `Web_Yoneticisi` |

**Filtreler (liste):** Aktif & Pasif durumuna göre, Şubelerine göre. **Liste:** `Kullanici_Lst`.
**Butonlar:** `Up_Button1` = "Kaydet"; `Button2` = "Seçili Kullanıcının Yetkilerini -" (yetki
**kopyalama**); `PersonListRefresh_Button`.

**Yetki matrisi — ~110 checkbox** (her menü/aksiyon ayrı izin). Başlıca gruplar:

- **Dashboard:** `Dash_Doluluk`, `Dash_Rez`
- **Tanımlar:** `Arac_Tanimla`, `Cari_Tanimlama`, `Personel_Tanim`, `Menu_Gider_Tanim`,
  `Menu_Kasa_Tanim`, `Subeler`, `Ozel_Kod`(*)…
- **İşlemler:** `Yeni_Kiralama`, `Yeni_Rezervasyon`, `Yeni_Bakim`, `Yeni_Baf`,
  `Yeni_Arac_Satis`, `Yeni_Ceza_Gecis`, `Filo_Kiralama`, `Serbest_SMS`, `BrokerMusaitlik`,
  `MusaitlikFiyat`, `Diger_Arac_Islemleri`, `Menu_Arac_Artir_Azalt`
- **Fatura:** `Menu_Islemler_Fatura`, `Menu_Islemler_Satis_Fatura_Ekle/Listesi`,
  `Menu_Islemler_Alis_Fatura_Ekle/Listesi`, `Menu_Islemler_Cek_Senet`,
  `Menu_Islemler_Diger_Gelirler`
- **Nakit:** `Nakit_Tahsilat`, `Nakit_Odeme`, `Kasa_Virman`
- **Bakiye:** `Cari_Borclandir`, `Cari_Alacaklandir`
- **Banka:** `Gelen_Havale`, `Kart_Tahsilat`, `Giden_Havale`, `Para_Yatirma`, `Para_Cekme`
- **Gider:** `Genel_Gider`, `Arac_Giderler`
- **Listeler:** `Tum_Araclar`, `Bos_Arac_Listesi`, `Kiradaki_Araclar`, `Acik_Sozlesme`,
  `Kapali_Sozlesme`, `Tum_Sozlesmeler`, `Rezervasyon_Listesi`, `Satis_Listesi`,
  `Baf_Listesi`, `Bakim_Listesi`, `Gecmis_Bakim_Listesi`, `Ceza_Gecis_Listesi`,
  `Sigorta_Muayene_Listesi`, `Cari_Kart_Listesi`, `Nakit/Banka/Bakiye_*_Listesi`…
- **Raporlar:** `Kasa_Raporu`, `Gelir_Gider_Tablosu`, `Cari_Raporlar`, `Genel_Borc_Alacak_Listesi`,
  `Km_Detay_Raporu`, `Kabis_Raporu`, `Donem_Analizi`, `Extra_Raporlari`, `Bakim_Periyodik_Rapor`,
  `Arac_Calisma_Tablosu`, `Arac_Park_Durumu`
- **Menü/sistem yetkileri:** `Menu_Yetkilendirme`, `Menu_Ayarlar`, `Menu_XML_Tarife`,
  `Menu_Stop_Sell`, `Menu_Rez_Yonetim`, `Menu_Sigorta_Tarife`, `Menu_Tarife_Genel/Grubu/Listesi`,
  `Menu_Kredi_Dosyasi`, `Menu_Lokasyon_Drop_Lst`, `Menu_Kiralama_Sartlari`, `Broker_Ayarlari`,
  `Kara_Liste_Menu`, `Anket_Sistemi`, `Rez_Kaynak_Mutabakat`

**Entegrasyon türü:** yok (saf sistem ekranı). İçinde entegrasyon-ilintili izinler:
`Serbest_SMS`, `Menu_XML_Tarife`, `Broker_Ayarlari`, `BrokerMusaitlik`.

**Clone durumu — 🟡 yapısal fark büyük.** Bizde `User` = `UserName, PasswordHash, DisplayName,
IsActive, Rol, AtanmisSube`; yetki = **4 sabit rol** (Admin/Yönetici/Operatör/Muhasebe) ×
**4 izin** (`ManageUsers/OperationsWrite/FinanceWrite/ViewReports`), `RolePermissions` matrisi.
Eksik kavramlar: ekran-bazlı ~110 granüler izin, **yetki grubu (template)**, **yetki kopyalama**,
`Web_Yoneticisi` rolü, kullanıcı üzerinde SMS Onay/Telefon, Dijital İmza Eposta, Kasa Kodu,
Personel Kodu, Entegrasyon Kod1 alanları. (Bizde şube kapsamı `AtanmisSube` ile var ✅.)

---

## 2. `ayarlar.aspx` — Firma / Sistem Ayarları (entegrasyon kimlik merkezi)

**Amaç:** Firma bilgileri, görünüm/renk kuralları, ek hizmet limitleri ve **tüm dış
entegrasyon kimlik bilgileri** tek ekranda (170 kontrol; sekmeli). Bu ekran clone'un en
büyük yapısal boşluğu: entegrasyonların **konfigürasyon zemini**.

**Firma/iletişim:** `Firma_Adi`, `Adres`, `Sehir`, `Ilce`, `Telefon`, `Telefon2`, `Fax`,
`Mobil1`, `Merkezi_Numara`, `Mail_Adresi`, `Web_Sitesi`, `Harita_Konum` (lat,long),
`Facebook`, `Instagram`, `Twitter`.

**Entegrasyon kimlikleri / uçları (ÖNEMLİ):**

| Entegrasyon | Alanlar |
|-------------|---------|
| **e-Fatura** | `E_Fatura_Pass` (+ `Fatura_Tipi`, `Fatura_Donem_Yaz`, `Fatura_Tahsilat`) |
| **SMS gateway** | `SMSTuru` (sağlayıcı), `SMSTokenUsername`, `SMSTokenPassword`, `SMS_Kll_Adi`, `SMS_Kll_Sifre`, `SMS_Baslik` (başlık/sender) |
| **SMS tetikleri** | `Otomatik_SMS`, `Opsyionlu_Sms_Gonderme`, `Sms_Rez`, `SMS_Kira_Basla`, `Sms_Kira_Bitis`, `Sms_Kira_Bitis_Uyari`, `Rez_Sube_SMS`, `Teslimatci_SMS`, `Anket_Mail` |
| **SMTP / mail** | `Mail_SMTP_Adres`, `Mail_Port`, `Mail_SSL`, `Mail_Kullanici_Adi`, `Mail_Kullanici_Sifre` (+ "Test Mail Gönder" butonu, `Iptal_Mail_Haber`) |
| **Sanal POS / ödeme** | `Garanti_Pos_Hesap`, `Odeme_Link_Sanal_Pos` (ödeme linki), `Sanal_Hesap_No`, `Provizyon_Hesap_No` (provizyon/depozit hold), `Online_Hesap_No` |
| **Banka hesapları** | `EURO_Hesap_No`, `Nakit_No`, `Hesap_No_Zorunlu` |
| **Web/XML kanalı** | `Web_Sitesi`, `Web_Rez_Goren`, **"Web XML Hazırla"** butonu (giden XML feed üretimi) |
| **Döviz (TCMB)** | `TCMB_Otomatik` (otomatik kur çekme), `Sistem_Doviz` |
| **Kimlik doğrulama** | `TC_Dogrula` (TC kimlik / NVI doğrulama) |

**Ek hizmet / ürün limitleri:** `Bebek_Koltugu`(+`_Aciklama`, `Max_Cocuk_Koltuk`), `Ek_Surucu`
(`Max_Ek_Surucu`), `Navigasyon` (`Max_Navigasyon`), `Mini_Hasar`/`Mini_Hasar_Dahil`
(`Max_Mini_Hasar`), `SCDW`/`SCDW_Dahil` (`Max_SCDW`), `LCF` (`Max_LCF`), `Wifi` (`Max_Wifi`),
`Genc_Surucu`, `CDW_Dahil`, `Muafiyet_Sigortasi`, `PAI`, `TP`, `IMM` (+ her birine `_Aciklama`).

**Operasyon kuralları:** `Farkli_Nokta_Biralabilir` (farklı nokta drop), `Drop_Mesafe_Yok_Sifir`,
`Fazla_KM_Uyar`, `Kira_Bitis_Onc_Saat`, `Iade_Islem_Saat`, `Mukkerer_Soz_No`, `Musteri_Kontrol`,
`Teslim_Alan_Eden_Zorunlu`, `Rez_Gun_Once`, `Rez_Gerceklesti_Kapat`, `Otomatik_Ceza_Indirimli`.

**Görünüm/renk kuralları:** `Renk_Gecikenler`, `Renk_Bugun_Cikacaklar/Donecekler`,
`Renk_Opsiyonlu`, `Renk_Limit_Bakiye`, `Renk_Alacakli`, `Renk_Kiralanmayan`,
`Renk_Rez_Atanan_Plaka` + HTML editör (sözleşme/footer şablon metni).

**Butonlar:** "Kaydet", "Test Mail Gönder", "Web XML Hazırla".

**Clone durumu — ❌ yok.** Clone'da firma/sistem ayar entity'si ya da ekranı **yok**; bu
değerler ya hard-coded ya da hiç yok. Entegrasyon kimlikleri için saklama yeri yok → tüm
adapter'lar stub. **Çok-kiracılı SaaS için en kritik açık:** tenant başına ayar tablosu
(`TenantSettings`) gerekli.

---

## 3. `globalsearch.aspx` — Global Hızlı Arama (JSON API)

**Amaç:** Üst menüdeki tek arama kutusundan ("Hızlı Arama — Müşteri, Plaka, RA No, TC, Dosya
No / Dosya No 2…") tüm modüllerde anlık arama. Bu `.aspx` bir **AJAX JSON ucu** (sayfa değil):
`{"results":[…],"count":N,"recent":true}`.

**Sonuç şeması (her sonuç):** `tur` (tür), `turAd`, `id`, `link` (ilgili ekrana derin bağlantı),
`baslik`, `alt1/alt2/alt3` (yardımcı satırlar), `eslesen` (eşleşme nedeni).
**Aranan türler (`tur`):** `kira` (Kiralama.aspx), `rezervasyon` (Rezervasyon.aspx), `servis`
(Arac_Servis_Islemleri.aspx), `baf` (Baf_Islemleri.aspx), `arac_satis` (Arac_Satis.aspx),
`musteri` (Musteri_Kayit.aspx), `arac` (Arac_kayit.aspx).
Sorgu boşken **son kayıtlar** (`recent:true`) döner.

**Entegrasyon türü:** dahili (cross-module federated search). Dış entegrasyon değil.

**Clone durumu — ❌ yok.** Clone'da global/federe arama yok; her liste kendi içinde
arar/filtreler. Backend-first API önceliğiyle uyumlu, eklenebilir bir özellik (tek `/search`
ucu + modül başına projeksiyon).

---

## 4. `web_site_yonetimi.aspx` — Web Sitesi CMS (turevweb.com)

**Amaç:** Halka açık kiralama web sitesinin (turevweb.com) **çok dilli içerik yönetimi** —
müşterinin online rezervasyon yaptığı portalın CMS'i.

**Diller / çeviri:** `DefaultLanguage`, dil ekle/sil (`ButtonLang`, `ButtonLangSaveAs`,
`ButtonLangSil`), çeviri listesi (`ButtonTranslateList/Save`), **Excel'den çeviri yapıştır**
(`CeviriExcellButton`, `CeviriExcellHtml`, `CeviriExcellYapistirButton`), kolonlar TR/DE/EN/FR/RU.

**İçerik blokları:**
- **Slider/banner:** `ButtonSlider`, `SliderResim`, filtre/sil
- **Menü:** `ButtonMenuSave/SaveAs/Delete/Filtre`
- **SEO:** `ButtonSeoSave/Delete/Filtre`, `SeoResim`, `ButtonSeoImageDelete`
- **"Neden biz" (Why we):** `ButtonWhyWeSave/Delete/Filtre`, `FileUploadWhy`
- **Yorumlar/değerlendirme:** `ButtonCommentSave/SaveAs/Delete/Filtre` (grid: Ad Soyad, Puan, Mesaj)
- **Sayfa içerik:** HTML editör + `Web_Ayar_AltMakale`, KVKK/Sözleşme makale yolları
  (`article/Kvkk/`, `article/Sozlesme/`), `Web_Ayar_Contract`
- **Ek hizmetler (sitede):** `Web_AddService_Additional_Driver`, `_Baby_Seat`, `_Navigation`,
  `_Young_Price` (+ `_Line`)
- **Lokasyon fırsatları:** `ButtonLokasyonFirsatlariSifirla`, "Geçici Lokasyon Fiyatlarını Sil"
- **Tema/görsel:** `LogoResim`, `FaviconResim`, `NoImage`, ana popup (`MainPopupFile/Button`),
  renkler (`Web_Ayar_Color_Bg/Bottom/Button_Bg/Search_Bg/Search_Passive_Bg` = hex)
- **Sigortalar (fuses):** `ButtonNewFuses/SaveFuses/DeleteFuses/FusesFiltre`
- **Genel:** `Web_Ayar_Adres`, `Web_Ayar_Dil`, "Site İçeriğini Yenile", "Varsayılanları Yükle"

**Entegrasyon türü:** **web rezervasyon portalı CMS** (kiralama firmasının kendi sitesi).
Giden tarafta `ayarlar`→"Web XML Hazırla" ve `web_rezervasyon.aspx` ile bağlı.

**Clone durumu — ❌ yok.** Clone'da web sitesi, CMS, çok dil yönetimi, web rezervasyon kabulü
yok (`web_rezervasyon` da clone'da yok; web-rez genel olarak stub kapsamında). SaaS modelinde
tenant başına public site = büyük ayrı modül.

---

## 5. `tabletyonetim.aspx` — Tablet (Mobil Teslim/Dönüş) Yönetimi

**Amaç:** Teslim/dönüş anında müşterinin imzaladığı **tablet uygulamasının** (`/Tablet/`)
yönetimi: tablet kullanıcıları, varsayılan akış metinleri, araç foto/aksesuar/lastik kontrol
şablonları, imzalı sözleşme maili.

**Tablet kullanıcıları:** "Aktif Kullanıcılar" grid (Kullanıcı Adı, Bilgisi, Personel, Menü);
`NewUserDefaultButton` ("Yeni Kullanıcı Ekle/Kaydet"), `DeleteUserButton`.
**Aksesuar/lastik kontrol:** `AksesuarEkleButton`/`AksesuarSilButton` (`AksesuarName`),
`LastikEkleButton`/`LastikSilButton` (`LastikName`) — teslimde işaretlenen kontrol listeleri.
**Varsayılan akışlar:** `TabletDefaultOnReservation`, `TabletDefaultOnRentChange`,
`TabletDefaultOnRentExit`, `TabletDefaultOnRentReturn` (rezervasyon/çıkış/değişim/dönüş ekran metni).
**Foto/medya:** `DefaultFotoDescreptions*` (varsayılan foto açıklamaları, JSON), Fotoğraf/Video sekmeleri.
**İmzalı sözleşme maili:** `MailSubject`, `MailBodyHtml`, `MailWebAddress`.
**RTF şablon:** `RichTextUpload/Button`, `RichTextListButton`, `RtfDeleteButton`.
**Arama/işlem:** `Tablet_Plaka_Ara` ("İşlemlerde Plaka Ara"), `SearchRez`, `SearchSozCikis/Donus/
Degisim/ErkenDonus`, `RezervasyonAyrintiButton`, `SozlesmeAyrinti2/3/4Button`.
**Bakım:** `MaintenanceButton` ("Sistem Bakımı Yap").
**Grid kolonları:** Söz. No, Rez. No, Sürücü Ad Soyad, Çıkış/Dönüş Tarih-Saat, Kullanıcı Bilgisi,
Tablet Son İşlem Tarih/Saat, Durum, Menü.

**Entegrasyon türü:** **mobil/tablet uygulama** (saha teslim cihazı; dijital imza + foto + e-posta).

**Clone durumu — ❌ yok.** Clone'da tablet/mobil teslim uygulaması, dijital imza, teslim foto/
hasar görsel akışı yok. (İlgili canlı ekranlar `mobil_odeme.aspx`, `mobil_teslimat.aspx` de
kapsam dışı listede, clone'da yok.)

---

## 6. `turevuzak.aspx` — TürevUzak (Uzak Erişim / AnyDesk)

**Amaç:** "Türev Uzak Erişim Uygulaması" — destek/uzak masaüstü; sayfa **AnyDesk**'e referans
veriyor. Vendor uzaktan destek aracı.

**Entegrasyon türü:** uzak masaüstü (AnyDesk). Operasyonel veri entegrasyonu değil; destek aracı.

**Clone durumu — ❌ yok** (gerek de yok; SaaS'ta uzak destek farklı ele alınır).

---

## 7. `serbest_sms.aspx` — Serbest (Manuel) SMS

**Amaç:** Cari/müşteri seçip **elle SMS** göndermek (kampanya/bilgilendirme).
**Alanlar:** "Cari Ara" → `Musteri_No`, `Ad`, `Soyad`, `Telefon` (Gsm No), `Aciklama` (mesaj),
`Tarih`, `Saat`, `Vade`, `Islem_Yapan`. **Butonlar:** "Sms Gönder", "Yeni SMS İşlemi".

**Entegrasyon türü:** **SMS gateway** (giden). Kimlik `ayarlar`→`SMSToken*`/`SMS_Kll_*`.

**Clone durumu — 🟡 altyapı var, ekran yok.** Clone'da `ISmsService.SendAsync(phone, message)`
portu var ama `StubSmsService` (no-op). Manuel SMS ekranı/serbest gönderim yok.

---

## 8. `otomatik_servisler.aspx` — Otomatik (Zamanlanmış) Servisler

**Amaç:** Arka plan/otomasyon işlerini elle tetikleme/izleme. **Alanlar:** `Islem_Turu`
(işlem türü), `Tarih1`, `Tarih2` (tarih aralığı), `Tarih_Listesi`.

**Entegrasyon türü:** otomasyon/scheduler (SMS tetikleri, kur çekme, ceza/HGS çekme gibi
periyodik işlerin koşturucusu).

**Clone durumu — ❌ yok.** Clone'da zamanlanmış görev altyapısı (BackgroundService/cron) yok;
otomasyon tetikleri (otomatik SMS, TCMB kur, HGS çekme) yok.

---

## 9–13. XML / Broker / Tedarikçi Entegrasyon Ekranları

TürevRent'in **B2B kanal/broker entegrasyon ailesi**: dış aggregator/broker firmalarından
gelen XML (araç+fiyat) ile yerel envanteri besleme ve giden fiyat feed'i. Clone'da bu ailenin
**tamamı yok**; clone'da yalnız basit `ReservationSource` master'ı (`Kod, Ad, Aktif`) var —
tedarikçi eşleme/katsayı kavramı yok.

### 9. `xml_firma_tanim.aspx` — XML Partner Firma Tanımı
**Amaç:** XML ile çalışılan broker/partner firmaları ve **fiyat katsayılarını** tanımlamak.
**Alanlar:** `Firma_Adi`; **fiyat katsayıları** `Kira_XML_Katsayi`, `Drop_XML_Katsayi`,
`Hizmet_XML_Katsayi`, `Xml_Siralama_Katsayi` ("Avantaj Katsayısı"); **araç alım oranları**
`Arac_Alim_Kira_Oran`, `Arac_Alim_Drop_Oran`, `Arac_Alim_Hizmet_Oran`; **komisyon**
`Alinan_Kom_Orani`, `Hizmet_Alinan_Kom_Orani`; `Full_Kasko`, `Km_Siniri`, `Depozito_Api`
(depozito API'si), `Calisma_Sekli` (çalışma şekli), `Ozel_Fiyat_Gonder` (özel fiyat gönder),
`Pasif`; HTML editör (açıklama/şartlar); gizli `Kayit_No`, `Sube_ID`, `Bayi_Cari_Kod`.
**Buton:** "Düzenle". **Entegrasyon türü:** **XML broker/aggregator** (çift yönlü; in+out katsayı).

### 10. `xml_disardan_arac.aspx` — Dış XML Aracı → Yerel Araç Eşleme
**Amaç:** Seçilen XML firmasının XML'deki araçlarını yerel araç/tipe eşlemek.
**Alanlar:** `XML_Firma` (dropdown), `Arama` ("Araç adı, **SIPP** veya tipi…"; SIPP = ACRISS
uluslararası araç sınıf kodu). **Butonlar:** Ara, Temizle, Kaydet. **Tür:** **gelen XML feed mapping**.

### 11. `xml_disardan_sube.aspx` — Dış XML Şubesi → Yerel Şube Eşleme
**Amaç:** XML firmasının lokasyon/şubelerini yerel şubeye eşlemek.
**Alanlar:** `XML_Firma`, `Arama` ("Şube adı, firma veya kod…"). **Butonlar:** Ara, Temizle,
Kaydet. **Tür:** **gelen XML feed mapping** (lokasyon).

### 12. `xml_fiyat_aktar.aspx` — Dosyadan Fiyat Aktarımı
**Amaç:** Dosyadan (Excel/XML) toplu fiyat içe aktarım.
**Alanlar:** `FileUpload1` + "Görüntüle", `Rez_Kaynak` (rezervasyon kaynağı), `Sube`, `Arac`
(filtre), liste `XML_Fiyat_Liste`. **Butonlar:** "Filtre Et", "Sadece Seçili Rezervasyon
Kaynağını Sil". **Tür:** **toplu fiyat içe-aktarım** (kanal bazlı).

### 13. `xml_rez_kaynak_tedarikci.aspx` — Rez Kaynak ↔ Tedarikçi + Oran
**Amaç:** Rezervasyon kaynağını (kanal) tedarikçiye bağlayıp fiyat oranı uygulamak.
**Alanlar:** `Rez_Kaynak`, `Tedarikci`, `Kira_Oran`, `Hizmet_Oran`, `Drop_Oran` (varsayılan 1).
**Buton:** "Aşağıya Yansıt" (oranı alta uygula/cascade). **Tür:** **kanal→tedarikçi fiyat eşleme**.

---

## 14. `dis_banka_entegrasyon_listesi.aspx` — Dış Banka Entegrasyonu

**Amaç:** Dış banka hesap hareketlerinin otomatik entegrasyonu (ekstre çekme/mutabakat) —
ad ve `bankaislemleri` ailesinden çıkarım. Canlıda **Runtime Error** (Server Error) döndü;
içerik alınamadı (alan listesi çıkarılamadı). Ekran TürevRent'te **var** ama bu taramada erişilemedi.

**Entegrasyon türü:** **banka** (gelen hesap hareketi / ekstre).
**Clone durumu — ❌ yok.** Clone'da banka entegrasyonu yok (banka işlemleri elle girilir;
`IPosService` stub sadece kart/POS içindir, banka ekstre çekme yok).

---

## 15–16. Log Ekranları (302 — erişilemedi)

- **`log_kayit.aspx`** → HTTP 302 (Default.aspx'e yönlendi; bu oturumda erişim engelli/yetki).
  Amaç (addan): kullanıcı işlem/erişim logu. **Clone durumu — 🟡:** clone'da `AuditLog` entity +
  `Auditing` servisi + `Components/Pages/Audit` ekranı **var** (denetim görünümü). Alan-bazlı
  karşılaştırma yapılamadı (canlı içerik 302).
- **`web_log_kayit.aspx`** → HTTP 302. Amaç: web sitesi (portal) erişim/işlem logu.
  **Clone durumu — ❌:** clone'da web sitesi olmadığından web log da yok.

---

## Dış Entegrasyon Envanteri (modül geneli)

| Entegrasyon | Canlı uç(lar) | Yön | Clone karşılığı | Durum |
|-------------|---------------|-----|-----------------|:-----:|
| **XML broker/partner** | `xml_firma_tanim`, `xml_disardan_arac`, `xml_disardan_sube`, `xml_rez_kaynak_tedarikci` | çift yönlü | — | ❌ yok |
| **XML/dosya fiyat aktarım** | `xml_fiyat_aktar`, `ayarlar`→"Web XML Hazırla" | gelen + giden | — | ❌ yok |
| **Web rezervasyon portalı** | `web_site_yonetimi`, `web_rezervasyon`(*) | gelen | — | ❌ yok |
| **SMS gateway** | `serbest_sms`, `ayarlar`→`SMSToken*`, otomatik tetikler | giden | `ISmsService` | 🟡 stub |
| **WhatsApp** | (ayrı ekran görülmedi) | giden | `IWhatsAppService` | 🟡 stub |
| **e-Fatura** | `ayarlar`→`E_Fatura_Pass`, `gelen_e_fatura_listesi`(*) | çift yönlü | `IEInvoiceService` | 🟡 stub |
| **Sanal POS / ödeme linki** | `ayarlar`→`Garanti_Pos_Hesap`, `Odeme_Link_Sanal_Pos`, `Provizyon_Hesap_No` | giden | `IPosService` (charge/auth/capture/refund) | 🟡 stub |
| **Dış banka (ekstre)** | `dis_banka_entegrasyon_listesi` | gelen | — | ❌ yok |
| **HGS/geçiş** | `hgs_gecis_listesi`(*) | gelen | `IHgsService` + `HgsReflectionService` | 🟡 stub (yansıtma mantığı var) |
| **KABİS (emniyet)** | `kabis_raporu`(*) | giden | `IKabisService` | 🟡 stub |
| **TC kimlik doğrulama** | `ayarlar`→`TC_Dogrula` | giden | — | ❌ yok |
| **TCMB döviz kuru** | `ayarlar`→`TCMB_Otomatik` | gelen | — | ❌ yok (kurlar elle) |
| **SMTP / mail** | `ayarlar`→`Mail_SMTP_*` + "Test Mail Gönder" | giden | — | ❌ yok |
| **Google Calendar** | (ayrı ekran görülmedi) | giden | `IGoogleCalendarService` | 🟡 stub |
| **Mobil/tablet (imza+foto)** | `tabletyonetim`, `/Tablet/`, `mobil_*` | çift yönlü | — | ❌ yok |
| **Uzak erişim (AnyDesk)** | `turevuzak` | — | — | ❌ yok |

(*) kapsam dışı modüllerde listelenen ilgili ekranlar; burada yalnız entegrasyon bağı için referans.

**Clone'daki port'lar (hepsi stub — `Infrastructure/Integrations/StubAdapters.cs`,
`AddIntegrationStubs()`):** `ISmsService`, `IWhatsAppService`, `IGoogleCalendarService`,
`IEInvoiceService`, `IPosService`, `IKabisService`, `IHgsService`. Tanımlı **port yok**:
XML broker, dosya fiyat aktarım, dış banka ekstre, TC kimlik, TCMB kur, SMTP.

---

## Genel Clone Durumu Özeti

**Clone'da var (çekirdek sistem):**
- Kullanıcı + auth (2-aşamalı login), 4 rol × 4 izin matrisi (`RolePermissions`), şube kapsamı
  (`AtanmisSube` / `BranchScope`), `PermissionGuard`. ✅ (granülerlikte zayıf)
- Denetim/audit: `AuditLog` + `Auditing` servisi + Audit ekranı. ✅
- Entegrasyon **port'ları** (7 arayüz) — sözleşme hazır, hepsi stub. 🟡

**Clone'da yok (bu modülün boşlukları):**
1. **Sistem ayarları ekranı/entity'si** (`ayarlar`) — tenant başına firma + tüm entegrasyon
   kimlik deposu. Entegrasyonları gerçeğe çevirmenin ön koşulu. **(En kritik)**
2. **XML/broker/tedarikçi entegrasyon ailesi** (firma tanım + araç/şube eşleme + fiyat aktarım
   + kanal-tedarikçi oran) — B2B kanal genişliğinin tamamı.
3. **Global hızlı arama** (`globalsearch`) — cross-module federe arama; clone'da hiç yok.
4. Web sitesi CMS (`web_site_yonetimi`) + web rezervasyon kabulü.
5. Mobil/tablet teslim uygulaması (`tabletyonetim`, imza/foto/hasar).
6. Otomasyon/scheduler (`otomatik_servisler`), uzak erişim (`turevuzak`), dış banka ekstre.
7. Yetki: ekran-bazlı granüler izin + yetki grubu (template) + yetki kopyalama.

**Yapısal fark (kullanıcı/yetki):** TürevRent ~110 checkbox ekran-bazlı yetki + yetki grubu;
clone 4 sabit rol × 4 izin. Kullanıcı kartında TürevRent'te olup clone'da olmayan alanlar:
SMS Onay/Telefon, Dijital İmza Eposta, Kasa Kodu, Personel Kodu, Entegrasyon Kod1, Web Yöneticisi.
