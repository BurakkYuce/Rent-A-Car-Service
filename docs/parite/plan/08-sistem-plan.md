# 08-sistem — ekleme planı (11 ekran)

Girdi: `~/turev-parite-2026-08/plan/eksikler.json` (`modul=08-sistem`, 11 kayıt) +
`docs/parite/08-sistem.md` + `docs/parite/10-ekleme-desenleri.md` (D1–D9) + `CLAUDE.md` §5.
Not: kaynak dosyada 15 ekran profillendi; 4'ü (`log_kayit`, `web_log_kayit`, `evrak_listesi`,
`yetki_reset`) canlıda 302→Default.aspx döndüren **kapalı/boş özellik** olduğundan
`eksikler.json`'a hiç girmedi (parite borcu üretilmedi) — bu plan kalan 11'i kapsar.

## Desen dağılımı (alt-kırılım dahil, 18 iş kalemi)

`ayarlar.aspx` tek ekran değil — 148 alanlık canlı ekranı 8 bağımsız çalışılabilir gruba
böldüm (aşağıda "hangi gruplar, hangi sırayla" bölümünde). Dağılım o 8 grup + diğer 10 ekranın
kendi kararı üzerinden:

| Desen | Adet | Nerede |
|---|---|---|
| D2 — kural taşıyan master | 6 | ayarlar Grup 1-5, `sifre_degistir.aspx` |
| D3 — liste/arama (tablo YOK) | 2 | `mobil_odeme.aspx` (PARA-Opus etiketli), `web_rezervasyon.aspx` |
| D8 — bloke (kimlik gerekir) | 3 | ayarlar Grup 6-8 |
| **YAPILMAZ** (bilinçli karar / kanıtsız) | 7 | `cikis.aspx`, `default.aspx`, `fonksiyonlar.aspx`, `globalsearch.aspx`, `kullanicilar.aspx`, `mobil_teslimat.aspx`, `web_site_yonetimi.aspx` |

**Toplam efor (D8 bloke hariç): 6,5 gün.**
- ayarlar Grup 1: 0,5g · Grup 2: 1,5g · Grup 3: 1g · Grup 4: 1,5g · Grup 5: 0,5g
- `sifre_degistir.aspx`: 0,5g
- `mobil_odeme.aspx`: 0,5g (yapısal kısım; tutar/kanal-muhasebe kararı ayrık PARA — Opus)
- `web_rezervasyon.aspx`: 0,5g

D8 bloke (efor yok, iş planına girmez): ayarlar Grup 6 (Banka/Pos hesap seçimi — online ödeme/POS
kimliği), Grup 7 (Ceza/Geçiş kod eşleme — e-Devlet/HGS entegratör kimliği), Grup 8 (SMS
entegrasyonu — SMS sağlayıcı kimliği).

---

## ayarlar.aspx — Ayarlar (148 alan, bizde ~%8)

Canlı tek monolitik ekran; biz `TenantSettings` (1 satır/tenant) + `Settings/Ayarlar.razor`
üzerine additive alan eklemeyi sürdürüyoruz (CLAUDE.md §5 — yeni tablo değil, mevcut tabloya
kolon). Kanıtladığım mevcut kapsam: Firma bilgisi, entegrasyon kimlik SLOT'ları (e-Fatura/SMS/POS
placeholder — henüz canlı değil), Görünüm+Operasyon (logo, varsayılan döviz/KDV, min/max gün,
rez-onay, dönemsel-faturalama job anahtarları), SMTP, WhatsApp günlük özet, PDF logo, halka açık
site. Aşağıdaki 8 grup, canlının kalan kategorilerini **çalışılabilirlik sırasına göre** kapatır
(önce bağımsız/ucuz, sonra kısmen bağımlı, en sona gerçek kimlik-blokeleri):

