# Toplama — 06-07-fiyat-rapor (FAZ-70 .. FAZ-79)

Kaynak plan: `docs/parite/plan/06-07-fiyat-rapor-plan.md` (24 ekran: Modül 06 Fiyat&Sigorta=13,
Modül 07 Raporlar=11).

Bu toplama, 24 ekranı FAZ-70..79 aralığında 10 faz dosyasına döker. 21 ekran fazlara dağıtıldı
(bazıları gruplanarak, plan dosyasının açık `gruplama:` etiketleri + dosya/desen ortaklığı taban
alınarak); 1 ekran tam BLOKE (`kabis_raporu.aspx`); 2 ekran YAPILMAZ. Ayrıca `fiyat_grup_
tanimlama.aspx`'in bir ALT-KAPSAMI (broker-auth kullanım senaryosu) da bloke — bu ekranın kendisi
FAZ-72'de kapsanıyor, sadece o AYRI/daha büyük alt-iş bloke olarak not edildi (aşağıda).

**Özel bulgular (bu toplamaya özgü, FAZ dosyalarında detaylı):**
1. **Tarife km kademesi (FAZ-71):** kod okunarak doğrulandı — plan dosyasının "RateCard zaten
   kademe-bazlı, Km alanları oraya eklenir" önerisi YANLIŞ hedef. `RateCard` fiyat motorunda
   `[Obsolete]` (`RateCardService.cs:31`) — yalnız `RateMatrix` eşleşmezse devreye giren geriye-uyum
   fallback'idir (`PricingService.cs`, `#pragma warning disable CS0618`). Aktif/birincil gün-
   kademesi yapısı `RateMatrix.Gun1..Gun7`/`GunHaftalik`/`GunAylik`'te — Km alanları BURAYA
   eklendi, RateCard'a değil. FAZ-71 kendi tek fazı olarak ayrıldı (diğer tarife alanlarıyla
   BİRLEŞTİRİLMEDİ) — kullanıcı talebiyle uyumlu.
2. **D4 rapor fazlarında P&L-yalnız-defterden kuralı:** FAZ-75 (sigorta_muayene), FAZ-77 (arac_
   genel_durumu_grafik+doluluk_grafik), FAZ-79 (gelir_tablosu) — hepsinde Test bölümünde bu kural
   AÇIKÇA ele alınıyor: FAZ-75/77'de rapor PARA TOPLAMI YAPMADIĞI için kural bağlayıcı değil (kanıtlı,
   sebebi yazılı); FAZ-79'da (gerçek P&L) kural TAM BAĞLAYICI — çift-sayım yasağı testi + Σ
   çapraz-doğrulama testi zorunlu tutuldu.

---

## BLOKE — D8 / alt-kapsam (kod yazılmaz, kimlik/credential gerekir; kullanıcıya sorulmadan açılmaz)

| Ekran / kapsam | Gerekli kimlik/credential | Not |
|---|---|---|
| `kabis_raporu.aspx` | KABİS (kayıp/çalıntı araç sorgu sistemi) entegrasyon kimliği/API erişimi |
  Repoda "Kabis"/"KABİS" terimi hiçbir dosyada geçmiyor (sıfır iz, grep doğrulandı). Tam ekran
  bloke — hiçbir faz kapsamıyor. |
