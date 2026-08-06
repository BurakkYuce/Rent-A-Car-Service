# Toplama — 02-tanim-master (FAZ-20 .. FAZ-31)

Kaynak plan: `docs/parite/plan/02-tanim-master-plan.md` (14 PR grubu, 27 ekran).
Bu toplama, o 14 PR grubunu FAZ-20..39 aralığında faz dosyalarına döker. 12 faz üretildi (14 PR
grubundan 2'si — `genel_kasa.aspx` ve `hesap_para_islem.aspx` — bilinçli olarak BU MODÜLDE faz
almadı, bkz. aşağıdaki not).

## Finans-çakışması notu (faz AÇILMADI)

### genel_kasa.aspx (plandaki PR-P1)
### hesap_para_islem.aspx (plandaki PR-P2)

Bu iki ekran finans modülünün TEMEL işiyle (`CashService`/`KasaHub.razor` — aynı dosyalar) doğrudan
çakışıyor: `genel_kasa.aspx` = Kasa/Banka Defteri raporunun hesap-bazlı filtresi, `hesap_para_islem.
aspx` = Kasa↔Banka virman formunun IBAN/hesap-bazlı genişletilmesi. Finans modülü (05-finans-plan.md)
kendi fazlarında AYNI dosyaları (`CashService.cs`, `KasaHub.razor`, `KasaBankaDefteri.razor`) daha
temelden ele alıyor — bu iki ekran **`docs/roadmap/FAZ-50-hesap-bazli-kasa-banka-defteri.md`**
içinde açıkça devralınmış durumda (FAZ-50 başlığı: "[TEMEL PR-A]", Kapsanan canlı ekran satırı
`hesap_para_islem.aspx` ve `genel_kasa.aspx`'i birebir listeliyor; Efor satırında "02-tanim-master
transferi" notu var). Bu modülde AYRI faz açmak aynı işi iki kez planlamak, aynı dosyada çakışan iki
PR üretmek olurdu — **FAZ-50 serisinde çözülüyor**, burada tekrar açılmadı.

---

## BLOKE — D8 (kod yazılmaz, kimlik/credential gerekir; kullanıcıya sorulmadan açılmaz)

| Ekran | Gerekli kimlik/credential | Not |
|---|---|---|
| `rakip_fiyat_analizi.aspx` | Dış rakip-fiyat veri sağlayıcısı/API sözleşmesi + token | Canlı
  TürevRent'te de bu ekranın gerçek işlevinin sağlıklı çalıştığı doğrulanamadı (grid tek kolon
  "Araç", veri boş; kodda tek eşleşme salt-görünüm bir yorum satırı) — düşük öncelikli bloke. |
| `serbest_sms.aspx` | SMS sağlayıcı API kimliği | `TenantSettings.SmsApiKey`/`SmsBaslik` alanları VAR
  ama hiçbir gönderim servisi/entegrasyonu bağlı değil — Twilio altyapısı bu repoda yalnız WhatsApp
  için kurulu (`TwilioWhatsAppService`), serbest-metin/zamanlı SMS için ayrı bir sağlayıcı
  sözleşmesi (veya aynı Twilio hesabına SMS kanalı ekleme kararı) kullanıcıya sorulmalı. |
| `xml_disardan_arac.aspx` | Harici XML/OTA broker feed'i — hangi firma(lar), feed URL/format,
  kimlik/credential | Gruplu bloke notu: bkz. `xml_disardan_sube.aspx` + `xml_firma_tanim.aspx`,
  üçü de aynı eksik altyapıya bağlı; kimlik/credential kullanıcıdan gelmeden "araç eşleştirme/
  mutabakat" ekranı anlamsız kalır. |
| `xml_disardan_sube.aspx` | Aynı XML/OTA broker feed kimliği, bu kez şube/lokasyon kodları için |
  Gruplu bloke notu: bkz. `xml_disardan_arac.aspx`. |
| `xml_firma_tanim.aspx` | Harici XML/OTA broker feed'i — hangi firma(lar), komisyon+katsayı
  sözleşme parametreleri | Gruplu bloke notu: bkz. `xml_disardan_arac.aspx`. Üçü (bu + araç + şube
  eşleştirme) BİRLİKTE aynı entegrasyon kararını bekler. D8 kataloğunun "XML broker" örneği tam bu
  üçlüye karşılık geliyor. |

**Bloke toplam:** 5 ekran, efor dışı, kullanıcı onayı gerekir.

---

## YAPILMAZ (bilinçli tasarım farkı / kapsam dışı — efor yok)

### hesap_tanimalama.aspx
**Gerekçe (plandan aynen taşındı):** Alan bazında **tam örtüşme** zaten var (`HesapKodu.Kod/Ad/
Aciklama/Aktif` ↔ canlı `Kod_Adı+Açıklama`). KISMİ işareti, canlı ekranın grid kolonu güvenilir
çıkarılamadığı için (kolon kaynağı=yok) verilen bir **ölçüm belirsizliği** — gerçek bir fonksiyonel
fark tespit edilmedi. Yapılacak bir iş yok; ilerideki bir tarama canlı grid kolonunu doğrularsa
yeniden değerlendirilir.

