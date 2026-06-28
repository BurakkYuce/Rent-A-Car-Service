# Parite — Modül 02: Tanım/Master + Kira Kuralları

> Kaynak: canlı `turev2.turevrac.com` diske kaydedilmiş HTML (`parite_html/`, 2026-06-28).
> Clone karşılığı: `src/RentACar.Domain/Entities/*.cs` + `AppDbContext`.
> Form alanı notasyonu: **Görünür etiket** → `name` (ASP.NET prefix `ctl00$ContentPlaceHolder1$` atılmış).
> Bu belge yalnız **ekran/alan YAPISINI** taşır; kimlik/müşteri verisi içermez.

İncelenen ekran sayısı: **22**
Özet: ✅ tam parite **4** · 🟡 kısmi (alan eksiği) **11** · ❌ clone'da yok **7**

---

## A. Basit sözlük master'ları (tek/iki alan + grid)

### 1. marka_tanim.aspx — Marka Tanımı ✅
- **Amaç:** Araç markası sözlüğü.
- **Form:** Marka adı → `TextBox2` (ph "Bir Marka Yazınız", text-uppercase).
- **Grid:** "Marka Listesi" (tek kolon).
- **Buton:** Ekle (`Button1`).
- **Clone durumu:** `Brand` (Kod, Ad, Aktif). ✅ Clone superset (Kod + Aktif ekstra). Canlıda sadece serbest metin marka adı; pasiflik yok.

### 2. iptal_sebepleri.aspx — İptal Sebepleri ✅
- **Amaç:** Rezervasyon/kira iptal sebebi sözlüğü.
- **Form:** İptal sebebi → `TextBox2` (ph "Bir İptal Sebebi Yazınız"); gizli `Kayit_No`.
- **Grid:** "Sebep".
- **Buton:** Ekle (`Button1`).
- **Clone durumu:** `CancelReason` (Kod, Ad, Aktif). ✅ Clone superset.

### 3. arac_segment.aspx — Araç Segment Tanımı ✅
- **Amaç:** Araç segmenti sözlüğü (Ekonomik/Orta/Lüks vb.).
- **Form:** Segment → `Segment` (ph "Araç Segmenti"); gizli `Kayit_No`(-1), `Degismez`.
- **Grid:** "Segment".
- **Buton:** Kaydet (`Button1`/`Up_Button1`).
- **Clone durumu:** `VehicleSegment` (Kod, Ad, Aciklama, Aktif). ✅ Clone superset.

### 4. arac_sahibi.aspx — Araç Sahibi (Grubu) Tanımı ✅
- **Amaç:** Araç sahibi/mülkiyet grubu sözlüğü (öz mal, kiralık, filo vb.).
- **Form:** Araç sahibi grubu → `Arac_SAhibiX` (ph "Araç Sahibi Grubu"); gizli `Hesap`(-1), `Grup_Old`.
- **Grid:** "Araç Sahibi Listesi".
- **Buton:** Kaydet.
- **Clone durumu:** `VehicleOwner` (Kod, Ad, Tur, Aktif). ✅ Clone superset.

### 5. hesap_tanimalama.aspx — Hesap (Kodu) Tanımlama 🟡
- **Amaç:** Muhasebe/cari hesap kodu sözlüğü (Hesap Kodu + Açıklama). `hesap_no_tanimlama`'dan FARKLI (bu, banka/IBAN değil, hesap-kodu kataloğu).
- **Form:** Hesap Kodu → `Kod_Adi` (ph "Hesap Kodu"); Açıklama → `Aciklama`; gizli `Kayit_No`(-1), `Degismez`.
- **Grid:** (boş/gizli render).
- **Buton:** Kaydet.
- **Clone durumu:** Birebir entity yok. En yakın `FinancialAccount` (Kod, Ad, Tur, Doviz) farklı kavram (kasa/banka). 🟡 "muhasebe hesap kodu" sözlüğü clone'da ayrı modellenmemiş; `FinancialAccount.Tur` ile gevşek eşlenebilir, "Açıklama" alanı yok.

---

## B. Sınıflandırmalı sözlükler

### 6. ozel_kod_tanim.aspx — Özel Kod Tanımı 🟡
- **Amaç:** Çok-amaçlı özel kod sözlüğü; bir **Tür** altında değerler tanımlanır (form/raporlarda filtre/etiket).
- **Form:**
  - Türü → `Turu` (select). Değerler: `Ozel-1`…`Ozel-6` (Özel Kod 1–6), `Noter`, `Satis_Kanali` (Satış Kanalı), `Ihale_Yeri`, `Ihale_Firmasi`, `Satis_Kampanya` (Araç Satış Kampanyası), `Statu`, `Vade`, `Kira_Takip`, `Uyari` (Uyarı Sebepleri).
  - Değer → `Ozel_Kod` (ph "Değer"); gizli `Kayit_No`(-1), `Degismez`.