### Grup 1 — Sigorta/ek-hizmet açıklama + max-gün metinleri
- **desen:** D2
- **eylem:** `EkHizmetTanim` (`src/RentACar.Domain/Entities/EkHizmetTanim.cs`) şu an yalnız
  `Kod/Ad/BirimUcret/KdvOrani/Aktif` taşıyor — `Aciklama` (nvarchar, pazarlama/hukuki açıklama
  metni) ve `MaxGun` (int?, ör. "Genç Sürücü max 30 gün") alanları eklenir; `EkHizmetList.razor`
  formuna 2 alan + `EkHizmetTanimInput`/`EkHizmetTanimService` güncellenir; kira formunda
  (`KiraFormPaneller/SekmeEkHizmet.razor`) seçim yanında açıklama tooltip + max-gün aşımında
  uyarı (blokaj değil, bilgi rozeti) gösterilir. Canlının PAI/IMM/Muafiyet/Genç Sürücü/Bebek
  Koltuğu/Navigasyon/Mini Hasar/SCDW/LCF/Ek Sürücü kategorileri bizde zaten `EkHizmetTanim`
  satırları — yeni tablo YOK, 2 kolon.
- **dokunulacak:** `src/RentACar.Domain/Entities/EkHizmetTanim.cs`,
  `src/RentACar.Application/EkHizmetler/EkHizmetTanimInput.cs`,
  `src/RentACar.Application/EkHizmetler/EkHizmetTanimService.cs`,
  `src/RentACar.Web/Components/Pages/EkHizmetler/EkHizmetList.razor`,
  `src/RentACar.Web/Components/Pages/Bookings/KiraFormPaneller/SekmeEkHizmet.razor`,
  migration `AddEkHizmetAciklamaMaxGun`
- **efor:** 0,5 gün
- **bağımlılık:** yok

### Grup 2 — Görsel tema/renk kodları (8 renk)
- **desen:** D2
- **eylem:** `TenantSettings`'e 8 nullable hex-string alan eklenir (`RenkLimitBakiye`,
  `RenkOpsiyonlu`, `RenkAlacakli`, `RenkBugunDonecekler`, `RenkBugunCikacaklar`,
  `RenkRezAtananPlaka`, `RenkGecikenler`, `RenkKiralanmayan`) + `Ayarlar.razor`'a "Görünüm Renk
  Kodları" fieldset'i (8 `<input type="color">`). Bugün bu durumlar CSS'te SABİT sınıflarla
  basılıyor (`k-red`/`k-green`/`badge danger` — `Home.razor`, `RentalList.razor`,
  `ReservationList.razor`); tüketim tarafı `MainLayout.razor`'ın `<head>`'ine tenant değerlerini
  CSS custom-property olarak basan bir `<style>` bloğu eklenir (`--tr-renk-gecikenler` vb., boşsa
  var olan sabit tasarım-token'a düşer) — mevcut sınıflar `var(--tr-renk-*)` kullanacak şekilde
  CSS'te yeniden yönlendirilir. Kod değişikliği yok, yalnız CSS değişkeni + ayar formu.
- **dokunulacak:** `src/RentACar.Domain/Entities/TenantSettings.cs`,
  `src/RentACar.Application/TenantSettings/TenantSettingsModel.cs`,
  `src/RentACar.Application/TenantSettings/TenantSettingsService.cs`,
  `src/RentACar.Web/Components/Pages/Settings/Ayarlar.razor`,
  `src/RentACar.Web/Components/Layout/MainLayout.razor` (tenant renk `<style>` enjeksiyonu),
  ilgili CSS dosyası (badge/rozet sınıfları), migration `AddTenantSettingsRenkKodlari`
- **efor:** 1,5 gün (8 alan + CSS-var kablolama + en az 4 tüketim noktasının doğrulanması)
- **bağımlılık:** yok

### Grup 3 — Fiyatlandırma/muhasebe kuralı parametreleri
- **desen:** D2 — **yapısal kısım**; eşik/formül kararı **PARA — Opus**
- **eylem:** `TenantSettings`'e `VarsayilanFiyatTuru` (string?, kira formunda `FiyatTuru`
  alanının varsayılan seçimi — `Application/Bookings/BookingInput.cs`'teki `FiyatTuru` zaten
  per-kira var, burada eklenen sadece FORM varsayılanı), `VarsayilanYakitSeviyesi`,
  `DropMesafeYokIseSifir` (bool — drop ücretinin mesafe tanımsızsa 0 mı yazılacağı;
  `Application/DropTanimlari/` tüketir), `SaatFarkiToleransDk` (int? — dönüş gecikme
  toleransı, `ReturnMath`'e girdi), `IadeIslemSaatSiniri` (int? — gün-aşımı hesaplama saat
  sınırı, `BookingMath`/`ReturnMath`'e girdi) eklenir. Bu 3 alan (`DropMesafeYokIseSifir`,
  `SaatFarkiToleransDk`, `IadeIslemSaatSiniri`) şu an **hiçbir yerde yok** (ne alan ne sabit) —
  grep doğrulandı; kodda karşılıkları YOK, doğrudan yeni. Alanları TenantSettings'e eklemek ve
  formu göstermek yapısal iştir; **hangi varsayılan değerin/eşiğin doğru olduğu ve
  `ReturnMath`/`BookingMath` formülüne TAM olarak nasıl gireceği tutar hesaplamasını değiştirir
  → PARA — Opus onayı + adversarial inceleme şart** (kural 5).
