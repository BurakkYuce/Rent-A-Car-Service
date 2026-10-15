# Site temaları — sözleşme

Halka açık firma sitesi üç temadan birini kullanır: `pazar`, `vitrin`, `rota`
(`TenantSettings.SiteTemasi`; null/bilinmeyen → `pazar`). Sayfalar veri mantığını paylaşır; temaya göre
**yedi slot** ve **tema css'i** değişir. Bu belge tema PR'larının uyacağı sözleşmedir.

## Anahtarlar ve eski anahtar eşlemesi

`Application/TenantSettings/SiteThemeKeys.cs` tek kaynak: `All = [pazar, vitrin, rota]`, `Default = pazar`.

| Eski anahtar | Yeni anahtar | Nerede |
|---|---|---|
| `tarife` | `pazar` | `SiteThemeKeys.LegacyAliases` (KALICI, silinmez) |
| `kontuar` | `rota` | aynı |

- `SiteThemeKeys.Normalize` eski anahtarı yeni karşılığına çevirir: `?tema=tarife` önizlemesi `pazar` basar
  (noindex dahil), ERP API'si eski anahtarla gelen PUT'u kabul eder ve **yeni anahtarı saklar**; geçerli değer
  listesi (hata mesajı, ERP seçenekleri) yalnız yeni anahtarları sayar.
- `RenameSiteThemeKeys` migration'ı kayıtlı değerleri çevirdi (NO FORCE / FORCE parantezi; Down geri çevirir).
- Sayfa kabuğu `.page[data-tema]` çözülen KANONİK anahtarı taşır (kayıt sabitinden; test ve css içindir).
- Geçiş dönemi: `pazar` ve `rota` kayıtları yeni temalar gelene dek eski Tarife / Kontuar bileşenlerini, css'ini
  ve görünen adını kullanır (`SiteThemes.Tarife` / `SiteThemes.Kontuar` alanları). Yeni tema PR'ı alanı,
  klasörü ve css'i yeniden adlandırır.

## Seçim ve önizleme

- ERP: `/app/ayarlar` → "Web sitesi görünümü" → Site teması (radyo) + her tema için "Önizle" bağlantısı.
- Sitede `?tema=<ad>` geçerli bir adsa (eski anahtar dahil) o istekte o tema basılır; kalıcı değildir (çerez
  yok), yanıt `X-Robots-Tag: noindex, follow` taşır, kanonik adres sorgusuz olduğundan değişmez. Geçersiz ad
  yok sayılır.
- Çözüm tek yerde: `ThemeAccessor` (istek başına bir kez).

## Kayıt

`SiteThemes.cs`:

```csharp
public sealed record SiteTheme(string Key, string Ad, string Aciklama, string CssPath,
    IReadOnlyList<string> FontPreloads, Type Hero, Type Card, Type Header, Type Footer,
    Type Home, Type Fleet, Type DetailHero)
{
    IReadOnlyList<ThemeFont> Fonts { get; init; }   // @font-face (App.razor parmak izli adresle basar)
    string DefaultBrand { get; init; }              // firma renk seçmediyse vurgu (#rrggbb)
    string ThemeColorLight / ThemeColorDark         // <meta name="theme-color">
}
SiteThemes.Resolve(key)  // eski anahtar → yeni; boş/bilinmeyen → SiteThemeKeys.Default
```

## Slotlar ve dağıtım

Her slot `DynamicComponent` ile aktif temanın bileşenine dağıtılır; sarmalayıcılar `Components/Themes/Themed*.razor`.

| Slot | Kim basar | Sarmalayıcı | Parametre |
|---|---|---|---|
| Header, Footer | kabuk (`Layout/MainLayout.razor`) | doğrudan `DynamicComponent` | aşağıdaki tablo |
| Home | `Pages/Home.razor` | `ThemedHome` | `Model` : `HomeModel` |
| Fleet | Home slotu (filo bölümü), `/musaitlik`, ilan detayındaki benzer araçlar, 404 | `ThemedFleet` | `Model` : `FleetModel` |
| DetailHero | `Pages/IlanDetay.razor` | `ThemedDetailHero` | `Model` : `DetailHeroModel` |
| Hero | Home slotu (temanın tercihi) | `ThemedHero` | aşağıdaki tablo |
| Card | Fleet slotu (temanın tercihi) | `ThemedCard` | aşağıdaki tablo |

Veri mantığı SAYFADA kalır (servis çağrıları, sıralama, işaret, teklif bağlantısı, metin/sayı hesapları);
slotlar yalnız sunar. SEO (`PageTitle`, `SayfaMeta`, JSON-LD, breadcrumb) sayfada kalır, temaya taşınmaz.

### Model slotlarının imzaları (SABİT — `SlotModels.cs`)

