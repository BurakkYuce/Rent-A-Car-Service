# Toplama — 05-finans (Faz 50-68)

Kaynak plan: `docs/parite/plan/05-finans-plan.md` (05a-finans-fatura + 05b-finans-kasa, 33 ekran).
Numara aralığı: **50-69** (19 faz üretildi, 50-68; 69 kullanılmadı).

---

## BLOKE (D8 — kimlik/credential gerekmeden yapılamaz, efor dışı)

| Ekran | Gerekçe | Gereken kimlik |
|---|---|---|
| `banka_islem_ara.aspx` | Kredi kartı/sanal-POS altyapısı olmadan listelenecek veri yok. | banka POS/sanal-pos entegratör kimliği |
| `banka_islemleri.aspx` (kısmi — 2/4 alt-akış) | Kalan 2 alt-akış (Kredi Kartı Tahsilatı/İade, Provizyon: Kart_No/CVV/Taksit/3D/Sanal_Pos_Deger/Odemelink) kart bilgisi işleme/saklama gerektirir. | PCI-DSS + banka POS entegratör kimliği |
| `ceza_gecis_listesi.aspx` | Esasen bir e-Devlet/UYAP scraping entegrasyonu (TC_Kimlik+Şifre ile hükümet portalından otomatik ceza çekme) — basit liste ekranı değil. | e-Devlet/UYAP kimliği |
| `hgs_gecis_listesi.aspx` | `IHgsService.GetCrossingsAsync` v1 STUB — gerçek adaptör yok, hiçbir geçiş verisi kalıcı saklanmıyor. Kalıcı bir `HgsGecis` tablosu olmadan liste ekranı yazmak, altında gerçek veri olmadan israf. | HGS gerçek adaptör kimliği (entegratör API erişimi) |

**Kısmi bloke alt-maddeler (efor içinde belirtilen, kendi fazına gömülü — ayrı satır AÇILMADI):**
- `fatura.aspx` (c): e-Fatura/GİB'e özgü alanlar (`EFatura_Firma`, `Cari_Entegre`, `EFatura_Tipi`/
  `Fatura_Sablon`/`EFatura_ID`/`FaturaSonDurum`, manuel çoklu döviz girişi, tenant-config bayrakları)
  — bkz. FAZ-51 Notlar. Gereken: e-Fatura/GİB entegratör kimliği.
- `fatura_islem_listesi.aspx` (c): "XML'e Aktar" (UBL-TR e-Fatura XML şeması) — bkz. FAZ-54 Notlar.
  Gereken: e-Fatura/GİB entegratör kimliği.

**Toplam bloke: 4 ekran tam + 2 ekranda kısmi alt-madde** (kaynak plandaki sayımla birebir örtüşür).

---

## YAPILMAZ

Bu modülde (05-finans-plan.md) **YAPILMAZ kalemi yok** — 02-tanim-master-plan.md'nin (kardeş plan)
3 YAPILMAZ kalemi bu modülün kapsamı DIŞINDA, burada tekrarlanmadı.

---

## KAPSANAN / EK FAZ GEREKMEZ (efor dışı, ama not edilmesi gereken)

Aşağıdaki ekranlar plan dosyasında "efor: 0" olarak işaretli çünkü başka bir faz onları TAM
kapsıyor — kendi faz dosyaları AÇILMADI, burada iz bırakılıyor:

| Ekran | Nasıl kapsanıyor |
|---|---|
| `banka_virman.aspx` | FAZ-50 (TEMEL PR-A) çözüyor — iki farklı banka hesabı arası virman artık mümkün. Ayrı iş yok. |
| `kasa_tanimi.aspx` | Tek gerçek eksiği (`Islem_Mail`/"Uyarı Mail Listesi") `02-tanim-master-plan.md`'nin PR-A'sında (`hesap_no_tanimlama.aspx`, `FinancialAccount.UyariMailListesi`) ZATEN planlı — burada tekrar sayılmadı. Bu modülde faz AÇILMADI. |
| `banka_islemleri.aspx` (2/4 alt-akış: Depozit Kapama, Gelen/Giden Havale) | `/depozito` ve `/finans/tahsilat`+`/finans/odeme` (Banka hesabıyla, FAZ-50 sonrası spesifik hesap seçimiyle) ile ZATEN karşılanıyor. Ek iş yok. |

