# Site temaları — sözleşme

Halka açık firma sitesi üç temadan birini kullanır: `tarife`, `vitrin`, `kontuar`
(`TenantSettings.SiteTemasi`; null/bilinmeyen → `tarife`). Sayfalar veri mantığını paylaşır; temaya göre
yalnız **dört slot** ve **tema css'i** değişir. Bu belge tema PR'larının uyacağı sözleşmedir.

## Seçim ve önizleme

- ERP: `/app/ayarlar` → "Web sitesi görünümü" → Site teması (radyo) + Renk seti (seçili temanın setleri,
  renk çipli kartlar; tema değişince temaya ait olmayan seçim temizlenir) + Kendi ana rengim (renk seçici +
  `#rrggbb`, temizlenebilir) + "Seçili görünümü önizle" ve tema başına "Önizle" bağlantıları.
- Önizleme parametreleri (`SitePreview`): `?tema=<anahtar>`, `?palet=<set anahtarı>`, `?renk=%23rrggbb`
  (yalnız KESİN `#rrggbb`). Geçerli olan o istekte basılır; kalıcı değildir (çerez yok), yanıt
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
    IReadOnlyList<string> FontPreloads, Type Hero, Type Card, Type Header, Type Footer)
{
    IReadOnlyList<ThemeFont> Fonts { get; init; }   // @font-face (App.razor parmak izli adresle basar)
    required IReadOnlyList<SitePalette> Palettes    // hazır renk setleri; İLK = varsayılan (bkz. "Palet sistemi")
    SitePalette DefaultPalette => Palettes[0]       // <meta name="theme-color"> da setin zemininden gelir
}
SiteThemes.Resolve(key)  // boş/bilinmeyen → Tarife
```

Yeni tema: `Components/Themes/<Ad>/` (4 slot bileşeni) + `wwwroot/css/tema-<ad>.css` + yazı tipleri
`wwwroot/fonts/` (woff2, latin + latin-ext, kendi barındırılır) + `SiteThemes` kaydı. Anahtar listesi
`Application/TenantSettings/SiteThemeKeys.cs` ile aynı olmalı (yapısal test).

## Slot imzaları (SABİT)

Sayfalar slotları `ThemedHero` / `ThemedCard` sarmalayıcılarıyla, kabuk (MainLayout) Header/Footer'ı
`DynamicComponent` ile basar. Parametre adları ve tipleri değişmez:

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

Kurallar: Hero arama için ORTAK `<AramaFormu Hedef="/musaitlik" Subeler="Subeler" />`'u kullanır; firma
işareti için ORTAK `<BrandMark>`; fiyat için `PriceText`. Card'da `Kart.Gun is null` iken "müsait" DENMEZ
(T1). Veri yoksa bölüm basılmaz (T2). Firma adına taahhüt metni yazılmaz (T1). JS eklenmez.

Diğer bölümler (SSS, nasıl çalışır, fiyat açıklaması, kapanış bandı, formlar, detay, müsaitlik, blog)
ORTAK markup'tır; temaya göre yalnız CSS ile değişir.

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
yalnız primitive bloğunda; primitive referansı yalnız palet yedeklerinde/muafiyetlerde; palet setleri ↔
`SitePaletteKeys` birebir; css yedekleri = varsayılan set; durum renkleri her sette AA; 3 tema × (/, /musaitlik,
ilan detay, /iletisim, 404) durum kodu + satır içi betik yok + dış kaynak yok; önizleme (`?tema`, `?palet`,
`?renk`) noindex + geçersiz değer yok sayılır + 404 taşır; kayıtlı set/renk basılır, temaya ait olmayan set
varsayılana düşer. `SitePaletteCssTests`: üreticinin AA değişmezleri, varsayılan setlerin bugünkü renkleri,
enjeksiyon denemeleri varsayılana düşer, çıktı yalnız `#rrggbb`.