- **Grid:** "Değer", "Türü".
- **Buton:** Kaydet.
- **Clone durumu:** `CustomCode` (Kod, Ad, Aciklama, Aktif). 🟡 **Eksik: `Turu` sınıflandırması** (14 sabit tür). Clone'da kodlar tek düz listede; tür ayrımı yok → tüketici formlar (statü/vade/satış kanalı/uyarı vb.) tür-bazlı filtreleyemez.

### 7. gider_tanimlama.aspx — Gider Tanımlama 🟡
- **Amaç:** Gider kalemi sözlüğü; bir **Gider Türü** altında gider adı.
- **Form:** Gider Türü → `Gider_Turu` (select: `Ofis`, `Arac`, `Servis`, `Personel`); Gider Adı → `Gider_Adi`; gizli `Kayit_No`(-1), `Degismez`.
- **Grid:** "Gider Adı", "Gider Türü".
- **Buton:** Kaydet.
- **Clone durumu:** `ExpenseCategory` (Kod, Ad, Aktif). 🟡 **Eksik: `Tur` (Ofis/Araç/Servis/Personel)**. Gider raporları tür kırılımı yapamaz.

---

## C. Para / Kasa / Hesap (banka)

### 8. para_tanimlama.aspx — Para (Döviz) Tanımlama 🟡 (para-ilgili)
- **Amaç:** Döviz tanımı + güncel kur.
- **Form:** Kısaltma → `Kisaltma`; Kur → `Kur`; Ülke → `Ulke`; gizli `Hesap`(-1).
- **Grid:** "Ülke", "Döviz Kısaltması", "Kur".
- **Buton:** Kaydet (`Button1`), **Kur Bilgilerini Al** (`Button2` — dış kaynaktan kur çek).
- **Clone durumu:** `Currency` (Kod, Ad, Sembol, Aktif). 🟡 **Eksik: `Kur` (güncel kur değeri) + `Ulke` + kur-çekme**. Para modülünde `Money.Rate` var ama döviz master'ında saklı/güncellenen bir kur alanı yok; çok-döviz dönüşümü için kur kaynağı eksik.

### 9. kasa_tanimi.aspx — Kasa Tanımı 🟡
- **Amaç:** Nakit kasa tanımı + işlem bildirim mailleri.
- **Form:** Kasa Bilgisi → `Kasa_Kodu`; Uyarı/işlem maili → `Islem_Mail` (ph "...mailler arasına ; koyunuz"); gizli `Kayit_No`(-1).
- **Grid:** "Kasa Tanımı", "Uyarı Mail Listesi".
- **Buton:** Kaydet, Düzenle.
- **Clone durumu:** `FinancialAccount` (Tur=Kasa) ile eşlenir. 🟡 **Eksik: `Islem_Mail` (uyarı mail listesi)**.

### 10. hesap_no_tanimlama.aspx — Hesap No (Banka/IBAN) Tanımlama 🟡
- **Amaç:** Banka hesabı/IBAN tanımı.
- **Form:** Hesap No/IBAN → `Hesap_No`; Döviz → `Doviz` (EURO/Kr/TL/USD); İşlem Şube → `Islem_Sube` (multiselect popup); Aktif → `Aktif` (chk); Hediye Çek → `Hediye_Cek` (chk); Banka Adı → `Banka_Adi`; Şube Adı → `Sube_Adi`; Özel Kod → `Ozel_Kod`; Epostalar → `Islem_Mail`; gizli `Hesap`(-1).
- **Grid:** "IBAN", "Doviz", "Banka", "Sube", "Özel Kod", "Uyarı Mail Listesi".
- **Buton:** Kaydet.
- **Clone durumu:** `FinancialAccount` (Kod, Ad, Tur, Doviz). 🟡 **Eksik: IBAN/HesapNo ayrı alan, Banka_Adi, Sube_Adi, Islem_Sube, Hediye_Cek, Islem_Mail, Ozel_Kod**. Clone yalnız Kod/Ad/Tur/Doviz tutuyor.

---

## D. Fiyat/Tarife grubu

