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
/// gelirse üretilen numara ertesi güne yazılmış olabilir. <see cref="OneOfExpected"/> iki komşu
/// günü de kabul eder. Alternatif (servis/repository imzalarına "simdi" sızdırmak) imza kirliliği
/// yaratacağı için tercih edilmedi.</para>
/// </summary>
public static class DocumentNoOracle
{
    private static readonly TimeZoneInfo Istanbul = Slice();

    private static TimeZoneInfo Slice()
    {
        foreach (var id in new[] { "Europe/Istanbul", "Turkey Standard Time" })
        {
            try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
            catch (TimeZoneNotFoundException) { }
            catch (InvalidTimeZoneException) { }
        }
        return TimeZoneInfo.Utc;
    }

    private static DateOnly Today()
        => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, Istanbul).DateTime);

    private static string D(int v, int digit) => v.ToString("D" + digit, CultureInfo.InvariantCulture);

    /// <summary>{yyyy}{dd}{MM}{TT}{sss} — elle kurulur, üretim kodu çağrılmaz.</summary>
    public static string Wait(int typeCode, long order, DateOnly? day = null)
    {
        var g = day ?? Today();
        return D(g.Year, 4) + D(g.Day, 2) + D(g.Month, 2) + D(typeCode, 2) + order.ToString("D3", CultureInfo.InvariantCulture);
    }

    /// <summary>Bugün ve yarın için beklenen iki numara (gün dönümü yarışına karşı).</summary>
    public static string[] ExpectedValues(int typeCode, long order)
    {
        var b = Today();
        return [Wait(typeCode, order, b), Wait(typeCode, order, b.AddDays(1))];
    }

    /// <summary>Üretilen numara bugünün ya da (gün dönümüne denk geldiyse) yarının numarası olmalı.</summary>
    public static void OneOfExpected(int typeCode, long order, string generated)
    {
        var accept = ExpectedValues(typeCode, order);
        Assert.True(accept.Contains(generated, StringComparer.Ordinal),
            $"Beklenen {string.Join(" veya ", accept)}, üretilen: {generated}");
    }

    /// <summary>GİB fatura numarası: seri(3) + yıl(4) + sıra(9).</summary>
    public static string ExpectInvoice(string series, long order, int? year = null)
        => series + D(year ?? Today().Year, 4) + order.ToString("D9", CultureInfo.InvariantCulture);
}
