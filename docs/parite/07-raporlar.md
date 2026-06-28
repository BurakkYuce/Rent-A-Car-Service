# 07 — RAPORLAR Modülü Parite Analizi

> Kaynak: diske kaydedilmiş canlı TürevRent HTML'leri (`parite_html/`, 28.06.2026 snapshot).
> Clone: `src/RentACar.Application/Reporting/*` + `src/RentACar.Web/Components/Pages/Reports/*`.
> Not: Canlı raporlar **DevExpress ASPxGridView** kullanır; grid başlıkları veri olmasa da render edilir, bu yüzden kolon listeleri statik HTML'den çıkarılabildi. **Gunraporu** ve **Genel Rapor** gridi postback ile dolduğundan kolonları statik HTML'de yok (sadece filtre paneli görünür) — kolonlar başlık/işlevden türetildi.

---

## Özet Tablo

| # | Canlı ekran (.aspx) | Başlık | Clone karşılığı | Durum |
|---|---------------------|--------|-----------------|:----:|
| 1 | `GunRaporu` | Günlük Faaliyet Raporu | `/raporlar/gunluk` (GunlukFaaliyet) | 🟡 |
| 2 | `Genel_Rapor` | Genel Raporlar ve Analizler | — (kısmen GelirGider+EkHizmet) | 🟡 |
| 3 | `Gelir_Tablosu` | Gelir Gider Tablosu | `/raporlar/gelir-gider` (GelirGider) | 🟡 |
| 4 | `Arac_Gelir_Gider_Tablosu` | Araç Ciro Raporu | — | ❌ |
| 5 | `KDV_Raporu` | KDV Rapor Listesi | `/raporlar/kdv-listesi` (KdvListesi) | 🟡 |
| 6 | `Extralar_Raporu` | Extralar Listesi (Ek Hizmet) | `/raporlar/ek-hizmet` (EkHizmetRaporu) | 🟡 |
| 7 | `Doluluk_Grafik` | Doluluk Raporu | `/raporlar/doluluk` (DolulukRaporu) | 🟡 |
| 8 | `Doluluk_Algoritma` | Doluluk İşlemleri (yield config) | — (fiyat motoru ertelendi) | ❌ |
| 9 | `Periyodik_Servis_Raporu` | Periyodik Servis Raporu | — (≠ `/raporlar/servis-ozet`) | ❌ |
| 10 | `Rezervasyon_Kaynak_Raporu` | Rezervasyon Kaynak Raporu | — | ❌ |
| 11 | `Fatura_Donem_Raporu` | Fatura Dönem Raporu | 🟡 (kısmen TahsilatFatura) | ❌ |
| 12 | `Bos_Arac_Raporu` | Boş Araç Raporu | — | ❌ |
| 13 | `Bos_Km_Detay` | KM Detay Listesi | — | ❌ |
| 14 | `Evrak_Listesi` | (boş/erişilemedi) | — | ❓ |

**Clone'da olup bu listede canlı eşi olmayan raporlar:** `/raporlar/cari-bakiye` (Genel Borç&Alacak / Hesap Extresi karşılığı), `/raporlar/filo` (Araç Güncel Durum karşılığı), `/raporlar/kasa-banka` (Genel Kasa / Banka Hareketleri karşılığı), `/raporlar/servis-ozet` (servis maliyet), `/raporlar/tahsilat-fatura` (mutabakat).

**Skor:** Canlı (incelenen) 13 rapor + 1 boş. Clone'da işlevsel tam eşleşme ❌ — en yakınları kısmi (🟡). Tamamen eşleşen ✅ yok; clone karşılıkları daha sade.

---

## 1. GunRaporu — Günlük Faaliyet Raporu