### 11. fiyat_grup_tanimlama.aspx — Tarife (Fiyat) Grup Tanımlama ❌
- **Amaç:** Adlandırılmış tarife grubu + grup oranı (çarpan) + acente/XML panel kimlikleri.
- **Form:** Tarife Adı → `Tarife_Grup`; Oran → `Tarife_Oran`; Kullanıcı Adı → `Kullanici_Adi`; Şifre → `Sifre`; gizli `Kayit_No`(-1), `Old_Grup`.
- **Grid:** "Kayit No", "Tarife Grup Adı", "Kullanıcı Adı".
- **Buton:** Farklı Kaydet (save-as).
- **Clone durumu:** ❌ **Entity yok.** `RateCard.Grup` serbest metin; yönetilen "tarife grubu" (oran çarpanı + panel kullanıcı/şifre) master'ı yok. Fiyat motorunun ön-koşullarından.

---

## E. Şube / Lokasyon

### 12. sube_tanimlama.aspx — Şube Tanımlama 🟡 (büyük gap)
- **Amaç:** Şube master'ı (ünvan, adres, mesai, komisyon, sözleşme/no formatları, entegrasyon, ücretsiz hizmetler, logo).
- **Form alanları:**
  - Şube Bilgisi → `TextBox2`; Web İsim → `Web_Isim`; Şube İsim → `Sube_Isim`; Firma Ünvanı → `Firma_Unvani_Uzun`; Durum → `Durum` (select).
  - Adres → `Adres`; İlçe → `Ilce`; Şehir → `Sehir_X` + `Sehir` (select); Mail → `Mail_Adres`; Telefon → `Telefon`.
  - **Mesai saatleri** (haftanın 7 günü Sabah/Akşam): `Pazartesi_Sabah`/`Pazartesi_Aksam` … `Pazar_Sabah`/`Pazar_Aksam`; `Once_Saat`.
  - **Konum:** `Konum` (enlem), `Boylam`; `Rez_Rengi` (color picker).
  - **Komisyon:** Kiradan Alınacak Kom. Oranı → `Alinan_Kom_Orani`; Hizmetten Alınacak Kom. Oranı → `Hizmet_Alinan_Kom_Orani`; `Komisyon_Maliyetten` (select).
  - Alış Şubesi Değil → `Alis_Subesi_Degil` (chk); Sıralama → `Siralama`; Web Şube Kodu → `Web_Sube_Kodu`; Bayi Cari ID → `Bayi_Cari_Kod`; Bayi Ofis → `Bayi_Ofis` (select); Web ID → `Web_ID`.
  - **Numaralandırma:** Sözleşme No Formatı → `Sozlesme_Format`; Sözleşme No → `Sube_Key_ID`; Nakit No → `Nakit_ID`; Banka No → `Banka_ID`; Entegrasyon Kod1 → `Entegrasyon_Kod1`.
  - **Ücretsiz hizmetler:** `Ucretsiz_Hizmet1` … `Ucretsiz_Hizmet10`.
  - Logo → `FileUpload1`.
  - **Şube değiştirme (rename) aracı:** Eski → `Eski` (select), Yeni Şube → `Yeni_Sube`, buton Değiştir.
  - gizli: `aktifTab`, `Kayit_No`(-1), `Sube_Tanimlama_Yapamaz`.
- **Grid:** "Şube Adı", "Şehir", "Web Şube Kodu", "Web ID", "Sayaç".
- **Buton:** Yeni Kayıt, Kaydet, Değiştir.
- **Clone durumu:** `Branch` (Kod, Ad, Adres, Telefon, Aktif). 🟡 **Eksik (geniş):** Web/Firma ünvanı, mesai saatleri (7 gün), enlem-boylam, rez rengi, komisyon oranları (2), sözleşme/nakit/banka no formatları + entegrasyon kod, ücretsiz hizmetler (10), bayi alanları, sıralama, logo, şube-rename aracı. Clone bilinçli "additive" (CLAUDE.md §6) — minimum master.

### 13. lokasyonlar.aspx — Lokasyon Tanımlama 🟡 (büyük gap)
- **Amaç:** Alış/dönüş lokasyonu (ofis) master'ı + drop ayarları.
- **Form alanları:**
  - Lokasyon Adı → `Lokasyon`; `Lokasyon_Sec` (select); Lokasyon Adı (İngilizce) → `Lokasyon_En`; Buluşma → `Bulusma` (select); Mail → `Mail_Adresi`; Telefon → `Telefon`; IATA → `IATA`; Web Gizle → `Web_Gizle` (select); Lokasyon Türü → `Lokasyon_Turu` (select).
  - Adres → `Adres`; Bina No → `Bina_No`; Tarif → `Tarif`; Ülke → `Lokasyon_Ulke` (select); Şehir → `Sehir`; İlçe → `Ilce`; Posta Kodu → `Posta_Kodu`; Maps Point (enlem,boylam) → `Maps_Point` (ph "41.011046, 28.957798"); Ek Açıklama → `Ek_Aciklama`; Web Sıralama → `Web_Siralama`; Şube → `Sube_KoduX` (select).
  - **Mesai saatleri** (7 gün Sabah/Akşam): `Pazartesi_Sabah` … `Pazar_Aksam`.
  - **Drop ayarları grid (Çıkış Şubesi Seçimi):** Aktif (`GridView2$..$Aktif_Chk`), Karşılama Türü (`DropKarsima_Turu`), Çalışma Şekli (`DropCalisma_Sekli`), Özel Mail (`TextBoxOzelMail`), Özel Tel (`TextBoxOzelTel`).
  - gizli `aktifTab`, `Hesap`(-1).