```csharp
enum FleetPlacement { Home, Availability, Similar, NotFound }
record FleetSummary(string Vurgu, string Aciklama, bool Canli = false);           // Canli → role="status"
record FleetEmptyState(string Baslik, string Metin, IReadOnlyList<SiteLink> Linkler, bool Canli = false);
record FleetModel(IReadOnlyList<ListingCard> Kartlar, FleetPlacement Yer)
{
    StayDates? Tarih;          // tüm kartlar için aynı aralık; null → tarihsiz ("müsait" DENMEZ)
    string? Baslik, BaslikId;  // h2 + id (yalnız Home kullanır; diğer yerlerde başlık sayfada)
    FleetSummary? Ozet;        // yalnız kart varken
    string? Uyari;             // yalnız kart varken (ör. "X bu tarihlerde müsait değil")
    FleetEmptyState? Bos;      // kart yokken; null → hiçbir şey basılmaz (T2)
}

record HomeModel(FleetBranding? Branding, IReadOnlyList<FleetShowcaseCard> Kartlar, FleetModel Filo,
    IReadOnlyList<SssSatiri> Sss, int SssAdet, IReadOnlyList<string> SubeAdlari, IReadOnlyList<Branch> Subeler,
    IReadOnlyList<SiteLink> Sayfalar, string? Sehir, string? KosulSayfasi) { string? Marka; }

record DetailRate(string Etiket, decimal Tutar, decimal? GunBasi);             // GunBasi null → günlük satır
record DetailHeroModel(FleetShowcaseDetail Ilan, IReadOnlyList<DetailRate> Tarife, string TeklifLinki,
    IReadOnlyList<string> SubeAdlari) { string? MetaSatiri; string? WaLinki; FleetBranding? Branding; string KdvMetni; }
```

| Slot | Kurallar |
|---|---|
| Home | Bölümleri tema seçer ve sıralar; ortak bölümler (`BolumGuvenSeridi`, `BolumNasilCalisir`, `BolumFiyatNasil`, `BolumSss`, `BolumCta`, `AramaFormu`, `BrandMark`) istenirse kullanılır. **Sarmalayıcı öğe YOK**: bölümler doğrudan `<main>`'in çocuğudur (base.css `main > .cta-bant:last-child`). SSS basılıyorsa `BolumSss Adet="Model.SssAdet"` — görünen soru adedi FAQPage JSON-LD adediyle aynı kalır (SEO L-4; render testi kilitler). Filo bölümü `Model.Filo.Kartlar` boşsa basılmaz (T2). Tek `h1` Hero'dadır. |
| Fleet | Kapsayıcıyı (`wrap`, `section`) ÇAĞIRAN verir; slot başlık (varsa), özet, uyarı ve listeyi ya da boş durumu basar. `Model.Yer` ile yerleşim seçilebilir (ör. Home'da yatay şerit, `Availability`'de ızgara). Kart başına `Model.Tarih` Card slotuna aynen geçer. İlk kartın `Oncelikli` işareti korunur (LCP). |
| DetailHero | Kendi kapsayıcısını (`wrap`) basar — tam genişlik galeri mümkün. Tek `h1` (ilan adı) buradadır. Tarife satırları ve gün başı yuvarlaması sayfadan gelir; tema hesap yapmaz. Tarih formu ORTAK `<AramaFormu Hedef="/musaitlik" Ilan="@Model.Ilan.Slug" …/>`. Kapak LCP: tembel değil, `fetchpriority="high"`, boyut öznitelikleri. |

### Parametreli slotların imzaları (SABİT)

Parametre adları ve tipleri değişmez:

| Slot | Parametre | Tip | Anlam |
|---|---|---|---|
| Hero | `Marka` | `string?` | Marka adı (boşsa "Filomuzdaki") |
| | `Branding` | `FleetBranding?` | Firma bilgisi (iletişim, adres, logo sürümü) |
| | `Subeler` | `IReadOnlyList<string>` | Aktif şube adları (arama formu) |
| | `Kartlar` | `IReadOnlyList<FleetShowcaseCard>` | Yayındaki ilanlar |
| | `Sehir` | `string?` | Hizmet bölgesi ("Esenyurt, İstanbul"); null → basılmaz |
| Card | `Kart` | `ListingCard` | Kartın tüm verisi (fiyat, hesap, teklif bağlantısı, işaret, öncelik) |
| | `Tarih` | `StayDates?` | Seçilen aralık (Bas, Bit, Gun); null → tarihsiz gezinti |
| Header | `Branding` | `FleetBranding?` | Firma bilgisi; iletişim için `ContactLinks.From(Branding)` |
| | `Nav` | `IReadOnlyList<SiteLink>` | Ana menü ("Ana sayfa" hariç) |
| Footer | `Branding` | `FleetBranding?` | Firma bilgisi |
| | `Sayfalar` | `IReadOnlyList<SiteLink>` | Ana sayfa + menü + firmanın içerik sayfaları |
| | `YasalSayfalar` | `IReadOnlyList<SiteLink>` | Yayındaki yasal sayfalar (boşsa sütun yok) |

Kurallar (tüm slotlar): arama için ORTAK `<AramaFormu>` (alan adları/doğrulama tek kaynak, T4); firma işareti
için ORTAK `<BrandMark>`; fiyat için `PriceText`. `Kart.Gun is null` / `Model.Tarih is null` iken "müsait"
DENMEZ (T1). Veri yoksa bölüm basılmaz (T2). Firma adına taahhüt metni yazılmaz (T1). JS eklenmez (CSP
`script-src 'self'`). Ham `FleetBranding.VurguRengi` hiçbir yere basılmaz.

