# Parite Analizi 04 — Kira / Rezervasyon Modülü

> Kaynak: diske kaydedilmiş canlı TürevRent HTML'leri (`parite_html/`).
> Yöntem: HTML dosyaları `grep`/`sed` ile tarandı (input adları `ctl00$ContentPlaceHolder1$*`,
> grid kolonları DevExpress "Grid_Alan" kolon-seçici option'larından, görünür etiketler).
> Karşılaştırma hedefi: `src/RentACar.*` (RentalContract / Reservation / Quotation / RentalAddOn +
> `/kiralar /rezervasyonlar /teklifler /takvim /musaitlik`).

**Kapsam:** 12 ekran. 4 ekran (web_rezervasyon, mobil_odeme, mobil_teslimat, servis_rezervasyon)
login/JS-gated olduğu için içerik yakalanamadı (sadece global arama kutusu döndü) — yine de işlev notu verildi.

---

## Genel tespit (önce oku)

Canlı **kiralama.aspx** tek başına **705 form alanı**, **rezervasyon.aspx 597 alan** içeriyor.
Clone'daki `RentalContract` ~35 alan, `Reservation` ~18 alan. Yani çekirdek iş akışı (rezervasyon →
kira → teslim/dönüş → tahsilat) **mevcut**, fakat canlının fiyat/sigorta/ödeme zenginliğinin
büyük çoğunluğu **eksik**. Asıl uçurum tek bir tabloda değil, **fiyat & ek hizmet & ödeme**
katmanında. Detay aşağıda; fiyat alanları için ayrı bölüm var.

---

## 1) kiralama.aspx — Kira Sözleşmesi (sistemin kalbi)

**Amaç:** Müşteriye araç teslim eden kira sözleşmesini açma/kapatma; sigorta paketleri, ek hizmetler,
tahsilat, provizyon, çıkış/dönüş KM-yakıt, uzatma — hepsi tek ekranda. (705 input)

### Form alanları (gruplandı)
- **Sözleşme/kira başı:** `RA_No`, `Sozlesme_No`, `Kira_Durum`, `Kiralama_Turu`, `Tarih`,
  `Bas_Tar`/`Bas_Saat`, `Bit_Tar`/`Bit_Saat`, `Bek_Tar`/`Bek_Saat` (beklenen dönüş),
  `Cikis_Ofisi(_ID)`, `Donus_Ofisi(_ID)`, `Cikis_Yeri`/`Donus_Yeri` (TextBox), `Teslimat_Turu`,
  `Adres_Teslim(_TL)`, `Islem_Sube`, `Kira_Adet`, `Gun_Hesap`, `Kira_Gun`.
- **Müşteri/sürücü:** `Musteri_No`, `Ad`/`Soyad`/`TC_Kimlik_No`, `Cep_Tel`, `Mail_Adresi`,
  `Mst_Adres`/`Mst_Sehir`/`Mst_Ilce`/`Mst_Ulke`, `Mst_Dog_Tar`/`Mst_Dogum_Yeri`,
  `Ehliyet_No`/`Ehliyet_Tar`/`Ehliyet_Yer`, `Mst_Pasaport_No/Yer`, `TC_Dogrulama`, `Gecici_Findex_Onay`,
  **Sürücü 1 ve 2 ayrı blok** (`Surucu1_*`, `Surucu2_*`: ad/soyad/TC/ehliyet/tel/doğum), `Ek_Surucu_TL`,
  `Ozel_Sofor_Bilgisi/ID`, `Kefil_Bilgisi`.
- **Araç:** `Plaka`, `Marka`, `Grubu`/`Tipi`, `Vites`, `Yakit_Turu`, `Yili`, `Ikame_Grup`/`Ikame_ID`
  (ikame araç), `Arac_Upgrate_Islem`/`Upcell_Fiyat`, `Kazali_Plaka`.
- **Çıkış (teslim):** `Cikis_KM`, `Cikis_Yakit`, lastik durumu (`On_Sag/Sol_Lastik_Once`,
  `Arka_*_Lastik_Once`, `Stepne_Once`, `Yedek_Anahtar_Once`, `Ilk_Yardim_Once`, `Zincir_Once`).
- **Dönüş (kapanış):** `Donus_KM`, `Donus_Yakit`, `Donus_Sebebi`, `Yapilan_KM`, `Fazla_Km`,
  `*_Lastik_Sonra`, `Sozlesme_Kapat`, `Kapatma_Sebebi_Zorunlu`.
