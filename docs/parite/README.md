# TürevRent Canlı Parite — Detaylı Gap Analizi (kapsam haritası + yol haritası)

> **Kaynak:** canlı `turev2.turevrac.com` (ÜMİT YÜCE oturumu, curl ile çekilmiş HTML). 2026-06-28 taraması.
> **Amaç:** Bu klasör (`docs/parite/`), klonun canlıya göre **eksik/var** durumunu ekran-ve-alan düzeyinde belgeler.
> Gelecek oturumların ve tüm yeni iş planlamasının **tek doğruluk kaynağı** budur.
> **Kapsam notu:** Yalnız ekran/alan **yapısı** belgelendi — kimlik bilgisi, çerez, müşteri verisi commit EDİLMEDİ.

## Erişim yöntemi (kanıtlı — gelecek oturumlar için)
- 403 KALKTI; site `Login.aspx`'e yönlendiriyor (auth-gated, public değil).
- Login JS-hash'li (`Giris1/KeyTrv`) → curl ile login OLMUYOR. **Yöntem:** kullanıcının açık tarayıcı oturum çerezini al:
  `curl -H "Cookie: ASP.NET_SessionId=<çerez>; tl=arac" http://turev2.turevrac.com/<sayfa>.aspx`
- Çerez SÜRELİ → her oturumda kullanıcıdan yenisini iste. WebFetch çalışmaz (http→https zorlar).
- Sayfa adları: `docs/parite/00-ekran-listesi.md` (156 ekran, indirme HTTP/boyut logu).
- Form alan adları: `name="ctl00$ContentPlaceHolder1$X"`. Grid'ler DevExpress `ASPxGridView` (kolonlar `dxgvHeader`/kolon-seçici option'larında).

## Kapsam özeti (8 modül, ~149 ekran analiz edildi)

| # | Modül | Ekran | ✅ tam | 🟡 kısmi | ❌ yok | Dosya |
|---|-------|------:|------:|--------:|------:|-------|
| 01 | Araç & Filo | 25 | 0 | 8 | 17 | `01-arac-filo.md` |
| 02 | Tanım/Master + Kira kuralları | 22 | 4 | 11 | 7 | `02-tanim-master.md` |
| 03 | Cari/CRM/Personel/Hukuk | 14 | 0 | 2 | 12 | `03-cari-crm.md` |
| 04 | Kira/Rezervasyon | 12 | 0 | 5 | 7 | `04-kira-rezervasyon.md` |
| 05 | Finans (fatura/banka/kasa/gider/ceza) | 33 | 5 | 22 | 6 | `05-finans.md` |
| 06 | Fiyat/Tarife/Kampanya/Maliyet/Sigorta | 14 | 0 | 8 | 6 | `06-fiyat-tarife-sigorta.md` |
| 07 | Raporlar | 13 | 0 | 6 | 7 | `07-raporlar.md` |
| 08 | Sistem/Entegrasyon/Web/Mobil | 16 | 0 | 3 | 13 | `08-sistem-entegrasyon.md` |
| | **TOPLAM** | **~149** | **~9** | **~65** | **~75** | |

**Yorum:** Ham ekran paritesi ~%6 tam + ~%44 kısmi. **Çekirdek operasyon (araç→rezervasyon→kira→fatura→tahsilat→temel rapor) uçtan uca çalışıyor**, ama her ekran canlıya göre **alan/derinlik olarak sığ**. Uçurum: fiyat motoru, sigorta ürün katmanı, kurumsal/filo kiralama, CRM/personel/hukuk, toplu/otomatik finans, export, entegrasyonlar.

## Kesişen (cross-cutting) büyük açıklar — öncelik sırası

### P1 — Fiyat & Sigorta katmanı (en büyük tek uçurum)
- **`tarifeler_xml`**: asıl fiyat kaynağı — Rez Kaynağı(kanal) × şube × lokasyon × tarih için **günlük fiyat matrisi (Gün 1-7) + onay iş akışı**. Clone'da yalnız tek-katman `RateCard`.
- **`arac_grubu` fiyat-kural alanları** (clone `VehicleGroup` 🟡): provizyon2/muafiyet2(+döviz), genç ehliyet yılı, aylık max KM, yakıt fiyatı, sonra-öde oranı, kredi kartı şartı, **KM-kademe**, koltuk/kapı/bagaj — ~16 alan eksik.
- **Sigorta ürünleri**: CDW/SCDW/IMM/LCF/PAI/Mini Hasar/Max Güvence + paket hizmetler (Paket_Hizmet1..6), `sigorta_tarife_listesi` zengin katalog (max-gün tavanı, TR/EN, çoklu döviz). Clone'un generic `EkHizmetTanim`/`RentalAddOn`'u karşılamıyor.
- **Kampanya/dinamik fiyat**: `fiyat_kampanya_yonetimi`, `rakip_fiyat_analizi`, `doluluk_algoritma` (doluluk bandına göre yield çarpanı), `Max_Esneklik`. Hiç yok.
- **`maliyet_hesaplama`**: filo/uzun-dönem → BAŞA BAŞ + KAR + TEKLİF FİYATI (KKDF/BSMV/damga/enflasyon). Yok.
- Kira/rezervasyon formunda fiyat alanları dev: `kiralama.aspx` ~705 alan, `rezervasyon.aspx` ~597 (clone RentalContract ~35, Reservation ~18).