Slot dışı kalan sayfalar (iletişim, SSS, blog, içerik sayfası, talep formu, 404 metni) ORTAK markup'tır; temaya
göre yalnız CSS ile değişir.

## Yeni tema ekleme / değiştirme kılavuzu

1. Anahtar: `SiteThemeKeys` (`All` sırası = ERP seçenek sırası). Var olan anahtarın yerine geçen tema anahtarı
   DEĞİŞTİRMEZ; anahtar değişirse eskisi `LegacyAliases`'a eklenir + veri migration'ı (bkz. `RenameSiteThemeKeys`).
2. Bileşenler: `Components/Themes/<Ad>/` altında yedi slot — `<Ad>Hero`, `<Ad>Card`, `<Ad>Header`, `<Ad>Footer`,
   `<Ad>Home`, `<Ad>Fleet`, `<Ad>DetailHero`. Parametreler yukarıdaki imzalarla BİREBİR (`[Parameter]` adları).
   Home/Fleet, Hero/Card'ı `ThemedHero`/`ThemedCard` ile ya da kendi bileşenini doğrudan çağırarak kullanabilir.
3. CSS: `wwwroot/css/tema-<ad>.css` (katman kuralları aşağıda); yazı tipleri `wwwroot/fonts/` (woff2, latin +
   latin-ext, kendi barındırılır, lisans dosyası).
4. Kayıt: `SiteThemes` alanı + `All` listesi; anahtarlar `SiteThemeKeys.All` ile aynı sırada (yapısal test).
5. ERP: `src/RentACar.Frontend/src/app/features/system/settings/settings-model.ts` `SITE_THEMES` + i18n
   `sistem.ayarlar.tema.<anahtar>.{ad,aciklama}` (`npm run i18n:tipler`).
6. Doğrulama: `PublicSiteThemeContractTests*` (token, kontrast, 3 tema × adres render, SSS adedi), 1440/390
   ekran görüntüsü, `prefers-reduced-motion`.

## CSS katmanları

- `wwwroot/css/base.css` — reset, düzen, erişilebilirlik, ortak bileşenlerin YAPISI. YALNIZ semantik ve
  bileşen token'ı kullanır; hex/rgb/hsl renk literali YASAK.
- `wwwroot/css/tema-<ad>.css` — dosyanın İLK `:root { }` bloğu **primitive**'ler (renk literalleri YALNIZ
  burada), ardından `:root` semantik + bileşen token'ları, `@media (prefers-color-scheme: dark)` semantik
  ezmeleri (yalnız primitive'lere referans), sonra temaya özgü slot stilleri.
- Sayfaya yalnız `base.css` + aktif tema css'i yüklenir (parmak izli). BrandPalette çıktısı tema css'inden
  SONRA basılır: `--brand`, `--on-brand`, `--brand-text`, `--brand-soft` her temada firmanın rengidir.
  Tema `--brand-ink: var(--on-brand)` tanımlar (firma rengi üstündeki yazı).

### Semantik token'lar (her tema HEPSİNİ tanımlar)

`--bg --bg-alt --surface --surface-raised --ink --ink-muted --line --brand --brand-ink --brand-text
--accent --accent-ink --focus --danger --ok --font-display --font-body --step--1 --step-0 --step-1
--step-2 --step-3 --step-4 --step-5 --space-1 … --space-8 --radius-s --radius-m --radius-l --shadow-1
--shadow-2 --measure`

base.css ayrıca şunları kullanır — her tema bunları da tanımlar: `--ink-subtle` (ipucu metni, AA),
`--line-strong` (form/panel çerçevesi), `--on-brand` ve `--brand-soft` (BrandPalette ezer; tema varsayılanı).

### Bileşen token'ları

`--btn-bg --btn-ink --btn-radius --card-bg --card-radius --card-border --field-bg --field-border
--price-ink --price-mark`

### Güvenlik

`FleetBranding.VurguRengi` ham DB değeridir; ASLA doğrudan stile/özniteliğe basılmaz — yalnız
`BrandPalette.Css(...)` çıktısı basılır (CSP `style-src 'unsafe-inline'` olduğundan tek savunma bu).

## Yapısal testler

`PublicSiteThemeContractTests`: base.css'te renk literali yok; her tema tüm token'ları tanımlar; literaller
yalnız primitive bloğunda; kayıt = `SiteThemeKeys.All`, her temanın yedi slot tipi dolu ve `[Parameter]`
imzaları sözleşmeyle aynı; eski anahtar eşlemesi; 3 tema × (/, /musaitlik, tarihli arama, yayındaki ilan
detayı, /iletisim, yayında olmayan ilan 404, eşleşmeyen adres 404) durum kodu + `data-tema` + satır içi betik
yok + dış kaynak yok + görünen SSS adedi = FAQPage adedi; önizleme noindex (`?tema=tarife` → `pazar`);
`--ink`/`--bg` ve `--brand-ink`/`--brand` AA (bağımsız hesap). Migration: `SiteThemeKeyMigrationTests`.