- **Grid:** "Şube Adı", "Çıkış Şubesi Seçimi", "Drop Ayarları", "Karşılama Türü", "Mesai Saatleri", "Özel Mail", "Özel Tel".
- **Buton:** Kaydet, Excel'e Aktar.
- **Clone durumu:** `Location` (Kod, Ad, Adres, Telefon, Sube, Aktif). 🟡 **Eksik:** İngilizce ad, buluşma/türü, IATA, web gizle, ülke/şehir/ilçe/posta/bina, Maps enlem-boylam, mesai saatleri, web sıralama, **drop ayarları matrisi** (lokasyon×şube karşılama/çalışma şekli/özel iletişim). Clone additive minimum.

### 14. lokasyon_sube_ara.aspx — Lokasyon/Şube Arama (drop matris) ❌
- **Amaç:** Lokasyon-şube drop kombinasyonları arama/raporu (filtre + Excel).
- **Filtreler:** İşlem Şube → `Islem_Sube` (select); Çıkış Lokasyon → `Cikis_Lokasyon`; Dönüş Lokasyon → `Donus_Lokasyon`; gizli `Banka_Islem_Iptal`, `Islem_Turu`, `HizmetKayitNo`.
- **Grid:** (dinamik, başlıksız render).
- **Buton:** Excel'e Aktar.
- **Clone durumu:** ❌ Karşılığı yok (drop/lokasyon arama ekranı). `lokasyonlar` drop matrisine bağlı; clone'da drop matrisi olmadığından bu ekran da yok.

---

## F. Rezervasyon kaynağı (kanal/acente config)

### 15. rezervasyon_kaynagi.aspx — Rezervasyon Kaynağı 🟡 (çok büyük gap)
- **Amaç:** Rezervasyon kaynağı/acente/kanalı tanımı — basit sözlük DEĞİL, kapsamlı **kanal konfigürasyonu** (tarife bağı, bakiyelendirme, provizyon/muafiyet seçeneği, komisyon, XML/online ayarları, ödeme matrisi, logo).
- **Form alanları (öne çıkanlar):**
  - Rezervasyon Kaynağı → `Kaynak_AdiX`; Cari bağ → `Rez_Kaynak` (cari ara); Kaynak Grubu → `Kaynak_Grubu`; İşlem Şube → `Islem_Sube`; Tarife → `Tarife` (select); Hizmet Döviz → `Hizmet_Doviz`; Çalışılacak Döviz → `Calisilacak_Doviz`.
  - **Bakiyelendirme:** `Bakiyelendirme`, `Bakiyelendirme_Str`, `Rez_Kay_Bakiye_Y`, `Rez_Kay_Bakiye_Dvz_Y`; `Bakiye_Kontrol_Etme`.
  - **Provizyon/Muafiyet:** `Provizyon_Secenek`, `Muafiyat_Secenek`, `Provizyon_Yok`.
  - **Hizmet/extra ücretleri:** `Bebek_Koltugu`, `Navigasyon`, `Ek_Surucu`, `Wifi`, `Zorunlu_Hizmet_Bedeli`, `Indirim`, `Puan_Orani`, `Max_Gun`, `NoShow`, `Min_Man_Suresi`.
  - **KDV/teminat flag:** `Kdv_Muafiyeti_Var`, `SCDW_Dahil`, `CDW_Dahil`, `LCF_Dahil`, `PAI_Dahil`, `Full_Credit_Var`, `Km_Sinirsiz`, `Sadece_Musteri_Odeme`.
  - **Komisyon/ödeme:** `F_Komisyon_Oran`, `On_Odeme_Orani`, `Payment_Turu`, `Acenta_Paneli_Odeme*` (ödeme yöntemi matrisi popup), `Maliyet_Yansitma`.
  - **XML/online:** `XML_Hizmet_Ozel`, `XML_Mail_Gitme`, `XML_Katsayi`, `XML_Hizmet`, `Frame_Kod`, `Faz_1_Timout`, `Faz_2_Timout`, `Sigorta_Kaynak_No`, `Drop_Kaynak_No`, `Uzatma_Rez_Kaynak`.
  - **Yetki/davranış:** `Rez_Pasif`, `Gizle`, `Otomatik_Mail_Gitme`, `Risk_Analiz_Yapma`, `Sube_Gor`, `Ayni_Yon_Drop`, `Acente_Fiyat_Degistir`, `Uzatamaz`, `Rez_Tarihler_Degisemez`.
  - **Matris (ceza/şart) flag'leri:** `Matris_Erken`, `Matris_Gec`, `Matris_Iptal`, `Matris_NoShow`, `Matris_Uzatma`.
  - **Logo:** `RezLogo` (file) + Kaydet (`ButtonSaveImage`); ayrıca zengin metin editörü (HtmlEditor) açıklama.
  - gizli: `aktifTab`, `Hesap`(-1), `Rez_Kaynak_No`, `Rez_Kaynak_Arac_Kisitlama`.
