# Modül 05a — finans-fatura (fatura / e-fatura / KDV)

**Kapsam:** 6 ekran. **Tamamı PARA ekranı** (kural 5 — talimat üzeri): tutar/KDV üreten veya
tutar/KDV taşıyan tüm satırların durumu `PARA — OPUS'A DEVİR`. Bu ajanın işi tutar doğruluğuna
karar vermek değil; alan/kolon envanterini ve kanıtı çıkarmaktır.

**Durum dağılımı:** PARA — OPUS'A DEVİR × 6 (bunların içinde fiili rota karşılığı: 4/6 var,
1/6 yok — `fatura_detay_listesi.aspx`, 1/6 kısmen/farklı-şekilde var — `kdv_raporu.aspx` yapısal
uyumsuz).

---

## fatura.aspx — Fatura
- **Durum:** PARA — OPUS'A DEVİR
- **Bizde:** `/faturalar` içindeki "Manuel Fatura" formu (`src/RentACar.Web/Components/Pages/Finance/InvoiceList.razor`)
- **kanit:** /faturalar · K2=%5 (5/100) · K3=❓ DOĞRULANAMADI (canlı kolon kaynağı `yok` — bu ekran zaten liste değil form/detay ekranı, kolon kavramı yok)
- **Canlı fazlası (bizde YOK — tek tek):**
  - Cari bilgisi inline düzenleme: `Musteri_No`, `Ad`, `Cep_Tel`, `Adres`, `Ilce`, `Sehir`, `Mail_Adresi`, `PostaKutusu` (bizde sadece mevcut cari'yi dropdown'dan seçme var, fatura ekranından cari bilgisi düzenlenemiyor)
  - Vergi bilgisi: `Vergi_Dairesi`, `Vergi_Numarasi` (fatura üzerinde vergi dairesi/no alanı yok — `Customer`/`Personel` entity'sinde de aranmalı, InvoiceList'te hiç yok)
  - `Islem_Sube` (fatura kesilen şube), `EFatura_Firma` (e-fatura entegratör firma seçimi)
  - `Cari_Entegre` (checkbox — cari'yi e-fatura sistemine entegre et), `Iade` (checkbox)
  - `Fatura_Saat` (fatura kesim saati — sadece tarih var bizde, saat yok)
  - `Fatura_Ozel_Kod` (özel kod select)
  - `EURO`/`USD`/`GBP` (manuel çoklu döviz kur girişi — bizde tek `Kur` alanı var ama bu formda bile giriş yok)
  - `Evrak_No`, `Tek_Plaka` (evrak no & plaka çapraz referansı)
  - `IadeFaturaNo`, `IadeFaturaTarihi` (iade faturası no/tarihi elle girişi — bizde `KaynakFaturaId` FK olarak Domain'de var ama bu formda görünmüyor/girilmiyor)
  - `Hiz_Bas_Tar` (Hizmet Başlangıç Tarihi — e-fatura zorunlu alanı, fatura tarihinden ayrı; bizde yok)
  - `EDoviz` (fatura para birimi select — bizde `Currency` var ama manuel formda seçim yok)
  - `Odeme_Turu` (Kart/Havale/Nakit), `Gonderim_Sekli` (Mail/Kargo/Posta) — fatura üzerinde ödeme/gönderim türü
  - `Kdv_Sifir_Sebep` (KDV istisna sebebi — 4 seçenek: İstisna Olmayan Diğer, 15/b Uluslararası Kuruluşlara, 11/1-A hizmet ihracatı, 350-Diğerleri)
  - `EFatura_Tipi` (TICARIFATURA/TEMELFATURA), `Fatura_Sablon` (e-fatura şablonu), `Internet_Sitesi`
  - `Teklif_Sorumlusu`/`EFatura_ID` (faturayı kesen personel/entegratör id), `FaturaSonDurum` (son durum metni)
  - `Footer_Ara_Toplam` (Ara Toplam), `Footer_Indirim_Toplam` (Toplam İndirim — indirim tutarı fatura seviyesinde ayrı gösteriliyor, bizde yok)
  - `Tevkifat_Oran`, `Kdv_Indirimi`, `Tevkifat_Kodu` (tevkifat/stopaj — Domain'de `TevkifatOran`/`TevkifatTutar` VAR ama bu formda giriş/görüntüleme YOK)
  - `OTV_Tutari`, `OTV_KDV` (ÖTV — Domain'de `Otv` alanı VAR ama formda giriş YOK)
  - `Footer_Doviz_Toplam`, `Footer_Doviz` (dövizli toplam ayrı gösterim)
  - Çok sayıda sistem/tenant-config bayrağı (hidden, veri alanı değil iş kuralı anahtarı): `Fatura_Kesmez`, `Kirik_Fatura_Hesap`, `Fatura_Iptal_Edemez`, `Fatura_Tek_Satir`, `Otomatik_Uzat`, `Kll_Fatura_Degismez`, `E_Fatura_Silemez`, `Sistem_Fatura_Donem_Yaz`, `Fatura_Alt_Kod_Gerekli`, `Kll_Sadece_Cari`, `E_Fatura_Degistir`, `Kdv_Sistemi`, `Alis_Fatura_Yansitma` vb. — bunlar per-tenant davranış anahtarları, `TenantSettings`'te karşılığı olup olmadığı ayrıca kontrol edilmeli.
- **Bizde fazlası:** yok (bizim manuel form canlının kesin alt kümesi).
- **Not:** Domain'de (`Invoice.cs`) `Otv`/`TevkifatOran`/`TevkifatTutar`/`DamgaVergisi`/`KaynakFaturaId` alanları zaten var ama `/faturalar` manuel-fatura UI'ı bunları hiç sormuyor/göstermiyor — veri modeli formdan daha zengin. Bu ekran canlıda tam bir e-Fatura düzenleme/kesim formu, bizde ise 6 alanlı basit "cari+tutar+kdv oranı+tarih" formu.

## fatura_detay_listesi.aspx — Fatura Detay Listesi
- **Durum:** PARA — OPUS'A DEVİR
- **Bizde:** `—` (eşleşen rota bulunamadı)
- **kanit:** — · K2=%0 (0/15) · K3=%0 (0/46)
- **Canlı fazlası:** Bu ekranın tamamı eksik — fatura SATIR bazlı (line-item), kira/rezervasyon bağlamıyla çapraz raporlama ekranı. Envanterde (`03-bizim-envanter.tsv`) ne `Reports/` ne `Finance/` altında satır-bazlı fatura detay raporu yok (grep: `FaturaDetay`/`InvoiceLine` sadece Domain'de `InvoiceLine` entity olarak var, ona bağlı bir liste/rapor sayfası yok). Kolonlar tek tek: ID, Cari Ünvan, Fatura ID, Evrak Tarihi, Cari No, Plaka, Satır Açıklama, Miktar, Vergi Dairesi, Vergi Numarası, Evrak Seri No, İş Yeri, Adres, Şehir, Evrak No, E Fatura No, İptal, Ofis, Toplam, Toplam KDV, Genel Toplam, Açıklama, Satır ID, İşlem Tipi, Birim Fiyat, KDV Oranı, İndirim, Tutar, Net Tutar, KDV Tutarı, Satır Genel Toplam, Vade Tarihi, Araç Sahibi, Cari Ad, Cari İlçe, Cari Mail, Cari Şehir, Cari Soyad, Dosya No, İşlem Detayı, Kira Çıkış Ofisi, Kira Detayı, Kira İşlem Şube, Kira RA No, Kiraya Veren, Rezervasyon Kaynağı. Filtreler de yok: Cari Filtre, Fatura No Aralığı, Plaka Ara, İşlem Kodu (Satir_Kodu), Rez Bilgileri Göster/Gizle, Tarih, Ofis.
- **Bizde fazlası:** —
- **Not:** `InvoiceLine` entity (Aciklama/Miktar/BirimNetFiyat/KdvOrani/SatirNet/SatirKdv/SatirToplam) veride mevcut ama satır-seviyeli, kira-bağlamlı bir liste/export ekranı hiç yazılmamış — sıfırdan ekran gerekir.

## fatura_donem_raporu.aspx — Fatura Dönem Raporu
- **Durum:** PARA — OPUS'A DEVİR
- **Bizde:** `/raporlar/fatura-donem` (`src/RentACar.Web/Components/Pages/Reports/FaturaDonem.razor`)
- **kanit:** /raporlar/fatura-donem · K2=%17 (2/12) · K3=%45 (5/11)
- **Canlı fazlası:**
  - Kolon: `Kayit No` (kira/rezervasyon kayıt no — faturadan ayrı), `Faturalanan` (faturalanmış mı bayrağı), `Plaka`, `Baş. Tarh`, `Bit. Tar` (kira başlangıç/bitiş), `Sözleşme No`
  - Filtre: `TextBox1` (Cari Ara), `Fatura_Durum` (Faturalanmamış / Faturalanmış seçimi), `Islem_Sube` (İşlem Şube filtresi)
- **Bizde fazlası:** Döviz, Durum, İade kolonları (canlıda bu kolonlar yok/görünmüyor).
- **Not:** Canlı ekran adının işaret ettiği asıl kullanım — dönem sonunda **faturalanmamış** kira/kayıtları bulma (`Fatura_Durum=Faturalanmamış` filtresi) — bizim raporda YOK; `Reports.GetFaturaDonemAsync` yalnız zaten kesilmiş `Invoice` kayıtlarını tarih aralığına göre listeliyor, faturalanmamış kirayı göstermiyor.

## fatura_islem_listesi.aspx — Fatura Listesi
- **Durum:** PARA — OPUS'A DEVİR
- **Bizde:** `/faturalar` (`src/RentACar.Web/Components/Pages/Finance/InvoiceList.razor`)
- **kanit:** /faturalar · K2=%0 (0/19) · K3=%43 (10/23) — K3 hesabı ekran tablosu + `ListExportCatalog.Faturalar` export kolonlarının birleşimiyle (No/Tarih/Net/KDV/Toplam/Durum/Cari/Vade/Para/Kur/Tür/Damga Vergisi/e-Fatura, 13 kolon) yapıldı.
- **Canlı fazlası:**
  - Filtre (19 alanın TAMAMI eksik): Cari Filtre (`TextBox1`), Fatura No Aralığı (`Fatura_No1`/`Fatura_No2`), Plaka Ara, `Fatura_Durumu` (Hepsi/Faturalanmayanlar/Faturalananlar), Tarih aralığı (`Tarih_Listesi`/`Tarih1`/`Tarih2`), `Ofis` — sayfada HİÇBİR arama/filtre alanı yok, sadece tüm faturaları düz liste basıyor.
  - Kolon: ID, Cari Kod (Cari Bilgi'den ayrı), Vergi Dairesi, Vergi Numarası, İptal (ayrı boolean — bizde Durum içinde), Ofis, Enteg. iletim (e-fatura entegrasyon iletim durumu ayrıntısı), Müş Özel Kod, Açıklama, Müşteri Ülke, Genel Toplam Dvz (döviz cinsinden toplam — TL karşılığından ayrı), Özel Kod
  - Aksiyon: "Seçili Olanları Faturala" (toplu faturalandırma — çoklu seçim + toplu işlem), "Fatura Durumları" (durum yönetim ekranı), "XML'e Aktar" export
- **Bizde fazlası:** `Tür` etiketi (İade/Manuel/Kira/Kira Fark/Serbest — canlıda görünmüyor), sayfa içi gömülü "Manuel Fatura Kes" formu (canlıda bu aynı sayfada değil, `fatura.aspx` ayrı ekranda).
- **Not:** Grid kolon örtüşmesi orta (%43) ama filtre/arama tarafı sıfır — büyük fatura hacminde kullanılamaz hale gelir; toplu faturalandırma aksiyonu da yok.

## gelen_e_fatura_listesi.aspx — Gelen E-Fatura Listesi
- **Durum:** PARA — OPUS'A DEVİR
- **Bizde:** `/gelen-efatura` (`src/RentACar.Web/Components/Pages/GelenEFaturalar/GelenEFaturaList.razor`)
- **kanit:** /gelen-efatura · K2=%0 (0/22) · K3=%35 (9/26)
- **Canlı fazlası:**
  - Kolon: `Fatura` (belge linki), `Göster` (ayrı önizleme kolonu), `Gider` (gidere dönüştürülmüş mü işareti), `Turu` (gider kategorisi: Periyodik Servis/Hasar-Kaza/Mekanik Arıza/Bakım ile eşleştirme), `Fatura Tipi`, tam KDV oran kırılımı: `Kdv 20`/`Kdv 20 Matrah`/`Kdv 10`/`Kdv 10 Matrah`/`Kdv 1`/`Kdv 1 Matrah`/`Kdv 0`/`Kdv 0 Matrah`/`Kdv 2`/`Kdv 2 Matrah`/`HesapKdv` (11 kolon — tek toplam KDV yerine oran bazlı ayrıştırma)
  - Filtre: `EFatura_Firma`, Fatura No aralığı, `Plaka`, `Arac_KM`, `Islem_Turu` (gider kategorisi filtresi), `Satir_Kodu`
  - Aksiyon: "Sadece Kdv Yansıt" (gideri değil sadece KDV'yi defter/beyannameye yansıtma seçeneği)
- **Bizde fazlası:** yok belirgin (Elle Gir + GİB'den Çek formları canlının "E-Faturaları Getir ve Listele"/"Sadece Listele" aksiyonlarına kabaca karşılık geliyor).
- **Not:** Canlı ekran gelen faturayı araç/km ve gider kategorisine (bakım/hasar/servis) bağlıyor + KDV'yi oran bazında (20/10/1/0/2) ayrıştırıyor; bizde `GelenEFatura` tek `NetTutar`/`KdvTutar` tutuyor, araç/gider kategorisi bağlama ve oran kırılımı yok.

## kdv_raporu.aspx — KDV Raporu
- **Durum:** PARA — OPUS'A DEVİR
- **Bizde:** `/raporlar/kdv-listesi` (`src/RentACar.Web/Components/Pages/Reports/KdvListesi.razor`)
- **kanit:** /raporlar/kdv-listesi · K2=%18 (2/11) · K3=%0 (0/20)
- **Canlı fazlası:** Yapısal fark nedeniyle kolonların TAMAMI ad-bazlı eşleşmiyor: ID, Fatura Tarihi, Fatura No, Cari Bilgi, Mail, Tel, Vergi Dairesi, Vergi Numarası, İade, İptal, Tutar 20, KDV 20, Tutar 10, KDV 10, Tutar 1, KDV 1, Tutar 0, KDV 0, Tutar Diğer, KDV Diger. Filtre: `Fatura_Turu` (Satış/Alış ayrımı), `Iptal` (Hepsi/İadeleri Gizle seçimi), `Islem_Sube`.
- **Bizde fazlası:** Oran-bazlı özet satırları + kart metrikleri (Toplam Net/KDV/Brüt/Fatura Adedi) — canlıda bu pivot/özet biçimi yok.
- **Not:** **Yapısal uyumsuzluk** — canlı rapor SATIR=fatura, sütun=oran (geniş format, `Fatura_Turu` ile Satış/Alış ayrı seçilebiliyor); bizim rapor SATIR=oran, sütun=toplam (dar/özet format) ve `ReportService.GetKdvListesiAsync` yalnız kendi kestiğimiz (satış) `Invoice` tablosunu tarıyor — gelen (alış) e-fatura KDV'sini kapsamıyor. Beyanname amaçlı kullanımda Alış-KDV'sinin dışarıda kalması önemli bir eksik olabilir; karar Opus'a.

---

TOPLAM: 6 ekran işlendi