- **Fatura:** `Fatura_Secim`, `Fatura_Firmaya`, `Fatura_Profili`, `Faturalama_Tipi`,
  `Filo_Fatura_Turu`, `Ozel_Fatura_Aciklama`, `Firma_Fatura`, `Bayi_Fatura_No`.
- **Ödeme:** `Odeme_Sekli`, `TextBoxTahsilat`, `TextBoxKalan`, kredi kartı (`TextBoxKKartNo/SKT/CVV/Sahibi`),
  **provizyon** (`TextBoxProvizyon_No/Tarih/Tutar`, `Provizyon_Cikis_Yok`), `Otomatik_Odeme_Tablosu`,
  `Fazla_Tutar_Taksit`.
- **Bakiye/cari:** `Bakiyeli`, `Bakiyelendirme(_Sekli)`, `Bakiye_Cari_No`, `Mst_Bakiye`/`Mst_Toplam`,
  `Firma_Bakiye`/`Firma_Toplam`, `Cari_Yansima`, `Risk_Izin`/`Risk_Limiti`.
- **Tablar (Active_Tab_Hizmet, aktifTab*):** ek hizmet, paket, HGS, sürücü, lastik vb. sekmeli.
- **Onlarca yetki bayrağı** (`Kll_*`: Kll_Fiyat_Giremez, Kll_Indirim_Orani, Kll_Kira_Silemez,
  Kll_Fatura_Kesemez, Kll_Sifir_Bedelli_Acamaz…) — kullanıcı-bazlı kısıt; clone'da `PermissionGuard`
  daha kaba.

### Butonlar
Kaydet / Sözleşme Kapat / Araç Değişim (`AracDegisimYenileButonu`) / Uzat / İptal / Yazdır / Provizyon Al
(çok sayıda `Button*`).

### Clone durumu: 🟡
`RentalContract` + `/kiralar` + `RentalDetail.razor` + `RentalPrint.razor` çekirdeği var:
sözleşme no, durum, müşteri/araç, bas/bit, çıkış/dönüş ofisi, çıkış/dönüş KM+yakıt, km limit + fazla km
ücreti, eksik yakıt birim ücreti, uzatma gün/bedeli, gün, günlük ücret, tutar, genel toplam, tahsilat,
bakiye, ek hizmetler (`RentalAddOn`). **Eksik (yüksek değerli):** sürücü-2 bloğu, sigorta ürünleri
(CDW/SCDW/IMM/LCF/Mini Hasar/Hırsızlık/Muafiyetsiz/Max Güvence), paket hizmetler (Paket_Hizmet1..6),
çok-döviz girişi (Doviz/Kur ekranda var ama clone tek para), provizyon + kredi kartı, damga vergisi,
HGS yansıtma, ikame/upgrade araç, lastik/ekipman teslim çek-listesi, kefil, kullanıcı-bazlı `Kll_*`
kısıtları. (Lastik/ekipman ve çoğu bayrak düşük öncelik; sigorta+paket+çok-döviz+provizyon yüksek.)

---

## 2) kira_listesi.aspx — Kira Listesi

**Amaç:** Açık/kapalı kira sözleşmelerini tarih/şube/durum/müşteri filtreleyip listeleme; kolon seçici
ile ~180 alandan kolon seçme; Excel.

### Filtreler (input)
`Tarih1`/`Tarih2` + `Tarih_Listesi`, `Plaka(_Ara)`, `Ad_Soyad`, `Musteri_No`, `Arac_Grubu`,
`Kira_Durum`, `Ofis`/`Ofis_Durum`, `Fatura_Durum`, `Evrak_Durum`, `Kira_Bakiye`/`Limit_Bakiye`,
`Hasarli`, `Cezali_Gecis`, `Performans`, `Personel_Listesi`/`Personel_Tip`, `Dosya_No(2)`,
`Makro_Adi` (kayıtlı filtre), `Grid_Alan`/`Grid_Alan_Deger` (kolon-bazlı serbest filtre),
`Kullanici_Detay`, `btnSaveLayout`.

