using Microsoft.Extensions.DependencyInjection;
using RentACar.Application.Bookings;
using RentACar.Application.Common;
using RentACar.Application.Customers;
using RentACar.Application.Vehicles;
using RentACar.Domain.Enums;
using RentACar.IntegrationTests.Infrastructure;

namespace RentACar.IntegrationTests;

/// <summary>
/// Pre-launch adversarial M3 — şube kapsamı artık LİSTE'nin yanı sıra GetAsync(id) + write yollarında da uygulanır.
/// Önceden: operatör(Merkez) başka şube (Ankara) kirasını listede görmüyordu ama ID ile OKUYUP İPTAL edebiliyordu.
/// (Tenant/RLS izolasyonu AYRI ve sağlam; bu tenant-içi yatay sınır.)
/// </summary>
[Collection("postgres")]
public sealed class SubeKapsamGetWriteTests(PostgresFixture fx)
{
    private static readonly DateTimeOffset Start = new DateTimeOffset(DateTime.UtcNow.Date, TimeSpan.Zero).AddDays(-3).AddHours(9);

    [Fact]
    public async Task Operator_baska_sube_kirasini_okuyamaz_ve_iptal_edemez()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var tenant = Guid.NewGuid();
        Guid rental;
        using (var seed = host.ScopeFor(tenant)) // Admin
        {
            var sp = seed.ServiceProvider;
            var v = await sp.GetRequiredService<VehicleService>().CreateAsync(new VehicleInput { Plaka = "06 ANK 01" });
            var m = await sp.GetRequiredService<CustomerService>().CreateAsync(new CustomerInput { Tip = CustomerType.Bireysel, Ad = "A", Soyad = "B" });
            rental = await sp.GetRequiredService<RentalService>().CreateDirectAsync(new BookingInput
            { MusteriId = m, VehicleId = v, BasTar = Start, BitTar = Start.AddDays(2), GunlukUcret = 100m, CikisOfisi = "Ankara" });
        }

        using (var op = host.ScopeFor(tenant, Guid.NewGuid(), "op", UserRole.Operator, assignedBranch: "Merkez"))
        {
            var rentals = op.ServiceProvider.GetRequiredService<RentalService>();
            Assert.Contains("kapsamınız dışında", (await Assert.ThrowsAsync<NoPermissionException>(() => rentals.GetAsync(rental))).Message);
            // İnceltme (2026-08-17): operatör kira İPTALİNİ artık hiç yapamaz (OperationsDelete yok)
            // ve yetki reddi şube-kapsam kontrolünden ÖNCE gelir — doğru sıra: önce "bu işlemi
            // yapabilir misin", sonra "bu kayda erişebilir misin". Şube-kapsam semantiği yukarıdaki
            // GetAsync assert'iyle korunmaya devam ediyor.
            Assert.Contains("OperationsDelete", (await Assert.ThrowsAsync<NoPermissionException>(() => rentals.CancelAsync(rental))).Message);
        }

        using (var op2 = host.ScopeFor(tenant, Guid.NewGuid(), "op2", UserRole.Operator, assignedBranch: "Ankara"))
            Assert.NotNull(await op2.ServiceProvider.GetRequiredService<RentalService>().GetAsync(rental)); // kendi şubesi

        using (var admin = host.ScopeFor(tenant))
            Assert.NotNull(await admin.ServiceProvider.GetRequiredService<RentalService>().GetAsync(rental)); // Admin tümü
    }
}
