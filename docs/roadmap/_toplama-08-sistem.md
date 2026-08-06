# Toplama — 08-sistem (FAZ-80 .. FAZ-85)

Kaynak plan: `docs/parite/plan/08-sistem-plan.md` (11 ekran; `ayarlar.aspx` 8 alt-gruba bölünmüş).
Bu toplama, o 11 ekranı FAZ-80..85 aralığında 6 faz dosyasına döker. `ayarlar.aspx`'in 8 grubu
1-4 gün kuralına göre 3 faz dosyasında birleştirildi (Grup 1+5, Grup 2, Grup 3+4); Grup 6-8 D8
bloke olduğundan faz almadı (aşağıda). Diğer 4 ekran (`sifre_degistir.aspx`, `mobil_odeme.aspx`,
`web_rezervasyon.aspx`) kendi başına 1 faz aldı. Kalan 7 ekran YAPILMAZ (aşağıda gerekçenin TAMAMI).

---

## BLOKE — D8 (kod yazılmaz, kimlik/credential gerekir; kullanıcıya sorulmadan açılmaz)

Bu 4 kalem `ayarlar.aspx`'in alt-gruplarıdır — AYRI ekran değildir, `ayarlar.aspx`'in kendi
sayımında (11 ekranın 1'i) zaten sayılmıştır.

| Alt-kalem | Gerekli kimlik/credential | Not |
|---|---|---|
| **ayarlar Grup 6** — Banka/Pos hesap seçimleri (`Online_Hesap_No`/`Provizyon_Hesap_No`/
  `Sanal_Hesap_No`/`EURO_Hesap_No`/`Garanti_Pos_Hesap`) | Sanal POS / banka entegratör kimliği |
  Hangi ledger hesabına online/POS tahsilatının otomatik postlanacağını seçen alanlar. Gerçek bir
  online ödeme/POS entegrasyonu (sanal POS API'si) OLMADAN bu seçim hiçbir otomasyonu tetiklemez —
  anlamsız placeholder ayar eklemek CLAUDE.md §9'daki "gerçek entegrasyonlar" bekleme listesine
  (POS/banka) bağımlı. |
| **ayarlar Grup 7** — Ceza/Geçiş kod eşleme (`Ceza_Kodu`/`Gecis_Kodu`) | e-Devlet ceza sorgusu /
  gerçek HGS entegratör kimliği | Dış sistemin (e-Devlet ceza sorgusu, HGS gerçek geçiş servisi)
  kod sözlüğüyle bizim `PenaltyType`/`HgsReflectionService` kodlarını eşleştirmeye yarar —
  otomatik senkronizasyon olmadan yalnız statik bir sözlük olur, TANIMSIZ değer üretir. |
| **ayarlar Grup 8** — SMS entegrasyonu (`SMSTuru`, `SMS_Kll_Adi/Sifre`, `SMSTokenUsername/
  Password`, `Rez_Gun_Once`, `Kira_Bitis_Onc_Saat`, 6 SMS şablon metni) | SMS sağlayıcı kimliği |
  `TenantSettings.SmsBaslik`/`SmsApiKeyEnc` zaten kimlik SLOT'u olarak var (boş kurulur) ama
  gerçek gönderim yolu YOK (CLAUDE.md §6: SMS "hepsi" tarafından da stub bırakıldı). Zamanlama
  alanları ve 6 şablon metni bir gönderim motoru olmadan tüketilemez — inşa edilirse
  kullanılmayan kod. |
| **ayarlar Grup 4 alt-kalemi — `TC_Dogrula`** | e-Devlet TC kimlik doğrulama API'si (Grup 7'nin
  ceza-sorgu kimliğinden AYRI bir kimlik) | Grup 7'ye taşınmadı çünkü farklı bir e-Devlet servisi
  (kimlik doğrulama, ceza sorgusu değil); FAZ-82'nin kapsamına GİRMEDİ, not olarak işaretlenip
  iş planına girmez. |

**Bloke toplam:** 4 alt-kalem (hepsi `ayarlar.aspx` içinde), efor dışı, kullanıcı onayı gerekir.

---

## YAPILMAZ (bilinçli tasarım farkı / kanıtsız — efor yok)

### cikis.aspx
**Gerekçe (plandan aynen taşındı):** Zaten fonksiyonel karşılığı **kodda doğrulandı**:
`POST /auth/logout` (`src/RentACar.Web/Identity/AuthEndpoints.cs:45-49`) cookie sign-out yapıp
`/login`'e yönlendiriyor; çağrı noktası `Components/Layout/MainLayout.razor:255-257`. Canlı ekran
da içerik üretmiyor (302 redirect) — bu ÇIKIŞ ekranının doğası, kıyaslanacak alan/kolon yok. Ek iş
gerekmez.

### default.aspx — referans sistem (ana sayfa BI süiti)
**Gerekçe (plandan aynen taşındı):** Canlının 24-sekmeli monolitik BI dashboard'unu
(Doluluk/Şube Karşılaştırma/Gelir Raporu/vb.) tek bir sayfaya geri toplamak, halihazırda **her
biri kendi modülünde ayrı, test edilmiş `/raporlar/*` ekranımız** olan raporları (bu modülün
kapsamı dışında, örn. Filo Analiz, Finans Analiz) tekilleştirmek olur — mimari gerileme (modüler-
rapor kararının tersine gitmek). `src/RentACar.Web/Components/Pages/Home.razor` zaten filo KPI
şeridi (Doluluk/RevPACD/ADR, satır 168), 6-ay gelir trendi (satır 176-182) ve ilgili rapora
hızlı-link (`/raporlar/filo-analiz`, `/raporlar/finans-analiz`, `/raporlar/periyodik-servis`)
içeriyor — "hangi rapora gidilir" keşfi zaten çözülü. Şube arama kutusu + tarih-aralığı
kısayolları gibi tek-tek UI kolaylıkları, ilgili `/raporlar/*` ekranının kendi parite
incelemesinde ele alınmalı (bu modülün kapsamı orası değil).

### fonksiyonlar.aspx — (başlık yok)
**Gerekçe (plandan aynen taşındı):** Canlı profilde hiçbir kanıt yok (tip=diğer, alan=0, kolon=0)
— eyleme dönüştürülebilir somut bir hedef tanımlanamıyor. Kural 6'nın "bilinçli tasarım farkı"
değil ama "kanıtsız → iş planına giremez" hâli. Tahmin yapılmadı; gerçek bir gap olup olmadığı
ancak canlıya çerezli tarayıcı erişimiyle (bkz. kullanıcı hafızası: canlı çerez erişim yöntemi)
yeniden bakılırsa netleşir — bu plan kapsamında iş kalemi ÜRETİLMEDİ.

### globalsearch.aspx — (başlık yok)
**Gerekçe (plandan aynen taşındı):** Kodda **iki ayrı** karşılığı doğrulandı: (1)
`MainLayout.razor:18-19` sol menü üstünde `GET /ara` arama kutusu ("Evrak No / Plaka ara…"), (2)
`MainLayout.razor:241-246` topbar'da "Ctrl+K" kısayol ipuçlu hızlı-arama kutusu (aynı `/ara`'ya
post eder). Sayfa karşılığı `src/RentACar.Web/Components/Pages/Search/Ara.razor`
(plaka/cari/kira/rez/fatura no arar). Canlının header'daki AJAX arama kutusu kavramsal ve
konumsal olarak **tam örtüşüyor** — parite doğrulandı, "❓ DOĞRULANAMADI" belirsizliği bu
incelemeyle kapandı, iş kalemi yok.

