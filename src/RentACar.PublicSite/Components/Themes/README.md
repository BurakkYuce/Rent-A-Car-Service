# Site temaları — sözleşme

Halka açık firma sitesi üç temadan birini kullanır: `anahtar` (varsayılan), `kiyas`, `paket`
(`TenantSettings.SiteTemasi`; null/bilinmeyen → `anahtar`). Sayfalar veri mantığını paylaşır; temaya göre
**yedi slot** ve **tema css'i** değişir. Bu belge tema PR'larının uyacağı sözleşmedir.

## Anahtarlar ve eski anahtar eşlemesi

`Application/TenantSettings/SiteThemeKeys.cs` tek kaynak: `All = [anahtar, kiyas, paket]`, `Default = anahtar`.

| Eski anahtar | Yeni anahtar | Nerede |
|---|---|---|
| `pazar`, `tarife` | `anahtar` | `SiteThemeKeys.LegacyAliases` (KALICI, silinmez; zincir yok — doğrudan bugünkü tema) |
| `rota`, `kontuar` | `kiyas` | aynı |
| `vitrin` | `paket` | aynı |

- `SiteThemeKeys.Normalize` eski anahtarı yeni karşılığına çevirir: `?tema=vitrin` önizlemesi `paket` basar
  (noindex dahil), ERP API'si eski anahtarla gelen PUT'u kabul eder ve **yeni anahtarı saklar**; geçerli değer
  listesi (hata mesajı, ERP seçenekleri) yalnız yeni anahtarları sayar.
- Migration'lar: `RenameSiteThemeKeys` (tarife → pazar, kontuar → rota) ve T4 `RetireOldSiteThemes` (Pazar, Rota,
  Vitrin söküldü: pazar/tarife → anahtar, rota/kontuar → kiyas, vitrin → paket; kayıtlı palet yeni temanın setlerinden
  değilse NULL → temanın varsayılan seti; Down no-op). NO FORCE / FORCE parantezi.
- Sayfa kabuğu `.page[data-tema]` çözülen KANONİK anahtarı taşır (kayıt sabitinden; test ve css içindir).

## Temalar