### P2 — Kira derinliği
- **Filo / uzun dönem kiralama** (aylık süre, vade gün, toplam km, taksit, damga) → ayrı entity, yok.
- **Ödeme/provizyon + komisyon**: KK provizyon, depozito (Max Güvence), acenta komisyonu, drop/adres teslim ücreti, broker yasakları.
- Kira/rez kuralları katmanı: `kiralama_kurallari(+basic)`, `kiralama_sartlari`, `rezsartlar` (min gün/kampanya/şart metni) — entity dahi yok.

### P3 — Araç kartı zenginleştirme
- `arac_kayit` ~100 alan; clone `Vehicle` ~18. Eksik: **Ozel_Kod1-5** (araca bağlı), alış/fatura vergi (Vergisiz/ÖTV/KDV), aylık+filo yönetim maliyeti, 2.el/güncel değer, filo giriş/çıkış, sahip/kredi bağı, ruhsat/tescil, bakım/lastik/km takip, operasyon kilitleri.
- ✅ İyi haber: **FiloStatus** (0KM STOK/HAVUZ/TAHSİS/USK/KSK/2.EL/SİPARİŞ) ve **SIPP** clone'da zaten var ve doğru ayrılmış.

### P4 — Finans olgunluğu
- **Toplu/otomatik**: `toplu_gider`, `toplu_tahsilat`, `otomatik_tahsilat` (batch/periyodik) — yok.
- **Gelen e-Fatura** (onayla/reddet/işle) — yok; outbound stub.
- **Cari↔cari virman** — yok (clone virman yalnız Kasa↔Banka tip-seviyesi). ⚠ Para hareketi → eklenince **adversarial inceleme şart**.
- `fatura`: ÖTV, tevkifat, iade faturası, manuel (kiradan bağımsız) fatura, damga vergisi — eksik.

### P5 — CRM / Personel / Hukuk (komple modüller yok)
- `musteri_crm` (risk/uyarı/İYS kanal-bazlı/doğum günü/temsilci/segment), `musteri_kayit` ~80 alan (clone ~36).
- **Personel** (maaş/işe giriş-çıkış/sürücü belgesi/tablet), **Hukuk** (dava/icra/avukat/tahsilat), Anket, Şikayet — hiç yok.

### P6 — Raporlama olgunluğu
- **Export (Excel/PDF) HİÇ yok** (clone'da print bile sadece fatura/sözleşmede).
- Araç/şube/grup/kaynak kırılımlı **kârlılık** raporları, gün-serili doluluk, periyodik servis (KM uyarı), boş araç/km raporları — yok.
- Mevcut filtreler yalnız tarih (şube/grup/plaka/cari kırılımı yok).

### P7 — Sistem & Entegrasyon
- **`ayarlar`** (tenant firma + tüm entegrasyon kimlikleri: e-Fatura/SMS/POS/SMTP/TCMB/XML) — yok; entegrasyonları stub'tan gerçeğe çevirmenin ön koşulu.
- **XML/broker/tedarikçi ailesi** (`xml_firma_tanim` katsayı/komisyon, `xml_disardan_arac/_sube` SIPP eşleme, `xml_fiyat_aktar`, `xml_rez_kaynak_tedarikci`) — yok.
- **Global hızlı arama** (`globalsearch`) — yok.
- **Yetki modeli farkı**: canlı ~110 ekran-bazlı checkbox + yetki grubu/şablon + kopyalama; clone 4 rol × 4 izin.
- Mobil/tablet, web CMS (`web_site_yonetimi`), uzak erişim, scheduler (`otomatik_servisler`) — yok.

## Önerilen yol haritası (parite verisiyle, faz faz)
1. **Fiyat Motoru v1 zemini** — `VehicleGroup`'u tam kural master'a zenginleştir (P1 alanları) + tarife matrisi (`tarifeler_xml` modeli: kanal×şube×tarih×gün) + sigorta ürün kataloğu. *Önce kalibrasyon:* canlı bir kira/fatura örneğinden **Gün_Hesapla + kuruş yuvarlama** ampirik doğrula.
2. **Araç kartı + Cari kartı zenginleştirme** (P3, P5 alanları — additive, düşük risk; mevcut "additive + dropdown" deseni).
3. **Kira derinliği** — filo/uzun dönem + ödeme/provizyon/komisyon + kira kuralları katmanı (P2).
4. **Finans olgunluğu** — toplu/otomatik işlemler, cari-cari virman (adversarial), e-fatura inbound, fatura vergi alanları (P4).
5. **Raporlama** — export (PDF/Excel) altyapısı + kırılımlı kârlılık raporları (P6).
6. **CRM/Personel/Hukuk** modülleri (P5) — yeni dikey'ler.
7. **Sistem/Entegrasyon** — `ayarlar` + entegrasyon kimlik deposu, XML/broker, global arama, yetki derinliği (P7). Gerçek entegrasyonlar kimlik/credential gerektirir.

## Kalibrasyon boşlukları (bu taramada ÇIKMADI — ileride canlı örnek gerekir)
- Kira **gün hesabı** (`Gun_Hesapla`) ve **kuruş yuvarlama** formülü — interaktif hesap/örnek fatura gerek (statik HTML'de yok).
- Kampanya/indirim formülleri — `Kampanya_Iframe.aspx` içinde, bu sette yok; ayrıca çekilmeli.
- Dinamik fiyat çarpan tablosu (`doluluk_algoritma` bantları) — alan yapısı var, değerler ampirik doğrulanmalı.