| `fiyat_grup_tanimlama.aspx` **alt-kapsamı** (broker feed auth kullanımı) | Hangi broker/XML-feed
  kimliği kullanılacağı | Bu ekranın MASTER KAYIT kısmı (Ad+Oran+kimlik alanları CRUD) **FAZ-72'de
  KAPSANIYOR** — bloke olan yalnız bu kaydın gerçek canlı kullanım amacı (broker'ın bu kimlikle
  bize XML/feed üzerinden girişi/doğrulaması, D7 — yeni dikey: broker feed auth). Ekran sayısına
  ikinci kez dahil EDİLMEDİ (FAZ-72'de zaten sayılı). |

**Bloke toplam:** 1 tam ekran (`kabis_raporu.aspx`, efor dışı) + 1 alt-kapsam notu (ekran zaten
FAZ-72'de sayılı, ek efor gerektirmez — sadece o kısmın gerçek kullanımı kullanıcı onayı bekler).

---

## YAPILMAZ (bilinçli tasarım farkı / kapsam dışı — efor yok)

### fiyat_kampanya_yonetimi.aspx
**Gerekçe (plandan aynen taşındı):** Extractor profili düşük güvenli (alan=3, satır=0 —
muhtemelen DevExpress callback'i statik HTML çıkarımına yakalanmamış). Gerçek işlevi
`kampanya_ara.aspx` ile örtüşüyor; "Kampanya Yenile" aksiyonu zaten `RentalRuleService`'teki
`KampanyaKodu` REPLACE mekanizmasıyla (CLAUDE.md FAZ 3) karşılanıyor. Ayrı bir ekran/efor açmak
yerine kapsam `kampanya_ara.aspx` planına birleştirildi.

### genel_rapor.aspx
**Gerekçe (plandan aynen taşındı):** Kullanıcının kendi alan/pivot tanımlayabildiği genel bir
rapor-oluşturucu (custom report builder — "Alan Ekle/Düzenle", pivot tablo `DataTableJson`,
kayıtlı rapor, "Tümünü Sil/Aktar"). Bizim mimarimiz sabit-şema tenant-owned tablolar + sabit rapor
sayfaları üzerine kurulu (CLAUDE.md §2 "temiz mimari, katmanlı"); kullanıcının serbestçe alan/pivot
tanımlayabildiği bir BI-motoru inşa etmek kendi başına haftalar sürecek ayrı bir kategori (D6/D7'den
daha büyük), ROI düşük — repo zaten 20+ sabit rapor sunuyor. Taklit edilmesi mimari gerileme olur
(talimat madde 6 örneğiyle birebir örtüşen durum). Özel bir kırılım isteği gelirse mevcut
raporlardan birine (örn. Karlılık) yeni boyut eklemek (D4) yeterli.

**YAPILMAZ toplam:** 2 ekran, efor yok.

---

## Faz listesi (FAZ-70 .. FAZ-79)

| Faz | Ad | Kapsanan ekran(lar) | Desen | Efor | Not |
|---|---|---|---|---|---|
| FAZ-70 | Çoklu-Seçim Kapsam Alanları | `broker_yasaklari.aspx`, `tarifeler_xml.aspx` | D2 | 2g |
  plan gruplaması aynen korundu |
| FAZ-71 | Tarife Km Kademesi | `tarifeler.aspx` (bölüm a) | D2 | 1,5g | **PARA — Opus**;
  kullanıcı talebiyle kendi fazı — hedef entity plan metninden farklı (`RateMatrix`, `RateCard`
  değil — bkz. yukarıdaki özel bulgu 1) |
| FAZ-72 | Tarife Grubu + Tarife Teminat/Görünürlük Alanları | `fiyat_grup_tanimlama.aspx`,
  `tarifeler.aspx` (bölüm b/c/d) | D2 | 2g | broker-auth alt-kapsamı BLOKE (yukarıda) |
| FAZ-73 | Fiyat Motoru Yüzey Genişletmeleri | `broker_musaitlik_listesi.aspx`,
  `doluluk_algoritma.aspx`, `kampanya_ara.aspx` | D3+D2 | 4g | kampanya 5-durum göçü wire-in
  riski taşıyor (test'te kapatıldı) |
| FAZ-74 | Maliyet Hesaplama Derinliği | `maliyet_hesaplama.aspx`,
  `maliyet_hesaplama_ara.aspx` | D2+D7 | 4g | **PARA — Opus** (Rotatif kredi formülü; kalem-toplama
  buna bağlı değil) |
| FAZ-75 | Sigorta Yüzeyleri Derinliği | `sigorta_gider_ara.aspx`, `sigorta_muayene.aspx`,
  `sigorta_tarife_listesi.aspx` | D3+D4/D1+D2 | 4g | D4 kuralı `sigorta_muayene`'de bağlayıcı değil
  (para değil, testte açık) |
| FAZ-76 | Rapor Filtre Derinliği Serisi | `bos_arac_raporu.aspx`, `bos_km_detay.aspx`,
  `gunraporu.aspx`, `periyodik_servis_raporu.aspx`, `rezervasyon_kaynak_raporu.aspx` | D3/D4 | 4g |
  plan gruplaması aynen korundu; `bos_km_detay` canlı-tarama doğrulaması önerilir; 2 bonus veri-
  doğruluğu bulgusu (iptal filtresi eksikleri) dahil edildi |
| FAZ-77 | Filo & Doluluk Grafik Derinliği | `arac_genel_durumu_grafik.aspx`,
  `doluluk_grafik.aspx` | D4 | 3g | D4 kuralı bağlayıcı değil (envanter/yüzde, para toplamı yok —
  testte açık) |
| FAZ-78 | Ek Hizmet Raporu Derinliği | `extralar_raporu.aspx` | D3 | 1,5g | |
| FAZ-79 | Karlılık/Gelir Tablosu Çok-Boyutlu Genişleme | `gelir_tablosu.aspx` | D4 | 3g |
  **PARA — Opus** (Potansiyel-gelir formülü + maliyet-referans gösterimi); D4 kuralı TAM
  BAĞLAYICI — çift-sayım yasağı + Σ çapraz-doğrulama testi zorunlu |

**Faz toplamı:** 10 faz, **29 gün** — plandaki toplamla (Modül 06: 17,5g + Modül 07: 11,5g = 29g)
BİREBİR eşleşiyor (bölme/birleştirmede efor kaybı/fazlalığı yok).

**Bloke (efor dışı):** 1 tam ekran (`kabis_raporu.aspx`).
**YAPILMAZ:** 2 ekran.

**Toplam ekran sayısı (bu modül):** 24 = 21 faz-kapsanan ekran (12 Modül-06 + 9 Modül-07) + 1
tam-bloke (`kabis_raporu.aspx`) + 2 YAPILMAZ (`fiyat_kampanya_yonetimi.aspx`, `genel_rapor.aspx`) =
24 ✓. (`fiyat_grup_tanimlama.aspx`'in broker-auth alt-kapsamı BLOKE tablosunda AYRICA not edildi
ama ekran sayısına ikinci kez dahil edilmedi — kendisi zaten 21'in içinde, FAZ-72.)
