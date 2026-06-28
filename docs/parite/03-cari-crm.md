# Parite 03 — Cari / Müşteri / CRM / Personel / Hukuk

> Kaynak: canlı `turev2.turevrac.com` HTML dökümleri (2026-06-28 taraması), `parite_html/`.
> Karşılaştırma hedefi: clone `src/` — `Domain/Entities/Customer.cs`, `/cariler` ekranları, `CustomerGroup`.
> NOT: Yalnız **alan adları / ekran yapısı** belgelenir; kimlik/müşteri verisi YOK.
> Form alan adı sözleşmesi: ASP.NET WebForms `name="ctl00$ContentPlaceHolder1$<Control>$<Alan>"`.
> Cari kayıt formunun control'ü `Cari_KartX`, personel formunda doğrudan `ContentPlaceHolder1`.

Bu modülde 14 ekran incelendi. Clone'da **Customer (Cari)** entity + `/cariler` (liste/düzenle/ekstre) + `CustomerGroup` master VAR; **Personel / Hukuk / Anket / Şikayet / CRM-analitik / Cari-virman / Gelen-mesaj YOK.**

Lejant: ✅ clone'da var · 🟡 kısmen/farklı yapıda · ❌ clone'da yok.

---

## 1. musteri_kayit.aspx — Cari (Müşteri) Kayıt Kartı  🟡

**Amaç:** Bireysel / Kurumsal / Servis cari kartı oluştur-düzenle. Tek formda sekmeli (tab) yapı: temel + Kimlik Bilgileri + Diğer Adres Bilgileri + Hizmet/yetki. Toplam 124 form kontrolü (çoğu hidden/teknik), **~80 anlamlı alan**.

Clone karşılığı: `Customer.cs` + `CustomerInput.cs` + `CustomerEdit.razor` → **~36 alan** (kabaca yarısı).

