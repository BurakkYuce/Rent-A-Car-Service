using Microsoft.Extensions.DependencyInjection;
using RentACar.Application.Bookings;
using RentACar.Application.Common;
using RentACar.Application.Customers;
using RentACar.Application.Penalties;
using RentACar.Application.Vehicles;
using RentACar.Domain.Enums;
using RentACar.IntegrationTests.Infrastructure;

namespace RentACar.IntegrationTests;

/// <summary>
/// Pre-launch adversarial H1/M4/L1 — OPERASYONEL yazan servis metodları Muhasebe (FinanceWrite var,
/// OperationsWrite YOK) tarafından çağrılamaz. Red SERVİS katmanında (PermissionGuard, "yetkiniz yok").
/// H1: /kiralar create/teslim/dönüş/iptal guard'sızdı (Muhasebe kira yazabiliyordu — ampirik kanıtlı).
/// Guard ilk adım olduğundan kurulum gerekmez (rastgele Id/boş input yeterli).
/// </summary>
[Collection("postgres")]
public sealed class OperasyonelYetkiTests(PostgresFixture fx)
{
    private async Task MuhasebeReddiAsync(Func<IServiceScope, Task> islem)
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid(), Guid.NewGuid(), "muhasebe", UserRole.Muhasebe);
        var ex = await Assert.ThrowsAsync<YetkiYokException>(() => islem(scope));
        Assert.Contains("yetkiniz yok", ex.Message); // red YETKİDEN (OperationsWrite), girdi/veri hatasından değil
    }

    private static T Svc<T>(IServiceScope s) where T : notnull => s.ServiceProvider.GetRequiredService<T>();

    private static readonly DateTimeOffset D = new(2026, 1, 1, 9, 0, 0, TimeSpan.Zero);

    [Fact] public Task Muhasebe_kira_olusturamaz() => MuhasebeReddiAsync(s => Svc<RentalService>(s).CreateDirectAsync(new BookingInput()));
    [Fact] public Task Muhasebe_kira_teslim_edemez() => MuhasebeReddiAsync(s => Svc<RentalService>(s).DeliverAsync(Guid.NewGuid(), 0, 0));
    [Fact] public Task Muhasebe_kira_donus_yapamaz() => MuhasebeReddiAsync(s => Svc<RentalService>(s).ReturnAsync(Guid.NewGuid(), 0, 0, D));
    [Fact] public Task Muhasebe_kira_iptal_edemez() => MuhasebeReddiAsync(s => Svc<RentalService>(s).CancelAsync(Guid.NewGuid()));
    [Fact] public Task Muhasebe_kira_uzatamaz() => MuhasebeReddiAsync(s => Svc<RentalService>(s).ExtendAsync(Guid.NewGuid(), D));
    [Fact] public Task Muhasebe_arac_olusturamaz() => MuhasebeReddiAsync(s => Svc<VehicleService>(s).CreateAsync(new VehicleInput { Plaka = "34X" }));
    [Fact] public Task Muhasebe_rezervasyon_olusturamaz() => MuhasebeReddiAsync(s => Svc<ReservationService>(s).CreateAsync(new BookingInput()));
    [Fact] public Task Muhasebe_cari_olusturamaz() => MuhasebeReddiAsync(s => Svc<CustomerService>(s).CreateAsync(new CustomerInput { Tip = CariType.Bireysel, Ad = "X" }));
    [Fact] public Task Muhasebe_ceza_olusturamaz() => MuhasebeReddiAsync(s => Svc<PenaltyService>(s).CreateAsync(new PenaltyInput { CezaTuru = "Hız" }));
}
