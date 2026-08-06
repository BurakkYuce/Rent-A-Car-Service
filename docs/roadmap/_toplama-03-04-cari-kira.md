# Toplama — 03-cari-crm + 04-kira-rezervasyon

Kaynak plan: `docs/parite/plan/03-04-cari-kira-plan.md` (22 ekran). Üretilen fazlar: **FAZ-40..49**
(10 dosya, `docs/roadmap/`). Bu dosya plana ayrılmaz iki liste taşır: **BLOKE** (D8 — kimlik/credential
gerekir) ve **YAPILMAZ** (bilinçli kapsam-dışı kararlar) + faz listesi özeti.

---

## BLOKE (D8 — kimlik/credential gerekir, kod yazılmaz)

### rezervasyon_kaynagi.aspx — alt-parça
**Ekran:** `rezervasyon_kaynagi.aspx` (ana yapısal kısmı FAZ-49'da kapsandı; bu alt-parça HARİÇ).
**Gerekli kimlik/credential:** XML broker/acente entegratör kimliği (canlı sistemin B2B ortak
entegrasyon katmanı) + dosya depolama altyapısı (broker paneli logo yükleme).
**Kapsam-dışı kalan alanlar:** `FrameKod`, `CalisilacakDoviz`, `Faz1Timeout`/`Faz2Timeout`,
`PaymentTuru`, `XmlHizmetOzel`, `XmlMailGitme`, acente paneli ödeme entegrasyonu, `RezLogo` yükleme
(gerçek dosya depolama + broker paneli).
**Not:** Açmadan önce kullanıcıya sorulur — port/stub hazırlanabilir ama kod yazılmaz. Efor
yazılmadı (plan dosyasında da yazılmamıştı).

---

## YAPILMAZ (bilinçli tasarım farkı — gerekçe plandan aynen taşındı)

### 1. kiralama.aspx — canlının ~30 sabit-kodlanmış ek hizmet/sigorta tipi
**Kapsam-dışı:** 128 alan (`B_Koltuk_*`, `CDW_*`, `LCF_*`, `SCDW_*`, `Genc_Surucu_*`, vb. ×
Dahil/Miktar/Tutar/Bedava).
**Gerekçe:** `EkHizmetTanim` generic master + `RentalAddOn` snapshot satır modeli ZATEN VAR ve tam
çalışıyor; 30 sabit kolonu ayrıca kodlamak — her yeni ek hizmet tipi için migration gerektiren,
"Bedava/Özel/Ücretsiz" gibi fiyat-tipi override'larını her kolonda tekrar eden — mimari gerileme
olur. Generic modelde yeni tip = yalnız veri satırı (`EkHizmetTanim` INSERT). Gerçek eksik: fiyat-tipi
override (satırda "Bedava" seçeneği, şu an `disabled` TODO) — bu KÜÇÜK parça D2 olarak ayrı ele
alınabilir (`RentalAddOn`'a `FiyatTipiOverride` enum eklenir), 0,5 gün — **faz dosyası almadı**,
istenirse ayrı bir faz olarak açılabilir.

### 2. kiralama.aspx — B2B Hizmet Alımı "görsel ama disabled" 9 alan
**Kapsam-dışı:** `Firma_Adi`, `Komisyon_Min_Oran/Tutar`, vb. (9 alan).
**Gerekçe:** bizim `StickyPanel` Dış Hizmet Alımı formu (`DisHizmetAlimi` entity, FAZ 4-B2) AYNI işi
TAM DEFTERLİ yapıyor; canlının kendisi de bu alanları placeholder/disabled tutmuş (gerçek özelliği
değil). Placeholder'ı taklit etmek geriye gidiş olurdu.

### 3. rezervasyon.aspx — sürücü serbest-metin + ek hizmet/sigorta + B2B + HGS/hasar + kart/provizyon
**Kapsam-dışı:** Sürücü serbest-metin (16 alan), ~30 sabit-kodlanmış ek hizmet/sigorta tipi, B2B
"görsel-ama-kayıtsız" alanlar, HGS/hasar manuel alanları, kart/provizyon erken-aşama detayları.
**Gerekçe:** AYNI gerekçeyle (`kiralama.aspx` alt-not #1/#2, yukarıdaki 1-2) rezervasyon aşamasında
da eklenmez. **Ek gerekçe (bu ekrana özel):** bu derinlik rezervasyon aşamasında toplanırsa "Kiraya
Çevir" ile kira mega-formuna geçişte İKİ KOPYA veri kaynağı oluşur (rezervasyon girişi + kira girişi)
— senkron/çakışma riski. Ürün kararı olarak bilinçli minimal tutulmuş (CLAUDE.md geçmişi bunu
doğruluyor); "Kiraya Çevir"den sonra derinlik zaten mega-formda geliyor. Bu, plan Kural 6'nın tam
örneği — körlemesine 400 alan eklenmez.

### 4. kira_listesi.aspx — kullanıcı-bazlı özelleştirilebilir grid
**Kapsam-dışı:** canlının 163-kolonluk **kullanıcı-bazlı özelleştirilebilir grid** sistemi
(`Grid_Alan`/`Grid_Alan_Deger`, tablo boyutu kaydet).
**Gerekçe:** kullanıcı-başına persisted grid-config UI'ı yüksek karmaşıklık/düşük iş değeri;
rolümüzde zaten yetki+şube kapsamı var, kullanıcı-bazlı kolon seçimi ek bir yetki yüzeyi açar. Onun
yerine sabit-ama-geniş kolon seti (FAZ-46'daki D3 eklemeleri) + mevcut Excel/CSV/PDF export (tüm
kolonlara zaten erişim veriyor) yeterli parite sağlar.

---

## Faz listesi (FAZ-40..49, toplam 10 faz)

| Faz | Ad | Desen | Efor | Kapsanan ekran |
|---|---|---|---|---|
| [FAZ-40](FAZ-40-cari-personel-alan-derinlik.md) | Cari & Personel Alan Derinliği | D1+D3 | 4,0g | musteri_kayit, musteri_genel_liste, musteri_listesi, personel_kayit, personel_listesi |
| [FAZ-41](FAZ-41-hukuk-crm-segment-derinlik.md) | Hukuk Dosyası & CRM Segment Derinliği | D1+D3+D4 | 2,25g | hukuk_birimi, hukuk_islem_listesi, musteri_crm |
| [FAZ-42](FAZ-42-sozlesme-bagli-anket.md) | Sözleşme-Bağlı Çıkış/Dönüş Anketi | D7 | 4,0g | anket_listesi |
| [FAZ-43](FAZ-43-donus-bagli-sikayet.md) | Teslim/Dönüş-Bağlı Şikayet Değerlendirme | D7 | 3,0g | sikayet_listesi |
| [FAZ-44](FAZ-44-assistans-talep-takibi.md) | Assistans Talep Takibi (Yol Yardım Mesajları) | D7 | 3,5g | musterigelenmesajlar |
| [FAZ-45](FAZ-45-personel-vardiya-raporu.md) | Personel Çalışma/Vardiya Raporu | D7 | 3,0g | personel_calisma_grafigi |
| [FAZ-46](FAZ-46-kira-listesi-kiralama-kurallari-derinlik.md) | Kira Listesi & Kiralama Kuralları Derinliği | D3+D2 | 4,5g | kira_listesi, kiralama_kurallari, kiralama_kurallari_basic, kiralama_sartlari |
| [FAZ-47](FAZ-47-kiralama-megaform-derinlik.md) | Kiralama Mega-Form: Şube/Teslim/Ödeme/Bakiye/2.Sürücü Derinliği | D6 | 4,0g | kiralama |
| [FAZ-48](FAZ-48-musaitlik-rezervasyon-filtre-derinlik.md) | Müsaitlik & Rezervasyon Filtre+Alan Derinliği | D3 | 4,0g | musait_arac_listesi, musaitlik_durum, rezervasyon, rezervasyon_listesi |
| [FAZ-49](FAZ-49-rezervasyon-kaynagi-kural-matrisi.md) | Rezervasyon Kaynağı Kural Matrisi | D2 | 3,0g | rezervasyon_kaynagi (D8 alt-parça hariç) |

**TOPLAM: 35,25 gün** — kaynak plandaki "TOPLAM YAPISAL EFOR (bloke hariç): ~35,25 gün" ile BİREBİR
eşleşiyor (yuvarlama/uydurma yok, plan eforu aynen taşındı — FAZ-TALIMATI kural 4).

**Ayrıca (bu fazların hiçbirinde efor YOK, ayrı takip gerektirir):**
- 8 ekranda **PARA — Opus kararı bekleyen alt-parça** var: hukuk_birimi/hukuk_islem_listesi
  (Tahsilat/Kalan defter-postlama biçimi), kira_listesi (Vade/Faturalanan kolon kaynağı),
  kiralama.aspx (çok-taraflı bakiye dağıtım mantığı + fiyat başlığı/indirim ayrıntısı),
  kiralama_kurallari (24-banka-kodu taksit/markup matrisi), rezervasyon.aspx/rezervasyon_listesi.aspx
  (çok-taraflı bakiye — kiralama.aspx ile AYNI karar), rezervasyon_kaynagi.aspx (komisyon/bakiye
  formülü). Her ilgili faz dosyasının **Notlar** bölümünde ayrıntılı işaretli; karar sonrası açılacak
  fazlar D5 olacağından CLAUDE.md §3.5 zorunlu adversarial inceleme gerekecek.
- 1 ekranda **D8 bloke** alt-parça (rezervasyon_kaynagi — yukarıda).
- 4 yerde **kısmi YAPILMAZ** (kiralama.aspx ×2, rezervasyon.aspx ×1, kira_listesi.aspx ×1 — yukarıda).

## Numaralandırma notu
D7 kalemleri (anket_listesi, sikayet_listesi, musterigelenmesajlar, personel_calisma_grafigi) talimat
gereği **her biri kendi fazına** ayrıldı (FAZ-42..45) — hiçbiri 4 günü aşmadığından alt-faza
bölünmedi. Geri kalan 6 küçük/orta grup, ayrılan 10 numaralık aralığa (FAZ-40..49) sığdırmak için
plan dosyasındaki bazı ayrı "gruplama" bloklarının ötesinde birleştirildi (ör. FAZ-40'ta Cari+Personel,
FAZ-46'da kira_listesi+kiralama_kurallari grubu, FAZ-48'de müsaitlik+rezervasyon grubu) — dosya
paylaşımı tam örtüşmese de aynı desen ailesi ve düşük-orta efor nedeniyle tek PR'a alındı; her fazın
**Notlar** bölümünde bu birleştirme açıkça belirtildi ve istenirse implementasyonda ayrı commit'lere
bölünebileceği not edildi (CLAUDE.md §3.4 küçük-PR ilkesiyle çakışmasın diye). Hiçbir faz eforu bu
birleştirme yüzünden değiştirilmedi — plan toplamı (35,25g) korundu.