- **Grid:** "Rezervasyon Kaynağı Listesi" + "ID", "Cari Bilgi", "Tarife", "Özel Kod".
- **Buton:** Kaydet, Excel'e Aktar.
- **Clone durumu:** `ReservationSource` (Kod, Ad, Aktif). 🟡 **Eksik (çok geniş):** kaynak yalnız ad/kod sözlüğü; canlıdaki tarife bağı, bakiyelendirme, provizyon/muafiyet seçeneği, hizmet ücretleri, KDV/teminat dahil flag'leri, komisyon/ön ödeme oranı, ödeme yöntemi matrisi, XML/online + matris flag'leri, logo — hiçbiri yok. (Fiyat/komisyon motoru ve acente paneli ertelendiğinden bilinçli.)

---

## G. Araç tipi & Araç grubu (fiyat-kural)

### 16. arac_tipi_tanimlama.aspx — Araç Tipi Tanımlama 🟡
- **Amaç:** Marka+Tip+Vites+Yakıt+Grup birleşimi olarak araç tipi sözlüğü.
- **Form:** Marka → `Marka` (select, geniş marka listesi); Vites → `Vites`; Grubu → `Grubu` (araç grubu select); Araç Tipi → `Arac_Tipi`; Yakıt Türü → `Yakit_Turu`; Marka listesi → `Marka_Lst`; gizli `Kayit_No1`, `Eski_Tip`.
- **Grid:** "Marka", "Tipi", "Vites", "Yakıt Türü", "Araç Grubu".
- **Buton:** Ekle, Değiştir, **Araç Bilgilerini Sistemden Çek** (dış katalogdan otomatik doldur).
- **Clone durumu:** `VehicleType` (Kod, Ad, Marka, Aktif). 🟡 **Eksik: `Vites`, `Yakit_Turu`, `Grubu` (araç grubu bağı)** ve "sistemden çek" entegrasyonu. Clone tipi yalnız Ad + opsiyonel Marka.

### 17. arac_grubu.aspx — Araç Grubu (FİYAT-KURAL MASTER) 🟡 ⭐ KRİTİK
- **Amaç:** Araç gruplama + gruba bağlı **kira kuralları/fiyat parametreleri** (sürücü yaşı, provizyon/muafiyet, KM limiti+aşım, web/SIPP/segment, koltuk/kapı/bagaj). Fiyat motorunun zemini.
- **Form alanları (TAM liste) ve clone eşlemesi:**

