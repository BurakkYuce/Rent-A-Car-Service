using Microsoft.Extensions.DependencyInjection;
using RentACar.Application.Bookings;
using RentACar.Application.Common;
using RentACar.Application.Customers;
using RentACar.Application.Vehicles;
using RentACar.Domain.Enums;
using RentACar.IntegrationTests.Infrastructure;

namespace RentACar.IntegrationTests;

/// <summary>
/// Teklif (quotation) modülü — bağımsız oracle. Beklenen gün/tutar senaryodan elle hesaplanır
/// (servis kodundan değil). Durum makinesi + kabul→rezervasyon dönüşümü + yetki + izolasyon.
/// </summary>
[Collection("postgres")]
public sealed class QuotationTests(PostgresFixture fx)
{
    // Sabit aralık: 3 tam gün (72 saat) → gün 3.
    private static readonly DateTimeOffset Bas = new(2026, 1, 1, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Bit = new(2026, 1, 4, 9, 0, 0, TimeSpan.Zero);

    private static async Task<(Guid musteri, Guid arac)> SeedAsync(IServiceScope scope)
    {
        var customers = scope.ServiceProvider.GetRequiredService<CustomerService>();
        var vehicles = scope.ServiceProvider.GetRequiredService<VehicleService>();
        var m = await customers.CreateAsync(new CustomerInput { Tip = CariType.Bireysel, Ad = "Teklif", Soyad = "Müşteri" });
        var v = await vehicles.CreateAsync(new VehicleInput { Plaka = "34TKF01" });
        return (m, v);
    }

    private static QuotationInput Input(Guid m, Guid v, decimal gunluk = 100m) => new()
    {
        MusteriId = m, VehicleId = v, BasTar = Bas, BitTar = Bit, GunlukUcret = gunluk, CikisOfisi = "MERKEZ"
    };

    [Fact]
    public async Task Create_computes_gun_tutar_and_allocates_no()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var svc = scope.ServiceProvider.GetRequiredService<QuotationService>();
        var (m, v) = await SeedAsync(scope);

        var id = await svc.CreateAsync(Input(m, v, 100m));

        var q = await svc.GetAsync(id);
        Assert.NotNull(q);
        Assert.Equal(3, q!.Gun);            // 72h / 24 = 3
        Assert.Equal(300m, q.Tutar);        // 3 × 100 (oracle, elle)
        Assert.Equal("TK-000001", q.No);
        Assert.Equal(QuotationStatus.Taslak, q.Durum);
        Assert.Null(q.ReservationId);
    }

    [Fact]
    public async Task Status_machine_send_then_cannot_send_again()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var svc = scope.ServiceProvider.GetRequiredService<QuotationService>();
        var (m, v) = await SeedAsync(scope);

        var id = await svc.CreateAsync(Input(m, v));
        Assert.True(await svc.SendAsync(id));
        Assert.Equal(QuotationStatus.Gonderildi, (await svc.GetAsync(id))!.Durum);
        // Gönderildi'den tekrar Gönder geçersiz.
        await Assert.ThrowsAsync<ValidationException>(() => svc.SendAsync(id));
    }

    [Fact]
    public async Task Accept_converts_to_reservation_and_links_both()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var svc = scope.ServiceProvider.GetRequiredService<QuotationService>();
        var reservations = scope.ServiceProvider.GetRequiredService<ReservationService>();
        var (m, v) = await SeedAsync(scope);

        var id = await svc.CreateAsync(Input(m, v, 150m));   // tutar 450
        var resId = await svc.AcceptAsync(id);

        // Teklif: Kabul + bağlı.
        var q = await svc.GetAsync(id);
        Assert.Equal(QuotationStatus.Kabul, q!.Durum);
        Assert.Equal(resId, q.ReservationId);

        // Rezervasyon: alanlar teklifle birebir taşınmış.
        var res = await reservations.GetAsync(resId);
        Assert.NotNull(res);
        Assert.Equal(ReservationStatus.Rezerv, res!.Durum);
        Assert.Equal("RZ-000001", res.ReservationNo);
        Assert.Equal(m, res.MusteriId);
        Assert.Equal(v, res.VehicleId);
        Assert.Equal(3, res.Gun);
        Assert.Equal(450m, res.Tutar);
        Assert.Equal(Bas, res.BasTar);
        Assert.Equal(Bit, res.BitTar);
    }

    [Fact]
    public async Task Accept_twice_rejected()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var svc = scope.ServiceProvider.GetRequiredService<QuotationService>();
        var (m, v) = await SeedAsync(scope);

        var id = await svc.CreateAsync(Input(m, v));
        await svc.AcceptAsync(id);
        // Kabul edilmiş teklif tekrar kabul edilemez.
        await Assert.ThrowsAsync<ValidationException>(() => svc.AcceptAsync(id));
    }

    [Fact]
    public async Task Rejected_quotation_cannot_be_accepted()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var svc = scope.ServiceProvider.GetRequiredService<QuotationService>();
        var (m, v) = await SeedAsync(scope);

        var id = await svc.CreateAsync(Input(m, v));
        Assert.True(await svc.RejectAsync(id));
        await Assert.ThrowsAsync<ValidationException>(() => svc.AcceptAsync(id));
    }

    [Fact]
    public async Task GecerlilikTarihi_before_start_rejected()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var svc = scope.ServiceProvider.GetRequiredService<QuotationService>();
        var (m, v) = await SeedAsync(scope);

        var input = Input(m, v);
        input.GecerlilikTarihi = Bas.AddDays(-1);
        await Assert.ThrowsAsync<ValidationException>(() => svc.CreateAsync(input));
    }

    [Fact]
    public async Task NonOperations_user_cannot_create()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var tenant = Guid.NewGuid();
        Guid m, v;
        using (var admin = host.ScopeFor(tenant))
            (m, v) = await SeedAsync(admin);

        // Muhasebe: FinanceWrite var, OperationsWrite YOK → teklif oluşturamaz.
        using var scope = host.ScopeFor(tenant, Guid.NewGuid(), "muh", UserRole.Muhasebe);
        var svc = scope.ServiceProvider.GetRequiredService<QuotationService>();
        await Assert.ThrowsAsync<ValidationException>(() => svc.CreateAsync(Input(m, v)));
    }

    [Fact]
    public async Task Quotations_are_tenant_isolated()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var t1 = Guid.NewGuid();
        var t2 = Guid.NewGuid();

        using (var s1 = host.ScopeFor(t1))
        {
            var (m, v) = await SeedAsync(s1);
            await s1.ServiceProvider.GetRequiredService<QuotationService>().CreateAsync(Input(m, v));
        }

        using var s2 = host.ScopeFor(t2);
        Assert.Empty(await s2.ServiceProvider.GetRequiredService<QuotationService>().ListAsync());
    }
}
