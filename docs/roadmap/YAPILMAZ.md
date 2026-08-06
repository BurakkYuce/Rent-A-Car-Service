# YAPILMAZ — bilinçli olarak eklenmeyen ekranlar ve gerekçeleri

Parite taraması canlıda olup bizde olmayan her şeyi listeledi. Bunların bir kısmı **eksik değil,
karar**: taklit edilmesi mimari gerileme olurdu ya da karşılığı zaten var. Bu dosya o kararların
**tam gerekçelerini** tek yerde toplar.

> Gerekçeler ekran-bazlı plan dosyalarından (`docs/parite/plan/*.md`) **aynen** taşındı; kısaltılmadı.
> Bir karar yanlış bulunursa ilgili plan dosyasında tartışılır ve buraya güncel hâli taşınır.

**Toplam: 12 tam + 1 kısmi (alt-parça) karar.**

---

## `hesap_tanimalama.aspx`

**Modül:** Tanım/Master

Alan bazında **tam örtüşme** zaten var (`HesapKodu.Kod/Ad/Aciklama/Aktif` ↔ canlı `Kod_Adı+Açıklama`). KISMİ işareti, canlı ekranın grid kolonu güvenilir çıkarılamadığı için (kolon kaynağı=yok) verilen bir **ölçüm belirsizliği** — gerçek bir fonksiyonel fark tespit edilmedi. Yapılacak bir iş yok; ilerideki bir tarama canlı grid kolonunu doğrularsa yeniden değerlendirilir.

## `tabletyonetim.aspx`

**Modül:** Tanım/Master

Saha tableti / dijital imza toplama DONANIM entegrasyonu (imza-tableti varsayılanları, RTF sözleşme şablon yükleme, aksesuar-fotoğraf şablonları). RentACar bu sürümde bulut-SaaS, saha donanımı filosu yönetmiyor; taklit edilmesi var olmayan bir donanım katmanını simüle eder — mimari gerileme. Kullanıcı ileride fiziksel saha-tablet operasyonu isterse ayrı bir roadmap kararı olarak açılmalı.

## `turevuzak.aspx`

**Modül:** Tanım/Master

İş kuralı içermeyen, 3. parti uzak-erişim destek aracına (TeamViewer benzeri) yönlendiren indirme sayfası. RentACar iş kapsamı dışı; hiçbir karşılık gerekmiyor.

## `fiyat_kampanya_yonetimi.aspx`

**Modül:** Fiyat/Tarife + Raporlar