| Canlı etiket | name | Clone (VehicleGroup) |
|---|---|---|
| Grup Bilgisi (ad/kod) | `TextBox2` | `Kod`/`Ad` ✅ |
| Araç Grup Kodu | `Arac_Grup_Kodu` | `Kod` ✅ |
| Açıklama | `TxtAciklama` | `Aciklama` ✅ |
| Sürücü Yaşı | `TxtSurucu_Yas` | `SurucuMinYas` ✅ |
| Genç Sürücü Yaşı | `TxtGenc_Yas` | `GencSurucuYas` ✅ |
| Ehliyet Yılı | `TxtEhliyet_Yil` | `EhliyetMinYil` ✅ |
| **Genç Ehliyet Yılı** | `TxtGenc_Surucu` | ❌ YOK |
| Provizyon Ücreti | `Provizyon_Ucreti` | `Provizyon` 🟡 (dövizsiz) |
| **Provizyon Döviz** (TL/EURO) | `Provizyon_Doviz` | ❌ YOK |
| **Provizyon2 Ücreti** | `Provizyon2_Ucreti` | ❌ YOK |
| **Provizyon2 Döviz** | `Provizyon2_Doviz` | ❌ YOK |
| Muafiyet Ücreti | `Muafiyet_Bedeli` | `MuafiyetTutari` ✅ |
| **Muafiyet Ücreti 2** | `Muafiyet_Bedeli2` | ❌ YOK |
| **Yakıt Fiyatı** | `Yakit_Fiyati` | ❌ YOK |
| Günlük KM | `Gunluk_KM` | `GunlukKmLimiti` ✅ |
| **Max KM/Aylık** | `Max_KM` | ❌ YOK (clone yalnız günlük) |
| KM Fiyatı (aşım) | `Km_Birim` | `AsimKmUcreti` ✅ |
| SIPP | `TxtSIPP` | `Sipp` ✅ |
| Kasa Türü | `Kasa_Turu` (select, ~23 değer) | `KasaTuru` ✅ |
| Koltuk Sayısı | `Koltuk_Sayisi` | `KoltukSayisi` ✅ |
| Kapı Sayısı | `Kapi_Sayisi` | `KapiSayisi` ✅ |
| Büyük Bagaj | `Buyuk_Bagaj` | `BagajSayisi` 🟡 (tek bagaj) |
| **Küçük Bagaj** | `Kucuk_Bagaj` | ❌ YOK |
| **Yakıt Türü** (grup) | `Yakit_Turu` (Dizel/Kurşunsuz/LPG/Elektrik/Hibrit/Tanımsız) | ❌ YOK |
| **Vites** (grup) | `Vites` (Otomatik/Düz) | ❌ YOK |
| **Web Araç Marka** | `Marka` | ❌ YOK |
| **Web Araç Tipi** | `Tipi` | ❌ YOK |
| **Sonra Öde Oranı** | `Sonra_Ode_Oran` | ❌ YOK |
| **Kredi Kartı Şartı** | `Kredi_Karti_Sart` | ❌ YOK |
| **Web Sıra** | `Web_Sira` | ❌ YOK |
| **Upgrate Sıra** | `Upgrate_Siralama` | ❌ YOK |
| **Entegrasyon Kod1** | `Entegrasyon_Kod1` (select) | ❌ YOK |
| **Özel Kod** | `Ozel_Kod` (select) | ❌ YOK |
| **WEB ID** | `TextBox3` | ❌ YOK |
| **SERVIS ID** | `Servis_ID` | ❌ YOK |
| Pasif | `Pasif` (chk) | `Aktif` (ters) ✅ |
| (Segment — canlıda bu ekranda alan YOK) | — | `Segment` (clone ekstra) |

- **Ek alt-grid:** "KM Sınır Değerlerini Kaydet" (`Button7`/`Button8`) → grup için **KM-kademe (tier) değer tablosu**. ❌ clone'da yok.
- **Grid (master liste):** "Araç Grubu", "Araç Sayısı", "Açıklama", "Durum", "Sürücü Yaşı", "Ehliyet Yılı", "SIPP", "Servis ID", "Web ID".
- **Buton:** Kaydet, Farklı Kaydet (save-as), Yeni, Excel'e Aktar, Web Kontrol, KM Sınır Değerlerini Kaydet.
- **Clone durumu:** `VehicleGroup` çekirdek kural alanlarını tutar (yaş, ehliyet, provizyon, muafiyet, günlük KM+aşım, SIPP, kasa, koltuk/kapı/bagaj) ama 🟡 **~16 alan eksik:** Genç ehliyet yılı, provizyon dövizi, Provizyon2 (+döviz), Muafiyet2, yakıt fiyatı, aylık max KM, küçük bagaj (ayrı), grup yakıt türü, grup vites, web marka/tip, sonra-öde oranı, kredi kartı şartı, web/upgrade sıra, entegrasyon kod, özel kod bağı, WEB ID, SERVIS ID, **KM-kademe tablosu**, "Web Kontrol"/"Farklı Kaydet" işlemleri.

---

## H. Kira kuralları / şartları (CLONE'DA TÜMÜ YOK) ❌

