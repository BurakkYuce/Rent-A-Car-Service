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
- `pazar` kendi temasında (`SiteThemes.Pazar`, `Themes/Pazar/`, `css/tema-pazar.css`; eski Tarife dosyaları ve
  Barlow yazı tipleri silindi). Geçiş dönemi: `rota` kaydı yeni Rota teması gelene dek eski Kontuar bileşenlerini,
  css'ini ve görünen adını kullanır (`SiteThemes.Kontuar` alanı); o PR alanı, klasörü ve css'i yeniden adlandırır.
- Pazar'ın kendi bileşeni `PazarPriceTag` (asılı fiyat etiketi — temanın imzası) slot değildir; Card, Hero ve
  DetailHero kullanır. Eski tarife'nin geçici palet setleri (`petrol`, `lacivert`, `zeytin`) Pazar setleriyle
  (`domates` varsayılan, `kobalt`, `hardal`, `cam`, `patlican`) değişti; kayıtlı eski anahtar varsayılana düşer.

## Seçim ve önizleme

- ERP: `/app/ayarlar` → "Web sitesi görünümü" → Site teması (radyo) + Renk seti (seçili temanın setleri,
  renk çipli kartlar; tema değişince temaya ait olmayan seçim temizlenir) + Kendi ana rengim (renk seçici +
  `#rrggbb`, temizlenebilir) + "Seçili görünümü önizle" ve tema başına "Önizle" bağlantıları.
- Önizleme parametreleri (`SitePreview`): `?tema=<anahtar>` (eski anahtar dahil), `?palet=<set anahtarı>`,
  `?renk=%23rrggbb` (yalnız KESİN `#rrggbb`). Geçerli olan o istekte basılır; kalıcı değildir (çerez yok), yanıt
  `X-Robots-Tag: noindex, follow` taşır, kanonik adres sorgusuz olduğundan değişmez, 404 sayfası geçerli
  önizlemeyi taşır. Geçersiz değer yok sayılır (o parametre hiç yokmuş gibi). Palet, çözülen temanın setlerinde
  aranır; başka temanın seti varsayılana düşer.
- Çözüm tek yerde: `ThemeAccessor.GetAppearanceAsync()` → `SiteAppearance(Theme, Palette, CustomPrimary)`
  (istek başına bir kez). Öncelik her eksende: önizleme → firmanın kaydı (`SiteTemasi` / `SitePaleti` /
  `SiteVurguRengi`) → temanın varsayılanı.

## Kayıt

`SiteThemes.cs`:

```csharp
public sealed record SiteTheme(string Key, string Ad, string Aciklama, string CssPath,
    IReadOnlyList<string> FontPreloads, Type Hero, Type Card, Type Header, Type Footer,
    Type Home, Type Fleet, Type DetailHero)
{
    IReadOnlyList<ThemeFont> Fonts { get; init; }   // @font-face (App.razor parmak izli adresle basar)
    required IReadOnlyList<SitePalette> Palettes    // hazır renk setleri; İLK = varsayılan (bkz. "Palet sistemi")
    SitePalette DefaultPalette => Palettes[0]       // <meta name="theme-color"> da setin zemininden gelir
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
  burada), ardından `:root` semantik + bileşen token'ları, `@media (prefers-color-scheme: dark)` ezmeleri,
  sonra temaya özgü slot stilleri.
- Sayfaya yalnız `base.css` + aktif tema css'i yüklenir (parmak izli). **Palet bloğu** (`SitePaletteCss`
  çıktısı) tema css'inden SONRA basılır ve aşağıdaki **palet token'larının** TAMAMINI (açık + koyu) ezer.
  Tema css'indeki aynı adlı tanımlar yalnız YEDEKTİR (blok basılamazsa) ve varsayılan setle aynı olmalı
  (yapısal test). Tema `--brand-ink: var(--on-brand)` tanımlar (ana renk üstündeki yazı).
- **Primitive kuralı:** primitive bloğundan sonra `var(--p-…)` YALNIZ `:root`'taki palet token yedeklerinde ve
  gerekçeli muafiyetlerde (`--danger --ok` durum renkleri; koyu moddaki siyah `--shadow-1 --shadow-2
  --ticket-drop`; `--mask-on --mask-off` alfa maskesi) geçebilir. Slot kuralları ve bileşen token'ları
  (`--board-*`, `--stage-*`, `--logo-plate`, `--field-bg`, `--line-soft` …) semantik token'a bağlanır —
  `color-mix(in oklab|srgb, …)` serbest — ki palet her şeyi değiştirsin. Açık moddaki gölgeler mürekkep tonunda:
  `color-mix(in srgb, var(--ink) 30%, transparent)`.

### Palet token'ları (palet bloğu basar; tema css'i yedeğini tanımlar)

Şema başına (açık `:root`, koyu `@media (prefers-color-scheme:dark)`): `--bg --bg-alt --surface
--surface-raised --ink --ink-muted --ink-subtle --line --line-strong --brand --on-brand --brand-text
--brand-soft --accent --accent-ink`.

Şemadan bağımsız (yalnız açık blokta; koyu mod da AYNI değeri görür): `--day-bg` (açık şemanın zemini — koyu
modda logo levhası), `--day-ink` (açık şemanın mürekkebi — iki modda sabit koyu levha/pano), `--night-bg` (koyu
şemanın zemini — sahne gölgesi), `--night-ink` (koyu şemanın mürekkebi — sabit koyu levha üstündeki yazı).

Kontrast güvencesi (üretici, tutmayan değeri zıt uca %2 adımla kaydırır; testler bağımsız WCAG hesabıyla
ölçer): metin 4.5:1 — ink / bg, surface, bg-alt, surface-raised; ink-muted ve ink-subtle / bg, surface, bg-alt;
brand-text / bg, surface, brand-soft; on-brand / brand; accent-ink / accent. Dolgu 3:1 — brand / bg, surface.
Setler için ayrıca night-ink / day-ink 4.5:1.

### Diğer semantik token'lar (her tema HEPSİNİ tanımlar)

`--brand-ink --focus --danger --ok --font-display --font-body --step--1 --step-0 --step-1 --step-2 --step-3
--step-4 --step-5 --space-1 … --space-8 --radius-s --radius-m --radius-l --shadow-1 --shadow-2 --measure`
(`--focus` = `var(--ink)`; `--danger`/`--ok` her setin zemininde AA olmalı — test).

### Bileşen token'ları

`--btn-bg --btn-ink --btn-radius --card-bg --card-radius --card-border --field-bg --field-border
--price-ink --price-mark` (+ temanın kendi bileşen token'ları; hepsi semantik token'a bağlı).

### Güvenlik

`FleetBranding.VurguRengi`, `FleetBranding.Paleti` ve `?palet=` / `?renk=` ham değerdir; ASLA doğrudan
stile/özniteliğe basılmaz — yalnız `SitePaletteCss` çıktısı basılır (renkler `Rgb`'den yeniden yazılır, palet
anahtarı yalnız kayıt aramasında kullanılır). CSP `style-src 'unsafe-inline'` olduğundan tek savunma bu.

## Palet sistemi — tema PR'ları için set tanımlama kılavuzu

1. Setler temanın klasöründe: `Components/Themes/<Ad>/<Ad>Palettes.cs` →
   `public static readonly IReadOnlyList<SitePalette> All = [...]`; kayıtta `Palettes = <Ad>Palettes.All`.
   İLK set varsayılandır. 4–5 set hedeflenir.
2. Set: `new SitePalette("anahtar", "Görünen ad", Light, Dark)`; anahtar `^[a-z0-9-]{1,30}$`, tema içinde
   tekil, KALICI (DB + adres). Şema: `new SchemeColors(Bg, Surface, Ink, Primary, Accent)` — hepsi küçük harf
   `#rrggbb`. `Accent` null → vurgu ana rengi izler (firma kendi rengini seçince o da değişir). Açık şemanın
   zemini mürekkepten açık, koyu şemanınki koyu olmalı (test). Her şema kendi `Primary`'sini taşır;
   firmanın "kendi ana rengim"i ikisini de ezer.
