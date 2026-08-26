using System.Globalization;

namespace RentACar.Domain.Common;

/// <summary>
/// Belge numarası biçimlendirme — SAF kurallar (Domain: hiçbir şeye bağımlı değil).
///
/// <para><b>Genel desen (kullanıcı kararı):</b> <c>{yyyy}{dd}{MM}{TT}{sss}</c> — yıl(4) +
/// AYIN GÜNÜ(2) + AY(2) + belge tipi(2) + o günün sırası(3). 26.08.2026 kira sözleşmesi =
/// <c>2026260801001</c>. Tamamı rakam, ayraç yok, sayaç HER GÜN 1'den başlar.</para>
///
/// <para><b>Gün/ay sırası bilinçlidir</b> (kullanıcı, alternatifini görerek seçti) ve numaranın
/// kronolojik SIRALANMADIĞI anlamına gelir: <c>20262608</c> (26 Ağu) ile <c>20260109</c> (1 Eyl)
/// alfabetik olarak ters düşer. Bu yüzden "en yeni" sıralamaları numaraya değil
/// <c>CreatedAtUtc</c>'ye dayanır. Aynı sebep 999 taşmasında da geçerlidir: <c>"1000" &lt; "999"</c>.</para>
///
/// <para><b>Eski numaralarla çakışma yapısal olarak imkânsız:</b> eski düzen HARF içerir
/// (<c>KS-000001</c>), yeni desen tamamı rakamdır → <c>(TenantId, No)</c> unique indeksleri güvende,
/// geriye dönük yeniden numaralandırmaya gerek yok.</para>
/// </summary>
public static class BelgeNo
{
    /// <summary>
    /// Günlük sayaç anahtarı — <c>TenantSequences.Name</c> değeri (<c>varchar(64)</c>, 11 karakter).
    /// Günü ANAHTARA gömmek sıfırlama kodu YAZMADAN günlük sıfırlama verir: yeni gün = yeni satır =
    /// NextValue 1. (Sıfırlama kodu yazmak yarış koşulu kaynağı olurdu.)
    /// </summary>
    public static string SayacAnahtari(BelgeNoTuru tur, DateOnly gun)
        => $"{Iki((int)tur)}:{Dort(gun.Year)}{Iki(gun.Month)}{Iki(gun.Day)}";

    /// <summary>Genel desen. <paramref name="sira"/> 999'u aşarsa numara doğal olarak 14 haneye çıkar.</summary>
    public static string Bicimle(BelgeNoTuru tur, DateOnly gun, long sira)
    {
        if (sira <= 0) throw new ArgumentOutOfRangeException(nameof(sira), sira, "Sıra 1'den küçük olamaz.");
        var kod = (int)tur;
        if (kod is < 1 or > 99) throw new ArgumentOutOfRangeException(nameof(tur), tur, "Tip kodu 1-99 aralığında olmalı.");

        return Dort(gun.Year) + Iki(gun.Day) + Iki(gun.Month) + Iki(kod)
             + sira.ToString("D3", CultureInfo.InvariantCulture);
    }

    // ───────────────────────── Fatura: GİB formatı ─────────────────────────

    /// <summary>
    /// e-Fatura/e-Arşiv fatura numarası: <b>3 karakter seri + 4 hane yıl + 9 hane sıra = 16 hane</b>
    /// (ör. <c>RNT2026000000001</c>). Genel 13 haneli desen bu zorunluluğu KARŞILAMAZ, bu yüzden
    /// fatura ayrı biçimlenir. Fatura numarası kesildikten sonra değiştirilemez — e-Fatura sonradan
    /// açıldığında yeniden numaralandırma yapılamayacağı için doğru format BAŞTAN kullanılır.
    /// </summary>
    public static string FaturaBicimle(string seri, int yil, long sira)
    {
        if (!SeriGecerliMi(seri))
            throw new ArgumentException(
                "Fatura seri kodu tam 3 karakter olmalı ve yalnız büyük harf (A-Z) veya rakam içermeli " +
                "(Türkçe karakter kabul edilmez).", nameof(seri));
        if (sira <= 0) throw new ArgumentOutOfRangeException(nameof(sira), sira, "Sıra 1'den küçük olamaz.");
        if (yil is < 1000 or > 9999) throw new ArgumentOutOfRangeException(nameof(yil), yil, "Yıl 4 haneli olmalı.");

        return seri + Dort(yil) + sira.ToString("D9", CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Fatura sayacı SERİ + YIL başınadır: mevzuat gereği sıra her yıl 1'den başlar ve her seri
    /// kendi içinde boşluksuz ilerler. (Diğer belgelerden farklı: onlar GÜN başına sıfırlanır.)
    /// </summary>
    public static string FaturaSayacAnahtari(string seri, int yil) => $"F:{seri}:{Dort(yil)}";

    /// <summary>Tam 3 karakter, yalnız <c>A-Z</c> veya <c>0-9</c>. Türkçe karakter (İ, Ğ, Ş…) yasak.</summary>
    public static bool SeriGecerliMi(string? seri)
        => seri is { Length: 3 } && seri.All(c => c is >= 'A' and <= 'Z' or >= '0' and <= '9');

    // Kültür-bağımsız rakamlar: bazı kültürlerde varsayılan biçimlendirme ASCII olmayan rakam üretir.
    private static string Iki(int v) => v.ToString("D2", CultureInfo.InvariantCulture);
    private static string Dort(int v) => v.ToString("D4", CultureInfo.InvariantCulture);
}
