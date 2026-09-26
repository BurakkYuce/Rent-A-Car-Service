using Microsoft.Extensions.DependencyInjection;
using RentACar.Application.Bookings;
using RentACar.Application.Common;
using RentACar.Application.Customers;
using RentACar.Application.Finance;
using RentACar.Application.ServiceRecords;
using RentACar.Application.Vehicles;
using RentACar.Domain.Enums;
using RentACar.IntegrationTests.Infrastructure;

namespace RentACar.IntegrationTests;

/// <summary>
/// İzin inceltmesi (2026-08-17): <c>OperationsDelete</c> ve <c>FinanceReverse</c>.
///
/// <para>Kilit karar: "yazabilen her şeyi yok edebilir" varsayımı kırıldı. Operatör kayıt AÇAR
/// (rezervasyon, müşteri, araç) ama belge YOK EDEMEZ / iptal EDEMEZ; defteri hiç kimse
/// FinanceWrite'la geri saramaz, ayrı <c>FinanceReverse</c> gerekir (varsayılan matriste
/// FinanceWrite sahipleri alır — davranış korunur, değer kullanıcı-bazlı kısıtlamada).</para>
///
/// <para>Testler canlı DB'ye karşı UÇTAN UCA: önce izinli rolle gerçek kayıt kurulur, sonra
/// kısıtlı rolle işlem denenir — enum değerini değil DAVRANIŞI doğrular.</para>
/// </summary>
[Collection("postgres")]
public sealed class IzinInceltmeTests(PostgresFixture fx)
{
    private static readonly DateTimeOffset Start = DateTimeOffset.UtcNow.AddDays(3);

    private static BookingInput Reservation(Guid vehicle, Guid customer)
        => new() { MusteriId = customer, VehicleId = vehicle, BasTar = Start, BitTar = Start.AddDays(2), GunlukUcret = 100m };

    // ---------------------------------------------------------------- OperationsDelete

    [Fact]
    public async Task Operator_rezervasyon_ACAR_ama_iptal_EDEMEZ_yonetici_eder()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var tenant = Guid.NewGuid();

        // Operatör kendi açtığı rezervasyonu bile iptal edemez (OperationsWrite yetmez).
        Guid resId;
        var vehicle = await TestVehicle.NewAsync(host, tenant); // servis müşteri/araç varlığını doğruluyor
        var account = await TestCustomer.NewAsync(host, tenant);
        using (var op = host.ScopeFor(tenant, Guid.NewGuid(), "op", UserRole.Operator))
        {
            var svc = op.ServiceProvider.GetRequiredService<ReservationService>();
            resId = await svc.CreateAsync(Reservation(vehicle, account));

            var ex = await Assert.ThrowsAsync<NoPermissionException>(() => svc.CancelAsync(resId));
            Assert.Contains("OperationsDelete", ex.Message);
        }

        // Yönetici aynı kaydı iptal EDEBİLİR — kayıt gerçekten Iptal durumuna geçer.
        using (var yon = host.ScopeFor(tenant, Guid.NewGuid(), "yon", UserRole.Yonetici))
        {
            var svc = yon.ServiceProvider.GetRequiredService<ReservationService>();
            Assert.True(await svc.CancelAsync(resId));
            Assert.Equal(ReservationStatus.Iptal, (await svc.GetAsync(resId))!.Durum);
        }
    }

    [Fact]
    public async Task Operator_arac_ve_musteri_SILEMEZ()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid(), Guid.NewGuid(), "op", UserRole.Operator);

        await Assert.ThrowsAsync<NoPermissionException>(() =>
            scope.ServiceProvider.GetRequiredService<VehicleService>().DeleteAsync(Guid.NewGuid()));
        await Assert.ThrowsAsync<NoPermissionException>(() =>
            scope.ServiceProvider.GetRequiredService<CustomerService>().DeleteAsync(Guid.NewGuid()));
    }

    /// <summary>
    /// İnceltme sırasında bulunan AÇIK: <c>ServiceRecordService.IptalAsync</c>'ta HİÇ guard yoktu —
    /// rolü ne olursa olsun oturum açan herkes servis kaydı iptal edebiliyordu. Bu test o açığın
    /// kapalı kaldığını KALICI olarak doğrular (Muhasebe: OperationsWrite'ı da olmayan rol).
    /// </summary>
    [Fact]
    public async Task Servis_iptali_artik_guardli_eski_ACIK_kapali()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid(), Guid.NewGuid(), "muh", UserRole.Muhasebe);
        var svc = scope.ServiceProvider.GetRequiredService<ServiceRecordService>();

        var ex = await Assert.ThrowsAsync<NoPermissionException>(() => svc.CancelAsync(Guid.NewGuid()));
        Assert.Contains("OperationsDelete", ex.Message);
    }

    // ---------------------------------------------------------------- FinanceReverse

    [Fact]
    public async Task Muhasebe_ters_kayit_ATABILIR_operator_ATAMAZ()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var tenant = Guid.NewGuid();

        // Muhasebe tahsilatı girer ve ters kaydını atabilir (FinanceReverse matristen gelir).
        Guid transactionId;
        var account = await TestCustomer.NewAsync(host, tenant); // Muhasebe cari açamaz (OperationsWrite yok)
        using (var acct = host.ScopeFor(tenant, Guid.NewGuid(), "muh", UserRole.Muhasebe))
        {
            var cash = acct.ServiceProvider.GetRequiredService<CashService>();
            transactionId = await cash.CollectAsync(new CashInput { CariId = account, Tutar = 250m });
            var reverseId = await cash.ReverseAsync(transactionId);
            Assert.NotEqual(Guid.Empty, reverseId);
        }

        // Operatör ters kayıt atamaz — FinanceWrite'ı zaten yok ama hata FinanceReverse'i söylemeli
        // (guard doğru izni istiyor; "FinanceWrite yok" genel reddi değil).
        using (var op = host.ScopeFor(tenant, Guid.NewGuid(), "op", UserRole.Operator))
        {
            var cash = op.ServiceProvider.GetRequiredService<CashService>();
            var ex = await Assert.ThrowsAsync<NoPermissionException>(() => cash.ReverseAsync(transactionId));
            Assert.Contains("FinanceReverse", ex.Message);
        }
    }
}