| Anahtar | Ad | Kimlik | İmza anı | Yazı |
|---|---|---|---|---|
| `anahtar` | Anahtar | VARSAYILAN. İletişim önce, telefonla/WhatsApp'la çalışan yerel firma. Kâğıt beyazı zemin, firma rengi yalnız düğme/bağlantı/küçük işarette, 1 px çizgi, gölge yok. Hero fotoğrafsız: firma adı (h1) + hizmet bölgesi, güven satırı (kuruluş yılı, Google puanı profile bağlı, "Bugün 08:00–21:00 açık" — `BranchOffices.TodayHours`, yalnız kesin okunabilen saat), WhatsApp (sabit koyu yeşil `--wa-bg`, beyaz yazı AA) + Ara, "tezgâh" (kompakt arama + depozito/km/yaş çipleri). Filo kartı ızgarada dikey, müsaitlikte yatay; fotoğrafsız ilanda silüet. "Kiralamadan önce" (firma beyanları satır satır), Google yorum bandı, ofisler (bugünkü saat), SSS. Detay: mobil ilk ekranda fiyat + depozito/km/iptal + Rezervasyon talebi / WhatsApp'tan sor. Boş bilgi alanı hiçbir öğe üretmez. Setler: `zumrut` (varsayılan), `lacivert`, `visne`, `grafit`. | Yok — açılış animasyonu bilinçli olarak yok (kullanıcı kararı: arama formu ilk karede kullanılabilir olmalı) | Plus Jakarta Sans 700–800 + Public Sans 400–600 |
| `kiyas` | Kıyas | Liste ve şeffaf fiyat önce; havalimanı/turist firması, çok sınıflı filo. Serin gri zemin, beyaz kartlar, 1 px çizgi, 8 px köşe. Hero: bölge başlığı + künye (ad, yıl, puan) + tam genişlik arama + veriden doğan şeffaflık satırları ("Fiyatlar KDV dahil" yalnız tüm ilanlar dahilse, depozitosuz/sınırsız km/iptal firmanın beyanı). Ana sayfada süzgeç boyutu başına "en düşük günlük fiyat" kutucukları (`/musaitlik?vites=…` GET). /musaitlik: solda JS'siz GET süzgeci (`FleetModel.Filtre`; telefonda listenin üstünde), kartta fiyat (toplam + gün × fiyat dökümü) adla aynı blokta ve en üstte, altında teknik satır (vites, yakıt, koltuk) ve koşul rozetleri; ana sayfada kartlar hero'nun hemen altında (1440'ta ilk ekranda fiyat görünür), (yeşil müşteri lehine, kehribar şart, gri bilgi — `--rozet-*` sabit, AA test), gün × fiyat dökümü. Görseller tek tip açık gri 4:3 kutuda `contain`. Detay: "Fiyata dahil" / "Şartlar", yapışkan tarife paneli. Setler: `mavi` (varsayılan), `turkuaz`, `kiremit`, `gece`. | Yok — açılış animasyonu yok; tek belirgin unsur eş genişlikli fiyat dökümü | IBM Plex Sans 400–600 + IBM Plex Mono 500 (yalnız fiyat/döküm) |
| `paket` | Paket | Aylık/uzun dönem ve kurumsal; sade, tipografik. Kâğıt zemin, ince ayraç, 4 px köşe, gölge yok; firma rengi tek vurgu. Hero: geniş başlık, en düşük AYLIK fiyat (yalnız ilanda girilmiş `AylikToplam`'dan; yoksa günlük — aylık fiyat uydurulmaz), araçlar/teklif, tarihle toplam formu. İki sütun büyük kart (aylık varsa "/ ay" büyük + günlük küçük). "Aylık fiyatlar" tablosu (30 gün / günlük; aylığı olmayan ilanda "Teklif alın"), kiralama koşulları, KOYU BANT kurumsal teklif (`id="kurumsal"`, `--day-ink` üstünde `--day-bg`): mevcut talep formuna GET ile geçer (`/rezervasyon-talebi?bas&bit&kurumsal=1` — serbest metin adrese GİRMEZ (KVKK), not talep sayfasının POST formunda yazılır; `kurumsal=1` başlığı ve not ipucunu değiştirir; yeni POST ucu yok). Fotoğrafsız ilanda tipografik "tabela". Detay: 16:10 görsel, büyük rakamlı tarife (Süre/Tutar/Gün başı), yapışkan panelde öne çıkan fiyat (aylık satır varsa o). Setler: `kiremit` (varsayılan), `lacivert`, `zeytin`, `grafit` (vurgu ana rengi izler). | Yok — animasyon yok; tek belirgin unsur büyük rakam | Paket Display 600–700 (Mona Sans'tan, genişlik 112; OFL ayrılmış adı "Mona" değiştirilmiş sürümde kullanılamadığından aile adı değişti) + Inter 400–500 |

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
public sealed record SiteTheme(string Key, string Name, string Description, string CssPath,
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
| DetailHero | `Pages/ListingDetail.razor` | `ThemedDetailHero` | `Model` : `DetailHeroModel` |
| Hero | Home slotu (temanın tercihi) | `ThemedHero` | aşağıdaki tablo |
| Card | Fleet slotu (temanın tercihi) | `ThemedCard` | aşağıdaki tablo |

Veri mantığı SAYFADA kalır (servis çağrıları, sıralama, işaret, teklif bağlantısı, metin/sayı hesapları);
slotlar yalnız sunar. SEO (`PageTitle`, `PageMeta`, JSON-LD, breadcrumb) sayfada kalır, temaya taşınmaz.

Ad kuralı: sınıf/özellik/parametre adları İngilizce; yalnız domain/DB alanını birebir yansıtan adlar Türkçe kalır
(`Baslik`, `GunlukFiyat`, `KdvDahil`, `AylikToplam`, `HaftalikToplam`, `SiteFacts` alanları, `FeatureItem.Etiket/Deger`)
ve form/URL alan adları (`bas`, `bit`, `basSaat`, `sube`, `ilan`, `kurumsal`).

### Model slotlarının imzaları (SABİT — `SlotModels.cs`)

```csharp
enum FleetPlacement { Home, Availability, Similar, NotFound }
record FleetSummary(string Headline, string Description, bool Live = false);       // Live → role="status"
record FleetEmptyState(string Title, string Text, IReadOnlyList<SiteLink> Links, bool Live = false);
record FleetModel(IReadOnlyList<ListingCard> Cards, FleetPlacement Placement)
{
    StayDates? Dates;          // tüm kartlar için aynı aralık; null → tarihsiz ("müsait" DENMEZ)
    string? Title, TitleId;    // h2 + id (yalnız Home kullanır; diğer yerlerde başlık sayfada)
    FleetSummary? Summary;     // yalnız kart varken
    string? Notice;            // yalnız kart varken (ör. "X bu tarihlerde müsait değil")
    FleetEmptyState? Empty;    // kart yokken; null → hiçbir şey basılmaz (T2)
    SiteFacts? Facts;          // firma beyanları (Home ve /musaitlik doldurur); tema isterse özet/çip basar
    FleetFilterModel? Filter;  // yalnız /musaitlik: süzgeç boyutları + taşınacak arama alanları (kartlar zaten süzülmüş)
}

record HomeModel(FleetBranding? Branding, IReadOnlyList<FleetShowcaseCard> Cards, FleetModel Fleet,
    IReadOnlyList<SssSatiri> Faq, int FaqCount, IReadOnlyList<string> BranchNames, IReadOnlyList<Branch> Branches,
    IReadOnlyList<SiteLink> Pages, string? Area, string? TermsPage) { string? BrandName; }

record DetailRate(string Label, decimal Amount, decimal? PerDay);                // PerDay null → günlük satır
record DetailHeroModel(FleetShowcaseDetail Listing, IReadOnlyList<DetailRate> Rates, string QuoteUrl,
    IReadOnlyList<string> BranchNames) { string? MetaLine; string? WhatsAppLink; FleetBranding? Branding; string VatText; }
```

| Slot | Kurallar |
|---|---|
| Home | Bölümleri tema seçer ve sıralar; ortak bölümler (`TrustSection`, `HowItWorksSection`, `PricingSection`, `FaqSection`, `CtaSection`, `SearchForm`, `BrandMark`) istenirse kullanılır. **Sarmalayıcı öğe YOK**: bölümler doğrudan `<main>`'in çocuğudur (base.css `main > .cta-bant:last-child`). SSS basılıyorsa `FaqSection Count="Model.FaqCount"` — görünen soru adedi FAQPage JSON-LD adediyle aynı kalır (SEO L-4; render testi kilitler). Filo bölümü `Model.Fleet.Cards` boşsa basılmaz (T2). Tek `h1` Hero'dadır. |
| Fleet | Kapsayıcıyı (`wrap`, `section`) ÇAĞIRAN verir; slot başlık (varsa), özet, uyarı ve listeyi ya da boş durumu basar. `Model.Placement` ile yerleşim seçilebilir (ör. Home'da yatay şerit, `Availability`'de ızgara). Kart başına `Model.Dates` Card slotuna aynen geçer. İlk kartın `Priority` işareti korunur (LCP). |
| DetailHero | Kendi kapsayıcısını (`wrap`) basar — tam genişlik galeri mümkün. Tek `h1` (ilan adı) buradadır. Tarife satırları ve gün başı yuvarlaması sayfadan gelir; tema hesap yapmaz. Tarih formu ORTAK `<SearchForm Action="/musaitlik" Listing="@Model.Listing.Slug" …/>`. Kapak LCP: tembel değil, `fetchpriority="high"`, boyut öznitelikleri. |

### Parametreli slotların imzaları (SABİT)

Parametre adları ve tipleri değişmez:

| Slot | Parametre | Tip | Anlam |
|---|---|---|---|
| Hero | `BrandName` | `string?` | Marka adı (boşsa "Filomuzdaki") |
| | `Branding` | `FleetBranding?` | Firma bilgisi (iletişim, adres, logo sürümü) |
| | `Branches` | `IReadOnlyList<string>` | Aktif şube adları (arama formu) |
| | `Cards` | `IReadOnlyList<FleetShowcaseCard>` | Yayındaki ilanlar |
| | `Area` | `string?` | Hizmet bölgesi ("Esenyurt, İstanbul"); null → basılmaz |
| Card | `Card` | `ListingCard` | Kartın tüm verisi (fiyat, hesap, teklif bağlantısı, işaret, öncelik) |
| | `Dates` | `StayDates?` | Seçilen aralık (Start, End, Days); null → tarihsiz gezinti |
| Header | `Branding` | `FleetBranding?` | Firma bilgisi; iletişim için `ContactLinks.From(Branding)` |
| | `Nav` | `IReadOnlyList<SiteLink>` | Ana menü ("Ana sayfa" hariç) |
| Footer | `Branding` | `FleetBranding?` | Firma bilgisi |
| | `Pages` | `IReadOnlyList<SiteLink>` | Ana sayfa + menü + firmanın içerik sayfaları |
| | `LegalPages` | `IReadOnlyList<SiteLink>` | Yayındaki yasal sayfalar (boşsa sütun yok) |

Ortak `<SearchForm>` alanları DOLU açılır (alış yarın İstanbul günü, iade +3 gün, saatler 10:00); alan adları
`bas`/`bit`/`sube` + EK `basSaat`/`bitSaat` (`HH:mm`, İstanbul). `/musaitlik` iki saat de geçerliyse aramayı o
saatlerle yapar (gün sayısı saatle), biri bile yoksa eski davranış (yalnız günler). `ListingCard.WhatsAppLink` /
`DetailHeroModel.WhatsAppLink` "WhatsApp'tan sor" bağlantısıdır: `WhatsAppAsk` (araç + seçilen tarih/saatle hazır mesaj,
numara `PhoneLink` ile); numara yoksa null → düğme basılmaz.

`FleetBranding.Facts` (`SiteFacts`) firmanın "kiralamadan önce" beyanlarını taşır: depozito tutarı + açıklama,
günlük km sınırı / sınırsız, asgari yaş + ehliyet yılı, sigorta özeti, iptal koşulu, Google puanı + yorum sayısı +
profil bağlantısı + puanın girildiği an, kuruluş yılı (ERP: `/app/ayarlar` → "Web sitesi bilgileri"). Repo daima
doldurur, alanlar tek tek boş olabilir: boş alan basılmaz (T2). Google puanı doluysa bağlantı da doludur (yalnız
https + Google alan adı); puan bağlantısız basılmaz. Çalışma saatleri şubede (`Branch.CalismaSaatleri`).
Ofis/şube bloğu ve "bugün açık" satırı ortak `BranchOffices` (`Build`, `TodayHours`): bugünkü saat yalnız metinden
kesin okunuyorsa ("09:00-18:00", "Her gün …", "Hafta içi …, Cumartesi …", "Mo-Sa …", "Pazar kapalı", "7/24")
verilir, aralıkla yazılır (önbellekte de doğru kalır); tanınmayan metin → satır yok.
Ortak yardımcılar: `SiteFactsText` (firma beyanlarının satır/çip/rozet metinleri), `VehicleSilhouette` (fotoğrafsız
ilan), `FleetFilter` (süzgeç: ilanın görünür özellik etiketlerinden vites/yakıt/koltuk/sınıf; en az iki değerli
boyut sunulur; boyut içinde VEYA, arası VE; tanınmayan değer yok sayılır — `/musaitlik` `?vites=&yakit=&koltuk=&sinif=`
ile süzer). `ListingCard.AllFeatures` görünür özelliklerin tamamıdır (`Features` başlıkta geçeni atan kart çipleri).

Kurallar (tüm slotlar): arama için ORTAK `<SearchForm>` (alan adları/doğrulama tek kaynak, T4); firma işareti
için ORTAK `<BrandMark>`; fiyat için `PriceText`. `Card.Days is null` / `Model.Dates is null` iken "müsait"
DENMEZ (T1). Veri yoksa bölüm basılmaz (T2). Firma adına taahhüt metni yazılmaz (T1). JS eklenmez (CSP
`script-src 'self'`). Ham `FleetBranding.AccentColor` hiçbir yere basılmaz.

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
6. Doğrulama: `PublicSiteThemeContractTests*` (token, kontrast, 3 tema × adres render, SSS adedi, taşma
   kuralları), 1440/390 ekran görüntüsü, `prefers-reduced-motion`, yatay taşma ölçümü (tarayıcı, CI'da değil):
   `node scripts/site-tasma.mjs [--genislik 320,390,768,1440] http://<tema-host>:5230 …` — animasyonlar
   duraklatılıp adım adım oynatılır (açılış anı SIRASINDA ve SONRASINDA), dinlenme hâlinde kırpılmış içerik de
   aranır; taşma varsa çıkış kodu 1.

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
  gerekçeli muafiyetlerde (`--danger --ok` durum renkleri; koyu moddaki siyah `--shadow-1 --shadow-2`; sabit
  WhatsApp düğmesi `--wa-bg --wa-ink` ve Kıyas rozetleri `--rozet-iyi-* --rozet-dikkat-*`, AA ayrıca test edilir)
  geçebilir. Slot kuralları ve bileşen token'ları (`--band-*`, `--siluet` gibi tema token'ları, `--logo-plate`,
  `--field-bg` …) semantik token'a bağlanır —
  `color-mix(in oklab|srgb, …)` serbest — ki palet her şeyi değiştirsin. Açık moddaki gölgeler mürekkep tonunda:
  `color-mix(in srgb, var(--ink) 30%, transparent)`.
- **Taşma kuralları** (yapısal test, `PublicSiteThemeContractTests.Overflow`): ızgara izi `minmax(0, Nfr)` —
  çıplak `1fr`in alt sınırı içeriğin min-content genişliğidir (tarih alanı ~170 px, 320 px'te taşırır);
  `grid-template-areas` yazan kural sütununu da yazar (örtük sütun `auto`); telefonda tek sütunlu ızgara
  `minmax(0, 1fr)`; uzun bağlantı metni bölünebilir (`.buyuk-baglanti` base.css'te `overflow-wrap: anywhere`,
  tema geri almaz); açılışta DÖNEN öğesi olan tema `main { overflow-x: clip }` yazar (salınım ekran kenarını
  aşar; `clip` kaydırma kabı kurmaz, yapışkan öğe ve dikey taşma etkilenmez).

### Palet token'ları (palet bloğu basar; tema css'i yedeğini tanımlar)

Şema başına (açık `:root`, koyu `@media (prefers-color-scheme:dark)`): `--bg --bg-alt --surface
--surface-raised --ink --ink-muted --ink-subtle --line --line-strong --brand --on-brand --brand-text
--brand-soft --accent --accent-ink`.

Şemadan bağımsız (yalnız açık blokta; koyu mod da AYNI değeri görür): `--day-bg` (açık şemanın zemini — koyu
modda logo levhası), `--day-ink` (açık şemanın mürekkebi — iki modda sabit koyu levha/pano), `--night-bg` (koyu
şemanın zemini — sahne gölgesi), `--night-ink` (koyu şemanın mürekkebi — sabit koyu levha üstündeki yazı).

Her şema bloğu ayrıca `color-scheme: light|dark` yazar — değer ZEMİNDEN (beyaz siyahtan çok kontrast veriyorsa
koyu). Tema css'indeki `color-scheme` bildirimi yalnız yedektir: koyu kimlikli bir setin açık şeması da koyu
olduğundan (aşağıda) doğru değer ancak bloktan gelebilir.

Kontrast güvencesi (üretici, tutmayan değeri zıt uca %2 adımla kaydırır; testler bağımsız WCAG hesabıyla
ölçer): metin 4.5:1 — ink / bg, surface, bg-alt, surface-raised; ink-muted ve ink-subtle / bg, surface, bg-alt;
brand-text / bg, surface, brand-soft; on-brand / brand; accent-ink / accent. Dolgu 3:1 — brand / bg, surface.
Açık kimlikli setler için ayrıca night-ink / day-ink 4.5:1 (sabit koyu levha).

### Koyu kimlik ve şemaya bağlı tema token'ları

Bir set `DarkInLightMode = true` bildirirse açık şeması da KOYU olabilir (koyu öncelikli tema; sökülen Vitrin'in
setleri böyleydi — bugün kayıtlı temalarda yok, üretici desteği korunur). O sette `--day-*` "açık levha/koyu levha" anlamını taşımaz (gündüz
zemini koyu, gündüz mürekkebi açık) — böyle setleri olan tema bu sabitleri levha için kullanmaz. Dolgu üstü koyu
yazı adayı bu sette açık şemanın ZEMİNİDİR (mürekkep açık renk).

Tema css'i hangi setin basıldığını bilemez. Şemanın koyu ya da açık olmasına göre değişmesi gereken tema
token'ları (fotoğrafsız sahne zemini, logo levhası, alan zemini, durum renkleri) `light-dark(açıkta, koyuda)` ile
yazılır; seçimi palet bloğunun `color-scheme`'i yapar. Durum renkleri böyle yazılabilir (`--danger:
light-dark(var(--p-red), var(--p-red-light))`) — yapısal test light-dark'ı setin şemasına göre çözer.

### Diğer semantik token'lar (her tema HEPSİNİ tanımlar)

`--brand-ink --focus --danger --ok --font-display --font-body --step--1 --step-0 --step-1 --step-2 --step-3
--step-4 --step-5 --space-1 … --space-8 --radius-s --radius-m --radius-l --shadow-1 --shadow-2 --measure`
(`--focus` = `var(--ink)`; `--danger`/`--ok` her setin zemininde AA olmalı — test).

### Bileşen token'ları

`--btn-bg --btn-ink --btn-radius --card-bg --card-radius --card-border --field-bg --field-border
--price-ink --price-mark` (+ temanın kendi bileşen token'ları; hepsi semantik token'a bağlı).

### Güvenlik

`FleetBranding.AccentColor`, `FleetBranding.Palette` ve `?palet=` / `?renk=` ham değerdir; ASLA doğrudan
stile/özniteliğe basılmaz — yalnız `SitePaletteCss` çıktısı basılır (renkler `Rgb`'den yeniden yazılır, palet
anahtarı yalnız kayıt aramasında kullanılır). CSP `style-src 'unsafe-inline'` olduğundan tek savunma bu.

## Palet sistemi — tema PR'ları için set tanımlama kılavuzu

1. Setler temanın klasöründe: `Components/Themes/<Ad>/<Ad>Palettes.cs` →
   `public static readonly IReadOnlyList<SitePalette> All = [...]`; kayıtta `Palettes = <Ad>Palettes.All`.
   İLK set varsayılandır. 4–5 set hedeflenir.
2. Set: `new SitePalette("anahtar", "Görünen ad", Light, Dark)`; anahtar `^[a-z0-9-]{1,30}$`, tema içinde
   tekil, KALICI (DB + adres). Şema: `new SchemeColors(Bg, Surface, Ink, Primary, Accent)` — hepsi küçük harf
   `#rrggbb`. `Accent` null → vurgu ana rengi izler (firma kendi rengini seçince o da değişir). Koyu şemanın
   zemini mürekkepten koyu; açık şemanınki açık — set `DarkInLightMode = true` ile koyu kimlik bildirmedikçe
   (bildirirse açık şema da koyu olmalı; test iki yönü de denetler). Her şema kendi `Primary`'sini taşır;
   firmanın "kendi ana rengim"i ikisini de ezer.
3. Türetilen tonlar (ikinci zemin, yükseltilmiş yüzey, ikincil/ipucu metni, çizgiler, hafif ton, vurgu üstü
   yazı) otomatik. Elle ince ayar istenirse init özellikleri: `BgAlt SurfaceRaised InkMuted InkSubtle Line
   LineStrong AccentInk` — bunlar da güvenceden geçer (tutmayan değer düzeltilir, basılmaz).
4. Aynı satırı `Application/TenantSettings/SitePaletteKeys.cs` → `ByTheme[<tema anahtarı>]`'na ekle:
   `new(Key, Name, Background = Light.Bg, Ink = Light.Ink, Primary = Light.Primary, Accent = Light.Accent,
   NightBackground = Dark.Bg)` — ERP kartları ve API aitlik doğrulaması bunu kullanır. `Palet_setleri_ve_ayar_anahtarlari_ayni`
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
FAQPage adedi; önizleme (`?tema` — `?tema=vitrin` → `paket` —, `?palet`, `?renk`) noindex + geçersiz değer yok
sayılır + 404 taşır; kayıtlı set/renk basılır, temaya ait olmayan set varsayılana düşer; taşma kuralları
(çıplak `fr` izi yok, alan şablonlu ızgara sütununu yazar, tek sütunlu ızgaralar `minmax(0, 1fr)`, büyük
bağlantı bölünebilir, dönen anahtar kare → `main` yatayda kırpar). Migration:
`SiteThemeKeyMigrationTests`. `SitePaletteCssTests`: üreticinin AA değişmezleri (her tema × her set × iki
şema, 216 renk ızgarası), varsayılan setlerin bugünkü renkleri, enjeksiyon denemeleri varsayılana düşer, çıktı
yalnız `#rrggbb`.