### kullanicilar.aspx — Kullanıcılar
**Gerekçe (plandan aynen taşındı):** Canlının hacmini oluşturan **kullanıcı-bazlı ~100 menü/izin
checkbox'ı** (Dash_Doluluk, Tanimlar, Arac_Tanimla, Yeni_Kiralama, Nakit_Islemler, Web_Yoneticisi
vb., her menü öğesi için ayrı toggle + "Yetki Kopyalama") bizde **bilinçli olarak**
rol+yetki-matrisi modeliyle karşılanıyor (CLAUDE.md §4: `UserRole`/`Permission`/`RolePermissions`
+ `/yetki` ekran-bazlı override — `src/RentACar.Web/Components/Pages/Authorization/Yetki.razor`,
rol-üstü sıkılaştırma, deny-by-default). Kullanıcı-bazlı yüzlerce checkbox'ı taklit etmek bu
matrisin üstüne İKİNCİ, çelişebilecek bir yetki kaynağı eklemek olur — mimari gerileme.
`src/RentACar.Web/Components/Pages/Users/UserList.razor` zaten CRUD+parola-sıfırlama+şube atama
yapıyor, doğru örtüşüyor. Not: "Yetki Kopyalama" (rol+şube'yi bir kullanıcıdan diğerine kopyalama
kısayolu) küçük bir UX kolaylığı olarak `/yetki` veya `/kullanicilar`'a ayrıca D2 eklenebilir ama
bu, checkbox-modeli kararından bağımsız, isteğe bağlı bir iş — bu planda zorunlu kalem olarak
YAZILMADI.

