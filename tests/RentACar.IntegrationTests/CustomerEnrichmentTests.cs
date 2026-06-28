using Microsoft.Extensions.DependencyInjection;
using RentACar.Application.Customers;
using RentACar.Domain.Enums;
using RentACar.IntegrationTests.Infrastructure;

namespace RentACar.IntegrationTests;

/// <summary>
/// Cari CRM/kimlik zenginleştirme — bağımsız oracle. Yeni additive alanların roundtrip'i
/// (Gsm2/Kaynak/temsilci/İYS/uyarı/ehliyet/risk mesajı/HGS yansıtma türü).
/// </summary>
[Collection("postgres")]
public sealed class CustomerEnrichmentTests(PostgresFixture fx)
{
    [Fact]
    public async Task New_crm_fields_roundtrip()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var svc = scope.ServiceProvider.GetRequiredService<CustomerService>();

        var ehliyetTar = new DateTimeOffset(2015, 3, 10, 0, 0, 0, TimeSpan.Zero);
        var riskTar = new DateTimeOffset(2026, 1, 5, 0, 0, 0, TimeSpan.Zero);

        var id = await svc.CreateAsync(new CustomerInput
        {
            Tip = CariType.Bireysel, Ad = "Ayşe", Soyad = "Yıldız",
            CepTel = "5551112233", Gsm2 = "5324445566", Kaynak = "Web",
            MusteriTemsilcisi = "Mehmet", IysIzinli = true, Uyari = true, UyariNedeni = "Geç ödeme",
            EhliyetNo = "ABC123", EhliyetSinifi = "B", EhliyetTarihi = ehliyetTar, EhliyetYeri = "İstanbul",
            RiskMesaji = "Dikkat", RiskTarihi = riskTar, HgsYansitmaTuru = "Faturalı"
        });

        var c = await svc.GetAsync(id);
        Assert.NotNull(c);
        Assert.Equal("5324445566", c!.Gsm2);
        Assert.Equal("Web", c.Kaynak);
        Assert.Equal("Mehmet", c.MusteriTemsilcisi);
        Assert.True(c.IysIzinli);
        Assert.True(c.Uyari);
        Assert.Equal("Geç ödeme", c.UyariNedeni);
        Assert.Equal("ABC123", c.EhliyetNo);
        Assert.Equal("B", c.EhliyetSinifi);
        Assert.Equal(ehliyetTar, c.EhliyetTarihi);
        Assert.Equal("İstanbul", c.EhliyetYeri);
        Assert.Equal("Dikkat", c.RiskMesaji);
        Assert.Equal(riskTar, c.RiskTarihi);
        Assert.Equal("Faturalı", c.HgsYansitmaTuru);
    }

    [Fact]
    public async Task New_fields_default_null_or_false()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var svc = scope.ServiceProvider.GetRequiredService<CustomerService>();

        var id = await svc.CreateAsync(new CustomerInput { Tip = CariType.Bireysel, Ad = "Boş" });
        var c = await svc.GetAsync(id);
        Assert.Null(c!.Gsm2);
        Assert.Null(c.Kaynak);
        Assert.Null(c.EhliyetNo);
        Assert.Null(c.EhliyetTarihi);
        Assert.Null(c.RiskTarihi);
        Assert.False(c.IysIzinli);
        Assert.False(c.Uyari);
    }

    [Fact]
    public async Task Update_clears_and_sets_crm_fields()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var svc = scope.ServiceProvider.GetRequiredService<CustomerService>();

        var id = await svc.CreateAsync(new CustomerInput
        { Tip = CariType.Bireysel, Ad = "Can", Uyari = true, UyariNedeni = "x", Kaynak = "Telefon" });

        await svc.UpdateAsync(id, new CustomerInput
        { Tip = CariType.Bireysel, Ad = "Can", Uyari = false, UyariNedeni = null, Kaynak = "Bayi" });

        var c = await svc.GetAsync(id);
        Assert.False(c!.Uyari);
        Assert.Null(c.UyariNedeni);
        Assert.Equal("Bayi", c.Kaynak);
    }
}
