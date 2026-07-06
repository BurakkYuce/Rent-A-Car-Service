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

    // ---- Denetim C7 — Dec/Int/Id davranış sabitleme ----

    [Fact]
    public void Dec_bos_null_nokta_ondalik_gecersiz_null()
    {
        Assert.Null(FormParse.Dec(""));
        Assert.Null(FormParse.Dec(null));
        Assert.Null(FormParse.Dec("   "));
        Assert.Null(FormParse.Dec("abc"));
        Assert.Equal(12.34m, FormParse.Dec("12.34")); // invariant: nokta ondalıktır
        Assert.Equal(12.34m, FormParse.Dec(" 12.34 ")); // trim'lenir
    }

    // MEVCUT DAVRANIŞ BELGESİ (koddan okundu): Dec invariant NumberStyles.Number kullanır →
    // virgül BİNLİK ayırıcı sayılır ve "12,34" → 1234 döner (12.34 DEĞİL). Türkçe klavye
    // alışkanlığıyla virgül ondalık girilirse tutar SESSİZCE 100× büyür — form katmanı için
    // bilinçli/dokümante footgun; davranış değişirse bu test kırılıp kararı yüzeye çıkarır.
    [Fact]
    public void Dec_virgul_binlik_ayirici_sayilir_belgelenen_davranis()
        => Assert.Equal(1234m, FormParse.Dec("12,34"));

    [Fact]
    public void Int_bos_null_sayi_gecersiz_null()
    {
        Assert.Null(FormParse.Int(""));
        Assert.Null(FormParse.Int(null));
        Assert.Null(FormParse.Int("abc"));
        Assert.Null(FormParse.Int("12.34")); // ondalık int değildir
        Assert.Equal(42, FormParse.Int("42"));
        Assert.Equal(-7, FormParse.Int(" -7 "));
    }

    [Fact]
    public void Id_bos_null_gecerli_guid_gecersiz_null()
    {
        Assert.Null(FormParse.Id(""));
        Assert.Null(FormParse.Id(null));
        Assert.Null(FormParse.Id("xyz"));
        var g = Guid.NewGuid();
        Assert.Equal(g, FormParse.Id(g.ToString()));
        Assert.Equal(g, FormParse.Id(" " + g + " ")); // trim'lenir
    }
}
