using Microsoft.Extensions.DependencyInjection;
using RentACar.Application.Customers;
using RentACar.Domain.Enums;
using RentACar.IntegrationTests.Infrastructure;

namespace RentACar.IntegrationTests;

/// <summary>
/// roadmap K4 — Customer residual (KVKK + ek adres/banka/fatura adresi, additive). BAĞIMSIZ ORACLE:
/// alanlar Create/Update üzerinden kalıcılaşıp Get ile geri okunur (Normalize/Apply eşlemesi).
/// </summary>
[Collection("postgres")]
public sealed class CustomerDerinlikTests(PostgresFixture fx)
{
    private static readonly DateTimeOffset Onay = new(2026, 6, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Kvkk_banka_fatura_roundtrip()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var svc = scope.ServiceProvider.GetRequiredService<CustomerService>();

        var id = await svc.CreateAsync(new CustomerInput
        {
            Tip = CariType.Kurumsal, Unvan = "ABC A.Ş.",
            KvkkOnay = true, KvkkOnayTarih = Onay, EkAdres = "Depo adresi",
            BankaIban = "tr120006...", BankaAdi = "Ziraat", FaturaAdresi = "Fatura adresi", FaturaUnvan = "ABC Ticaret A.Ş."
        });

        var c = await svc.GetAsync(id);
        Assert.True(c!.KvkkOnay);
        Assert.Equal(Onay, c.KvkkOnayTarih);
        Assert.Equal("Depo adresi", c.EkAdres);
        Assert.Equal("TR120006...", c.BankaIban);     // büyük harfe normalize
        Assert.Equal("Ziraat", c.BankaAdi);
        Assert.Equal("Fatura adresi", c.FaturaAdresi);
        Assert.Equal("ABC Ticaret A.Ş.", c.FaturaUnvan);

        // Update: KVKK geri çek + banka temizle
        await svc.UpdateAsync(id, new CustomerInput
        { Tip = CariType.Kurumsal, Unvan = "ABC A.Ş.", KvkkOnay = false, BankaIban = null });
        var c2 = await svc.GetAsync(id);
        Assert.False(c2!.KvkkOnay);
        Assert.Null(c2.BankaIban);
    }
}