### 1.1 Kimlik / temel
| Etiket (canlı) | name (`Cari_KartX$…`) | Tip | Clone |
|---|---|---|---|
| Müşteri Türü (Bireysel/Kurumsal/Servis) | `Firma_Tipi` (hidden seçici) | hidden | ✅ `Tip` (CariType: Bireysel/Kurumsal/Servis) |
| Müşteri No | `Musteri_No` | hidden | 🟡 yok (clone'da müşteri no/sıra yok; sadece Guid Id) |
| Ad | `Musteri_Ad` | text | ✅ `Ad` |
| Soyad | `Musteri_Soyad` | text | ✅ `Soyad` |
| TC Kimlik No | `TC_Kimlik` | text | ✅ `TcKimlik` |
| TC Doğrulama (NVI) | `TC_Dogrulama` | checkbox | ❌ |
| Fatura Ünvanı | `Fatura_Unvani` | text | 🟡 `Unvan` (tek ünvan alanı) |
| Vergi Dairesi | `Vergi_Dairesi` | text | ✅ `VergiDairesi` |
| Vergi Numarası | `Vergi_Numarasi` | text | ✅ `VergiNo` |
| (oto vergi no üret) | `Otomatik_Vergi_Numarasi` | hidden | ❌ |
| Kurumsal No | `Kurumsal_No` | text | ❌ |
| Müşteri Türü (ehliyet uyumu) | `Musteri_Tipi` | select: Türk Ehliyetli / Yabancı-Yabancı Ehliyetli / Türk-Yabancı Ehliyetli | ❌ |
| Müşteri Sınıfı (**segment**) | `Mst_Sinif` | select: Düşük / Orta / Yüksek / VIP / Personel / Problemli | ❌ |
| Özel Cari Tip | `Ozel_Cari_Tip` | select: Yurtiçi / Yurtdışı / 2.El / Grup İçi / Standart / VIP / Hassas | ❌ |
| Özel Kod | `Ozel_Kod` (+`System_Ozel_Kod`) | text | 🟡 `CustomCode` master var ama cariye bağlı alan yok |
| Entegrasyon Kodu | `Entegrasyon_Kodu` | text | ❌ |
| Konuşma Dili | `Dil` | select | ❌ |
| Döviz (cari varsayılan) | `Doviz` | select | ❌ |

### 1.2 İletişim
| Etiket | name | Tip | Clone |
|---|---|---|---|
| Cep Tel / Gsm | `Cep_Tel` | text | ✅ `CepTel` |
| Gsm2 | `GSM2` | text | ✅ `Gsm2` |
| Telefon (Tel2) | `Tel2` | text | ❌ (clone'da 2 telefon var, 3.sü yok) |
| İş Telefonu | `Is_Telefonu` | text | ❌ |
| Mail Adresi | `Mail_Adresi` | text | ✅ `Email` |
| Mail İzin | `Mail_Izin` | checkbox | 🟡 clone tek `IysIzinli` bool |
| SMS İzin | `Sms_Izin` | checkbox | 🟡 (IYS'ye gömülü) |
| Telefon İzin | `Telefon_Izin` | checkbox | 🟡 (IYS'ye gömülü) |

> **Önemli fark:** Canlıda izin 3 ayrı kanal (Mail/SMS/Telefon). Clone'da tek `IysIzinli`. İYS uyumu için kanal-bazlı izin gerekebilir.

### 1.3 Adres
| Etiket | name | Tip | Clone |
|---|---|---|---|
| Şehir | `Sehir` | text | 🟡 `Il` |
| İlçe | `Ilce` | text | ✅ `Ilce` |
| Mahalle-Köy | `Mahalle_Koy` | text | ❌ |
| Ülke | `Ulke` | text | ❌ |
| Adres (ev) | `Adres` | textarea | ✅ `Adres` |
| Kayıtlı İl | `Kayitli_Il` | text | ❌ (nüfus kayıt) |
| Kayıtlı İlçe | `Kayitli_Ilce` | text | ❌ |
| İş Adresi | `Is_Adresi` | textarea | ❌ |
| Fatura Adresi Farklı | `Fatura_Adres_Farkli` | select Evet/Hayır | ❌ |
| Fatura Adresi | `Fatura_Adresi` | textarea | ❌ |

### 1.4 Kimlik (nüfus) Bilgileri sekmesi
| Etiket | name | Clone |
|---|---|---|
| Baba Adı | `Baba_Adi` | ❌ |
| Anne Adı | `Anne_Adi` | ❌ |
| Doğum Yeri | `Dogum_Yeri` | ❌ |
| Doğum Tarihi | `Dogum_Tar` | ❌ (doğum günü takip için gerekli) |
| Cilt No | `Cilt_No` | ❌ |
| Seri No | `Seri_No` | ❌ |
| Aile Sıra No | `Aile_Sira` | ❌ |
| Sıra No | `Sira_No` | ❌ |

### 1.5 Ehliyet & Pasaport
| Etiket | name | Clone |
|---|---|---|
| Ehliyet No | `Ehliyet_No` | ✅ `EhliyetNo` |
| Ehliyet Sınıfı | `Ehliyet_Sinifi` | ✅ `EhliyetSinifi` |
| Ehliyet Veriliş Tarihi | `Ehliyet_Tar` | ✅ `EhliyetTarihi` |
| Ehliyet Verilen Yer | `Ehliyet_Yer` | ✅ `EhliyetYeri` |
| Ehliyet Ülke | `Ehliyet_Ulke` | ❌ |
| Pasaport Numarası | `Pasaport_No` | ❌ |
| Pasaport Tarihi | `Pasaport_Tar` | ❌ |
| Pasaport Verilen Yer | `Pasaport_Yer` | ❌ |

### 1.6 CRM / temsilci / ticari
| Etiket | name | Tip | Clone |
|---|---|---|---|
| Müşteri Temsilcisi | `Musteri_Temcilcisi` | select | 🟡 `MusteriTemsilcisi` (serbest string) |
| Rezervasyon Kaynağı | `Rezervasyon_Kaynagi` | select | 🟡 `Kaynak` (serbest string) |
| Web İndirim | `Web_Indirim` | text | ❌ |
| Bayi Komisyon Oranı | `Bayi_Komisyon` | text | ❌ |
| Broker (cari) | `Broker` | checkbox | ❌ |
| Doğum Günü Takip | `Dogum_Gunu_Takip` | checkbox | ❌ |

### 1.7 Finans / risk / fatura
| Etiket | name | Tip | Clone |
|---|---|---|---|
| Tarife | `Tarife` | select | 🟡 `Tarife` (serbest string) |
| Vade Gün | `Vade_Gun` | text | ✅ `VadeGun` |
| Risk Limiti | `Risk_Limiti` | text | ✅ `RiskLimiti` |
| Risk Mesajı | `Risk_Mesaj` | text | ✅ `RiskMesaji` |
| Risk Mesajı Tarihi | `Risk_Mesaj_Tarih` | text | ✅ `RiskTarihi` |
| Risk İzinli | `DropDownListRisk_Izin` | select Normal/İzinli | ❌ |
| (risk analiz izinsiz) | `Risk_Analiz_Izinsiz` | hidden | ❌ |
| Findex Zorunlu | `Findex_Zorunlu` | checkbox | ❌ |
| Fatura Dönemi | `Fatura_Donemi` | select: Kira Bitişi / Kira Başlangıcı / Ay Sonu / Ay 1. Günü / Mutabakat Üzerine / İlk Rez Baz / Son Rez Baz / Otomatik Fatura Yok | ❌ |
| Fatura Tek Satır | `Fatura_Tek_Satir` | checkbox | ❌ |
| Tevkifat Kodu | `Tevkifat_Kodu` | select | ❌ |
| Tevkifat Fatura Durumu | `Tevkifat_Fatura_Durumu` | select: Serbest / Sadece Tevkifatsız / Sadece Tevkifatlı | ❌ |
| (e-fatura alanı zorunlu) | `E_Fatura_Alan_Zorunlu` | hidden | ❌ |
| Geçiş Yansıtma (HGS/OGS) | `Gecis_Yansit_Musteri` | select: Varsayılan / Ayrı / Sadece Kapanınca Ayrı | 🟡 `HgsYansitmaTuru` (serbest string) |
| Banka Bilgileri (IBAN vb.) | `Banka_Bilgileri` | textarea | ❌ |
| Şifre (web rez) | `Sifre` | text | ❌ |

### 1.8 Uyarı / durum / kara liste
| Etiket | name | Tip | Clone |
|---|---|---|---|
| Uyarı (mesaj metni) | `Uyari` | textarea | 🟡 clone `Uyari` **bool** + `UyariNedeni` string |
| Uyarı Nedeni | `Uyari_Nedeni` | select | 🟡 `UyariNedeni` (string) |
| Uyarı Liste Zamanı | `Kara_Zamani` | text | ❌ |
| (kara listeye ekleyen) | `Kara_Liste_Ekleyen` | hidden | 🟡 clone `KaraListe` bool var, "ekleyen/zaman" izi yok |
| Araç Verilmez | `Arac_Verilmez` | checkbox | ❌ |
| Pasif | `Pasif` | checkbox | ✅ `Pasif` |

### 1.9 KVKK anonimleştirme (clone'da hiç yok ❌)
`Cari_Anonim`, `Kira_Anonim`, `Fatura_Anonim`, `Ceza_Anonim`, `Rezervasyon_Anonim`, `Musteri_Karti_Anonim` (checkbox) + `KVKK_Modul` (hidden). Modül bazlı kişisel veri anonimleştirme.

### 1.10 Yetki / diğer
| Etiket | name | Clone |
|---|---|---|
| Bakiye Görebilir (şube) | `Bakiye_Gor` (select, ör. MERKEZ) | ❌ |
| İşlem Şube | `Islem_Sube` (select) | 🟡 (şube serbest metin) |
| Açıklama | `Aciklama` (textarea) | ❌ |
| Merkez Kurumsal (HQ bağı) | `Merkez_Kurumsal` (checkbox) | ❌ |
| Fatura Kiralayan İsim | `Fatura_Kiralayan_Isim` (checkbox) | ❌ |
| Yaş/Ehliyet Serbest | `Yas_Ehliyet_Serbest` (checkbox) | ❌ |
| Kart Özel Yetki | `Cari_Kart_Ozel_Yetki` (hidden) | ❌ |

**Butonlar:** Kaydet. (Sekmeli kart içinden ilişkili modüllere kısayollar: faturalar, ceza/geçiş, banka.)

---

## 2. musteri_listesi.aspx — Cari Listesi  🟡

**Amaç:** Cari kartlarını kira agregalarıyla listele (DevExpress grid, kolon seçici + Excel).

**Filtreler:** Cari Ara (`TextBox1`=ad/ünvan, `TextBox2`=Soyad) · Cari Tipi (`Cari_Tipi` select) · Aktif/Pasif Durumu (`Durum` select) · Hepsi (`Hepsi` checkbox).

**Grid kolonları:** Cari Bilgi · Soyad · Tc Kimlik No · Pasaport No · Cep Tel · Tel2 · Tel3 · Mail Adresi · Ülke · Şehir · Özel Kod · Önem · Uyarı · Kiralama Adeti · Toplam Gün · Toplam Kira · Toplam Hizmet · Bakiye · Ortalama Günlük · Ortalama Km · İlk Kira Tar. · Son Kira Tar. · ID.

**Butonlar:** Yeni Bireysel Kayıt · Yeni Kurumsal Kayıt · Excel'e Aktar · Tablo Ayarlarını Kaydet (`btnSaveLayout`).

**Clone (`CustomerList.razor`):** kolonlar Tür · Ad/Ünvan · TC/Vergi No · Telefon · Kaynak · Kira(adet) · Ciro · Son Kira · Durum. Filtre: Query · Tip · İYS · Uyarı · Kara liste + sayfalama.
🟡 Temel var; canlının agregaları (Toplam Hizmet, Ortalama Km/Günlük, İlk/Son kira, Bakiye, Önem) ve Excel/kolon-seçici eksik.

---

## 3. musteri_genel_liste.aspx — Cari Genel Liste (geniş)  ❌

**Amaç:** Carinin tüm alanlarını + çoklu yetkili (Yetkili1-3) + ünvan varyantlarını gösteren geniş döküm grid'i. Tarih filtreli (`Tarih1/Tarih2/Tarih_Listesi`).

**Grid kolonları (40+):** Cari Bilgi · A-Musteri · Soyad · Tc Kimlik No · Pasaport No · Cep Tel · Tel2 · Tel3 · Mail Adresi · Adres · Şehir · İlçe · Doğum Tarihi · Vergi Dairesi · Vergi Numarası · Entegrasyon Kodu · Özel Kod · Önem · Uyarı · Uyarı Nedeni · Uyarı Tarih · Vade Gün · Müşteri Temsilcisi · İşlem Şube · Bakiye Şube · **Ünvan1/2/3** · **Yetkili1/2/3** · **Yetkili Tel1/2/3** · **Yetkili Mail1/2/3**.

> **Yapısal fark:** Kurumsal caride **3 yetkili kişi** (ad+tel+mail) + 3 ünvan varyantı saklanıyor. Clone'da yetkili kişi kavramı YOK. ❌

---

## 4. musteri_crm.aspx — CRM Müşteri Analizi  ❌

**Amaç:** Müşteri davranış/segment analizi — DevExpress pivot/grid, kolon seçici + layout kaydet. Müşteri başına kira agregaları + segment.

**Filtreler:** Çıkış Şube (`Islem_Sube`) · Rez. Kaynağı (`Rez_Kaynagi`) · Tarih1/Tarih2 (`Tarih_Listesi` preset) · Kiralama Adeti (`Kira_Adet`).

**Grid kolonları:** Müşteri · Müşteri Tel · Müşteri Mail · Doğ.Tar · Toplam Adet · Toplam Gün · Kira Bedeli · Hizmet Bedeli · Ortalama Kira Bedeli · Ortalama KM · İlk Kira Zamanı · Son Kira Zamanı · (Segment).

**Clone:** Hiç yok. ❌ — segmentasyon, doğum günü/sadakat, temsilci performansı, müşteri yaşam-boyu-değer analizi clone'da bulunmuyor. (Görevin işaret ettiği risk/uyarı/İYS/temsilci alanları clone `Customer`'da kısmen var ama **CRM analitik ekranı** yok.)

---

## 5. musterigelenmesajlar.aspx — Müşteriden Gelen Mesajlar  ❌

**Amaç:** Araç/kira ile ilgili müşteriden gelen mesaj/bildirim kaydı (mobil/SMS dönüşleri).

**Filtreler:** Plaka (`Plaka`) · Tarih1/Tarih2 (`Tarih_Listesi`).

**Grid kolonları:** Ad · Soyad · Cep Tel · Plaka · Sözleşme No · Kira Kaydı · Araç Hareket · Mesaj · Sebep · Yedek Lastik · Zaman.

**Clone:** Yok. ❌

---

## 6. cari_virman.aspx — Cari Virman (Cari→Cari Aktarım)  ❌

**Amaç:** İki cari hesap arasında **bakiye/borç-alacak aktarımı** (kaynak cari → hedef cari).

**Form alanları:**
| Etiket | name | Tip |
|---|---|---|
| Kaynak Müşteri (kod) | `Kaynak_Musteri_No` | text |
| Kaynak Cari Bilgi | `Kaynak_Ad` | text (readonly) |
| Hedef Cari Kodu | `Hedef_Musteri_No` | text |
| Hedef Cari Bilgi | `Hedef_Ad` | text (readonly) |
| Tarih | `Tarih` | text |
| Vade | `Vade` | text |
| Tutar | `Tutar` | text |
| Döviz / Kur | `Doviz` (select) / `Kur` (text) | select/text |
| Makbuz Numarası | `Makbuz_No` | text |
| İşlem Şube | `Islem_Sube` | select |
| İşlemi Yapan | `Islem_Yapan` | text |
| Açıklama | `Aciklama` | textarea |

**Butonlar:** Kaydet · Cari Virman Listesi.

> **Clone farkı:** Clone'da virman **yalnız Kasa↔Banka** (`CashService.TransferAsync`, kod yorumu "cari yok"). **Cari→Cari** virman YOK. ❌ Çift-taraflı defterle (Borç Hedef cari / Alacak Kaynak cari) modellenebilir.

### 6b. cari_virman_islem_ara.aspx — Cari Virman Listesi  ❌
Arama: `TextBox1` · Tarih1/Tarih2 (`Tarih_Listesi`). Buton: Yeni Cari Virman Kaydı. (Grid satırları dökümde boş; kolonlar virman alanlarını yansıtır.) Clone'da yok.

---

## 7. personel_kayit.aspx — Personel Kayıt  ❌ (clone'da modül yok)

**Amaç:** Şirket personeli (sürücü/operasyon) kartı. Maaş ekleme + tablet ataması.

**Form alanları:**
| Etiket | name | Tip |
|---|---|---|
| Sıra No | `Sira_No` | text |
| Ad | `Musteri_Ad` | text |
| Soyad | `Musteri_Soyad` | text |
| TC Kimlik No | `TC_Kimlik` | text |
| Kimlik No | `Kimlik_No` | text |
| Görevi | `Gorev_Tanim` | select |
| Şube / İşlem Şube | `Islem_Sube` | select |
| Gsm | `Cep_Tel` | text |
| İş Telefonu | `Is_Telefonu` | text |
| Ev Telefonu | `Ev_Telefonu` | text |
| Mail Adresi | `Mail_Adresi` | text |
| Ev Adresi | `Adres` | textarea |
| Kayıtlı İl / İlçe | `Il` / `Ilce` | text |
| Şehir / Mahalle-Köy | `Sehir`? / `Mahalle` | text |
| Baba Adı / Ana Adı | `Baba_Adi` / `Anne_Adi` | text |
| Doğum Yeri / Tarihi | `Dogum_Yeri` / `Dogum_Tar` | text |
| Cilt No / Aile Sıra No | `Cilt_no` / `Aile_Sira_No` | text |
| Kan Grubu | `Kan_Grubu` | text |
| Sürücü Belge No | `S_Belge_No` | text |
| Sürücü Sınıfı | `S_Sinif` | text |
| Veriliş Tarihi / Yeri | `S_Verilis_Tar` / `S_Verilis_Yeri` | text |
| İş Giriş Tarihi | `Is_Giris_Tar` | text |
| İş Çıkış Tarihi | `Is_Cikis_Tar` | text |
| Rac Tablet No | `Rac_Tablet_No` | text |
| Referans | `Referans` | text |
| Açıklama | `Aciklama` | textarea |
| Pasif | `Pasif` | checkbox |

**Butonlar:** Kaydet · Toplu Kaydet · Yeni Personel · Personel Listesi · **Maaş Ekle** (`ButtonMaas`).

> Clone'da `Department` (departman sözlüğü) var ama **Personel entity'si yok**. Maaş/işe giriş-çıkış/sürücü belgesi/tablet ataması tamamen eksik. ❌

### 7b. personel_listesi.aspx — Personel Listesi  ❌
Filtre: Personel Ara (`TextBox1`) · Aktif (`Aktif` checkbox). Grid: Ad · Soyad · Tc Kimlik No · Cep Tel · Mail Adresi · Rac Tablet · İşlem Şube. Buton: Yeni Personel · Excel'e Aktar.

### 7c. personel_calisma_grafigi.aspx — Personel Çalışma Grafiği  ❌
Filtre: Şube (`OfisX` select) · Tarih (`Tarih1`). Personelin günlük/şube bazlı çalışma/vardiya görselleştirmesi (grafik). Clone'da yok.

---

## 8. hukuk_birimi.aspx — Hukuk Birimi (Dava/İcra Kaydı)  ❌

**Amaç:** Tahsil edilemeyen cari alacakların hukuki takip kaydı (avukat, dosya, tahsilat).

**Form alanları:**
| Etiket | name | Tip |
|---|---|---|
| Cari (kod) | `Musteri_No` | text |
| Cari Bilgi | `Ad` | text (readonly) |
| Dosya Numarası | `Dosya_No` | text |
| Tarih | `Tarih` | text |
| Tutar | `Tutar_Temp` | text |
| Tahsilat | `Tahsilat` | text |
| Durum | `Durum` | select |
| Fatura Numarası | `Fatura_No_Temp` | text |
| Avukat-1 / Tel / Mail | `Avukat1` / `Avukat1_Telefon` / `Avukat1_Mail` | text |
| Avukat-2 / Tel / Mail | `Avukat2` / `Avukat2_Telefon` / `Avukat2_Mail` | text |
| Açıklama | `Aciklama` | textarea |

**Butonlar:** Kaydet · Hukuk İşlem Listesi.

### 8b. hukuk_islem_listesi.aspx — Hukuk İşlem Listesi  ❌
Filtre: Ad Soyad (`Ad_Soyad`) · Dosya No (`Dosya_No`) · Fatura No (`Fatura_No`) · Tarih1/Tarih2. Grid: Müşteri · Dosya No · Avukat 1 · Tarih · Tutar · Tahsilat · **Kalan** · Durum · ID. Butonlar: Yeni Hukuk Kaydı · Excel · Tablo Ayarları.

> Clone'da hukuk/dava modülü yok. ❌ Cari alacak yaşlandırması var (rapor) ama hukuki takibe bağlanmıyor.

---

## 9. anket_listesi.aspx — Anket (Müşteri Memnuniyet) Listesi  ❌

**Amaç:** Teslim/dönüş sonrası müşteri anketleri (8 sorulu) ve yanıtları.

**Filtreler:** Cari (`TextBox1`) · Çıkış Ofisi (`Islem_Sube`) · Anket Türü (`Anket_Turu`) · Anket Durumu (`Durum`) · Tarih1/Tarih2.

**Grid kolonları:** Cari Bilgi · Telefon · Plaka · Söz. No · Çıkış Ofisi · Kiraya Veren · Anket Yapan · Baş./Bit. Tarih · **Soru1..Soru8 / Cevap1..Cevap8 / Açıklama1..Açıklama8** · Sonuç · Zaman · ID.

**Butonlar:** Filtrele · Excel · Tablo Ayarları. Clone'da yok. ❌

---

## 10. sikayet_listesi.aspx — Şikayet Listesi  ❌

**Amaç:** Müşteri şikayet/öneri kayıtları (kanal, yer, puan, çözüm).

**Filtreler:** Müşteri (`Musteri`) · Ofis (`Ofis`) · Şikayet Kanalı (`Sikayet_Kanali`) · Şikayet Yeri (form: `Durumx` durum) .

**Grid kolonları:** Müşteri Bilgisi · Cep Tel · Plaka · Belge No · Kayit No · Turu · Çıkış Ofisi · Teslim Eden · Teslim Alan · Özet · Açıklama · Cevap · **Puan** · Sonuç · Baş. Tar.

**Butonlar:** Excel · Tablo Ayarları. Clone'da yok. ❌

---

## Özet tablo

| # | Ekran | Amaç | Clone durumu | Not |
|---|---|---|---|---|
| 1 | musteri_kayit | Cari kayıt kartı (~80 alan) | 🟡 ~36 alan | KVKK, nüfus, pasaport, yetkili, segment, fatura dönemi, tevkifat, kanal-bazlı izin eksik |
| 2 | musteri_listesi | Cari liste + kira agregaları | 🟡 | Excel/kolon-seçici + Toplam Hizmet/Ort.Km/Bakiye/Önem eksik |
| 3 | musteri_genel_liste | Geniş döküm + 3 yetkili | ❌ | Yetkili kişi (ad/tel/mail ×3) kavramı yok |
| 4 | musteri_crm | CRM müşteri analizi/segment | ❌ | Segment/yaşam-boyu-değer/temsilci performansı yok |
| 5 | musterigelenmesajlar | Müşteriden gelen mesajlar | ❌ | — |
| 6 | cari_virman (+ara) | Cari→Cari bakiye aktarımı | ❌ | Clone virman yalnız Kasa↔Banka |
| 7 | personel_kayit | Personel kartı + maaş + tablet | ❌ | Department var, Personel entity yok |
| 7b | personel_listesi | Personel listesi | ❌ | — |
| 7c | personel_calisma_grafigi | Çalışma grafiği | ❌ | — |
| 8 | hukuk_birimi (+liste) | Dava/icra takip + avukat | ❌ | Cari yaşlandırma var, hukuki takip yok |
| 9 | anket_listesi | 8 sorulu memnuniyet anketi | ❌ | — |
| 10 | sikayet_listesi | Şikayet + puan + çözüm | ❌ | — |

**Skor:** 14 ekran → ✅ 0 tam · 🟡 2 kısmi (musteri_kayit, musteri_listesi) · ❌ 12 yok.

### Önerilen iş sırası (bu modül için)
1. **musteri_kayit zenginleştirme** (additive): nüfus (baba/anne/doğum), pasaport, segment (`Mst_Sinif`), kanal-bazlı izin (Mail/SMS/Telefon), doğum tarihi+takip, KVKK anonim bayrakları, fatura dönemi/tevkifat, açıklama. — engelsiz genişlik.
2. **Yetkili kişiler** (kurumsal cari için child tablo: ad/tel/mail) → musteri_genel_liste paritesi.
3. **Cari→Cari virman** (çift-taraflı defter: Borç Hedef / Alacak Kaynak cari, dengeli) — para tutan PR → adversarial inceleme zorunlu.
4. **Personel modülü** (entity + liste + maaş kaydı; sürücü atamasına zemin).
5. **Hukuk takip** (cari alacak yaşlandırmasına bağlı dava/icra kaydı).
6. **Anket + Şikayet** (operasyonel CRM; teslim/dönüş akışına bağlanır).
7. **CRM analiz ekranı** (segment + agrega; 1 ve 4 bittikten sonra).
</content>
</invoke>