### 18. kiralama_kurallari.aspx — Kiralama Kuralları (Promosyon/Kampanya) ❌
- **Amaç:** Tarih/gün/min-max gün bazlı **promosyon/kampanya/indirim kuralı**; şube + kaynak + araç grubu kapsamına uygulanır.
- **Form alanları:**
  - Durum → `Durum` (Aktif/Pasif/Taslak/İptal); Kural Adı → `Kural_Adi`.
  - **Rezervasyon tarih penceresi:** `Rez_Bas_Tar`, `Rez_Bit_Tar`. **Kira tarih penceresi:** `Bas_Tar`, `Bit_Tar`.
  - Hızlı İşlem → `Hizli_Islem` (chk).
  - Min Gün → `Min_Gun`, Max Gün → `Max_Gun`; İskonto → `Iskonto`; Sonra Öde Oranı → `Sonra_Ode_Oran`; Hediye Gün → `Hediye_Gun`.
  - **Promosyon:** Bağlı mı → `Promosyon_Bagli` (EVET/HAYIR); Promosyon Kodu → `Promosyon_Kodu`; Promosyon Türü → `Promosyon_Turu` (Çoklu/Tek); Kupon Geçerliliği → `KuponGercerlilik` (Hepsi / Sadece İlk Bedel); butonu "Promosyon Kodları" (`Up_Button3`).
  - **Hesaplama:** `Hesaplama` (Oran/Serbest); `HesaplamaTuru` (banka taksit kampanyası listesi: AKBANK, GARANTI, ISBANK, YKB, ZIRAATBANK, … ~25 banka).
  - **Haftanın günleri:** `Pazartesi`…`Pazar` (chk).
  - **Kapsam grid'leri (Aktif chk ile çoklu seçim):** GridView2 = Şube ("Şube Adı"), GridView3 = Kaynak ("Kaynak Adı"), GridView4 = Araç Grubu ("Araç Grubu", ~240 satır).
  - gizli `Hesap`(-1), `Turu`.
- **Buton:** Ekle, Promosyon Kodları.
- **Clone durumu:** ❌ **Tümüyle yok.** Promosyon/kampanya/indirim kuralı katmanı clone'da modellenmemiş (entity, servis, ekran yok).

### 19. kiralama_kurallari_basic.aspx — Kiralama Kuralları (Basit) ❌
- **Amaç:** `kiralama_kurallari`'nın sade sürümü (yalnız iskonto + promosyon kodu + tarih pencereleri).
- **Form:** Durum → `Durum`; Kural Adı → `Kural_Adi`; Rez tarih penceresi `Rez_Bas_Tar`/`Rez_Bit_Tar`; Kira tarih penceresi `Bas_Tar`/`Bit_Tar`; İskonto → `Iskonto`; Promosyon Kodu → `Promosyon_Kodu`; gizli `Hesap`(-1).
- **Buton:** Ekle.
- **Clone durumu:** ❌ Yok (18 ile aynı eksik).

### 20. kiralama_sartlari.aspx — Kiralama Şartları (Min Gün) ❌
- **Amaç:** Şube/haftanın günü/tarih bazlı **minimum kiralama gün** şartı.
- **Form:** Şube → `Sube` (select); Min Gün → `Min_Gun`; Haftanın Günü → `Hafta_Gun` (Farketmez/Pazartesi…Pazar); Başlangıç → `Bas_Tar`; Bitiş → `Bit_Tar`; gizli `Hesap`(-1).
- **Buton:** Kaydet.
- **Clone durumu:** ❌ Yok. Min-gün/şart kuralı motoru clone'da yok.

### 21. rezsartlar.aspx — Rezervasyon Şartları (Metin) ❌
- **Amaç:** Müşteri/genel bazlı **rezervasyon şart/koşul metni** (sözleşme öncesi gösterilen şartlar).
- **Form:** Müşteri → `Musteri` (ara); Şartlar → `Sart` (metin); gizli `Resim_Dzn`, `Web_Sitesi`.
- **Grid:** (dinamik, kayıtlı tablo düzeni).
- **Buton:** Tablo Ayarlarını Kaydet, Excel'e Aktar.
- **Clone durumu:** ❌ Yok. Şart/koşul metni master'ı clone'da yok.

---

## I. Servis tanım

### 22. servis_tanim_tablosu.aspx — Periyodik Servis Tanım Tablosu ❌
- **Amaç:** Araç tipi bazında **periyodik servis KM** tanımı (her satır bir araç tipi; düzenlenebilir Periyodik KM).
- **Form:** Her satır için Periyodik KM → `GridView1$ctlNN$TxtPeriyodik_KM` (örn 10000/15000/20000/40000).
- **Grid:** "Marka", "Tipi", "Vites", "Yakıt Türü", "Açıklama", "Periyodik KM".
- **Buton:** Tümünü Kaydet.
- **Clone durumu:** ❌ Karşılığı yok. `ServiceRecord` (servis işlemi) var ama "araç tipi → periyodik bakım KM" tanım tablosu yok; periyodik bakım vade/uyarısı bu tanıma bağlı olamıyor.

