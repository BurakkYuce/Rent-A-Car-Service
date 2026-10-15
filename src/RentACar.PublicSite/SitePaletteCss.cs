using System.Text;
using RentACar.PublicSite.Components.Themes;

namespace RentACar.PublicSite;

/// <summary>Bir şemanın (açık/koyu) basılan semantik renkleri — hepsi kontrast güvencesinden geçmiş.</summary>
public sealed record SchemeTokens(
    Rgb Bg, Rgb BgAlt, Rgb Surface, Rgb SurfaceRaised,
    Rgb Ink, Rgb InkMuted, Rgb InkSubtle, Rgb Line, Rgb LineStrong,
    Rgb Brand, Rgb OnBrand, Rgb BrandText, Rgb BrandSoft, Rgb Accent, Rgb AccentInk);

/// <summary>Çözülmüş palet: iki şema + şemadan bağımsız dört sabit (<c>--day-*</c>, <c>--night-*</c>).</summary>
public sealed record PaletteTokens(SitePalette Palette, SchemeTokens Light, SchemeTokens Dark)
{
    /// <summary>Açık şemanın zemini — İKİ modda da aynı (koyu modda logo levhası, sabit açık yüzey).</summary>
    public Rgb DayBg => Light.Bg;
    /// <summary>Açık şemanın mürekkebi — iki modda aynı (sabit koyu levha/pano).</summary>
    public Rgb DayInk => Light.Ink;
    /// <summary>Koyu şemanın zemini — iki modda aynı (sahne gölgesi).</summary>
    public Rgb NightBg => Dark.Bg;
    /// <summary>Koyu şemanın mürekkebi — iki modda aynı (sabit koyu levha üstündeki yazı).</summary>
    public Rgb NightInk => Dark.Ink;
}

/// <summary>
/// Sitenin renk değişkenlerini temanın palet setinden (+ firmanın isteğe bağlı kendi ana renginden) üretir.
/// Çıktı App.razor'da tema css'inden SONRA basılır; aynı özgüllükte sonraki kazandığı için semantik renk
/// token'larının tamamı paletten gelir. Tema css'indeki aynı adlı tanımlar yalnız yedektir (blok basılamazsa).
///
/// <para><b>Neden renkler olduğu gibi basılmıyor:</b> her değer bir İŞTE kullanılıyor ve her işin kontrast şartı
/// var. Mürekkep, ikincil ve ipucu metni zeminlerde METİN (4.5:1); firma rengi hem DOLGU (birincil düğme —
/// sayfada görünmeli, zemin ve kartla 3:1, WCAG 1.4.11; üstündeki yazı 4.5:1 olacak şekilde SEÇİLİR) hem
/// METİN (bağlantı/fiyat vurgusu — zemin, kart ve hafif tonla 4.5:1). Şartı tutmayan değer (elle verilmiş
/// olsa da) yeterli olana kadar zıt uca (açık zeminde siyaha, koyu zeminde beyaza) %2'lik adımlarla
/// kaydırılır; tutan değer AYNEN korunur. Her renkte saf siyah ya da beyazdan biri en az 4.58:1 verdiği için
/// dolgu üstü yazı daima AA'dır.</para>
///
/// <para><b>Enjeksiyon:</b> çıktı ham ayar/sorgu metninden DEĞİL, çözümlenmiş RGB bileşenlerinden yazılır
/// (<see cref="Rgb.Hex"/>). Biçimi bozuk özel renk sessizce yok sayılır (setin ana rengi kalır), bilinmeyen
/// palet anahtarı varsayılan sete düşer — CSS'e serbest metin hiçbir yoldan giremez.</para>
/// </summary>
public static class SitePaletteCss
{
    public const double TextContrast = 4.5;
    public const double UiContrast = 3.0;

    /// <summary>Her şemada basılan semantik token'lar (sıra = çıktı sırası).</summary>
    public static readonly IReadOnlyList<string> SchemeTokenNames =
    [
        "--bg", "--bg-alt", "--surface", "--surface-raised", "--ink", "--ink-muted", "--ink-subtle", "--line",
        "--line-strong", "--brand", "--on-brand", "--brand-text", "--brand-soft", "--accent", "--accent-ink",
    ];

