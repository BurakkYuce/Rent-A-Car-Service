# Site temaları — sözleşme

Halka açık firma sitesi üç temadan birini kullanır: `tarife`, `vitrin`, `kontuar`
(`TenantSettings.SiteTemasi`; null/bilinmeyen → `tarife`). Sayfalar veri mantığını paylaşır; temaya göre
yalnız **dört slot** ve **tema css'i** değişir. Bu belge tema PR'larının uyacağı sözleşmedir.

## Seçim ve önizleme

- ERP: `/app/ayarlar` → "Web sitesi görünümü" → Site teması (radyo) + her tema için "Önizle" bağlantısı.
- Sitede `?tema=<ad>` geçerli bir adsa o istekte o tema basılır; kalıcı değildir (çerez yok), yanıt
  `X-Robots-Tag: noindex, follow` taşır, kanonik adres sorgusuz olduğundan değişmez. Geçersiz ad yok sayılır.
- Çözüm tek yerde: `ThemeAccessor` (istek başına bir kez).

## Kayıt

`SiteThemes.cs`:

```csharp
public sealed record SiteTheme(string Key, string Ad, string Aciklama, string CssPath,
    IReadOnlyList<string> FontPreloads, Type Hero, Type Card, Type Header, Type Footer)
{
    IReadOnlyList<ThemeFont> Fonts { get; init; }   // @font-face (App.razor parmak izli adresle basar)
    string DefaultBrand { get; init; }              // firma renk seçmediyse vurgu (#rrggbb)
    string ThemeColorLight / ThemeColorDark         // <meta name="theme-color">
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
yalnız primitive bloğunda; 3 tema × (/, /musaitlik, ilan detay, /iletisim, 404) durum kodu + satır içi betik
yok + dış kaynak yok; önizleme noindex; `--ink`/`--bg` ve `--brand-ink`/`--brand` AA (bağımsız hesap).