### tabletyonetim.aspx
**Gerekçe (plandan aynen taşındı):** Saha tableti / dijital imza toplama DONANIM entegrasyonu
(imza-tableti varsayılanları, RTF sözleşme şablon yükleme, aksesuar-fotoğraf şablonları). RentACar
bu sürümde bulut-SaaS, saha donanımı filosu yönetmiyor; taklit edilmesi var olmayan bir donanım
katmanını simüle eder — mimari gerileme. Kullanıcı ileride fiziksel saha-tablet operasyonu isterse
ayrı bir roadmap kararı olarak açılmalı.

### turevuzak.aspx
**Gerekçe (plandan aynen taşındı):** İş kuralı içermeyen, 3. parti uzak-erişim destek aracına
(TeamViewer benzeri) yönlendiren indirme sayfası. RentACar iş kapsamı dışı; hiçbir karşılık
gerekmiyor.

**YAPILMAZ toplam:** 3 ekran, efor yok.

---

## Faz listesi (FAZ-20 .. FAZ-31)

| Faz | Ad | Kapsanan ekran(lar) | Desen | Efor | Not |
|---|---|---|---|---|---|
| FAZ-20 | Sözlük derinlik paketi | `arac_grubu.aspx`, `para_tanimlama.aspx`,
  `hesap_no_tanimlama.aspx` | D1/D2 | 1,5g | plan 1,75g idi; `FinancialAccount.Sube` wire-in
  kodda zaten yapılmış çıktı (−0,25g) |
| FAZ-21 | Filo Kiralama derinlik + liste/arama | `filo_arac_kiralama.aspx`,
  `filo_kiralama_listesi.aspx` | D2+D3 | 1,5g | |
| FAZ-22 | Lokasyon × Drop derinlik paketi | `lokasyonlar.aspx`, `lokasyon_sube_ara.aspx` | D2 | 3,5g | |
| FAZ-23 | Şube derinlik paketi | `sube_tanimlama.aspx` | D2 | 3g | child-tablo + birleştirme aracı |
| FAZ-24 | Rez. Kaynağı × Tedarikçi oranları | `xml_rez_kaynak_tedarikci.aspx` | D2 | 0,75g |
  oran-yansıtma mantığı Opus'a devredildi |
| FAZ-25 | Rez Şartları (yeni dikey) | `rezsartlar.aspx` | D1 | 1g | yeni tenant-owned tablo |
| FAZ-26 | Otomatik Servisler günlük log | `otomatik_servisler.aspx` | D2 | 1g | yeni tablo |
| FAZ-27 | Karşılaştırmalı Durum Analizi | `karsilastirmali_durum_analizi.aspx` | D4 | 2g |
  hacim raporu, P&L değil |
| FAZ-28 | Detaylı Araç Listesi (konsolide) | `detayli_arac_listesi.aspx` | D3+D1 | 2g | |
| FAZ-29 | Toplu Gider + Toplu Tahsilat (tek-cari modu) | `toplu_gider.aspx`,
  `toplu_tahsilat.aspx` | D5 | 1,5g | **PARA — Opus**, zorunlu adversarial |
| FAZ-30 | Otomatik Tahsilat (manuel tetik) | `otomatik_tahsilat.aspx` | D5-bitişik | 1,5g |
  **PARA — Opus**, zorunlu adversarial |
| FAZ-31 | XML Fiyat Aktar: canlı ızgara + toplu silme | `xml_fiyat_aktar.aspx` | D3+D5-bitişik | 1g |
  **PARA — Opus**, zorunlu adversarial |

**Faz toplamı:** 12 faz, **~20,25 gün** (plandaki 14 PR'ın toplamı ~22,5g'den, `genel_kasa`+
`hesap_para_islem`'in ~2g'lik yapısal payı FAZ-50'ye devredildiği ve FAZ-20'de 0,25g'lik zaten-
yapılmış bir wire-in kaleminin düştüğü için düşük çıkıyor — bkz. üstteki not).

**Bloke (efor dışı):** 5 ekran.
**YAPILMAZ:** 3 ekran.
**FAZ-50'ye devredilen (bu modülde faz almadı):** 2 ekran (`genel_kasa.aspx`, `hesap_para_islem.aspx`).

**Toplam ekran sayısı (bu modül):** 27 — 12 faz (17 ekran) + FAZ-50 devri (2) + Bloke (5) +
YAPILMAZ (3) = 27 ✓.
(`hesap_tanimalama.aspx` YAPILMAZ listesinde sayılıyor, PR-A grubunda tekrar sayılmadı — plandaki
aynı kural bu toplamada da korundu.)
