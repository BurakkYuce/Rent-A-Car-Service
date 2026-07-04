using RentACar.Web;

namespace RentACar.IntegrationTests;

/// <summary>
/// FormParse.Date UTC-normalize regresyonu. Smoke bulgusu: form tarihi (tz'siz) yerel-offset (+03:00)
/// DateTimeOffset üretiyordu → Npgsql timestamptz'e yazarken 500. BAĞIMSIZ ORACLE: sonucun offset'i
/// DAİMA sıfır (UTC) olmalı — 18 form-tarih yazma ucunun tümünü tek noktada korur. DB gerektirmez.
/// </summary>
public sealed class FormParseTests
{
    [Fact]
    public void Date_tzsiz_girdi_UTC_offset_sifir_doner()
    {
        var d = FormParse.Date("2026-07-04");
        Assert.NotNull(d);
        Assert.Equal(TimeSpan.Zero, d!.Value.Offset); // timestamptz YALNIZ offset 0 kabul eder
    }

    [Fact]
    public void Date_offsetli_girdi_UTC_e_normalize_eder()
    {
        var d = FormParse.Date("2026-07-04T12:00:00+05:00");
        Assert.NotNull(d);
        Assert.Equal(TimeSpan.Zero, d!.Value.Offset);
        Assert.Equal(7, d.Value.UtcDateTime.Hour); // 12:00 +05:00 → 07:00 UTC
    }

    [Fact]
    public void Date_bos_veya_gecersiz_null_doner()
    {
        Assert.Null(FormParse.Date(""));
        Assert.Null(FormParse.Date(null));
        Assert.Null(FormParse.Date("   "));
        Assert.Null(FormParse.Date("abc"));
    }
}