- **dokunulacak:** `src/RentACar.Domain/Entities/TenantSettings.cs`,
  `src/RentACar.Application/TenantSettings/TenantSettingsModel.cs`,
  `src/RentACar.Application/TenantSettings/TenantSettingsService.cs`,
  `src/RentACar.Web/Components/Pages/Settings/Ayarlar.razor`,
  `src/RentACar.Application/Bookings/ReturnMath.cs`,
  `src/RentACar.Application/Bookings/BookingMath.cs` (PARA — Opus onayından sonra kablolama),
  migration `AddTenantSettingsFiyatParametreleri`
- **efor:** 1 gün (alan+form) — formül kablolaması Opus onayı sonrası ayrı efor
- **bağımlılık:** PARA — Opus (eşik/formül kararı)

### Grup 4 — İş kuralı checkbox'ları (canlıda ~45)
- **desen:** D2 (parçalı — bir kısmı zaten karşılanıyor, bir kısmı yeni alan)
- **eylem:** Tek-tek 45 checkbox yazmak yerine, kodda **gerçek karşılığı olan/olmayan**
  ayrımını yaptım:
  - `Fatura_Donem_Yaz` ≈ zaten var (`TenantSettings.DonemselFaturalamaJob`) — **atlanır**.
  - `TCMB_Otomatik`: bizde TCMB çekimi tenant-bazlı değil, **global** arka-plan job'ı
    (`src/RentACar.Web/Jobs/TcmbKurJob.cs`, 6 saatte bir, kimliksiz public feed). Canlının
    checkbox'ı muhtemelen "bu tenant fiyatlamada TCMB kurunu OTOMATİK mi kullanır, yoksa hep
    elle mi girilir" anlamına geliyor — bu zaten `KurCozucu`'da açık-kur-öncelikli davranışla
    KISMEN var (`Application/Kur/KurCozucu.cs`: açık kur>0 kazanır). Tenant-level "zorla
    otomatik, elle girişi kilitle" anahtarı yeni: `TenantSettings.KurElleGirisKilitli` (bool).
  - `TC_Dogrula`: e-Devlet TC doğrulama servisi gerektirir → **D8 bloke**, bu gruba dahil değil,
    Grup 7'ye taşınmadı çünkü ayrı kimlik (e-Devlet kimlik doğrulama API'si); not olarak
    işaretlenip iş planına girmez.
  - `Cari_Tek`, `Baf_Bakim_Cikart`, `SCDW_Dahil`/`CDW_Dahil`/`TP_Dahil`, `Farkli_Nokta_Biralabilir`
    gibi kalanlar: kodda ilgili MODÜL var (Cari tekilliği `Customer` benzersizlik kuralında,
    BAF-bakım ayrımı Araç Karnesi'nde, sigorta dahil/hariç `EkHizmetTanim`/fiyat motorunda,
    farklı-nokta `DropTanim`'de) ama **tenant-bazlı aç/kapa anahtarı YOK** — hepsi şu an "her
    zaman açık" sabit davranış. Bunları tek PR'da `TenantSettings`'e ~6-8 bool alan olarak
    eklemek ve her birini ilgili servisin başındaki bir guard'a bağlamak (guard GİRİŞ noktasında,
    bkz. CLAUDE.md "yorumdaki hafifletme bayatlar" dersi) bu grubun işi.
