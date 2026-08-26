using System.Globalization;

namespace RentACar.IntegrationTests.Infrastructure;

/// <summary>
/// Belge numarasının BAĞIMSIZ yeniden-uygulaması — testlerin beklenen değeri buradan alır.
///
/// <para><b>Neden ayrı bir uygulama (CLAUDE.md §3, bağımsız oracle):</b> beklenen değer, test edilen
/// kodun kendisinden türetilmemeli. Bu sınıf <c>BelgeNo</c>'yu ÇAĞIRMAZ; biçimi elle kurar. İkisi
/// ayrışırsa test kırmızıya döner — tam olarak istenen budur.</para>
///
/// <para><b>Gece yarısı toleransı:</b> gerçek "şimdi" ile koşan bir test tam gün dönümüne denk
/// gelirse üretilen numara ertesi güne yazılmış olabilir. <see cref="BeklenenlerdenBiri"/> iki komşu
/// günü de kabul eder. Alternatif (servis/repository imzalarına "simdi" sızdırmak) imza kirliliği
/// yaratacağı için tercih edilmedi.</para>
/// </summary>
public static class BelgeNoOracle
{
    private static readonly TimeZoneInfo Istanbul = Dilim();

    private static TimeZoneInfo Dilim()
    {
        foreach (var id in new[] { "Europe/Istanbul", "Turkey Standard Time" })
        {
            try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
            catch (TimeZoneNotFoundException) { }
            catch (InvalidTimeZoneException) { }
        }
        return TimeZoneInfo.Utc;
    }

    private static DateOnly Bugun()
        => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, Istanbul).DateTime);

    private static string D(int v, int hane) => v.ToString("D" + hane, CultureInfo.InvariantCulture);

    /// <summary>{yyyy}{dd}{MM}{TT}{sss} — elle kurulur, üretim kodu çağrılmaz.</summary>
    public static string Bekle(int tipKodu, long sira, DateOnly? gun = null)
    {
        var g = gun ?? Bugun();
        return D(g.Year, 4) + D(g.Day, 2) + D(g.Month, 2) + D(tipKodu, 2) + sira.ToString("D3", CultureInfo.InvariantCulture);
    }

    /// <summary>Bugün ve yarın için beklenen iki numara (gün dönümü yarışına karşı).</summary>
    public static string[] Beklenenler(int tipKodu, long sira)
    {
        var b = Bugun();
        return [Bekle(tipKodu, sira, b), Bekle(tipKodu, sira, b.AddDays(1))];
    }

    /// <summary>Üretilen numara bugünün ya da (gün dönümüne denk geldiyse) yarının numarası olmalı.</summary>
    public static void BeklenenlerdenBiri(int tipKodu, long sira, string uretilen)
    {
        var kabul = Beklenenler(tipKodu, sira);
        Assert.True(kabul.Contains(uretilen, StringComparer.Ordinal),
            $"Beklenen {string.Join(" veya ", kabul)}, üretilen: {uretilen}");
    }

    /// <summary>GİB fatura numarası: seri(3) + yıl(4) + sıra(9).</summary>
    public static string FaturaBekle(string seri, long sira, int? yil = null)
        => seri + D(yil ?? Bugun().Year, 4) + sira.ToString("D9", CultureInfo.InvariantCulture);
}
