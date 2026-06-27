using Microsoft.Extensions.DependencyInjection;
using RentACar.Application.Authorization;
using RentACar.Application.Common;
using RentACar.Application.Expenses;
using RentACar.Application.Finance;
using RentACar.Application.Penalties;
using RentACar.Application.VehicleSales;
using RentACar.Domain.Enums;
using RentACar.IntegrationTests.Infrastructure;

namespace RentACar.IntegrationTests;

public sealed class RolePermissionMatrixTests
{
    [Theory]
    [InlineData(UserRole.Admin, Permission.ManageUsers, true)]
    [InlineData(UserRole.Admin, Permission.FinanceWrite, true)]
    [InlineData(UserRole.Admin, Permission.OperationsWrite, true)]
    [InlineData(UserRole.Admin, Permission.ViewReports, true)]
    [InlineData(UserRole.Yonetici, Permission.ManageUsers, false)]
    [InlineData(UserRole.Yonetici, Permission.FinanceWrite, true)]
    [InlineData(UserRole.Yonetici, Permission.OperationsWrite, true)]
    [InlineData(UserRole.Yonetici, Permission.ViewReports, true)]
    [InlineData(UserRole.Operator, Permission.ManageUsers, false)]
    [InlineData(UserRole.Operator, Permission.FinanceWrite, false)]
    [InlineData(UserRole.Operator, Permission.OperationsWrite, true)]
    [InlineData(UserRole.Operator, Permission.ViewReports, false)]
    [InlineData(UserRole.Muhasebe, Permission.ManageUsers, false)]
    [InlineData(UserRole.Muhasebe, Permission.FinanceWrite, true)]
    [InlineData(UserRole.Muhasebe, Permission.OperationsWrite, false)]
    [InlineData(UserRole.Muhasebe, Permission.ViewReports, true)]
    public void Matrix_matches_locked_decision(UserRole role, Permission perm, bool expected)
        => Assert.Equal(expected, RolePermissions.Has(role, perm));

    [Fact]
    public void Null_role_has_no_permission()
    {
        foreach (var p in Enum.GetValues<Permission>())
            Assert.False(RolePermissions.Has(null, p));
    }
}

[Collection("postgres")]
public sealed class FinanceAuthorizationTests(PostgresFixture fx)
{
    // Operator (FinanceWrite YOK) hiçbir finansal yazma işlemi yapamaz → ValidationException.
    [Fact]
    public async Task Operator_cannot_perform_finance_writes()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid(), Guid.NewGuid(), "op", UserRole.Operator);
        var cash = scope.ServiceProvider.GetRequiredService<CashService>();
        var expenses = scope.ServiceProvider.GetRequiredService<ExpenseService>();
        var sales = scope.ServiceProvider.GetRequiredService<VehicleSaleService>();
        var penalties = scope.ServiceProvider.GetRequiredService<PenaltyService>();

        await Assert.ThrowsAsync<ValidationException>(() => cash.CollectAsync(
            new CashInput { CariId = Guid.NewGuid(), Tutar = 100m }));
        await Assert.ThrowsAsync<ValidationException>(() => expenses.CreateAsync(
            new ExpenseInput { NetTutar = 100m, OdemeYontemi = OdemeYontemi.Nakit }));
        await Assert.ThrowsAsync<ValidationException>(() => sales.CreateAsync(
            new VehicleSaleInput { VehicleId = Guid.NewGuid(), AliciCariId = Guid.NewGuid(), SatisNet = 100m }));

        // Ceza KAYDI operasyoneldir (serbest), ama YANSITMA finanstır → reddedilir.
        var pid = await penalties.CreateAsync(new PenaltyInput { CezaTuru = "Hız", CariId = Guid.NewGuid(), Tutar = 100m });
        await Assert.ThrowsAsync<ValidationException>(() => penalties.YansitAsync(pid));
    }

    // Muhasebe (FinanceWrite VAR) finansal yazma yapabilir.
    [Fact]
    public async Task Muhasebe_can_perform_finance_writes()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid(), Guid.NewGuid(), "muh", UserRole.Muhasebe);
        var cash = scope.ServiceProvider.GetRequiredService<CashService>();

        var id = await cash.CollectAsync(new CashInput { CariId = Guid.NewGuid(), Tutar = 250m });
        Assert.NotEqual(Guid.Empty, id);
    }
}