- **dokunulacak:** `src/RentACar.Domain/Entities/TenantSettings.cs`,
  `src/RentACar.Application/TenantSettings/TenantSettingsModel.cs`,
  `src/RentACar.Application/Kur/KurCozucu.cs`, `src/RentACar.Application/Bookings/BookingMath.cs`
  (drop/sigorta guard'ları), `src/RentACar.Application/Customers/CustomerService.cs` (Cari_Tek),
  `src/RentACar.Web/Components/Pages/Settings/Ayarlar.razor`,
  migration `AddTenantSettingsIsKurallari`
- **efor:** 1,5 gün (6-8 anahtar + guard bağlama; kalan ~35 checkbox'ın çoğu ya zaten
  karşılanıyor ya kanıtsız/düşük değerli — bu PR'a girmez, ayrı canlı doğrulama gerekir)
- **bağımlılık:** `TC_Dogrula` alt-kalemi D8 (e-Devlet kimlik doğrulama API'si) — gruptan çıkarıldı

### Grup 5 — RTF şablon dosya yükleme + Find/Replace editörü
- **desen:** D2
- **eylem:** Bizde zaten **farklı ama eşdeğer** bir tasarım var: `BelgeSablon`
  (`src/RentACar.Domain/Entities/BelgeSablon.cs`) tenant başına İSİMLİ şablon + bölüm-bazlı
  metin override (`BelgeBasligi`/`HukukiMetinSol`/`HukukiMetinSag`/`EkKosullarVarsayilan`/
  `AltBilgi`) + token-substitution (`{FirmaMarka}` vb., `BelgeSablonCozumleyici.cs`) sağlıyor.
  Canlının RTF-dosya-yükle + serbest-metin-üzerinde-Find/Replace modeli yerine bizde
  YAPILANDIRILMIŞ bölüm+token modeli var — bu bilinçli bir tasarım farkı (serbest RTF yerine
  güvenli token). Gerçek eksik: canlıdaki "UcResimler" (belge içine resim/logo gömme, imza
  alanı vb.) — `BelgeSablon`'a `ImzaAlaniGoster` (bool) + belge-içi ek görsel slotu (ör.
  "Kaşe/İmza görseli") eklenir; Find/Replace'in kendisi TEKRARLANMAZ (token sistemi zaten o işi
  görüyor, ayrı bir metin-editörü mimari yinelenme olur).
- **dokunulacak:** `src/RentACar.Domain/Entities/BelgeSablon.cs`,
  `src/RentACar.Application/BelgeSablon/BelgeSablonCozumleyici.cs`,
  `src/RentACar.Web/Components/Pages/Settings/BelgeSablonList.razor`,
  migration `AddBelgeSablonImzaAlani`
- **efor:** 0,5 gün
- **bağımlılık:** yok

### Grup 6 — Banka/Pos hesap seçimleri
- **desen:** D8 — **bloke**
- **gerekçe:** `Online_Hesap_No`/`Provizyon_Hesap_No`/`Sanal_Hesap_No`/`EURO_Hesap_No`/
  `Garanti_Pos_Hesap` — hangi ledger hesabına online/POS tahsilatının otomatik postlanacağını
  seçen alanlar. Gerçek bir online ödeme/POS entegrasyonu (sanal POS API'si) OLMADAN bu seçim
  hiçbir otomasyonu tetiklemez — anlamsız placeholder ayar eklemek CLAUDE.md §9'daki "gerçek
  entegrasyonlar" bekleme listesine (POS/banka) bağımlı. **bloke — sanal POS/banka entegratör
  kimliği.**

### Grup 7 — Ceza/Geçiş kod eşleme
- **desen:** D8 — **bloke**
- **gerekçe:** `Ceza_Kodu`/`Gecis_Kodu` dış sistemin (e-Devlet ceza sorgusu, HGS gerçek geçiş
  servisi) kod sözlüğüyle bizim `PenaltyType`/`HgsReflectionService` kodlarını eşleştirmeye
  yarar — otomatik senkronizasyon olmadan yalnız statik bir sözlük olur, TANIMSIZ değer üretir.
  **bloke — e-Devlet ceza sorgusu / gerçek HGS entegratör kimliği** (CLAUDE.md §9, madde 1).

### Grup 8 — SMS entegrasyonu
- **desen:** D8 — **bloke**
- **gerekçe:** `SMSTuru`, `SMS_Kll_Adi/Sifre`, `SMSTokenUsername/Password`, `Rez_Gun_Once`,
  `Kira_Bitis_Onc_Saat`, 6 SMS şablon metni. Bizde `TenantSettings.SmsBaslik`/`SmsApiKeyEnc`
  zaten kimlik SLOT'u olarak var (boş kurulur) ama gerçek gönderim yolu YOK (CLAUDE.md §6:
  SMS "hepsi" tarafından da stub bırakıldı). Zamanlama alanları (`Rez_Gun_Once`,
  `Kira_Bitis_Onc_Saat`) ve 6 şablon metni bir gönderim motoru olmadan tüketilemez — inşa
  edilirse kullanılmayan kod. **bloke — SMS sağlayıcı kimliği** (CLAUDE.md §9, madde 1).

**gruplama:** Grup 1+5 → "Ayarlar Derinlik PR-1" (bağımsız, düşük risk); Grup 2 → "Ayarlar
Derinlik PR-2" (CSS-var kablolaması tek başına, geniş dokunuş yüzeyi test ister); Grup 3+4 →
"Ayarlar Derinlik PR-3" (TenantSettings şema + guard bağlama tek migration'da toplanır, Grup 3
formül kısmı PARA-Opus onayına kadar guard'sız/pasif-varsayılan eklenir).

---

## cikis.aspx — Object moved
- **desen:** YAPILMAZ
- **gerekçe:** Zaten fonksiyonel karşılığı **kodda doğrulandı**: `POST /auth/logout`
  (`src/RentACar.Web/Identity/AuthEndpoints.cs:45-49`) cookie sign-out yapıp `/login`'e
  yönlendiriyor; çağrı noktası `Components/Layout/MainLayout.razor:255-257`. Canlı ekran da
  içerik üretmiyor (302 redirect) — bu ÇIKIŞ ekranının doğası, kıyaslanacak alan/kolon yok.
  Ek iş gerekmez.
- **dokunulacak:** yok (doğrulama amaçlı okunanlar: `src/RentACar.Web/Identity/AuthEndpoints.cs`,
  `src/RentACar.Web/Components/Layout/MainLayout.razor`)
- **efor:** —

## default.aspx — TürevRent (ana sayfa BI süiti)
- **desen:** YAPILMAZ
- **gerekçe:** Canlının 24-sekmeli monolitik BI dashboard'unu (Doluluk/Şube Karşılaştırma/Gelir
  Raporu/vb.) tek bir sayfaya geri toplamak, halihazırda **her biri kendi modülünde ayrı, test
  edilmiş `/raporlar/*` ekranımız** olan raporları (bu modülün kapsamı dışında, örn. Filo
  Analiz, Finans Analiz) tekilleştirmek olur — mimari gerileme (modüler-rapor kararının
  tersine gitmek). `src/RentACar.Web/Components/Pages/Home.razor` zaten filo KPI şeridi
  (Doluluk/RevPACD/ADR, satır 168), 6-ay gelir trendi (satır 176-182) ve ilgili rapora
  hızlı-link (`/raporlar/filo-analiz`, `/raporlar/finans-analiz`, `/raporlar/periyodik-servis`)
  içeriyor — "hangi rapora gidilir" keşfi zaten çözülü. Şube arama kutusu + tarih-aralığı
  kısayolları gibi tek-tek UI kolaylıkları, ilgili `/raporlar/*` ekranının kendi parite
  incelemesinde ele alınmalı (bu modülün kapsamı orası değil).
- **dokunulacak:** yok (doğrulama: `src/RentACar.Web/Components/Pages/Home.razor`)
- **efor:** —

## fonksiyonlar.aspx — (başlık yok)
- **desen:** YAPILMAZ
- **gerekçe:** Canlı profilde hiçbir kanıt yok (tip=diğer, alan=0, kolon=0) — eyleme
  dönüştürülebilir somut bir hedef tanımlanamıyor. Kural 6'nın "bilinçli tasarım farkı" değil
  ama "kanıtsız → iş planına giremez" hâli. Tahmin yapılmadı; gerçek bir gap olup olmadığı
  ancak canlıya çerezli tarayıcı erişimiyle (bkz. kullanıcı hafızası: canlı çerez erişim
  yöntemi) yeniden bakılırsa netleşir — bu plan kapsamında iş kalemi ÜRETİLMEDİ.
- **dokunulacak:** yok
- **efor:** —

## globalsearch.aspx — (başlık yok)
- **desen:** YAPILMAZ
- **gerekçe:** Kodda **iki ayrı** karşılığı doğrulandı: (1) `MainLayout.razor:18-19` sol menü
  üstünde `GET /ara` arama kutusu ("Evrak No / Plaka ara…"), (2) `MainLayout.razor:241-246`
  topbar'da "Ctrl+K" kısayol ipuçlu hızlı-arama kutusu (aynı `/ara`'ya post eder). Sayfa
  karşılığı `src/RentACar.Web/Components/Pages/Search/Ara.razor` (plaka/cari/kira/rez/fatura no
  arar). Canlının header'daki AJAX arama kutusu kavramsal ve konumsal olarak **tam örtüşüyor**
  — parite doğrulandı, "❓ DOĞRULANAMADI" belirsizliği bu incelemeyle kapandı, iş kalemi yok.