    /// <summary>Şemadan bağımsız sabitler (yalnız açık blokta basılır, koyu mod da aynısını görür).</summary>
    public static readonly IReadOnlyList<string> FixedTokenNames = ["--day-bg", "--day-ink", "--night-bg", "--night-ink"];

    /// <summary>Temanın setlerinde anahtar (büyük/küçük harf ve kenar boşluğu tolere edilir); yoksa null.</summary>
    public static SitePalette? Find(SiteTheme theme, string? key)
    {
        if (string.IsNullOrWhiteSpace(key)) return null;
        var k = key.Trim().ToLowerInvariant();
        return theme.Palettes.FirstOrDefault(p => p.Key == k);
    }

    /// <summary>Anahtardan set; boş/bilinmeyen/temaya ait olmayan → temanın varsayılan (ilk) seti.</summary>
    public static SitePalette Resolve(SiteTheme theme, string? key) => Find(theme, key) ?? theme.Palettes[0];

    public static PaletteTokens Tokens(SiteTheme theme, string? paletteKey, string? customPrimary)
        => Tokens(Resolve(theme, paletteKey), customPrimary);

    /// <summary>Setin iki şeması; <paramref name="customPrimary"/> KESİN "#rrggbb" ise setin ana rengini ezer.</summary>
    public static PaletteTokens Tokens(SitePalette palette, string? customPrimary)
    {
        Rgb? custom = Rgb.TryParse(customPrimary, out var c) ? c : null;
        var light = Scheme(palette.Light, custom, onInk: null);
        // Dolgu üstündeki koyu yazı adayı iki şemada da açık şemanın mürekkebi (temanın "koyu yazısı").
        var dark = Scheme(palette.Dark, custom, onInk: light.Ink);
        return new PaletteTokens(palette, light, dark);
    }

    /// <summary><c>&lt;style&gt;</c> bloğunun içeriği (tema seçimi css ile aynı kural: <c>prefers-color-scheme</c>).</summary>
    public static string Build(SiteTheme theme, string? paletteKey, string? customPrimary)
        => Css(Tokens(theme, paletteKey, customPrimary));

    public static string Css(PaletteTokens t)
    {
        var sb = new StringBuilder(900);
        sb.Append(":root{").Append(Vars(t.Light))
            .Append(";--day-bg:").Append(t.DayBg.Hex)
            .Append(";--day-ink:").Append(t.DayInk.Hex)
            .Append(";--night-bg:").Append(t.NightBg.Hex)
            .Append(";--night-ink:").Append(t.NightInk.Hex)
            .Append('}');
        sb.Append("@media (prefers-color-scheme:dark){:root{").Append(Vars(t.Dark)).Append("}}");
        return sb.ToString();
    }

    private static string Vars(SchemeTokens s)
    {
        Rgb[] values =
        [
            s.Bg, s.BgAlt, s.Surface, s.SurfaceRaised, s.Ink, s.InkMuted, s.InkSubtle, s.Line, s.LineStrong,
            s.Brand, s.OnBrand, s.BrandText, s.BrandSoft, s.Accent, s.AccentInk,
        ];
        var sb = new StringBuilder(400);
        for (var i = 0; i < values.Length; i++)
        {
            if (i > 0) sb.Append(';');
            sb.Append(SchemeTokenNames[i]).Append(':').Append(values[i].Hex);
        }
        return sb.ToString();
    }