### mobil_teslimat.aspx — Mobil Teslimatlar
**Gerekçe (plandan aynen taşındı):** Canlıdaki `Rac_Tablet_Say` (tablet sayacı) alanı doğruluyor:
bu, referans sistem'in **ayrı bir native/tablet saha uygulaması** ekosistemine ait bir oturum-log
tablosu (Kayıt No, Çıkış Zamanı, Teslim Zamanı, Çıkış Ofisi). Bizim mimarimiz TEK platform —
responsive Blazor Server statik-SSR — tüm cihazlardan (masaüstü/tablet/telefon) AYNI kira
mega-formunu (`/kiralar/{id}`) kullanır; teslim/dönüş zaman-damgaları ve çıkış ofisi zaten
`RentalContract` üzerinde tutuluyor (Kira mega-form deseni, CLAUDE.md §6). Ayrı bir "tablet
oturumu" tablosu eklemek, aynı gerçeği iki kaynaktan (RentalContract + ayrı oturum log'u) taşımak
anlamına gelir — senkronizasyon riski yaratan bilinçli bir mimari gerileme olur. Gerçek bir native
saha-app kararı gelirse (kullanıcı talebi), o zaman D7 (yeni dikey) olarak yeniden
değerlendirilir.

### web_site_yonetimi.aspx — Web Sayfanızın Yönetimi
**Gerekçe (plandan aynen taşındı):** PLAN-TALIMATI'nın kendi örneği: canlı **çok-dilli (5 dil) tam
tema/CMS/SEO/menü yönetim paneli** (Web_Language_*, Web_Slider_*, Web_WhyWe_*, Web_Comment_*
testimonial, Web_Ayar_Color_* tema, Web_Menu_* 12-tip menü-builder+301 yönlendirme,
Web_Translate_*, sayfa-bazlı Web_Seo_*). Bizim halka-açık site modülü (`/web-sitesi`
`src/RentACar.Web/Components/Pages/WebSite/WebSiteHub.razor` + `/site-icerik`
`SiteIcerikYonetim.razor` + `/blog-yonetim`) **PR-0..9'da bilinçli olarak** araç-vitrini + basit
statik-sayfa/SSS + blog'a daraltıldı (bkz. kullanıcı hafızası: "Public site özellik TAMAMLANDI"),
tek-dilli. Çok-dilli tema motoru + sürükle-bırak menü-builder + testimonial/slider yönetimi +
sayfa-bazlı çok-dilli SEO'yu şimdi eklemek, kapsamı bilinçli daraltılmış bir modülü canlının
**farklı ürün kategorisindeki** (tam CMS) haline geri büyütmek olur — mimari gerileme + orantısız
efor. Kod doğrulaması: `WebSiteHub.razor` başlığı "Web Sitesi — İlanlar" (satır 24), yalnız araç
ilanı odaklı; modül-kapalıysa `_modul` guard'ı sunucuda da kontrol ediyor (satır 16-20,
çift-savunma deseni — `[Authorize]` yalnız rolü kontrol eder, menüyü gizlemek tek başına koruma
değildir). Gerçek genişlik farkı kabul edilir, iş planına girmez.