- **dokunulacak:** yok (doğrulama: `src/RentACar.Web/Components/Layout/MainLayout.razor`,
  `src/RentACar.Web/Components/Pages/Search/Ara.razor`)
- **efor:** —

## kullanicilar.aspx — Kullanıcılar
- **desen:** YAPILMAZ
- **gerekçe:** Canlının hacmini oluşturan **kullanıcı-bazlı ~100 menü/izin checkbox'ı**
  (Dash_Doluluk, Tanimlar, Arac_Tanimla, Yeni_Kiralama, Nakit_Islemler, Web_Yoneticisi vb., her
  menü öğesi için ayrı toggle + "Yetki Kopyalama") bizde **bilinçli olarak** rol+yetki-matrisi
  modeliyle karşılanıyor (CLAUDE.md §4: `UserRole`/`Permission`/`RolePermissions` +
  `/yetki` ekran-bazlı override — `src/RentACar.Web/Components/Pages/Authorization/Yetki.razor`,
  rol-üstü sıkılaştırma, deny-by-default). Kullanıcı-bazlı yüzlerce checkbox'ı taklit etmek bu
  matrisin üstüne İKİNCİ, çelişebilecek bir yetki kaynağı eklemek olur — mimari gerileme.
  `src/RentACar.Web/Components/Pages/Users/UserList.razor` zaten CRUD+parola-sıfırlama+şube
  atama yapıyor, doğru örtüşüyor. Not: "Yetki Kopyalama" (rol+şube'yi bir kullanıcıdan
  diğerine kopyalama kısayolu) küçük bir UX kolaylığı olarak `/yetki` veya `/kullanicilar`'a
  ayrıca D2 eklenebilir ama bu, checkbox-modeli kararından bağımsız, isteğe bağlı bir iş —
  bu planda zorunlu kalem olarak YAZILMADI.