3. Türetilen tonlar (ikinci zemin, yükseltilmiş yüzey, ikincil/ipucu metni, çizgiler, hafif ton, vurgu üstü
   yazı) otomatik. Elle ince ayar istenirse init özellikleri: `BgAlt SurfaceRaised InkMuted InkSubtle Line
   LineStrong AccentInk` — bunlar da güvenceden geçer (tutmayan değer düzeltilir, basılmaz).
4. Aynı satırı `Application/TenantSettings/SitePaletteKeys.cs` → `ByTheme[<tema anahtarı>]`'na ekle:
   `new(Key, Ad, Zemin = Light.Bg, Murekkep = Light.Ink, Ana = Light.Primary, Vurgu = Light.Accent, GeceZemin =
   Dark.Bg)` — ERP kartları ve API aitlik doğrulaması bunu kullanır. `Palet_setleri_ve_ayar_anahtarlari_ayni`
   uyuşmazlıkta beklenen satırı yazar.
5. Tema css'indeki palet token yedekleri varsayılan setin değerleriyle aynı olmalı
   (`Tema_css_yedekleri_varsayilan_setle_ayni`); `--day-*`/`--night-*` yedekleri de.
6. Doğrulama: `SitePaletteCssTests` (her set × iki şema AA, 216 renk ızgarası özel ana renk olarak her setin
   zemininde), `PublicSiteThemeContractTests` (primitive kuralı, durum renkleri her sette AA). Ekran görüntüsü
   için `?tema=<ad>&palet=<set>&renk=%23rrggbb`.

## Yapısal testler

`PublicSiteThemeContractTests`: base.css'te renk literali yok; her tema tüm token'ları tanımlar; literaller
yalnız primitive bloğunda; primitive referansı yalnız palet yedeklerinde/muafiyetlerde; kayıt =
`SiteThemeKeys.All`, her temanın yedi slot tipi dolu ve `[Parameter]` imzaları sözleşmeyle aynı; eski anahtar
eşlemesi; palet setleri ↔ `SitePaletteKeys` birebir; css yedekleri = varsayılan set; durum renkleri her sette
AA; 3 tema × (/, /musaitlik, tarihli arama, yayındaki ilan detayı, /iletisim, yayında olmayan ilan 404,
eşleşmeyen adres 404) durum kodu + `data-tema` + satır içi betik yok + dış kaynak yok + görünen SSS adedi =
FAQPage adedi; önizleme (`?tema` — `?tema=tarife` → `pazar` —, `?palet`, `?renk`) noindex + geçersiz değer yok
sayılır + 404 taşır; kayıtlı set/renk basılır, temaya ait olmayan set varsayılana düşer. Migration:
`SiteThemeKeyMigrationTests`. `SitePaletteCssTests`: üreticinin AA değişmezleri (her tema × her set × iki
şema, 216 renk ızgarası), varsayılan setlerin bugünkü renkleri, enjeksiyon denemeleri varsayılana düşer, çıktı
yalnız `#rrggbb`.
