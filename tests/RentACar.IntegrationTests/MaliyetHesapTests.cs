using RentACar.Application.Pricing;

namespace RentACar.IntegrationTests;

/// <summary>
/// roadmap L2 — filo maliyet hesaplayıcı (salt-hesap, DB yok). BAĞIMSIZ ORACLE: elle hesaplanmış değerler.
/// </summary>
public sealed class MaliyetHesapTests
{
    [Fact]
    public void Finansmansiz_amortisman_kar_teklif()
    {
        // 1.000.000 alış, %30 kalıntı → 700.000 amortisman; 36 ay; %20 kâr; %20 KDV; finansman/gider/damga = 0.
        var s = MaliyetHesapService.Hesapla(new MaliyetHesapInput
        {
            AlisBedeli = 1_000_000m, ResidualYuzde = 0.30m, SureAy = 36,
            FaizOran = 0m, KkdfOran = 0m, BsmvOran = 0m, DamgaOran = 0m, AylikGider = 0m,
            KarMarji = 0.20m, KdvOran = 0.20m
        });

        Assert.Equal(300_000m, s.ResidualDeger);
        Assert.Equal(700_000m, s.NetAmortisman);
        Assert.Equal(0m, s.FinansmanFaiz);
        Assert.Equal(700_000m, s.ToplamMaliyet);
        Assert.Equal(19_444.44m, s.BasaBasAylik);    // 700000/36
        Assert.Equal(140_000m, s.Kar);               // 700000×0.20
        Assert.Equal(840_000m, s.TeklifNet);
        Assert.Equal(23_333.33m, s.TeklifAylikNet);  // 840000/36
        Assert.Equal(1_008_000m, s.TeklifKdvli);     // 840000×1.20
    }

    [Fact]
    public void Finansman_kkdf_bsmv_faiz_uzerinden()
    {
        // 1.000.000 alış, kalıntı 0, 12 ay, %40 yıllık faiz, KKDF+BSMV %15+%15 = %30 (faiz üzerinden).
        var s = MaliyetHesapService.Hesapla(new MaliyetHesapInput
        {
            AlisBedeli = 1_000_000m, ResidualYuzde = 0m, SureAy = 12,
            FaizOran = 0.40m, KkdfOran = 0.15m, BsmvOran = 0.15m,
            DamgaOran = 0m, AylikGider = 0m, KarMarji = 0m, KdvOran = 0.20m
        });

        Assert.Equal(400_000m, s.FinansmanFaiz);      // 1.000.000 × 0.40 × 12/12
        Assert.Equal(120_000m, s.FinansmanVergi);     // 400.000 × 0.30
        Assert.Equal(1_520_000m, s.ToplamMaliyet);    // 1.000.000 + 400.000 + 120.000
        Assert.Equal(1_824_000m, s.TeklifKdvli);      // 1.520.000 × 1.20
    }

    [Fact]
    public void Sifir_alis_reddedilir()
        => Assert.Throws<RentACar.Application.Common.ValidationException>(
            () => MaliyetHesapService.Hesapla(new MaliyetHesapInput { AlisBedeli = 0m, SureAy = 12 }));
}