### Grid kolonları (kolon-seçiciden — örnek seçki, ~180 alan)
Plaka, Marka, Tipi, Araç Grubu, Ad, Soyad, Cep Tel, Müşteri TC, Dosya No, Soz. No, Başlangıç/Bitiş
Tarihi, Baş./Bit. Zaman, Gün, **Günlük**, **Kira Bedeli**, **Liste Fiyat**, **Genel Toplam (+Dvz)**,
**Fiyat / Fiyat Türü**, **İndirim** (kolon-seçicide var), **Kalan / Kalan Döviz**, **Toplam Tahsilat /
Toplam Kalan**, Çıkış/Dönüş Ofisi, Çıkış/Dönüş KM, Çıkış/Dönüş Yakıt, **Ek Hizmet(ler) / Ek Sürücü /
Bebek-Çocuk Koltuğu / Navigasyon / Mini Hasar / SCDW / Max. Güvence / Hırsızlık Sig.**, **Geçiş Bedeli
/ Geçiş %10 / Geçiş Kalan** (HGS), **Km Aşım Bedeli / Ek Km**, **Hasar Bedeli**, **Drop Bedeli / LCF
Bedeli / Upcell Fiyat / Üyelik Bedeli**, **Alacağımız/Ödenen/Fatura Komisyon**, Tarife, Kampanya/Promosyon,
Rez. Kaynak, Fatura No/Durumu/Kalan, Vade Tar, Kiralama/Ödeme Şekli, Durum, Kirada/Döndü.

### Clone durumu: 🟡
`RentalList.razor` (`/kiralar`) + `RentalFilter`/`RentalRow` var (tarih/plaka/müşteri/durum filtre +
liste). **Eksik:** kolon-seçici (180 alan), kayıtlı filtre makroları, layout kaydetme, finansal kolon
zenginliği (HGS, komisyon, sigorta kalemleri ayrı kolon), Excel export.

---

## 3) rezervasyon.aspx — Rezervasyon

**Amaç:** İleri tarihli araç+tarih ön kaydı; fiyat/ek hizmet/sigorta seçimi; opsiyon tarihi;
kaynak (acenta/web/broker); kiraya çevirme zemini. (597 input — kiralama.aspx'in fiyat/ek hizmet
çekirdeğini paylaşır; teslim/dönüş/lastik alanları yok.)

### Form alanları (kiralama'dan farklılaşan)
- **Rezervasyon başı:** `Rez_No_ID`/`RA_No`, `Rez_Kaynak(_No)`/`Rezervasyon_Kaynagi2`,
  `Dis_Rez_Kaynak`/`Dis_Rez_No` (dış/broker), `Opsiyon_Tarih`/`Opsion_Gun`/`Opsiyon_Tar_Mutlak`
  (opsiyon süresi), `Rez_Grubu`, `Kira_Adet`, `Gun_Hesap`/`Kira_Gun`.
- **Tarife/fiyat:** `Tarife(_Id)`, `Fiyat`/`Fiyat_Turu`, `Donemli_Fiyat`, `Aylik_30_Gun`,
  `Gunluk_Bedel` (grid), `Indirim`/`Indirim2`/`Indirim_Dvz`/`Indirim_Turu`, `Doviz`/`Doviz_Kur`,
  `KDV`/`Kdv_*`, `Genel_Toplam`, `Kira_Tutar`, `Hediye_Gun`/`Hediye_Km`.
- **Ek hizmet/sigorta:** kiralama ile aynı set (`Ek_Hizmet1/2`, `Ek_Bedel3/4`, `Paket_Hizmet1..6`,
  `Ek_KM(_TL)`, CDW/SCDW/IMM/LCF/Mini Hasar/Max Güvence vb. — bkz. fiyat bölümü).
- **Komisyon:** `F_Komisyon_Oran/Turu/Tutar`.
- **Müşteri:** kiralama ile benzer (`Musteri_No`, ad/soyad/tel, sürücü bilgisi).