---

## J. Özet tablo (master | clone entity | eksik alanlar | durum)

| # | Canlı ekran | Clone entity | Eksik/fark | Durum |
|---|---|---|---|---|
| 1 | marka_tanim | `Brand` | — (clone superset) | ✅ |
| 2 | iptal_sebepleri | `CancelReason` | — | ✅ |
| 3 | arac_segment | `VehicleSegment` | — | ✅ |
| 4 | arac_sahibi | `VehicleOwner` | — | ✅ |
| 5 | hesap_tanimalama | (`FinancialAccount` gevşek) | ayrı "hesap kodu" kavramı + Açıklama yok | 🟡 |
| 6 | ozel_kod_tanim | `CustomCode` | **`Turu`** (14 tür sınıflandırması) | 🟡 |
| 7 | gider_tanimlama | `ExpenseCategory` | **`Tur`** (Ofis/Araç/Servis/Personel) | 🟡 |
| 8 | para_tanimlama | `Currency` | **`Kur`**, `Ulke`, kur-çekme | 🟡 |
| 9 | kasa_tanimi | `FinancialAccount`(Kasa) | `Islem_Mail` | 🟡 |
| 10 | hesap_no_tanimlama | `FinancialAccount`(Banka) | IBAN, Banka/Şube adı, İşlem şube, Hediye çek, Mail, Özel kod | 🟡 |
| 11 | fiyat_grup_tanimlama | — | **entity yok** (tarife grup oranı + panel kullanıcı/şifre) | ❌ |
| 12 | sube_tanimlama | `Branch` | mesai, enlem/boylam, komisyon, sözleşme/no formatları, entegrasyon, ücretsiz hizmetler×10, bayi, logo, rename | 🟡 |
| 13 | lokasyonlar | `Location` | İng. ad, IATA, ülke/şehir/ilçe/posta, Maps, mesai, **drop matrisi** | 🟡 |
| 14 | lokasyon_sube_ara | — | **ekran yok** (drop/lokasyon arama) | ❌ |
| 15 | rezervasyon_kaynagi | `ReservationSource` | tarife/bakiyelendirme/provizyon-muafiyet/komisyon/ödeme matrisi/XML/matris flag/logo (~40 alan) | 🟡 |
| 16 | arac_tipi_tanimlama | `VehicleType` | `Vites`, `Yakit_Turu`, `Grubu` bağı, sistemden çek | 🟡 |
| 17 | **arac_grubu** | `VehicleGroup` | Genç ehliyet yılı, provizyon dövizi, Provizyon2(+döviz), Muafiyet2, yakıt fiyatı, aylık max KM, küçük bagaj, grup yakıt/vites, web marka/tip, sonra-öde oranı, kredi kartı şartı, web/upgrade sıra, entegrasyon kod, özel kod, WEB/SERVIS ID, **KM-kademe tablosu** (~16 alan) | 🟡 |
| 18 | kiralama_kurallari | — | **tümü yok** (promosyon/kampanya kuralı + kapsam grid'leri) | ❌ |
| 19 | kiralama_kurallari_basic | — | **tümü yok** | ❌ |
| 20 | kiralama_sartlari | — | **tümü yok** (min-gün şartı) | ❌ |
| 21 | rezsartlar | — | **tümü yok** (rez şart/koşul metni) | ❌ |
| 22 | servis_tanim_tablosu | — | **tümü yok** (araç tipi → periyodik bakım KM) | ❌ |

### En kritik 3 boşluk
1. **arac_grubu fiyat-kural alanları** (🟡): fiyat motorunun girdileri olan Provizyon2/Muafiyet2 (+döviz), genç ehliyet yılı, aylık max KM, yakıt fiyatı, sonra-öde oranı, kredi kartı şartı ve **KM-kademe tablosu** clone'da yok. Fiyat motoru v1 öncesi kapatılmalı.
2. **Kira kuralları/şartları katmanının tamamı** (❌): `kiralama_kurallari(+basic)` (promosyon/kampanya/indirim), `kiralama_sartlari` (min-gün), `rezsartlar` (şart metni) — 4 ekran, hiçbir entity yok. Kampanya/indirim ve min-gün doğrulaması clone'da imkânsız.
3. **rezervasyon_kaynagi derinliği** (🟡): clone'da kaynak yalnız Kod/Ad; canlıda tarife bağı, bakiyelendirme, provizyon/muafiyet seçeneği, komisyon/ön-ödeme oranı, ödeme matrisi ve matris (ceza/şart) flag'leri kanal-bazlı fiyat/komisyon davranışını belirliyor.