- **Amaç:** Tek bir günün operasyonel özeti (o gün ne oldu: yeni kira/rezervasyon, çıkış/dönüş, tahsilat, fatura).
- **Filtreler:** `Tarih` (tek gün, datepicker, default bugün) · `İşlem Şube` (select: Hepsi/MERKEZ…) · buton `Git_Form()` (Listele).
- **Sonuç kolonları:** Grid postback ile dolduğundan statik HTML'de yok. İşlevsel olarak günün sayaç/tutar dökümü (yeni rez, yeni kira, çıkış, dönüş, tahsilat, fatura).
- **Gruplama/özet:** Gün bazlı tek özet; şube kırılımı filtreden.
- **Export:** DevExpress ortak araç çubuğu (Excel/PDF) — diğer raporlarla aynı altyapı.
- **Clone durumu 🟡:** `/raporlar/gunluk` → `GunlukFaaliyetDto(YeniRezervasyon, YeniKira, Cikis, Donus, TahsilatAdet, TahsilatTutar, FaturaAdet, FaturaTutar)`. İşlev paralel ve sağlam. **Fark:** clone'da yalnız `gun` filtresi var, **İşlem Şube filtresi yok**; export yok.

## 2. Genel_Rapor — Genel Raporlar ve Analizler

- **Amaç:** Dönem genel analiz panosu; sekmeli (tab) birleşik rapor.
- **Filtreler:** `Tarih1` / `Tarih2` (tarih aralığı) · `Raporla` butonu.
- **Sekmeler (tab):** `Kiralar (Tablo)` · `Kiralar (Grafik)` · `Ek Hizmet Detayları`.
- **Sonuç kolonları:** Grid postback ile doluyor (statik HTML'de yok); kira listesi + grafik + ek hizmet detay birleşimi.
- **Export:** Excel (içerikte `Excel`/`Raporla` butonları).
- **Clone durumu 🟡/❌:** Tek bir "genel analiz panosu" yok. Parçaları clone'da ayrı sayfalarda var (GelirGider + EkHizmet). Birleşik sekmeli görünüm + grafik **yok**.

## 3. Gelir_Tablosu — Gelir Gider Tablosu

- **Amaç:** Dönemsel gelir-gider tablosu; **şube / plaka / grup** kırılımlı kârlılık + doluluk + araç-başı gelir analizi.
- **Filtreler:** `Tarih1`/`Tarih2` (aralık) · `Plaka` · `Araç Sahibi` · `Araç Grupları` (çoklu) · `Plaka Şubesi` · `Kdv Durum` · `Gruplama Tipi` · `RentTo Durumu` · `Excel'e Aktarım Modu` (export format).
- **Sonuç kolonları (çoklu grid):**
  - *Şube özet:* Şube · Araç Sayısı · Kazanç · Kira · Hizmet · Toplam Gün · Potansiyel · Doluluk · Araç Başı Gelir · Ortalama Fiyat · Söz. Sayısı
  - *Plaka detay:* Plaka · SIPP · Gelir · Gider · Sonuç · Ortalama · Gün · Boş Gün · Ana Maliyet · Yönetim Maliyet · Toplam Maliyet · Marka · Tipi · Yakıt Türü · Vites · Araç Grubu
  - *Diğer kırılım:* Rezervasyon Kaynağı · Rez Alan · Baş./Bit. Tar. · Cari Bilgi · Toplam Kira · Grup · Bakiye · Kira Gün · Rez. Kaynak · Toplam
- **Gruplama/özet:** Gruplama Tipi seçimine göre (şube/plaka/grup/kaynak/cari) + genel toplam satırları.
- **Export:** DevExpress exporter — Excel/PDF/CSV (`Aktarım Modu` dropdown).
- **Clone durumu 🟡:** `/raporlar/gelir-gider` → `GelirGiderDto(GelirToplam, GiderToplam, KdvTahsil, KdvIndirilecek, NetKar, GelirKirilim[], GiderKirilim[])`. **Çok daha sade:** yalnız genel toplam + SourceType bazlı kırılım. **Eksik:** şube/plaka/grup/kaynak kırılımı, doluluk, potansiyel, araç-başı gelir, ortalama fiyat, ana/yönetim maliyet ayrımı. Filtre yalnız `from/to`.

## 4. Arac_Gelir_Gider_Tablosu — Araç Ciro Raporu

- **Amaç:** Araç bazında detaylı ciro/kâr-zarar (P&L) tablosu; kira + her ek hizmet kalemi + maliyetler.
- **Filtreler:** `Tarih1`/`Tarih2` · `Plaka` · `Araç Grubu` · `Şube` · `Aktarım Biçimi` (export format).
- **Sonuç kolonları (çok geniş):**
  - *Şube/özet:* Sube · Potansiyel · Çalışma · Doluluk · Kira Gelir · Hizmet Gelir · Toplam Kazanç · Ort. Kira · Hizmet Oran · Ort. Araç · Çalış. Araç · Sigorta Hizmet · Araç Başı · Sigorta Gelir
  - *Plaka detay:* Grup · Plaka · Boş Gün · Ana Maliyet · Yönetim Maliyet · Toplam Maliyet · Baş./Bit. Tar.+Saat · Kiralayan · Sözleşme No · Kiraya Veren · Rez. Kaynak · Marka · Tipi · Yakıt Türü · Vites · Çıkış/Dönüş Ofisi
  - *Ek hizmet kalemleri (kolon başına):* Bebek Koltuğu · Navigasyon · Ek Sürücü · CDW · LCF · Mini Hasar · Hırsızlık · Wifi · Genç Sürücü · Ek Km · Adres Teslim · Kış Lastiği · Üyelik · IMM · Drop · Kira Farkı · Max Güvence · Paket1–6 · Süper Mini Hasar · SCDW · UpSell Fiyat · Ek Açıklama 1–4
- **Export:** Excel + PDF (`pdF` butonu mevcut).
- **Clone durumu ❌:** Araç-bazlı detaylı P&L raporu **yok**. (Gelir-gider clone'u şube/plaka kırılımı dahi içermiyor; bu rapor en kapsamlı eksiklerden.)

## 5. KDV_Raporu — KDV Rapor Listesi

- **Amaç:** Fatura bazında KDV detay listesi (beyanname dökümü), oran kolonları yatay.
- **Filtreler:** `Tarih1`/`Tarih2` · `Tarih_Listesi` (Tarih türü) · `Fatura_Turu` (Satış/Alış) · `Iptal` (Hepsi / İadeleri Gizle) · `Çıkış Şube` (`Islem_Sube`: Hepsi/MERKEZ).
- **Sonuç kolonları:** ID · Fatura Tarihi · Fatura No · Cari Bilgi · Mail · Tel · Vergi Dairesi · Vergi Numarası · İade · İptal · **Tutar 20 / KDV 20 · Tutar 10 / KDV 10 · Tutar 1 / KDV 1 · Tutar 0 / KDV 0 · Tutar Diğer / KDV Diğer** (oran sütunları yatay).
- **Gruplama/özet:** Fatura satır listesi + genel toplam (DevExpress footer).
- **Export:** Excel.
- **Clone durumu 🟡:** `/raporlar/kdv-listesi` → `KdvListesiDto(Satirlar[Oran,Net,Kdv,Brut,FaturaAdet], ToplamNet, ToplamKdv, ToplamBrut, FaturaAdet)`. **Fark:** clone **oran bazında dikey agregat** (beyanname özeti) verir; canlı **fatura bazında yatay oran** detay listesi. Cari/VKN/mail/tel/iade-iptal kolonları, Satış/Alış ayrımı, şube filtresi **yok**. Filtre yalnız `from/to`.

## 6. Extralar_Raporu — Extralar Listesi (Ek Hizmet)

- **Amaç:** Satılan ek hizmet (ekstra) kalemlerinin satır-bazlı dökümü.
- **Filtreler:** `Tarih1`/`Tarih2` · `Tarih_Listesi` (Baş./Bit. Tarih) · `Rapor_Turu` (Kira/Rezervasyon) · `Icerik` (Sadece Ekstralar / Kira Bedeli Dahil) · `Kime_Ait` (Sadece Satış Ofis / Sadece Web+API / Rez+Ofis / Sadece Rez / Filtresiz) · `SatisGoser` · `Çıkış Şube`.
- **Sonuç kolonları:** Kayıt No · Türü · Açıklama · TL Fiyat · Fiyat · Döviz · Baş. Zaman · Bit. Zaman · Plaka · RA No · Müşteri · Rez. Kaynağı · Kiraya Veren · Teslim Eden · Ek Hizmet Satan · Ek Hizmet Satan Log · Tur · Ç. Ofisi · İlk Tahsilat.
- **Gruplama/özet:** Satır liste + toplam.
- **Export:** Excel.
- **Clone durumu 🟡:** `/raporlar/ek-hizmet` → `EkHizmetRaporDto(Satirlar[Ad,ToplamMiktar,Net,Kdv,Brut,KiraAdet], toplamlar)`. **Fark:** clone **ek hizmet adına göre agregat**; canlı **kalem-bazlı satır listesi** (plaka/müşteri/satan/döviz/zaman). Çoklu filtre (Kira/Rez, Web/Ofis, döviz) **yok**.

## 7. Doluluk_Grafik — Doluluk Raporu

- **Amaç:** Dönem doluluk oranı, **gün bazlı** zaman serisi + grafik.
- **Filtreler:** `Tarih1`/`Tarih2` · `Şube` · `Rezervasyon Kaynağı` · `Araç Grubu` · `Aktarım Modu` (export) · `Page size`.
- **Sonuç kolonları:** Tarih · Toplam Araç · Kira Doluluk · Rez Doluluk · Toplam Doluluk · **Doluluk %**.
- **Gruplama/özet:** Gün satırları + grafik görünümü.
- **Export:** DevExpress exporter (Excel/PDF/CSV).
- **Clone durumu 🟡:** `/raporlar/doluluk` → `DolulukDto(AracSayisi, DonemGun, AracGun, KiraGun, DolulukYuzde)`. **Fark:** clone **dönem için tek özet %** verir; canlı **gün-bazlı seri + kira/rez ayrımı + grafik**. Şube/grup/kaynak filtresi yok.
  - **Formül karşılaştırması:** Clone: `KiraGun = Σ (kira efektif aralığı ∩ dönem) takvim-günü (kapsayıcı, +1)`, `AracGun = AracSayisi × DönemGün`, `% = round(KiraGun×100/AracGun, 2)`. İptal kiralar hariç; efektif bitiş = gerçek dönüş varsa o, yoksa planlanan. Canlıda ayrıca **Rez Doluluk** (rezervasyonlu ama henüz kiraya dönmemiş) ayrı kolon — clone bunu ayırmıyor (yalnız kira-gün). Canlı muhtemelen şube/grup kapasitesiyle de kırıyor.

## 8. Doluluk_Algoritma — Doluluk İşlemleri (yield/oran yapılandırma)

- **Amaç:** **Rapor değil** — yield/dinamik fiyat yapılandırma ekranı. Başlık: *"Doluluk İşlemleri (ÖZEL JSON SERVİSLER İÇİN GEÇERLİDİR MUTLAKA DANIŞINIZ)"*. Doluluk bandına göre fiyat çarpan/oranları tanımlanır.
- **Alanlar (filtre değil, kayıt formu):** `Bölge` (çoklu seçim) · `Araç Grubu` (çoklu seçim) · `Baş. Tarih` · `Bit. Tarih` · doluluk bandı oranları: `%10 %20 %30 %40 %50 %60 %70 %80 %90 %100` (her biri serbest sayı girişi) · butonlar `Yeni Kayıt` / `Kaydet`.
- **Çıktı:** Tanımlı oran kayıtları (özel JSON fiyat servisleri tüketir).
- **Export:** PDF butonu var.
- **Clone durumu ❌:** Yok. Bu **fiyat/yield motoru** parçası — CLAUDE.md'de fiyat motoru bilinçli ertelendi (canlı parite 403). Bizdeki doluluk hesabı yalnız raporlama amaçlı; band-bazlı fiyat çarpanı kavramı yok.

## 9. Periyodik_Servis_Raporu — Periyodik Servis Raporu

- **Amaç:** Araç periyodik bakım takibi — KM bazlı bakım uyarı/zamanı listesi.
- **Filtreler:** `Araç` · `Araç Durumu` · `Otopark` · `Uyarı` (uyarı eşiği).
- **Sonuç kolonları:** Plaka · Marka · Tipi · Model · Yakıt Türü · Vites · Şube · İşlem Tarihi · İşlem KM · Uyarı KM · Kalan KM · Şuanki KM.
- **Gruplama/özet:** Araç listesi (Kayıt Sayısı:N footer).
- **Export:** Excel.
- **Clone durumu ❌:** `/raporlar/servis-ozet` **bu değil** — o `ServiceCostSummaryDto(VehicleId, Plaka, Tip, Toplam, Adet)` yani **maliyet özeti**. Periyodik bakım **KM uyarı raporu** (Uyarı KM / Kalan KM / Şuanki KM) clone'da **yok**.

## 10. Rezervasyon_Kaynak_Raporu — Rezervasyon Kaynak Raporu

- **Amaç:** Rezervasyon kaynağı (acente/broker/web/ofis) bazında satış performansı.
- **Filtreler:** `Tarih1`/`Tarih2` · `Araç Grubu` · `Excel'e Aktarım Modu`.
- **Sonuç kolonları:** Firma Adı · Genel Toplam · Döviz · Rez. Gün · Rez. Sayısı.
- **Gruplama/özet:** Kaynak/firma bazında gruplu + genel toplam.
- **Export:** DevExpress exporter (Excel/PDF).
- **Clone durumu ❌:** Yok. (Gelir-gider clone'unda kaynak kırılımı da yok.)

## 11. Fatura_Donem_Raporu — Fatura Dönem Raporu

- **Amaç:** Faturalanacak/faturalanmış dönem kalemleri (vade bazlı fatura takibi).
- **Filtreler:** `Tarih1`/`Tarih2` · `Cari Ara` · `İşlem Şube` · `Fatura_Durum` (Faturalanmamış/Faturalanmış) · `Tarih_Listesi` (Tarih Önemsiz/Tarih).
- **Sonuç kolonları:** Kayıt No · Cari Bilgi · Vade · Tutar · Fat. Tarih · Faturalanan · Fatura No · Plaka · Baş. Tarih · Bit. Tar · Sözleşme No.
- **Gruplama/özet:** Cari/dönem bazlı liste + toplam.
- **Export:** Excel + PDF (`pdF`).
- **Clone durumu ❌/🟡:** `/raporlar/tahsilat-fatura` → `TahsilatFaturaDto(FaturaAdet, FaturaToplam, TahsilatAdet, TahsilatToplam, Fark)` yani **fatura↔tahsilat mutabakat özeti**. Canlı **dönem fatura kalem listesi** (vade/sözleşme/plaka bazlı) — farklı işlev, clone'da bu liste **yok**.

## 12. Bos_Arac_Raporu — Boş Araç Raporu

- **Amaç:** Gün bazında boştaki (kirada olmayan) araç sayısı + ilgili maliyet (atıl filo / doluluk tersi).
- **Filtreler:** `Tarih1`/`Tarih2` · `Page size`.
- **Sonuç kolonları:** Tarih · Toplam Araç · Toplam Kira · Toplam Bakım · Toplam Baf.
- **Gruplama/özet:** Gün satırları + toplam.
- **Export:** Excel + PDF.
- **Clone durumu ❌:** Yok. (Doluluk raporunun tamamlayıcısı; clone doluluk gün-bazlı seri vermediğinden bu da yok.)

## 13. Bos_Km_Detay — KM Detay Listesi

- **Amaç:** Kira başı KM detayı (çıkış/dönüş KM farkı, araç başına yapılan KM analizi).
- **Filtreler:** `Tarih1`/`Tarih2` · `Plaka Ara` · `Şube Seç` · `Page size`.
- **Sonuç kolonları:** Plaka · Tipi · İşlem Türü · Baş. Tarih · Bit. Tarih · Çıkış KM · Dönüş KM · Fark · Arac Fark · Marka · Vites · Yakıt Türü · Çıkış Ofisi · Dönüş Ofisi.
- **Gruplama/özet:** Kira/işlem satır listesi (Kayıt Sayısı:N) + toplam.
- **Export:** Excel + PDF.
- **Clone durumu ❌:** Yok. KM/teslim-dönüş verisi modelde var ama bu rapor üretilmiyor.

## 14. Evrak_Listesi — (içerik yok)

- Dosya boş/placeholder (130 byte, erişilemedi). Muhtemelen "Evrak/Doküman Listesi". **Clone durumu: ❓/❌.**

---

## Export (Excel/PDF) — Kritik Çapraz Bulgu

- **Canlı:** Tüm raporlar DevExpress `ASPxGridView` + exporter ile gelir. Çoğunda `Aktarım Modu` / `Aktarım Biçimi` / `Excel'e Aktarım Modu` dropdown'u (format seçimi) + Excel/PDF/CSV export butonları var. Yani **canlıda Excel + PDF export standarttır.**
- **Clone:** `Components/Pages/Reports/` altında **hiçbir export yok** — ne Excel/CSV ne PDF. **Print bile yok** (`window.print` yalnız `InvoicePrint.razor` ve `RentalPrint.razor`'da — fatura/sözleşme; raporlarda değil).
- **Sonuç:** Rapor export'u **modül genelinde sıfır** — canlı paritesine göre yatay (cross-cutting) en büyük eksik.

## Filtre Derinliği — Kritik Çapraz Bulgu

- **Canlı:** Raporlar zengin filtreli (tarih aralığı + **Şube** + **Araç Grubu** + **Plaka** + **Cari** + **Rez. Kaynağı** + fatura/iade durumu + gruplama tipi + döviz).
- **Clone:** Yalnız tarih (`gun` veya `from`/`to`). **Şube, araç grubu, plaka, cari, kaynak, durum, gruplama filtreleri yok** — şube kapsamı (BranchScope) raporlara da bağlanmamış.

---

## Genel Değerlendirme & Kritik 3 Eksik

1. **Rapor export (Excel/PDF) tamamen yok** — canlıda her raporda standart; clone'da hiçbirinde (print bile yok). Yatay, yüksek görünürlüklü eksik.
2. **Araç/şube/grup/kaynak kırılımlı kârlılık raporları yok** — `Araç Ciro Raporu` (#4, araç-bazlı P&L + tüm ek hizmet kalemleri), `Gelir Gider Tablosu`nun şube/plaka/grup kırılımı (#3), `Rezervasyon Kaynak Raporu` (#10) clone'da ya yok ya çok sade. Mevcut `GelirGiderDto` yalnız genel toplam + SourceType kırılımı.
3. **Filtre/derinlik açığı + doluluk gün-serisi yok** — clone raporlarında şube/araç grubu/plaka filtreleri yok; `DolulukRaporu` tek özet % verirken canlı gün-bazlı seri + Kira/Rez doluluk ayrımı + grafik sunuyor; `Bos_Arac` ve `Bos_Km_Detay` (atıl filo / KM analizi) ile `Periyodik Servis (KM uyarı)` raporları tümüyle yok. `Doluluk_Algoritma` ise rapor değil — fiyat/yield motoru parçası (bilinçli ertelendi).

> Not: Clone'da bu 13'ün dışında değer üreten 5 rapor var (`cari-bakiye`+yaşlandırma, `filo`, `kasa-banka`, `servis-ozet`, `tahsilat-fatura`) — bunlar canlının başka rapor/liste ekranlarına denk gelir; bu modül listesinde birebir eşleri yer almıyor.
