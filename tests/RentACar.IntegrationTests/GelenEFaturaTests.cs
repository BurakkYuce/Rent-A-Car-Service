using Microsoft.Extensions.DependencyInjection;
using RentACar.Application.Common;
using RentACar.Application.GelenEFaturalar;
using RentACar.Domain.Enums;
using RentACar.IntegrationTests.Infrastructure;

namespace RentACar.IntegrationTests;

/// <summary>
/// Gelen e-Fatura triage — bağımsız oracle. Elle giriş + ETTN benzersizlik + durum akışı
/// (Beklemede→Onaylandı/Reddedildi; Onaylandı→İşlendi; geçersiz geçiş reddi) + GİB-sync stub (boş) +
/// yetki (FinanceWrite) + tenant izolasyon (racar_app). Beklenen değerler senaryodan, koddan değil.
/// </summary>
[Collection("postgres")]
public sealed class GelenEFaturaTests(PostgresFixture fx)
{
    private static GelenEFaturaInput Sample(string ettn = "ETTN-1") => new()
    {
        Ettn = ettn, GonderenVkn = "1234567890", GonderenUnvan = "Tedarikçi A.Ş.",
        Tarih = new DateTimeOffset(2026, 5, 1, 0, 0, 0, TimeSpan.Zero),
        NetTutar = 1000m, KdvTutar = 200m, GenelToplam = 1200m, Currency = "try"
    };

    [Fact]
    public async Task CreateManual_roundtrips_beklemede()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var svc = scope.ServiceProvider.GetRequiredService<GelenEFaturaService>();

        var id = await svc.CreateManualAsync(Sample());
        var r = await svc.GetAsync(id);
        Assert.NotNull(r);
        Assert.Equal("ETTN-1", r!.Ettn);
        Assert.Equal("Tedarikçi A.Ş.", r.GonderenUnvan);
        Assert.Equal(1200m, r.GenelToplam);
        Assert.Equal("TRY", r.Currency);                       // döviz upper normalize
        Assert.Equal(GelenEFaturaDurum.Beklemede, r.Durum);    // başlangıç durumu
    }

    [Fact]
    public async Task Duplicate_ettn_rejected()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var svc = scope.ServiceProvider.GetRequiredService<GelenEFaturaService>();

        await svc.CreateManualAsync(Sample("DUP-1"));
        await Assert.ThrowsAsync<ValidationException>(() => svc.CreateManualAsync(Sample("DUP-1")));
    }

    [Fact]
    public async Task Validation_requires_ettn_vkn_unvan_and_nonnegative()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var svc = scope.ServiceProvider.GetRequiredService<GelenEFaturaService>();

        await Assert.ThrowsAsync<ValidationException>(() => svc.CreateManualAsync(new GelenEFaturaInput { Ettn = "", GonderenVkn = "1", GonderenUnvan = "X" }));
        await Assert.ThrowsAsync<ValidationException>(() => svc.CreateManualAsync(new GelenEFaturaInput { Ettn = "E", GonderenVkn = "", GonderenUnvan = "X" }));
        await Assert.ThrowsAsync<ValidationException>(() => svc.CreateManualAsync(new GelenEFaturaInput { Ettn = "E", GonderenVkn = "1", GonderenUnvan = "X", GenelToplam = -5m }));
    }

    [Fact]
    public async Task Durum_akisi_ve_gecersiz_gecis_reddi()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var svc = scope.ServiceProvider.GetRequiredService<GelenEFaturaService>();

        var id = await svc.CreateManualAsync(Sample("AKIS-1"));
        // Beklemede iken İşle → reddedilir (yalnız onaylanmış işlenir).
        await Assert.ThrowsAsync<ValidationException>(() => svc.IsleAsync(id));

        // Beklemede → Onayla → Onaylandı.
        Assert.True(await svc.OnaylaAsync(id));
        Assert.Equal(GelenEFaturaDurum.Onaylandi, (await svc.GetAsync(id))!.Durum);
        // Tekrar Onayla → reddedilir (artık Beklemede değil).
        await Assert.ThrowsAsync<ValidationException>(() => svc.OnaylaAsync(id));

        // Onaylandı → İşle → İşlendi.
        Assert.True(await svc.IsleAsync(id));
        Assert.Equal(GelenEFaturaDurum.Islendi, (await svc.GetAsync(id))!.Durum);
    }

    [Fact]
    public async Task Reddet_neden_kaydeder()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var svc = scope.ServiceProvider.GetRequiredService<GelenEFaturaService>();

        var id = await svc.CreateManualAsync(Sample("RED-1"));
        Assert.True(await svc.ReddetAsync(id, "Mükerrer fatura"));
        var r = await svc.GetAsync(id);
        Assert.Equal(GelenEFaturaDurum.Reddedildi, r!.Durum);
        Assert.Equal("Mükerrer fatura", r.RedNedeni);
    }

    [Fact]
    public async Task SyncFromGib_stub_bos_doner()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var svc = scope.ServiceProvider.GetRequiredService<GelenEFaturaService>();

        // Stub IEInvoiceService boş liste → 0 eklenir (entegrasyon kimliği yok).
        var added = await svc.SyncFromGibAsync(
            new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 12, 31, 0, 0, 0, TimeSpan.Zero));
        Assert.Equal(0, added);
        Assert.Empty(await svc.ListAsync());
    }

    [Fact]
    public async Task NonFinance_user_cannot_manage()
    {
        using var host = new TestHost(fx.AppConnectionString);
        // Operatör: OperationsWrite var, FinanceWrite YOK → reddedilmeli.
        using var scope = host.ScopeFor(Guid.NewGuid(), Guid.NewGuid(), "op", UserRole.Operator);
        var svc = scope.ServiceProvider.GetRequiredService<GelenEFaturaService>();
        await Assert.ThrowsAsync<ValidationException>(() => svc.CreateManualAsync(Sample("YETKI-1")));
    }

    [Fact]
    public async Task GelenEFaturalar_are_tenant_isolated()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var t1 = Guid.NewGuid();
        var t2 = Guid.NewGuid();

        using (var s1 = host.ScopeFor(t1))
            await s1.ServiceProvider.GetRequiredService<GelenEFaturaService>().CreateManualAsync(Sample("T1-ETTN"));

        using var s2 = host.ScopeFor(t2);
        var svc2 = s2.ServiceProvider.GetRequiredService<GelenEFaturaService>();
        Assert.Empty(await svc2.ListAsync());
        // Aynı ETTN başka tenant'ta serbest (benzersizlik tenant-içi).
        await svc2.CreateManualAsync(Sample("T1-ETTN"));
        Assert.Single(await svc2.ListAsync());
    }
}
