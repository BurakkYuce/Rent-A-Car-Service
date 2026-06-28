# Parite 06 — Fiyat / Tarife / Kampanya / Maliyet + Araç Regülasyon (Sigorta/MTV/Muayene/Servis)

> Kaynak: diske kaydedilmiş canlı `turev2.turevrac.com` HTML (ÜMİT YÜCE oturumu, `yucerent`, 2026-06-28).
> Yalnız ekran/alan **yapısı** belgelenir — kimlik/müşteri verisi YOK.
> Alan adları WebForms `name="ctl00$ContentPlaceHolder1$X"` → tabloda `X` (kısaltılmış) verilir.
> 14 ekran incelendi. Clone karşılığı: `/Users/burak/Desktop/demo-apps/demo/src/`.

## İçindekiler
- A) FİYAT/TARİFE/KAMPANYA/MALİYET (7 ekran)
  1. `tarifeler.aspx` — Tarife Tanımlama (km/sigorta kademesi)
  2. `tarifeler_xml.aspx` — XML Tarife (kanal-bazlı günlük fiyat matrisi + onay)
  3. `fiyat_kampanya_yonetimi.aspx` — Fiyat Kampanya Yönetimi
  4. `kampanya_ara.aspx` — Kampanya Ara
  5. `maliyet_hesaplama.aspx` — Araç Maliyet/Filo Kiralama Hesaplama
  6. `maliyet_hesaplama_ara.aspx` — Maliyet Teklif Listesi
  7. `rakip_fiyat_analizi.aspx` — Rakip Fiyat Analizi (dinamik fiyat)
- B) ARAÇ REGÜLASYON / SİGORTA (7 ekran)
  8. `sigorta_tarife_listesi.aspx` — Sigorta & Ek Hizmet Tarife Kataloğu
  9. `sigorta_muayene.aspx` — Sigorta/Muayene Vade Panosu (grid)
  10. `sigorta_gider_ara.aspx` — Sigorta Gider Arama
  11. `arac_sigorta_islemleri.aspx` — Araç Sigorta İşlemi (+ Zeyil)
  12. `arac_mtv_islemleri.aspx` — Araç MTV İşlemi
  13. `arac_muayene_islemleri.aspx` — Araç Muayene İşlemi
  14. `arac_servis_islemleri.aspx` — Araç Servis/Hasar İşlemi
