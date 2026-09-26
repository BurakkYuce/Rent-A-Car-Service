namespace RentACar.Application.Common;

/// <summary>
/// Telefon numarasından tıklanabilir adres üretir: <c>tel:</c> ve <c>wa.me</c>.
///
/// <para><b>Normalizasyon SUNUCUDA.</b> Aynı kural bugün <c>rc-kira-tabs.js</c> içinde JS olarak da
/// yaşıyor (kira ekranının paylaş barı). Yeni yüzeylerde o JS'i klonlamak yerine buraya bakılır —
/// kopyalanan kural birinde düzeltilip diğerinde kalır (O12c dersi: kural TEK yerde).</para>
///
/// <para>TR varsayımı bilinçli: <c>0…</c> → <c>90…</c>, <c>5…</c> → <c>90 5…</c>. Zaten ülke kodu
/// taşıyan numaralar (<c>00…</c> ya da 12+ hane) olduğu gibi bırakılır — yurt dışı müşteri numarası
/// bozulmasın.</para>
/// </summary>
public static class PhoneLink
{
    /// <summary>Yalnız rakamlar; ülke kodu TR varsayımıyla tamamlanır. Geçersizse null.</summary>
    public static string? Normalize(string? tel)
    {
        if (string.IsNullOrWhiteSpace(tel)) return null;

        var d = new string(tel.Where(char.IsAsciiDigit).ToArray());
        if (d.StartsWith("00", StringComparison.Ordinal)) d = d[2..];   // 0090… → 90…
        else if (d.StartsWith('0')) d = "9" + d;                        // 0532… → 90532…
        else if (d.StartsWith('5') && d.Length == 10) d = "90" + d;     // 532… → 90532…

        return d.Length is >= 10 and <= 15 ? d : null;
    }

    /// <summary><c>tel:+90…</c> — mobilde arama, masaüstünde çoğu tarayıcıda no-op (metin de basılır).</summary>
    public static string? Tel(string? tel) => Normalize(tel) is { } d ? "tel:+" + d : null;

    /// <summary><c>https://wa.me/90…</c> (isteğe bağlı hazır metinle).</summary>
    public static string? Wa(string? tel, string? message = null)
    {
        if (Normalize(tel) is not { } d) return null;
        var url = "https://wa.me/" + d;
        return string.IsNullOrWhiteSpace(message) ? url : url + "?text=" + Uri.EscapeDataString(message);
    }
}