### Clone durumu: 🟡
`Reservation` + `/rezervasyonlar` + `ReservationList.razor` + `ReservationService` + kira çevirme
var; ayrıca **rezervasyon kaynağı** (`ReservationSource`) ve **teklif** (`Quotation`) ayrı modüller.
**Eksik:** opsiyon tarihi/süresi (Opsiyon_Tar/Opsion_Gun), tarife seçimi, ek hizmet/sigorta seçimi
rezervasyonda (clone'da ek hizmet sadece kirada), çok-döviz, komisyon, hediye gün/km, dış/broker rez no.

---

## 4) rezervasyon_listesi.aspx — Rezervasyon Listesi

**Amaç:** Gelen/giden rezervasyonları onaylı/onaysız, kaynak, statü, tarih, plakalı/plakasız
filtreleyip listeleme; kolon seçici; Excel.

### Filtreler (input)
`Tarih1`/`Tarih2`+`Tarih_Listesi`, `Plaka(_Durum)`, `Ad_Soyad`, `Musteri_No`, `Arac_Grubu`,
`Rez_Kaynak`, `RezStatus`/`Rezervasyon_Durum`/`Rezerv_Tip`, `Onayli`/`Onaysiz_Gosterme`, `Ofis(_Durum)`,
`Ayri_Ofis`, `Dis_Rez_Gorebilir`, `Upgrate_Dosya`, `Renk_Kiralanmayan`, `Grup_Goster`,
`Grid_Alan(_Deger)`, `Performans`, `btnSaveLayout`.

### Grid kolonları (seçki)
RA No / Rac-No, Plaka, Marka, Araç Grubu, Verilen Grup, Kiralanan Araç Sınıfı, Ad/Soyad/Cep Tel,
Başlangıç/Bitiş Tarihi+Zaman, **Gün / Günlük Bedel / Kira Bedeli / Fiyat (+Döviz) / Fiyat Türü**,
**Genel Toplam (+Dvz) / Kalan Döviz / Toplam Tahsilat / Toplam Kalan**, **İndirim / Vade Farkı /
Drop Bedeli / LCF Bedeli / Upcell Fiyat / Geçiş Bedeli**, **Alacağımız/Verilen Komisyon**, Ek Hizmet,
Ek Sürücü, Bebek Koltuğu, Navigasyon, SCDW/S.Mini Hasar/Max Güvence/Muafiyetli Sigorta, Rez. Kaynak(2),
Rez. Statü/Kupon, Onay Kodu, Opsiyon (Tarih Önemsiz), NoShow, Gerçekleşti, İptal/İptal Sebebi/İptal Zaman,
Web Status/Sebep/Servis ID, utm_source/medium/campaign (web izleme), Talep Türü/Geldiği Birim.

### Clone durumu: 🟡
`ReservationList.razor` (`/rezervasyonlar`) + `/takvim` (`ReservationCalendar.razor`) var. **Eksik:**
kolon-seçici, onaylı/onaysız + gelen/giden ayrımı, NoShow/Gerçekleşti durumları, web/utm izleme kolonları,
kupon, Excel/layout.

---

## 5) filo_arac_kiralama.aspx — Filo (Uzun Dönem) Araç Kiralama

**Amaç:** Operasyonel/uzun dönem kiralama sözleşmesi (aylık, vade gün, taksit, damga vergisi, kredi);
kısa-dönem kiralamadan ayrı akış. (30 input — odaklı ekran)

### Form alanları
`Sozlesme_No`, `Musteri_No`/`Ad`, `Plaka`, `Arac_Tipi`, `Bas_Tarih`/`Imza_Tarih`/`Tarih`,
**`Sure_Ay` (kira süresi-ay), `Vade_Gun`, `Fiyat`/`Fiyat_Turu`, `Toplam_Km_Siniri`,
`Damga_Vergi_Turu`, `Fatura_Turu`**, `Makbuz_No`, `Kredi_No`, `Satis_Temsilci`, `Islem_Turu`,
`Sistem_Islem_Turu`, `FileUpload1`/`Import` (toplu içe aktarım).

### Clone durumu: ❌
Clone'da uzun-dönem/filo kiralama **yok**. `RentalContract` günlük kira için; aylık süre (`Sure_Ay`),
vade gün, toplam km sınırı (filo), damga vergisi, taksitli ödeme tablosu yok. Ayrı entity/akış gerekir.

---

## 6) filo_kiralama_listesi.aspx — Filo Kiralama Listesi

**Amaç:** Filo (uzun dönem) sözleşmelerini tarih/plaka/müşteri ile listeleme.
**Filtreler:** `Tarih1`/`Tarih2`+`Tarih_Listesi`, `Plaka`, `Ad_Soyad`, `Musteri_No`, `Arac`, `btnSaveLayout`.
**Clone durumu: ❌** — Filo modülü olmadığı için listesi de yok.

---

## 7) web_rezervasyon.aspx — Web (Online) Rezervasyon

**Amaç:** Web sitesinden gelen online rezervasyon kaydı/yönetimi.
**Not:** HTML login/JS-gated döndü (sadece global arama `Plaka_Ara`/`RA_Ara`). Alanlar yakalanamadı;
işlevi web kanalı rezervasyon (utm_*/web status kolonları rezervasyon_listesi'nde görünüyor).
**Clone durumu: ❌** — Public web booking / online kanal entegrasyonu yok (rezervasyon kaynağı tablosu
var ama self-servis web formu yok).

---

## 8) mobil_odeme.aspx — Mobil/Tablet Ödeme

**Amaç:** Tablet üzerinden teslim anında ödeme/imza alma (Rac Tablet akışı).
**Not:** Login/JS-gated; alan yakalanamadı.
**Clone durumu: ❌** — Tablet/mobil ödeme akışı yok.

## 9) mobil_teslimat.aspx — Mobil/Tablet Teslimat

**Amaç:** Tablet üzerinden araç teslim/iade (KM-yakıt-hasar foto, imza).
**Not:** Sadece `Rac_Tablet_Say` yakalandı (gerisi gated).
**Clone durumu: ❌** — Tablet teslimat akışı yok.

## 10) servis_rezervasyon.aspx — Servis Rezervasyon

**Amaç:** Servis/bakım için araç rezervasyonu (atölye randevu).
**Not:** Login/JS-gated; alan yakalanamadı.
**Clone durumu: ❌** — Servis modülü var (bakım) ama servis randevu/rezervasyon ekranı yok.

---

## 11) broker_musaitlik_listesi.aspx — Broker Müsaitlik & Fiyat Sorgu

**Amaç:** Broker/XML kanalı için tarih+saat+grup bazında müsaitlik ve **XML fiyat servisi** sorgulama.
### Alanlar
`ArTarih1`/`ArTarih2`, `Bas_Saat`/`Bit_Saat`, `Cikis`/`Donus` (ofis), `Kira_Gun`, `Doviz`,
`Rez_Kaynak`, **`XML_Fiyat_Servisi`** (broker fiyat kaynağı), `Analiz`, `Sonuc`, `Gizle`, `Key_Hack`.
### Clone durumu: 🟡
`/musaitlik` (`MusaitlikArama.razor`) tarih/grup müsaitlik araması var. **Eksik:** broker/XML fiyat
servisi entegrasyonu, çok-döviz fiyat dönüşü, kaynak-bazlı fiyat analizi (fiyat motoru/entegrasyon — ertelenmiş).

---

## 12) broker_yasaklari.aspx — Broker Yasakları / Kısıtları

**Amaç:** Broker/rezervasyon kaynağı bazında satış kısıtı tanımlama (araç grubu, bölge, tarih aralığı,
minimum gün).
### Alanlar
`Rez_Kaynagi`, `AracGrubu`, `Bolge1`, `Bas_Tar`/`Bit_Tar`, **`Min_Gun`** (min kiralama günü kısıtı),
`Kayit_No`, `listBox`/`listBox2` (çoklu seçim).
### Clone durumu: ❌
Broker/kaynak bazlı yasak/kısıt kuralı (min gün, bölge, grup, tarih) yok. Rezervasyon kaynağı tablosu
var ama kural motoru yok.

---

## FİYAT / TUTAR HESAP ALANLARI (fiyat motoru paritesi için)

> kiralama.aspx + rezervasyon.aspx ortak fiyat çekirdeği. Bunlar **fiyat motoru v1**'in girdileri.
> Clone bugün yalnızca `Gun × GunlukUcret` (`BookingMath.Compute`) + dönüş ek bedelleri + ek hizmet
> brütü (`RentalTotals`) hesaplıyor. Aşağıdaki kalemlerin çoğu clone'da **yok**.

### Temel kira fiyatı
| Canlı alan | Açıklama | Clone |
|---|---|---|
| `Kira_Gun` / `Gun_Hesap` | gün sayısı (24h blok) | ✅ `Gun` (`BookingMath.ComputeGun`) |
| `Gunluk_Kira` / `Kira_Fiyat` / `Fiyat` | günlük ücret | ✅ `GunlukUcret` |
| `Liste_Fiyat` / `Liste_Indirim` | liste fiyatı + liste indirimi | ❌ |
| `Tarife(_Id)` / `Fiyat_Turu` / `Donemli_Fiyat` / `Aylik_Km` / `Aylik_30_Gun` | tarife & dönemsel/aylık fiyat | ❌ (tarife master yok) |
| `Kira_Tutar` / `Kira_TL` / `Tutar` | baz kira tutarı | ✅ `Tutar` |
| `Genel_Toplam` / `Onceki_Genel_Toplam` | genel toplam | ✅ `GenelToplam` |
| `Kira_Adet` | birden çok araç/satır | ❌ (tek araç) |

### İndirim & kampanya
| `Indirim` / `Indirim2` / `Indirim_Dvz` / `Indirim_Turu` / `Kll_Indirim_Orani` | indirim tutar/oran/döviz | ❌ |
| `Kampanya_Adi` / `Kampanya_IDX` / `CampId` / `Promosyon_Kodu` | kampanya/promosyon | ❌ |
| `Hediye_Gun` / `Hediye_Km` | bedava gün/km | ❌ |
| `Upcell_Fiyat(_TL)` | upgrade/upsell farkı | ❌ |

### KDV & vergi
| `KDV` / `Kdv_Detay` / `Kdv_Dahil_Degil` / `Kdv_Sistemi` / `Kdv_Muafiyeti` / `Ozel_Kdv_Kodu` | KDV ayrıştırma/muafiyet | 🟡 (KDV faturada + `RentalAddOn.KdvOrani`; kira ekranında KDV ayrıştırma yok) |
| `Damga_Vergisi(_Orani)` / `Damga_Yansit` / `Otomatik_Damga_Vergisi` | damga vergisi | ❌ |
| `Vade_Farki_Ay` / `Vade_Farki_Hizmet(_TL)` | vade farkı | ❌ |

### Çok-döviz
| `Doviz` / `Doviz_Kur` / `Sistem_Doviz` / `EuroKurDeger` / `Kalan_Doviz` / `Kalan_Kur` / `Hizmet_Doviz` / `Hizmet_Kur` | döviz + kur + dövizli kalan | ❌ (clone tek para; `Money` tipi var ama kira akışı kullanmıyor) |

### Ek hizmetler & sigorta paketleri (her biri: Dahil / Bedava / TL / T bayrakları)
| Grup | Canlı alanlar | Clone |
|---|---|---|
| Sigorta muafiyet ürünleri | `CDW_*`, `SCDW(_TL/_T)`, `IMM(_TL/_T)`, `LCF(_TL/_T)`, `Mini_Hasar(_TL)`, `Super_Mini_Hasar_Sigortasi(_TL)`, `Hirsizlik_Sigorta(_TL)`, `Muafiyetsiz(_TL)`, `Max_Guvence(_TL)` | ❌ (genel `RentalAddOn` ile elle girilebilir, isimli ürün/dahil-bedava mantığı yok) |
| Ekipman | `B_Koltuk_TL`/`C_Koltuk_TL` (bebek/çocuk koltuğu+adet), `Navigasyon_TL`, `Wifi_TL`, `Kis_Lastigi_TL`, `Sarj_Cihazi_TL`, `YolYardim_TL`, `Yedek anahtar` | 🟡 (`RentalAddOn` ile manuel) |
| Sürücü | `Ek_Surucu_TL`, `Genc_Surucu_TL`, `Uyelik_Bedeli`/`Uyelik_Karti_TL` | 🟡 (manuel add-on) |
| Serbest ek | `Ek_Hizmet1/2(_TL)`, `Ek_Bedel3/4(_TL)`, `Iptal_Bedeli/Iptal_TL` | 🟡 (`RentalAddOn`) |
| **Paketler** | `Paket_Hizmet1..6` + `_Gunluk` / `_Toplam` / `_TL` / `_Dahil` / `_Bedava` | ❌ (paket kavramı yok) |

### KM & yakıt & HGS & hasar
| `Ek_KM(_TL)` / `Km_Ucret` / `KM_Asim_Bedeli(_TL)` / `Fazla_Km` / `Sinirsiz_KM` / `Km_Sinir(_Hesap_Turu)` | ek/aşım km | ✅ `KmLimit`/`FazlaKmUcret`/`FazlaKm`/`FazlaKmBedeli` (sınırsız bayrağı yok) |
| `Cikis_Yakit`/`Donus_Yakit`/`Yakit_TL`/`Yakit_Manuel` | yakıt farkı | ✅ `YakitBirimUcret`/`EksikYakit`/`YakitBedeli` |
| `Gecis_TL` / `Gecis_8_TL` / `HGS_Hizmet_Orani` / `HGS_Aylik_Hizmet_Orani` / `Min_HGS_Hizmet` / `Gecis_Yansitma_Turu` | HGS/geçiş yansıtma | ❌ (kira ekranında; HGS ayrı modülde var) |
| `Hasar_Bedeli`/`Hasar_TL`/`Hasar_Nedeni` | hasar bedeli | ❌ (kira kapatmada hasar kalemi yok) |
| `Yikama_TL` | yıkama | ❌ |

### Drop / teslimat / komisyon / uzatma
| `Web_Drop_Bedeli` / `Adres_Teslim_TL` | farklı nokta bırakma / adrese teslim ücreti | ❌ |
| `F_Komisyon_Oran/Turu/Tutar` / `Komisyon_Min_Oran/Tutar` / `Tedarik_Oran/Tutar` / `Komisyon_Otomatik` | acenta/kaynak komisyonu | ❌ |
| `Uzatma_Fiyat` / `Uzatma_Hesaplanan` / `Uzatmalar_Kira_Fiyat` / `Yeni_Gun` / `Kira_Farki(_TL)` | uzatma | 🟡 (`UzatmaGun`/`UzatmaBedeli` alanı var, ayrı uzatma fiyat akışı yok) |

### Depozito / provizyon / tahsilat
| `Max_Guvence_*` (güvence/depozito), `TextBoxProvizyon_No/Tarih/Tutar`, `Provizyon_Cikis_Yok` | depozito + kredi kartı provizyonu | ❌ |
| `TextBoxTahsilat` / `TextBoxKalan` / `Odeme_Sekli` / kredi kartı | tahsilat | 🟡 (`Tahsilat`/`Bakiye` alanı var; ödeme şekli/kart/provizyon yok — tahsilat ayrı Kasa/Banka modülünde) |

---

## ÖZET TABLO

| # | Ekran | İşlev | Clone karşılığı | Durum |
|---|---|---|---|---|
| 1 | kiralama.aspx | Kira sözleşmesi (705 alan) | `RentalContract` + `/kiralar` + RentalDetail/Print | 🟡 |
| 2 | kira_listesi.aspx | Kira listesi + kolon seçici | `RentalList` `/kiralar` | 🟡 |
| 3 | rezervasyon.aspx | Rezervasyon (597 alan) | `Reservation` + `/rezervasyonlar` (+Quotation) | 🟡 |
| 4 | rezervasyon_listesi.aspx | Rezervasyon listesi | `ReservationList` + `/takvim` | 🟡 |
| 5 | filo_arac_kiralama.aspx | Uzun dönem/filo kiralama | — | ❌ |
| 6 | filo_kiralama_listesi.aspx | Filo kira listesi | — | ❌ |
| 7 | web_rezervasyon.aspx | Online web rezervasyon | — | ❌ |
| 8 | mobil_odeme.aspx | Tablet ödeme | — | ❌ |
| 9 | mobil_teslimat.aspx | Tablet teslimat | — | ❌ |
| 10 | servis_rezervasyon.aspx | Servis randevu rezervasyon | — | ❌ |
| 11 | broker_musaitlik_listesi.aspx | Broker müsaitlik + XML fiyat | `/musaitlik` (MusaitlikArama) | 🟡 |
| 12 | broker_yasaklari.aspx | Broker/kaynak satış kısıtları | — | ❌ |

**Skor:** ✅ 0 · 🟡 5 · ❌ 7 (4'ü gated içerik; işlev bazında zaten yok).

### Kritik 3 eksik (öncelik)
1. **Fiyat & sigorta katmanı** — tarife/liste fiyat/indirim/kampanya + sigorta ürünleri (CDW/SCDW/
   IMM/LCF/Mini Hasar/Max Güvence) + paket hizmetler + çok-döviz. Clone yalnız `Gun×GunlukUcret`.
   Fiyat motoru v1'in temel girdileri bunlar.
2. **Filo / uzun dönem kiralama** (filo_arac_kiralama + listesi) — aylık süre, vade gün, toplam km
   sınırı, damga vergisi, taksit; ayrı entity gerekir. Tamamen yok.
3. **Ödeme/provizyon ve komisyon** — kredi kartı provizyon, depozito (Max Güvence), acenta/kaynak
   komisyonu (F_Komisyon, Tedarik), drop/adres teslim ücreti. Kira ekranında para akışının operasyonel
   yarısı eksik (+ broker kuralları: min gün/bölge yasakları).