- [FİYATLANDIRMA KURALLARI / FORMÜLLER](#fiyatlandirma-kurallari--formuller) ← kritik
- [Clone durumu](#clone-durumu)
- [Özet tablo](#ozet-tablo)

---

# A) FİYAT / TARİFE / KAMPANYA / MALİYET

## 1. `tarifeler.aspx` — Tarife Tanımlama

**Amaç:** Bir tarifenin **gün kademesi başına km politikası + dahil sigorta paketi**ni tanımlar. (Dikkat: bu ekranda günlük KİRA fiyatı YOK — fiyat matrisi `tarifeler_xml`'de; bu ekran "kaç gün → kaç km dahil → aşım km bedeli + sigorta dahillikleri" kuralıdır.) Sekmeler: **Tarife Tanımlama** / **Tarife Listesi**.

**Kademe matrisi (6 satır: Gün 1 … Gün 6), kolonlar:** `Tarife Adı | Gün Aralığı | Km Sınırı | Aşım Bedeli`

| Alan (name=X) | Etiket | Tip | Not |
|---|---|---|---|
| `Tarife` | Tarife Adı | text | tarife başlığı |
| `Gun1..Gun6` | Gün 1…6 → "Gün Aralığı" | text | kademe gün üst sınırı |
| `Km1..Km6` | "Km Sınırı" | text | o kademede dahil km |
| `Km1_Ucret..Km6_Ucret` | "Aşım Bedeli" | text | km aşımı birim ücreti |
| `Indirim` | İndirim | text | tarife geneli indirim |
| `Tarife_Grubu` | Tarife Grubu | select | `- / Dönem` |
| `Islem_Sube` | İşlem Şube | select | `Hepsi / MERKEZ` |
| `Tarih` | İşl. Tarih | text(date) | işlem tarihi |
| `Tarife_Bitis_Tar` | İpt. Tarih | text(date) | iptal/bitiş |
| `Bas_Tar` / `Bit_Tar` | Baş./Bit. Tarih | text(date) | **sezon geçerlilik penceresi** |
| `SCDW_Dahil` | Sigorta SCDW | checkbox | SCDW dahil mi |
| `Mini_Hasar_Dahil` | Mini Hasar | checkbox | |
| `Hirsizlik_Dahil` | Hırsızlık Dahil | checkbox | |
| `SCDW_Zorunlu` | SCDW Zorunlu | checkbox | |
| `Gosterme` | Gösterme | checkbox | listede gizle |
| `Hepsi` | Aktifleri Göster | checkbox | liste filtresi |

**Butonlar:** Kaydet (×2), Vazgeç, "Evet, Devam Et" (onay modal).

---

## 2. `tarifeler_xml.aspx` — XML Tarife (kanal-bazlı günlük fiyat matrisi)

**Amaç:** Asıl **günlük kira fiyat listesi**. Kanal (Rez Kaynağı) + şube + lokasyon bazında, **onay iş akışlı**, geçerlilik tarihli, dinamik-indirim oranlı fiyat matrisi. Sekmeler: **Xml Tarife Tanımlama** / **Xml Tarife Listesi** / **Log Takip**.

**Fiyat matrisi:** `Tarife Adı` + **Gün 1 … Gün 7** (`Gun1..Gun7`) → her gün-kademesi için günlük fiyat.

| Alan (name=X) | Etiket | Tip | Not |
|---|---|---|---|
| `Tarife` | Tarife Adı | text | |
| `Gun1..Gun7` | Gün 1…7 | text | **günlük fiyat / gün-kademesi (7'ye kadar)** |
| `Turu` | Türü | select | **`Fiyat` / `Kampanya`** ← kampanya da bu matriste |
| `Max_Esneklik` | Karşılaştırma Sistemi İndirim Oranı % | text | **dinamik fiyat: rakip karşılaştırma indirimi** |
| `Drm` | Durum | select | `Aktif / Pasif` |
| `Onay` | Onay Durumu | select | `Onaylı / Bekliyor` |
| `Onaylayan` / `Onay_Zaman` | Tarih / Onaylayan | text | onay izi |
| `Create_DateTime` / `Last_DateTime` | Ekleme / Son Değişiklik | text | audit |
| `Tarih` | İşlem Tarihi | text(date) | |
| `Bas_Tar` / `Bit_Tar` | Kira Başlama / Bitiş Tarihi | text(date) | **fiyatın geçerli olduğu kira tarih aralığı** |
| `Kira_Suresi` | Max Kira Kapsamı | text | max kira süresi kapsamı |
| `RezKaynagi` / `Rez_Kaynagi` | Rez Kaynağı | text/select | **kanal: `Temel`, `ERENRENTACAR`** (çoklu kanal fiyatı) |
| `Islem_Sube` / `Sube` | İşlem Şube / Fiyat Listesi Oluştur | text/select | `Şube Seçiniz / MERKEZ` |
| `Ozel_Lkasyon` | Özel Lokasyon | text | lokasyon-özel fiyat |
| `OnayDrm`,`TarihX`,`Tarih1`,`Tarih2`,`Durum`,`Liste` | (Liste sekmesi filtreleri) | select/text | Filtrele |

**Kanal/onay modeli:** Fiyat listesi `Şube + Rez Kaynağı` kombinasyonu için "**Fiyat Listesi Oluştur**" ile üretilir; her kayıt **onaya** düşer (Onaylı/Bekliyor) → onaylı olmadan kanala yansımaz. `Türü=Kampanya` ile kampanya fiyatı aynı yapıda tutulur.

---

## 3. `fiyat_kampanya_yonetimi.aspx` — Fiyat Kampanya Yönetimi

**Amaç:** Kampanya listesi + seçilen kampanyanın fiyat detayı. Detay **iframe** ile yüklenir → `Kampanya_Iframe.aspx` (bu HTML setinde YOK). Başlıklar: `Fiyat Kampanya Yönetimi`, `Kampanya Listesi`, `Kampanya Fiyat Detayı`, `Broker Tedarikçi Oranları`.

**Görünür alanlar:** `Plaka_Ara`, `RA_Ara`, `Kayit_No`, `KampanyaYenileButonu` (Yenile). Detay JS ile gelir.

**iframe sekmeleri (JS referansı):** `Kiralama_Kurallari` (Kiralama Kuralları), `Kiralama_Kurallari_Basic`, `Oranlar` (oranlar/indirim). → Kampanya = **kural + oran** yapısı (min gün, indirim oranı vb. iframe içinde). **Bu detay indirilemedi; iframe ayrıca çekilmeli.**

---

## 4. `kampanya_ara.aspx` — Kampanya Ara

**Amaç:** Kampanyaları (kuralları) arama.

| Alan (name=X) | Etiket | Tip | Değerler |
|---|---|---|---|
| `txtKuralAdi` | Kampanya/Kural Adı | text | |
| `ddlTarihTipi` | Tarih Tipi | select | `Talep Tarihi / Rezervasyon Tarihi` |
| `txtBasTar` / `txtBitTar` | Tarih aralığı | text(date) | |
| `ddlDurum` | Durum | select | `Tümü / Planlandı / Aktif / Pasif / Taslak / İptal` |

> Kampanya = **"Kural"** olarak adlandırılıyor; yaşam döngüsü Taslak→Planlandı→Aktif→Pasif/İptal.

---

## 5. `maliyet_hesaplama.aspx` — Araç Maliyet / Filo Kiralama Hesaplama  ⭐

**Amaç:** **Uzun dönem / filo kiralama** için araç **toplam maliyet + başa-baş + teklif fiyatı** hesaplayıcı. (Günlük rent-a-car değil; operasyonel kiralama teklifi.) Menüde "Yeni Filo Kiralama / Maliyet Hesaplama / Maliyet Teklif Listesi" altında. DevExpress `dxCurrencyEdit*` alanları.

**Üst bilgi:** `Plaka`, `Arac_Bilgisi`, `Müşteri`, `Hazırlayan`.

**Girdi parametreleri (maliyet bileşenleri):**
| Etiket | Açıklama |
|---|---|
| Araç Satın Alma ÖTV+KDV / Araç Alım Bedeli (KDV HARİÇ) | satın alma bedeli |
| 2. El Değer Kaybı (yıpranma/amortisman) | amortisman |
| İkinci El Bedeli % DÖNÜŞ | kalıntı/residual değer % |
| Vade Farkı/Banka Faizi+Masraflar | finansman maliyeti |
| Kasko / Trafik Sigortası / MTV | yıllık vergi+sigorta |
| Bakım / Lastik / Muayene-Egzost Emisyon | periyodik bakım |
| Araç Takip Sistemi / Trafik Belgesi+Tescil+Plaka | sabit masraflar |
| Yedek Araç / Yönetim Gideri / Enflasyon | işletme + enflasyon |
| Yıllık Km Limiti / BAKIM PERİYODU | km/bakım periyodu |
| Kira Süresi/AY / Toplam Km Limiti | kiralama süresi (ay) |

**Kredi/finansman bloğu:** `Kredi_Hesaplama_Sekli` = **`Eşit Taksitli` / `Rotatif`**; Kredi Taksit Oran/KKDF/BSMV; Banka Dosya ve Diğer Masraflar.

**Çıktılar (hesaplanan):**
| Etiket | Anlam |
|---|---|
| TOPLAM | toplam maliyet |
| BAŞA BAŞ NOKTASI | break-even |
| KAR +/- | kâr/zarar |
| TEKLİF FİYATI KDV HARİÇ | net teklif |
| Sözleşme Damga Vergisi | damga |
| TEKLİF FİYATI SÖZLEŞME DAHİL | + damga |
| KDV / KDV + DAMGA VERGİSİ DAHİL | brüt teklif |

**Butonlar:** `Kaydet`, `Farklı Kaydet`, **`Sadece Hesapla`** (kaydetmeden hesapla).

---

## 6. `maliyet_hesaplama_ara.aspx` — Maliyet Teklif Listesi

**Amaç:** Kaydedilmiş maliyet/teklif hesaplarını arama.
**Filtreler:** `TextBox1` (Teklif Bilgisi), `Tarih_Listesi` (`Tarih Önemsiz / Tarih`), `Tarih1`/`Tarih2`.
**Grid (`GridView1`):** `Kayit No | Teklif Başlığı | Tarih | Plaka | Araç Fiyatı`. (Column Chooser + sayfalama.)

---

## 7. `rakip_fiyat_analizi.aspx` — Rakip Fiyat Analizi  ⭐ (dinamik fiyat)

**Amaç:** Rakip/pazar fiyatlarını ofis + tarih bazında çekip karşılaştırma (dinamik fiyatlamanın girdisi; `tarifeler_xml.Max_Esneklik` ile bağlantılı).

| Alan (name=X) | Etiket | Tip | Not |
|---|---|---|---|
| `ArTarih1` / `Bas_Saat` | Başlangıç Tarihi/Saat | text | |
| `ArTarih2` / `Bit_Saat` | Bitiş Tarihi/Saat | text | |
| `Cikis` | Çıkış Ofisi | select | büyük lokasyon listesi (Adana-… vb.) |
| `Donus` | Dönüş Ofisi | select | aynı lokasyon listesi |

**Grid (`ASPxGridViewRakipAnalizListesi`):** kolon `Arac` (+ dinamik rakip kolonları, veri boş geldi).
**Butonlar:** `Ara`, **`Excel'e Aktar`**.

---

# B) ARAÇ REGÜLASYON / SİGORTA

## 8. `sigorta_tarife_listesi.aspx` — Sigorta & Ek Hizmet Tarife Kataloğu  ⭐

**Amaç:** Online/rezervasyonda satılan **tüm ek hizmet + sigorta ürünlerinin fiyat & açıklama kataloğu** (günlük birim ücret, max gün sınırı, TR + EN açıklama, görünürlük anahtarı). Bu, kira toplamına eklenen kalemlerin master kaynağıdır. Üst: `Baslik` (Default), `Doviz` (`EURO/Kr/TL/USD`), `Listelem_Yonetemi` (`Sadece Parktakiler / Tüm Gruplar`).

**Ek hizmet / sigorta kalemleri (her biri: aktif chk + birim ücret + max gün + açıklama):**
- **Ek hizmet:** Bebek Koltuğu (`Bebek_Koltugu`), Çocuk Koltuğu (`Max_Cocuk_Koltuk`), Navigasyon, Ek Sürücü, Wifi (`Wifi_Bedeli` + `Max_Wifi`), Şarj Cihazı, Adrese Teslim, Kış Lastiği, Üyelik Bedeli, İptal Bedeli, Ek Hizmet 1/2.
- **Sigorta/teminat:** SCDW (`Max_SCDW`), Mini Hasar (`Max_Mini_Hasar`), LCF (`Max_LCF`), CDW, PAI, **IMM** (İhtiyari Mali Mesuliyet), **Muafiyet Sigortası** (`Muafiyet_Sigortasi_*`), Yol Yardım (`YolYardim_Max`), Genç Sürücü (`Max_Genc_Surucu`), Max Güvence, Süper Mini.
- **Paket Sigorta Hizmeti 1–6:** `ChkPaketHizmetN` + `Max_HizmetN` + `Paket_HizmetN_Tanim` + `Paket_HizmetN_Aciklama`.
- **Ek KM Paketleri 1–4:** `ChkKmPaketN` + `Km_PaketN_Aciklama`.
- **Çok dilli:** her kalemin `*_Tanim_En` ve `*_Aciklama_En` (İngilizce tanım/açıklama) alanı var.

> Her teminatın **Max (Gün)** alanı: o teminatın faturalanacağı max gün tavanı (uzun kirada teminat ücreti N günle sınırlı). Bu, fiyat motorunda ek-hizmet hesaplamasının kuralıdır.

---

## 9. `sigorta_muayene.aspx` — Sigorta/Muayene Vade Panosu (grid)

**Amaç:** Tüm filonun sigorta/kasko/muayene/Z-İzni/seyrusefer **vade takip** ekranı (salt okunur grid).
**Filtreler:** `Arac_Sahibi` (`Hepsi / Bizim / Dış Araç`), `Turu` (İşlem Tipi: `Hepsi / Muayene / Trafik Sigortası / Kasko / Z-İzni / Seyrusefer`), `ArTarih_Listesi` (`Tarih Önemsiz / Bitiş`) + tarih aralığı, `Arac_Sahip_Isim`.
**Grid (`GridViewSig_Muayene`):**
`Plaka | Marka | Tipi | Yılı | Yakıt Turu | Vites | Şube | Grup | Araç Belge No | Araç Şasi No | Araç Sahibi | Sigorta Bit. Tar | Trafik Pol. No | Trafik Acenta | Kasko Bit. Tar | Kasko Pol. No | Kasko Acenta | Muaye Bit. Tarih | Z-İzni | Seyrusefer | Motor No | Araç Kimde | Sigorta Firma | Kasko Firma`
(Excel/Column Chooser/gruplama + sayfalama.)

---

## 10. `sigorta_gider_ara.aspx` — Sigorta Gider Arama

**Amaç:** Sigorta giderlerini (kasko/trafik) arama.
**Filtreler:** `TextBox1` (Cari), `Tarih_Listesi`+`Tarih1/2`, `Ofis` (`Hepsi/MERKEZ`), `Plaka`, `Gider_Adi` (Sigorta Türü: `Tüm Giderler / Kasko / Trafik Sigortası`), `Sat_Aktif_Sigorta` chk.
**Grid (`GridView1`):** `Gider Adı | Cari Bilgi | Plaka | Marka | Tipi | Yakıt Türü | Vites | Model | Ödeme Aracı | Tarih | TL Toplam | Vergi | Kalan | Fatura Toplam | Evrak No | Durum | Açıklama | Hazır Açıklama | Sözleşme No | Kira Müşteri`.

---

## 11. `arac_sigorta_islemleri.aspx` — Araç Sigorta İşlemi (+ Zeyil)

**Amaç:** Araç sigorta poliçesi gideri kaydı + ödeme + **zeyil (poliçe değişikliği)** alt-sistemi.

| Alan (name=X) | Etiket |
|---|---|
| `Tarih` | Tarih |
| `Islem_Sube` | İşlem Şube (MERKEZ) |
| `Gider_Adi` | Sigorta Türü |
| `Evrak_No` | Poliçe No |
| `Sigorta_Bas_Tar` / `Sigorta_Bit_Tar` | Sigorta Baş./Bit. Tarih |
| `Arac_Degeri` / `IMM_Degeri` / `Aksesuar_Degeri` | Araç/IMM/Aksesuar Değeri |
| `Tutar` / `Kalan` | Tutar / Kalan |
| `Plaka`,`Arac_Bilgisi` | Araç |
| `Sigorta_Firmasi` / `Musteri_No`(Acente) | Sigorta Firması / Acente |
| `Islem_KM` | İşlem KM |
| **Ödeme bloğu** | `Odeme_Tarihi`,`Odeme`(+`Odeme_Doviz`/`Odeme_Kur`),`Odeme_Turu`(Nakit/Banka),`Kasa_Kodu`,`Hesap_No`(IBAN),`Aciklama` |
| **Zeyil bloğu** | `Zeyil_Tarihi`,`Zeyil_Tanizimi`,`Zeyil_No`,`Zeyil_Aciklama`,`Zeyil_Tipi`,`Zeyil_Neden`,`Zeyil_Degeri`,`Zeyil_Brut_Prim`,`Zeyil_Net_Prim`,`Zeyil_Vergi` |
| Araç bilgi (salt) | `PlakaX`,`Tipi`,`Vites`,`Arac_Grubu`,`Marka`,`Yili`,`Yakit_Turu` |

**Zeyil grid (`ASPxGridViewZeyil`):** `Zeyil No | Zeyil Tarih | Tanzim Tarihi | Zeyil Değeri | Bürüt | Net | Fon/Vergi | Tipi | Neden`.
**Toplu giriş:** `FileUpload1` (Excel: Plaka, Tür(Kasko/Trafik)…).

---

## 12. `arac_mtv_islemleri.aspx` — Araç MTV İşlemi

**Amaç:** MTV (motorlu taşıt vergisi) dönem tahakkuk + ödeme kaydı.

| Alan (name=X) | Etiket |
|---|---|
| `Tarih` | Tahakkuk Tarihi |
| `Bandrol_Donemleri` | Tahakkuk Dönemi (`2026-2, 2026-1, 2025-2 …` — **6 aylık dönemler**) |
| `Tutar`(+`Tutar_Doviz`/`Tutar_Kur`) / `Kalan` | Tutar / Kalan |
| `Gider_Adi` | Gider Adı |
| `Islem_Sube` / `Evrak_No` / `Islem_Yapan` | Şube / Evrak / İşlem Yapan |
| `Plaka`,`Arac_Bilgisi` / `Musteri_No`(Kurum) | Araç / Kurum |
| **Ödeme** | `Odeme_Tarihi`,`Odeme`(+döviz/kur),`Odeme_Turu`(Nakit/Banka),`Kasa_Kodu`,`Hesap_No`,`Aciklama` |
| Araç bilgi (salt) | `PlakaX`,`Tipi`,`Vites`,`Arac_Grubu`,`Marka`,`Yili`,`Yakit_Turu` |
| Toplu | `FileUpload1` (Excel: Plaka, Donem, Evrak No…) |

---

## 13. `arac_muayene_islemleri.aspx` — Araç Muayene İşlemi

**Amaç:** Araç muayene gideri + geçerlilik + ödeme.

| Alan (name=X) | Etiket |
|---|---|
| `Tarih` | Tarih |
| `Muayene_Bit_Tar` | Muayene Geçerli Tarih (vade) |
| `Tutar` / `Ceza_Tutari` / `Kalan` | Tutar / **Ceza** / Kalan |
| `Gider_Adi` / `Islem_Sube` / `Evrak_No` / `Islem_Yapan` | |
| `Plaka`,`Arac_Bilgisi` / `Musteri_No`(Muayene Firması) | Araç / Firma |
| `Islem_KM` | İşlem KM |
| **Ödeme** | `Odeme_*`,`Kasa_Kodu`,`Hesap_No`,`Aciklama` |

---

## 14. `arac_servis_islemleri.aspx` — Araç Servis / Hasar İşlemi  ⭐

**Amaç:** Servis/bakım **VE hasar/kaza** yönetimi tek ekranda: iş akışı durumu, çıkış/dönüş km+yakıt, kusur/hasar beyanı, **yansıtma (rücu)**, işçilik kırılımı, servis faturası kalemleri.

**İşlem türü:** `CheckBoxPeriyodik`(Periyodik Servis), `CheckBoxHasar`(Hasar-Kaza), `CheckBoxMekanik`(Mekanik Arıza), `CheckBoxBakim`(Bakım).
**Durum (`DropDownListDurum`):** `Servis Talebi → Servise Gönder → Devam Ediyor → Servisten Al → Tamamlandı`.

| Blok | Alanlar (name=X) |
|---|---|
| Temel | `Tarih`,`Tutar`(+döviz/kur),`Kdv_Orani`,`Kdv_Tutari`,`Kalan`,`UcretBilgi`,`Gider_Adi`(Servis),`Islem_Sube`,`Evrak_No`,`Islem_Yapan` |
| Araç/Servis | `Plaka`,`Arac_Bilgisi`,`Musteri_No`/`Servis_Bilgisi`/`Servis_Yeri`(Servis Firması) |
| Zaman | `TxtGonderilme_Tar/Saat`,`Plan_Tarih/Saat`,`TxtBitis_Tar/Saat` |
| KM/Yakıt | `Islem_KM`(Çıkış),`Cikis_Yakit`(0–9),`TxtDonus_Km`,`Donus_Yakit`(0–9),`Uyari_KM` |
| Açıklama | `TxtOnc_Aciklama`,`TxtSnr_Aciklama`,`Aciklama`,`Hazir_Aciklama`,`Kayit_Log` |
| **Hasar/Kusur** | `DropDownKimOdeyecek`(`Biz/Kasko/Sigorta/Müşteri/Karşı Taraf`), `Kusur_Durumu`(`0/25/50/75/100`), `Beyan_Turu`(`Tutanak Yok/Beyan/Anlaşmalı Tutanak/Resmi Tek Taraflı/Resmi Çift Taraflı/Cam/Rücu Edilemeyen Hasar/Hasar Yansıtma`), `Karsi_Plaka`,`Karsi_Trafik_Sigortasi`,`Kaza_Tarihi`,`Kaza_Sorumlusu`,`Hasar_Dosya_No`,`Deger_Kaybi` |
| **Yansıtma (rücu)** | `Yansitma_Tutari`,`Garanti_Tutari`,`Yansitma_Cari_Kod`,`Yansitma_Cari`,`Yansitma_Tutar`,`Sozlesme_No` |
| **İşçilik kırılımı** | `Kaporta_Iscilik_Servis`,`Boya_Iscilik_Servis`,`Trim_Iscilik_Servis`,`Elektrik_Iscilik_Servis`,`Mekanik_Iscilik_Servis`,`Sasi_Iscilik_Servis`,`Toplam_Servis`(Toplam İşçilik) |
| **Servis faturası** | `Fatura_Bilgisi`,`Fatura_Tarih`,`Fatura_No`,`Fatura_Tutar`,`Fatura_KDV`,`Fatura_Genel_Toplam` |
| **Ödeme** | `Odeme_*`,`Kasa_Kodu`,`Hesap_No` |

**Fatura kalem grid (`FaturaGridView1`):** `Açıklama | Birim Fiyat | Toplam Fiyat | İndirim | Tutar | KDV | Genel Toplam`.

---

# FİYATLANDIRMA KURALLARI / FORMÜLLER

> En kritik bölüm — fiyat motoru v1+ paritesinin temeli. Canlı tam formüller 403 ile engelli; aşağısı **HTML alan yapısından çıkarılan model**.

## F1. Günlük kira fiyatı — iki katmanlı tarife
1. **Km/teminat kademesi (`tarifeler.aspx`):** 6 gün-kademesi. Her kademe = `Gün Aralığı` (üst sınır) → `Km Sınırı` (dahil km) → `Aşım Bedeli` (km aşımı birim ücreti). + tarife-geneli `İndirim`, sezon penceresi (`Bas_Tar/Bit_Tar`), şube, dahil teminatlar (SCDW/Mini Hasar/Hırsızlık) ve `SCDW Zorunlu`.
2. **Günlük fiyat matrisi (`tarifeler_xml.aspx`):** asıl para. **Gün 1…7** = gün-kademesi başına günlük fiyat. Boyutlar: **Şube × Rez Kaynağı (kanal) × Özel Lokasyon × tarih penceresi**. Her satır `Türü = Fiyat | Kampanya`, **onay** gerektirir (Onaylı/Bekliyor), audit izli.
   - **Çıkarım:** efektif günlük ücret = ilgili kanal+şube+lokasyon+tarih için, kira gün sayısının düştüğü gün-kademesinin (Gün 1…7) fiyatı.

## F2. Gün kademesi mantığı
- 7 kademeye kadar; uzun kirada düşük günlük ücret (kademeli iniş). Clone'da kademe `MinGun/MaxGun`, canlıda 1..7 satır pozisyonu. **Gün sayma konvansiyonu** (24h blok / yukarı yuvarlama) HTML'den çıkmıyor — canlı `Gun_Hesapla` paritesi hâlâ gerekli.

## F3. Km / aşım
- Kademe başına **dahil km** (`Km_N`) + **aşım birim ücreti** (`Km_N_Ucret`). Aşım = `(gerçek_km − dahil_km) × aşım_ücreti`. + `sigorta_tarife_listesi`'nde **Ek KM Paketleri 1–4**.

## F4. Sezon / dönem
- `tarifeler.Bas_Tar/Bit_Tar` + `Tarife_Grubu = Dönem` ve `tarifeler_xml.Bas_Tar/Bit_Tar` (kira tarih aralığı) → tarih-bazlı sezon fiyatı. Clone `RateCard.GecerliBas/Bit` ile kısmen var.

## F5. Kampanya / kural
- Kampanya = **Kural** (`kampanya_ara`: Taslak/Planlandı/Aktif/Pasif/İptal). İki sunum: (a) `tarifeler_xml.Türü=Kampanya` matrisi, (b) `fiyat_kampanya_yonetimi` → `Kampanya_Iframe.aspx` (Kiralama Kuralları + Oranlar; min gün/indirim oranı **iframe içinde, indirilemedi**).

## F6. Dinamik fiyat (rakip karşılaştırma)
- `tarifeler_xml.Max_Esneklik` = **"Karşılaştırma Sistemi İndirim Oranı %"** → rakip fiyatına göre esneklik tavanı.
- `rakip_fiyat_analizi` ofis+tarih bazlı rakip fiyat çeker (besleme). → fiyatı rakibe göre indirim oranı kadar otomatik ayarlayan **dinamik fiyat** çekirdeği.

## F7. Ek hizmet / teminat fiyatı (`sigorta_tarife_listesi`)
- Her ek hizmet/teminat: **günlük birim ücret × gün**, ama **Max (Gün)** tavanıyla sınırlı (örn. SCDW max 10 gün → 30 günlük kirada 10 gün SCDW). Paket Sigorta 1–6 ve Ek KM Paketleri 1–4 ayrı kalemler. Çoklu döviz + TR/EN açıklama.

## F8. Filo/uzun-dönem maliyet (`maliyet_hesaplama`)
- Günlük rentten ayrı **operasyonel kiralama** maliyet modeli. Bileşenler: (satın alma ÖTV+KDV − residual %) + finansman (vade farkı / KKDF / BSMV, Eşit Taksitli/Rotatif) + (kasko+trafik+MTV+bakım+lastik+muayene+takip+yedek araç+yönetim) yıllık × süre + enflasyon.
  - **BAŞA BAŞ NOKTASI** (break-even), **KAR +/-**, **TEKLİF FİYATI** (KDV hariç → +damga → +KDV) çıktıları. "Sadece Hesapla" / "Kaydet" / "Farklı Kaydet".

---

# Clone durumu

Kaynak: `/Users/burak/Desktop/demo-apps/demo/src/`.

| Canlı ekran/özellik | Clone karşılığı | Durum |
|---|---|---|
| `tarifeler` (gün+km+teminat kademesi) | `RateCard` (Grup, MinGun/MaxGun, GunlukUcret, GecerliBas/Bit, Aktif) | 🟡 kısmî — sadece günlük ücret kademesi; **km sınırı/aşım, dahil teminat (SCDW/Mini/Hırsızlık), indirim, şube YOK** |
| `tarifeler_xml` (kanal×şube×lokasyon fiyat matrisi + onay) | — | ❌ yok — `RateCard` tek katman; kanal (`ReservationSource` var ama fiyata bağlı değil), **onay iş akışı, Gün 1-7 matris, lokasyon-özel YOK** |
| `fiyat_kampanya_yonetimi` / `kampanya_ara` | — | ❌ **Kampanya/kural motoru hiç yok** |
| `maliyet_hesaplama` / `_ara` | `ReportService` "servis maliyeti" raporu (alakasız) | ❌ **Filo maliyet/başa-baş/teklif hesaplayıcı yok** |
| `rakip_fiyat_analizi` + `Max_Esneklik` | — | ❌ **Dinamik/rakip fiyat hiç yok** |
| `sigorta_tarife_listesi` (teminat+ek hizmet kataloğu, max gün, paketler) | `EkHizmetTanim` (Kod/Ad/BirimUcret/KdvOrani) | 🟡 generic ek hizmet master var; **Max-gün tavanı, paket/teminat tipleri (PAI/IMM/LCF/SCDW/Muafiyet), TR/EN, döviz YOK** |
| `sigorta_muayene` (vade panosu grid) | InsurancePolicy/Mtv/Inspection + "vade panosu" (CLAUDE.md'de var) | 🟡 vade panosu mevcut; **Z-İzni/Seyrusefer, acenta/motor no kolonları, Bizim/Dış filtre eksik olabilir** |
| `arac_sigorta_islemleri` (+ Zeyil) | `InsurancePolicy` (Tip/PoliceNo/Baş/Bit/Firma/Acenta/Prim) | 🟡 poliçe var; **Zeyil (endorsement) alt-sistemi, IMM/Aksesuar değeri, ödeme entegrasyonu, Excel toplu YOK** |
| `arac_mtv_islemleri` | `MtvRecord` (Donem/Tutar/Vade/Odendi) | 🟡 kayıt var; **döviz/kur, ödeme bloğu (kasa/banka), evrak, Excel toplu YOK** |
| `arac_muayene_islemleri` | `InspectionRecord` (MuayeneTarihi/Bitis/Ucret) | 🟡 kayıt var; **Ceza, ödeme bloğu, firma, İşlem KM YOK** |
| `arac_servis_islemleri` (servis+hasar+yansıtma+işçilik) | `ServiceRecord`+`ServiceLine` (+ `DamageFile`, `HasarSorumlu`, `KusurOrani`) | 🟡 servis+işçilik kalemi + kusur oranı var; **Beyan türü, yansıtma/rücu cari, işçilik kırılımı (kaporta/boya/trim/elektrik/mekanik/şase), çıkış/dönüş yakıt, servis faturası, iş akışı durumları kısmi/eksik** |
| Fiyat motoru | `PricingService` + `BookingMath` | 🟡 manuel ücret kazanır, yoksa `RateCard` lookup; **kademe/km/teminat/kampanya/dinamik YOK** |

---

# Özet tablo

| # | Ekran | Amaç | Clone |
|---|---|---|---|
| 1 | tarifeler | Gün+km+teminat kademe kuralı | 🟡 RateCard (kısmî) |
| 2 | tarifeler_xml | Kanal×şube fiyat matrisi + onay | ❌ |
| 3 | fiyat_kampanya_yonetimi | Kampanya yönetimi (iframe) | ❌ |
| 4 | kampanya_ara | Kampanya/kural arama | ❌ |
| 5 | maliyet_hesaplama | Filo maliyet/başa-baş/teklif | ❌ |
| 6 | maliyet_hesaplama_ara | Maliyet teklif listesi | ❌ |
| 7 | rakip_fiyat_analizi | Rakip/dinamik fiyat | ❌ |
| 8 | sigorta_tarife_listesi | Teminat+ek hizmet kataloğu | 🟡 EkHizmetTanim (kısmî) |
| 9 | sigorta_muayene | Sigorta/muayene vade panosu | 🟡 vade panosu var |
| 10 | sigorta_gider_ara | Sigorta gider arama | 🟡 (gider altyapısı var) |
| 11 | arac_sigorta_islemleri | Sigorta poliçe + zeyil | 🟡 InsurancePolicy (zeyil yok) |
| 12 | arac_mtv_islemleri | MTV tahakkuk+ödeme | 🟡 MtvRecord (ödeme yok) |
| 13 | arac_muayene_islemleri | Muayene+ceza+ödeme | 🟡 InspectionRecord (kısmî) |
| 14 | arac_servis_islemleri | Servis+hasar+yansıtma | 🟡 ServiceRecord (kısmî) |

**Skor:** ✅ 0 · 🟡 8 · ❌ 6 (14 ekran).

## Kritik 3 eksik (fiyat motoru paritesi için)
1. **`tarifeler_xml` — kanal×şube×lokasyon×tarih günlük fiyat matrisi (Gün 1-7) + onay iş akışı.** Asıl fiyat kaynağı; clone'da tek katmanlı `RateCard` var.
2. **Kampanya/kural + dinamik fiyat motoru** (`fiyat_kampanya_yonetimi` + `Kampanya_Iframe.aspx` + `rakip_fiyat_analizi` + `Max_Esneklik`). Clone'da hiç yok.
3. **`maliyet_hesaplama` — filo/uzun-dönem maliyet + başa-baş + teklif hesaplayıcı** (KKDF/BSMV/damga/enflasyon dahil). Clone'da yok.

## Eksik veri / takip
- **`Kampanya_Iframe.aspx` indirilemedi** — kampanya kural/oran (min gün, indirim oranı) detayı orada. Ayrıca çekilmeli.
- Canlı tam fiyat **formülleri** (Gun_Hesapla, kademe seçim, max-gün uygulama) 403 engelli — kurallar HTML alan yapısından çıkarıldı, ampirik doğrulama gerekir.
