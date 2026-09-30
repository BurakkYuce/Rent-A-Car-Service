using RentACar.PublicSite;

namespace RentACar.IntegrationTests;

/// <summary>Hizmet bölgesi metni — saf birim testi. Beklenen metinler elle yazılmıştır.</summary>
public sealed class ServiceAreaTests
{
    [Fact]
    public void Tek_il_tek_ilce_ilce_ve_il()
        => Assert.Equal("Esenyurt, İstanbul", ServiceArea.Describe([("İstanbul", "Esenyurt"), (" İstanbul ", "Esenyurt")]));

    [Fact]
    public void Tek_il_farkli_ilceler_yalniz_il()
        => Assert.Equal("İstanbul", ServiceArea.Describe([("İstanbul", "Esenyurt"), ("İstanbul", "Kadıköy")]));

    [Fact]
    public void Ilcesi_bos_sube_varsa_yalniz_il()
        => Assert.Equal("Antalya", ServiceArea.Describe([("Antalya", "Muratpaşa"), ("Antalya", null)]));

    [Fact]
    public void Birden_cok_il_en_fazla_uc_il()
        => Assert.Equal("İstanbul, Ankara, İzmir",
            ServiceArea.Describe([("İstanbul", "A"), ("Ankara", "B"), ("İzmir", "C"), ("Bursa", "D"), ("Ankara", "E")]));

    [Fact]
    public void Il_verisi_yoksa_null()
    {
        Assert.Null(ServiceArea.Describe([]));
        Assert.Null(ServiceArea.Describe([(null, "Esenyurt"), ("  ", null)]));
    }
}