**YAPILMAZ toplam:** 7 ekran, efor yok.

---

## Faz listesi (FAZ-80 .. FAZ-85)

| Faz | Ad | Kapsanan ekran | Desen | Efor | Not |
|---|---|---|---|---|---|
| FAZ-80 | Ayarlar Derinlik PR-1: Ek Hizmet Açıklama/Max-Gün + Belge Şablon İmza Alanı |
  `ayarlar.aspx` (Grup 1+5) | D2 | 1g | düşük risk, para değişikliği yok |
| FAZ-81 | Ayarlar Derinlik PR-2: Görsel Tema Renk Kodları | `ayarlar.aspx` (Grup 2) | D2 | 1,5g |
  6/8 kavramın bugün hiç görsel karşılığı yok — kod okunarak plandan daha dar/gerçekçi kapsam
  netleştirildi |
| FAZ-82 | Ayarlar Derinlik PR-3: Fiyat/Muhasebe Parametreleri + İş Kuralı Anahtarları |
  `ayarlar.aspx` (Grup 3+4) | D2 (parçalı) | 2,5g | Grup 3 formül-kablolaması PARA-Opus onayına
  kadar guard'sız/pasif; Grup 4 guard'ları AKTİF bağlanır — **zorunlu adversarial inceleme**;
  `KismiGunEsigiSaat` (canlı-kalibre, PR #141) ile karıştırılmaması gereken bir tuzak not edildi |
| FAZ-83 | Şifre Değiştirme (Self-Service) | `sifre_degistir.aspx` | D2 | 0,5g | yetki-yükseltme/
  CSRF guard'ı kritik (kimlik FORM'dan değil `ICurrentUser.UserId`'den) |
| FAZ-84 | Mobil/Tablet Tahsilat: Kanal Filtresi (Yapısal) | `mobil_odeme.aspx` | D3 (yapısal) |
  0,5g | **PARA — Opus onayı bekliyor**; şema önerisi (Kanal → `CashTransaction`, ledger'a
  dokunma) hazırlandı — **zorunlu adversarial inceleme** onay sonrası |
| FAZ-85 | Web Rezervasyon: Kolon Derinliği | `web_rezervasyon.aspx` | D3 | 0,5g | tek dosya
  değişikliği, domain/servis dokunulmuyor |

**Faz toplamı:** 6 faz, **6,5 gün** (D8 bloke hariç — planın kendi toplamıyla birebir örtüşüyor).

**Bloke (efor dışı):** 4 alt-kalem, hepsi `ayarlar.aspx` içinde (Grup 6/7/8 + `TC_Dogrula`).
**YAPILMAZ:** 7 ekran.

**Toplam ekran sayısı (bu modül):** 11 — 6 faz (4 ekran: `ayarlar.aspx` [3 faza bölündü] +
`sifre_degistir.aspx` + `mobil_odeme.aspx` + `web_rezervasyon.aspx`) + YAPILMAZ (7) = 11 ✓.
(Bloke 4 alt-kalem `ayarlar.aspx`'in İÇİNDE sayıldı, ayrı ekran olarak tekrar sayılmadı — planın
kendi tablosundaki "D8 — bloke | 3 | ayarlar Grup 6-8" satırıyla aynı kural.)