    private static SchemeTokens Scheme(SchemeColors s, Rgb? custom, Rgb? onInk)
    {
        var bg = Rgb.Parse(s.Bg);
        var surface = Rgb.Parse(s.Surface);
        var inkBase = Rgb.Parse(s.Ink);
        // Koyu zemin mi: beyaz siyahtan daha çok kontrast veriyorsa. Kontrastı artıran yön buna göre (açıkta
        // siyaha, koyuda beyaza); türetme oranları da (koyu zeminde tonlar daha belirgin olmalı).
        var dark = Rgb.Contrast(bg, Rgb.White) > Rgb.Contrast(bg, Rgb.Black);
        var away = dark ? Rgb.White : Rgb.Black;

        var bgAlt = Optional(s.BgAlt) ?? Rgb.Mix(bg, inkBase, dark ? 0.07 : 0.04);
        var surfaceRaised = Optional(s.SurfaceRaised) ?? (dark ? Rgb.Mix(surface, inkBase, 0.04) : surface);
        var ink = Adjust(inkBase, away, t => Text(t, bg, surface, bgAlt, surfaceRaised));
        // İkincil ve ipucu metni mürekkebe doğru kaydırılır: mürekkep bu zeminlerde AA olduğundan sınır güvenli.
        var inkMuted = Adjust(Optional(s.InkMuted) ?? Rgb.Mix(ink, bg, 0.25), ink, t => Text(t, bg, surface, bgAlt));
        var inkSubtle = Adjust(Optional(s.InkSubtle) ?? Rgb.Mix(ink, bg, 0.38), ink, t => Text(t, bg, surface, bgAlt));
        var line = Optional(s.Line) ?? Rgb.Mix(bg, ink, dark ? 0.12 : 0.16);
        var lineStrong = Optional(s.LineStrong) ?? Rgb.Mix(bg, ink, dark ? 0.32 : 0.45);

        var primary = custom ?? Rgb.Parse(s.Primary);
        // Dolgu: zemin ve kartla 3:1'i tutmayan renk (açıkta sarı/beyaz, koyuda lacivert) kaydırılır.
        var brand = Adjust(primary, away, t => Rgb.Contrast(t, bg) >= UiContrast && Rgb.Contrast(t, surface) >= UiContrast);
        var onBrand = OnColor(brand, onInk ?? ink);
        var brandSoft = Rgb.Mix(bg, primary, dark ? 0.22 : 0.12);
        // Metin: hem sayfa/kart zemininde hem hafif ton (seçili satır, bilgi kutusu) üstünde okunur.
        var brandText = Adjust(primary, away, t => Text(t, bg, surface, brandSoft));

        Rgb accent, accentInk;
        if (s.Accent is null)
        {
            accent = brand;
            accentInk = onBrand;
        }
        else
        {
            accent = Rgb.Parse(s.Accent);
            accentInk = Optional(s.AccentInk) is { } given && Rgb.Contrast(given, accent) >= TextContrast
                ? given
                : OnColor(accent, onInk ?? ink);
        }

        return new SchemeTokens(bg, bgAlt, surface, surfaceRaised, ink, inkMuted, inkSubtle, line, lineStrong,
            brand, onBrand, brandText, brandSoft, accent, accentInk);
    }

    private static Rgb? Optional(string? hex) => hex is null ? null : Rgb.Parse(hex);

    private static bool Text(Rgb t, params ReadOnlySpan<Rgb> grounds)
    {
        foreach (var g in grounds)
            if (Rgb.Contrast(t, g) < TextContrast) return false;
        return true;
    }

    /// <summary>
    /// Dolgu üstündeki yazı. Önce beyaz ya da temanın koyu mürekkebi (kontrastı yüksek olan) denenir. Orta
    /// parlaklıktaki renklerde (ör. #3399cc) ikisi de 4.5:1'in altında kalabiliyor — mürekkep saf siyah DEĞİL.
    /// O durumda saf siyah/beyazdan yüksek olan seçilir: bu ikisinden biri HER renkte en az 4.58:1 verir
    /// (matematiksel alt sınır), bu yüzden sonuç daima AA'yı geçer.
    /// </summary>
    public static Rgb OnColor(Rgb fill, Rgb darkInk)
    {
        var cw = Rgb.Contrast(Rgb.White, fill);
        var ci = Rgb.Contrast(darkInk, fill);
        if (cw >= TextContrast && cw >= ci) return Rgb.White;
        if (ci >= TextContrast) return darkInk;
        return cw >= Rgb.Contrast(Rgb.Black, fill) ? Rgb.White : Rgb.Black;
    }

    /// <summary>Rengi <paramref name="toward"/> yönüne %2'lik adımlarla karıştırır; koşulu sağlayan İLK (yani asıl
    /// renge en yakın) tonu döner. Koşul hiç sağlanmazsa hedefin kendisi döner.</summary>
    private static Rgb Adjust(Rgb color, Rgb toward, Func<Rgb, bool> ok)
    {
        for (var step = 0; step <= 50; step++)
        {
            var candidate = Rgb.Mix(color, toward, step / 50.0);
            if (ok(candidate)) return candidate;
        }
        return toward;
    }
}