Extractor profili düşük güvenli (alan=3, satır=0 — muhtemelen DevExpress callback'i statik HTML çıkarımına yakalanmamış). Gerçek işlevi `kampanya_ara.aspx` (aşağıda) ile örtüşüyor; "Kampanya Yenile" aksiyonu zaten `RentalRuleService`'teki `KampanyaKodu` REPLACE mekanizmasıyla (CLAUDE.md FAZ 3) karşılanıyor. Ayrı bir ekran/efor açmak yerine kapsam `kampanya_ara.aspx` planına birleştirildi.

## `genel_rapor.aspx`

**Modül:** Fiyat/Tarife + Raporlar

Kullanıcının kendi alan/pivot tanımlayabildiği genel bir rapor-oluşturucu (custom report builder — "Alan Ekle/Düzenle", pivot tablo `DataTableJson`, kayıtlı rapor, "Tümünü Sil/Aktar"). Bizim mimarimiz sabit-şema tenant-owned tablolar + sabit rapor sayfaları üzerine kurulu (CLAUDE.md §2 "temiz mimari, katmanlı"); kullanıcının serbestçe alan/pivot tanımlayabildiği bir BI-motoru inşa etmek kendi başına haftalar sürecek ayrı bir kategori (D6/D7'den daha büyük), ROI düşük — repo zaten 20+ sabit rapor sunuyor. Taklit edilmesi mimari gerileme olur (talimat madde 6 örneğiyle birebir örtüşen durum). Özel bir kırılım isteği gelirse mevcut raporlardan birine (örn. Karlılık) yeni boyut eklemek (D4) yeterli.

## `cikis.aspx — Object moved`

**Modül:** Sistem

Zaten fonksiyonel karşılığı **kodda doğrulandı**: `POST /auth/logout` (`src/RentACar.Web/Identity/AuthEndpoints.cs:45-49`) cookie sign-out yapıp `/login`'e yönlendiriyor; çağrı noktası `Components/Layout/MainLayout.razor:255-257`. Canlı ekran da içerik üretmiyor (302 redirect) — bu ÇIKIŞ ekranının doğası, kıyaslanacak alan/kolon yok. Ek iş gerekmez.

## `default.aspx — TürevRent (ana sayfa BI süiti)`

**Modül:** Sistem

Canlının 24-sekmeli monolitik BI dashboard'unu (Doluluk/Şube Karşılaştırma/Gelir Raporu/vb.) tek bir sayfaya geri toplamak, halihazırda **her biri kendi modülünde ayrı, test edilmiş `/raporlar/*` ekranımız** olan raporları (bu modülün kapsamı dışında, örn. Filo Analiz, Finans Analiz) tekilleştirmek olur — mimari gerileme (modüler-rapor kararının tersine gitmek). `src/RentACar.Web/Components/Pages/Home.razor` zaten filo KPI şeridi (Doluluk/RevPACD/ADR, satır 168), 6-ay gelir trendi (satır 176-182) ve ilgili rapora hızlı-link (`/raporlar/filo-analiz`, `/raporlar/finans-analiz`, `/raporlar/periyodik-servis`) içeriyor — "hangi rapora gidilir" keşfi zaten çözülü. Şube arama kutusu + tarih-aralığı kısayolları gibi tek-tek UI kolaylıkları, ilgili `/raporlar/*` ekranının kendi parite incelemesinde ele alınmalı (bu modülün kapsamı orası değil).

## `fonksiyonlar.aspx — (başlık yok)`

**Modül:** Sistem

Canlı profilde hiçbir kanıt yok (tip=diğer, alan=0, kolon=0) — eyleme dönüştürülebilir somut bir hedef tanımlanamıyor. Kural 6'nın "bilinçli tasarım farkı" değil ama "kanıtsız → iş planına giremez" hâli. Tahmin yapılmadı; gerçek bir gap olup olmadığı ancak canlıya çerezli tarayıcı erişimiyle (bkz. kullanıcı hafızası: canlı çerez erişim yöntemi) yeniden bakılırsa netleşir — bu plan kapsamında iş kalemi ÜRETİLMEDİ.

## `globalsearch.aspx — (başlık yok)`

**Modül:** Sistem

Kodda **iki ayrı** karşılığı doğrulandı: (1) `MainLayout.razor:18-19` sol menü üstünde `GET /ara` arama kutusu ("Evrak No / Plaka ara…"), (2) `MainLayout.razor:241-246` topbar'da "Ctrl+K" kısayol ipuçlu hızlı-arama kutusu (aynı `/ara`'ya post eder). Sayfa karşılığı `src/RentACar.Web/Components/Pages/Search/Ara.razor` (plaka/cari/kira/rez/fatura no arar). Canlının header'daki AJAX arama kutusu kavramsal ve konumsal olarak **tam örtüşüyor** — parite doğrulandı, "❓ DOĞRULANAMADI" belirsizliği bu incelemeyle kapandı, iş kalemi yok.

## `kullanicilar.aspx — Kullanıcılar`

**Modül:** Sistem

Canlının hacmini oluşturan **kullanıcı-bazlı ~100 menü/izin checkbox'ı** (Dash_Doluluk, Tanimlar, Arac_Tanimla, Yeni_Kiralama, Nakit_Islemler, Web_Yoneticisi vb., her menü öğesi için ayrı toggle + "Yetki Kopyalama") bizde **bilinçli olarak** rol+yetki-matrisi modeliyle karşılanıyor (CLAUDE.md §4: `UserRole`/`Permission`/`RolePermissions` + `/yetki` ekran-bazlı override — `src/RentACar.Web/Components/Pages/Authorization/Yetki.razor`, rol-üstü sıkılaştırma, deny-by-default). Kullanıcı-bazlı yüzlerce checkbox'ı taklit etmek bu matrisin üstüne İKİNCİ, çelişebilecek bir yetki kaynağı eklemek olur — mimari gerileme. `src/RentACar.Web/Components/Pages/Users/UserList.razor` zaten CRUD+parola-sıfırlama+şube atama yapıyor, doğru örtüşüyor. Not: "Yetki Kopyalama" (rol+şube'yi bir kullanıcıdan diğerine kopyalama kısayolu) küçük bir UX kolaylığı olarak `/yetki` veya `/kullanicilar`'a ayrıca D2 eklenebilir ama bu, checkbox-modeli kararından bağımsız, isteğe bağlı bir iş — bu planda zorunlu kalem olarak YAZILMADI.

## `mobil_teslimat.aspx — Mobil Teslimatlar`

**Modül:** Sistem

Canlıdaki `Rac_Tablet_Say` (tablet sayacı) alanı doğruluyor: bu, TürevRent'in **ayrı bir native/tablet saha uygulaması** ekosistemine ait bir oturum-log tablosu (Kayıt No, Çıkış Zamanı, Teslim Zamanı, Çıkış Ofisi). Bizim mimarimiz TEK platform — responsive Blazor Server statik-SSR — tüm cihazlardan (masaüstü/tablet/telefon) AYNI kira mega-formunu (`/kiralar/{id}`) kullanır; teslim/dönüş zaman-damgaları ve çıkış ofisi zaten `RentalContract` üzerinde tutuluyor (Kira mega-form deseni, CLAUDE.md §6). Ayrı bir "tablet oturumu" tablosu eklemek, aynı gerçeği iki kaynaktan (RentalContract + ayrı oturum log'u) taşımak anlamına gelir — senkronizasyon riski yaratan bilinçli bir mimari gerileme olur. Gerçek bir native saha-app kararı gelirse (kullanıcı talebi), o zaman D7 (yeni dikey) olarak yeniden değerlendirilir.

## `web_site_yonetimi.aspx — Web Sayfanızın Yönetimi`

**Modül:** Sistem

PLAN-TALIMATI'nın kendi örneği: canlı **çok-dilli (5 dil) tam tema/CMS/SEO/menü yönetim paneli** (Web_Language_*, Web_Slider_*, Web_WhyWe_*, Web_Comment_* testimonial, Web_Ayar_Color_* tema, Web_Menu_* 12-tip menü-builder+301 yönlendirme, Web_Translate_*, sayfa-bazlı Web_Seo_*). Bizim halka-açık site modülü (`/web-sitesi` `src/RentACar.Web/Components/Pages/WebSite/WebSiteHub.razor` + `/site-icerik` `SiteIcerikYonetim.razor` + `/blog-yonetim`) **PR-0..9'da bilinçli olarak** araç-vitrini + basit statik-sayfa/SSS + blog'a daraltıldı (bkz. kullanıcı hafızası: "Public site özellik TAMAMLANDI"), tek-dilli. Çok-dilli tema motoru + sürükle-bırak menü-builder + testimonial/ slider yönetimi + sayfa-bazlı çok-dilli SEO'yu şimdi eklemek, kapsamı bilinçli daraltılmış bir modülü canlının **farklı ürün kategorisindeki** (tam CMS) haline geri büyütmek olur — mimari gerileme + orantısız efor. Kod doğrulaması: `WebSiteHub.razor` başlığı "Web Sitesi — İlanlar" (satır 24), yalnız araç ilanı odaklı; modül-kapalıysa `_modul` guard'ı sunucuda da kontrol ediyor (satır 16-20, çift-savunma deseni — `[Authorize]` yalnız rolü kontrol eder, menüyü gizlemek tek başına koruma değildir). Gerçek genişlik farkı kabul edilir, iş planına girmez.

## `para_tanimlama.aspx` — KISMİ karar (yalnız Kur alt-parçası)

**Modül:** Tanım/Master

Bu ekranın **Ülke** alanı yapılacak (D1, FAZ-20 kapsamında). Yapılmayan yalnız **Kur alanı**:

> Kur zaten AYRI ve tek kaynaktan yönetiliyor (`/kurlar`, TCMB otomatik + tenant sabit kur).
> `Currency` entity'sine statik/manuel bir `Kur` alanı eklemek **çift-kaynak** yaratır — iki yerde
> kur olur, hangisinin geçerli olduğu belirsizleşir. Para yolunda bu belirsizlik kabul edilemez.

---

## Bu kararlar nasıl gözden geçirilir

Her biri bir **varsayıma** dayanıyor (ör. "saha tableti kullanmıyoruz", "çoklu dil kapsam dışı").
Varsayım değişirse karar da değişir. Değiştirmek isteyen:

1. İlgili plan dosyasındaki gerekçeyi oku (`docs/parite/plan/`).
2. Gerekçedeki varsayımın hâlâ geçerli olup olmadığını söyle.
3. Geçerli değilse ekran normal akışa girer: desen ata → faz dosyası yaz → sıraya koy.