- **dokunulacak:** yok (doğrulama: `src/RentACar.Web/Components/Pages/Users/UserList.razor`,
  `src/RentACar.Web/Components/Pages/Authorization/Yetki.razor`)
- **efor:** —

## mobil_odeme.aspx — Mobil/Tablet Tahsilat Görünümü
- **desen:** D3 (yapısal kısım) — kanal-ayrımının ledger'a yansıması **PARA — Opus**
- **eylem:** `/kasa` (`src/RentACar.Web/Components/Pages/Finance/KasaHub.razor`) genel
  Kasa/Banka defterini kapsıyor; canlının "mobil/tablet kanalından yapılan tahsilat" alt-görünümü
  için ayrı ekran AÇMADAN mevcut listeye bir **Kanal** filtresi eklenir. Yapısal eylem: tahsilat
  giriş formuna (virman/tahsilat POST uçları) opsiyonel `kanal` alanı (Masaüstü/Mobil/Tablet,
  varsayılan Masaüstü) eklenir, `/kasa` grid'ine Kanal kolonu + filtre dropdown'u eklenir.
  **Karar verilmeyen kısım:** bu alan `AccountLedgerEntry` (immutable, defter) tablosuna mı
  yoksa yalnız üst-seviye tahsilat kayıt tipine mi yazılacağı, ledger şemasına dokunduğundan
  **PARA — Opus** onayı + adversarial inceleme gerektirir (kural 5 + CLAUDE.md §3.5 — para
  tutan PR'da zorunlu adversarial inceleme).
- **dokunulacak:** `src/RentACar.Web/Components/Pages/Finance/KasaHub.razor`,
  `src/RentACar.Domain/Entities/AccountLedgerEntry.cs` (PARA-Opus onayı sonrası kolon eklenirse),
  ilgili tahsilat/virman POST uçları (`src/RentACar.Web/Finance/` altında)
- **efor:** 0,5 gün (yapısal: form alanı + grid filtresi) — şema/muhasebe kararı ayrı, PARA-Opus
- **bağımlılık:** PARA — Opus (kanal etiketinin ledger'a yazılma şekli)

## mobil_teslimat.aspx — Mobil Teslimatlar
- **desen:** YAPILMAZ
- **gerekçe:** Canlıdaki `Rac_Tablet_Say` (tablet sayacı) alanı doğruluyor: bu, TürevRent'in
  **ayrı bir native/tablet saha uygulaması** ekosistemine ait bir oturum-log tablosu (Kayıt No,
  Çıkış Zamanı, Teslim Zamanı, Çıkış Ofisi). Bizim mimarimiz TEK platform — responsive Blazor
  Server statik-SSR — tüm cihazlardan (masaüstü/tablet/telefon) AYNI kira mega-formunu
  (`/kiralar/{id}`) kullanır; teslim/dönüş zaman-damgaları ve çıkış ofisi zaten
  `RentalContract` üzerinde tutuluyor (Kira mega-form deseni, CLAUDE.md §6). Ayrı bir
  "tablet oturumu" tablosu eklemek, aynı gerçeği iki kaynaktan (RentalContract + ayrı oturum
  log'u) taşımak anlamına gelir — senkronizasyon riski yaratan bilinçli bir mimari gerileme
  olur. Gerçek bir native saha-app kararı gelirse (kullanıcı talebi), o zaman D7 (yeni dikey)
  olarak yeniden değerlendirilir.
- **dokunulacak:** yok (doğrulama: `RentalContract` teslim/dönüş alanları, kira mega-form)
- **efor:** —

## sifre_degistir.aspx — Şifre Değişikliği (self-service)
- **desen:** D2
- **eylem:** Bizde yalnız ADMİN'in başka kullanıcının parolasını sıfırladığı akış var
  (`POST /kullanicilar/sifre`, eski parola sorgusu yok). Yeni: giriş yapmış HERHANGİ bir
  kullanıcının kendi parolasını eski parolayı doğrulayarak değiştirdiği self-service akış.
  `IPasswordHasher.Verify(hash, password)` zaten var (`src/RentACar.Application/Common/
  IPasswordHasher.cs`) — `UserService`'e `ChangeOwnPasswordAsync(Guid userId, string eski,
  string yeni)` eklenir (eski parola `Verify` ile doğrulanır, yeni parola min-6 kuralı
  `ResetPasswordAsync`'teki gibi tekrarlanır). Endpoint kullanıcı kimliğini FORM'dan değil
  `ICurrentUser.UserId`'den alır (CSRF/yetki-yükseltme guard — başka kullanıcının id'sini
  post edip parolasını değiştirmeyi engeller). Yeni sayfa `/profil/sifre-degistir`
  (`[Authorize]`, herhangi rol) + `MainLayout.razor`'daki üst-bar kullanıcı adı yanına
  "Şifre Değiştir" linki (satır ~251, Çıkış formundan önce).
- **dokunulacak:** `src/RentACar.Application/Users/UserService.cs` (yeni metod),
  yeni `src/RentACar.Web/Components/Pages/Users/SifreDegistir.razor`,
  yeni endpoint `src/RentACar.Web/Identity/AuthEndpoints.cs` içine veya ayrı
  `src/RentACar.Web/Identity/ProfileEndpoints.cs`,
  `src/RentACar.Web/Components/Layout/MainLayout.razor` (link)
- **efor:** 0,5 gün
- **bağımlılık:** yok

## web_rezervasyon.aspx — Web (Acente) Rezervasyonları
- **desen:** D3
- **eylem:** Veri zaten `Reservation` entity'sinde var, `/rezervasyonlar`
  (`src/RentACar.Web/Components/Pages/Bookings/ReservationList.razor`) grid'inde eksik
  kolonlar olarak duruyor — kodda doğrulandı: `Reservation.CikisOfisi` (satır 27),
  `Reservation.Kaynak` (satır 68), `Customer.CepTel`/`Ad`/`Soyad` (Customer.cs 22-23, 41)
  hepsi entity'de mevcut, sadece grid'e YANSIMIYOR (bugünkü grid: No/Müşteri/Araç/Tarih
  [birleşik]/Gün/Tutar/Durum — satır 86-102). Eylem: grid'e "Teslim Tarihi" ayrı kolon (bugün
  Tarih hücresinde birleşik), "Cep Telefonu" kolonu, "Alış Şube" kolonu (CikisOfisi), "Kaynak"
  kolonu eklenir + üste bir "Kaynak = Web/Acente" filtre dropdown'u (canlının kanal-filtreli
  izleme ekranına en yakın karşılık, ayrı ekran açmadan). Yeni tablo YOK, domain değişikliği YOK.
- **dokunulacak:** `src/RentACar.Web/Components/Pages/Bookings/ReservationList.razor`,
  (gerekirse) `src/RentACar.Application/Bookings/ReservationService.cs` liste projeksiyonu
- **efor:** 0,5 gün
- **bağımlılık:** yok

## web_site_yonetimi.aspx — Web Sayfanızın Yönetimi
- **desen:** YAPILMAZ
- **gerekçe:** PLAN-TALIMATI'nın kendi örneği: canlı **çok-dilli (5 dil) tam tema/CMS/SEO/menü
  yönetim paneli** (Web_Language_*, Web_Slider_*, Web_WhyWe_*, Web_Comment_* testimonial,
  Web_Ayar_Color_* tema, Web_Menu_* 12-tip menü-builder+301 yönlendirme, Web_Translate_*,
  sayfa-bazlı Web_Seo_*). Bizim halka-açık site modülü (`/web-sitesi`
  `src/RentACar.Web/Components/Pages/WebSite/WebSiteHub.razor` + `/site-icerik`
  `SiteIcerikYonetim.razor` + `/blog-yonetim`) **PR-0..9'da bilinçli olarak** araç-vitrini +
  basit statik-sayfa/SSS + blog'a daraltıldı (bkz. kullanıcı hafızası: "Public site özellik
  TAMAMLANDI"), tek-dilli. Çok-dilli tema motoru + sürükle-bırak menü-builder + testimonial/
  slider yönetimi + sayfa-bazlı çok-dilli SEO'yu şimdi eklemek, kapsamı bilinçli daraltılmış bir
  modülü canlının **farklı ürün kategorisindeki** (tam CMS) haline geri büyütmek olur — mimari
  gerileme + orantısız efor. Kod doğrulaması: `WebSiteHub.razor` başlığı "Web Sitesi — İlanlar"
  (satır 24), yalnız araç ilanı odaklı; modül-kapalıysa `_modul` guard'ı sunucuda da kontrol
  ediyor (satır 16-20, çift-savunma deseni — `[Authorize]` yalnız rolü kontrol eder, menüyü
  gizlemek tek başına koruma değildir). Gerçek genişlik farkı kabul edilir, iş planına girmez.
- **dokunulacak:** yok (doğrulama: `src/RentACar.Web/Components/Pages/WebSite/WebSiteHub.razor`,
  `SiteIcerikYonetim.razor`)
- **efor:** —

---

TOPLAM: 11 ekran planlandı