**02-tanim-master-plan.md ile çapraz-modül not:** `hesap_para_islem.aspx` ve `genel_kasa.aspx`
(o planın PARA—Opus grubunda, kendi eforu 1+1=2 gün tahmin edilmişti) **BU FAZLA (FAZ-50) TAMAMEN
ÇÖZÜLÜYOR** — 02-tanim-master roadmap'ini yürüten kişi/ajan bu iki ekranı **"FAZ-50 ile kapsanan"**
olarak işaretlemeli, tekrar iş açmamalı. FAZ-50'nin efor tablosunda bu ikisinin kalan ince-UI
maliyeti (~0,5 gün, orijinal 2 günlük tahminin büyük kısmı zaten TEMEL PR-A'nın kendisi) açıkça
transfer olarak gösterildi (bkz. FAZ-50 tablo başlığı ve Notlar).

---

## PARA — Opus'a devir noktaları (bu roadmap kararı VERMEZ, yalnız yapısal iskelet kurar)

Aşağıdaki her nokta ilgili faz dosyasında **"KARAR GEREKLİ — Opus/kullanıcı kararı"** olarak açıkça
işaretlendi:

| Faz | Karar konusu |
|---|---|
| FAZ-50 | Geriye dönük `AccountRef=null` Kasa/Banka satırları: null-toleranslı kova mı, backfill ("Varsayılan Kasa/Banka") mı — bakiye sürekliliğini etkiler. |
| FAZ-51 | Otv/Tevkifat/Damga Vergisi'nin deftere ayrı satır olarak mı yansıyacağı, yoksa bilgi-amaçlı mı kalacağı. |
| FAZ-54 | Toplu faturalamada hangi kiraların "faturalanabilir" sayılacağı + KDV/kur çözüm yöntemi (satır-bazlı snapshot vs toplu-tek-kur). |
| FAZ-55 | "Sadece Kdv Yansıt" aksiyonunun dengeli karşı hesabı — `Alacak Cari` mı `Alacak Gider` mı. |
| FAZ-56 | Bakiye düzeltmenin dengeli karşı hesabı — yeni `LedgerAccountType.MuhasebeDuzeltmesi` mi, mevcut Gelir/Gider mi (P&L atıf riski). |
| FAZ-60 | Çok-satırlı ceza + kısmi ödeme birleştiğinde "Kalan" hesap şeması — toplam-bazlı mı satır-bazlı mı. |
| FAZ-64 | Gider kısmi ödemesinin defter postlamasını nasıl böldüğü — tek-seferde-tam-postla-yalnız-takip mi, iki-aşamalı-AçıkHesap mı. |

Bu 7 nokta kaynak plandaki "PARA — Opus'a devir" listesiyle (`bakiye_islem`, `banka_virman`/
`kasa_virman` [FAZ-50 içinde], `cezalar`, `ceza_listesi`, `gider_islemleri`, `fatura.aspx`-b,
`fatura_islem_listesi.aspx`-b, `gelen_e_fatura_listesi.aspx`-b) birebir örtüşür.

---

## Faz listesi (50-68, toplam ~46,25 gün)

| Faz | Ad | Efor | Bağımlılık |
|---|---|---|---|
| FAZ-50 | Hesap-bazlı Kasa/Banka Defteri [TEMEL PR-A] (+02-tanim-master transferi) | 6 gün | yok (öncelikli) |
| FAZ-51 | Fatura Bilgi + Para Alanları | 3,5 gün | yok |
| FAZ-52 | Fatura Detay Listesi | 1,5 gün | yok |
| FAZ-53 | Fatura Dönem + KDV Raporu | 3,5 gün | FAZ-55 (KDV Alış-kısmı) |
| FAZ-54 | Fatura Listesi + Toplu Faturalama | 3 gün | yok |
| FAZ-55 | Gelen e-Fatura KDV Kırılım + Yansıtma | 3,5 gün | yok |
| FAZ-56 | Bakiye Düzeltme | 2 gün | yok |
| FAZ-57 | Banka/Kasa Hareket Listeleri | 2,5 gün | FAZ-50 |
| FAZ-58 | Virman Geçmişi | 1 gün | FAZ-50 (kısmi) |
| FAZ-59 | Cari Virman Derinliği | 1,5 gün | yok |
| FAZ-60 | Trafik Cezası Derinliği | 4 gün | yok |
| FAZ-61 | Extre Özeti | 1,5 gün | yok |
| FAZ-62 | Genel Borç/Alacak Filtre | 1 gün | yok |
| FAZ-63 | Gider Arama + Tanımlama | 1 gün (+10dk) | yok |
| FAZ-64 | Gider İşlemleri Derinliği | 2,5 gün | yok |
| FAZ-65 | Hesap Ekstresi Filtre | 1 gün | yok |
| FAZ-66 | Müşteri Taksit + Kredi Takip Wire-in | 4,25 gün | yok |
| FAZ-67 | Nakit İşlem Bağımsız Ekran | 1,5 gün | FAZ-50 (kısmi) |
| FAZ-68 | Tahsilat Raporu Satır Modu | 1,5 gün | yok |

**Toplam: ~46,25 gün** (kaynak planın ~45,5-45,75 gün native toplamı + 0,5 gün 02-tanim-master
transferi [`hesap_para_islem.aspx`+`genel_kasa.aspx`, FAZ-50 içinde eritildi]).

**D5 (para) faz sayısı: 8** (FAZ-50, 51, 54, 55, 56, 60, 64 — zorunlu adversarial + defter dengesi
+ idempotency testi şablonda işaretli; FAZ-67 D3 ama regresyon-testi şart koşuldu, D5 tam ağırlığında
değil).

**Bloke: 4 tam ekran + 2 kısmi alt-madde (efor dışı). YAPILMAZ: 0. KAPSANAN/ek-faz-gerekmez: 3 ekran
+ 1 çapraz-modül transferi.**
