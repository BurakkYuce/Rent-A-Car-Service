using Microsoft.Extensions.DependencyInjection;
using RentACar.Application.CustomCodes;
using RentACar.Application.ExpenseCategories;
using RentACar.IntegrationTests.Infrastructure;

namespace RentACar.IntegrationTests;

/// <summary>
/// roadmap G5 — master 🟡 alan-eksiği kapatma (additive). BAĞIMSIZ ORACLE: ExpenseCategory.Tur (gider kırılımı)
/// ve CustomCode.Turu (sınıflandırma) roundtrip; opsiyonel (boş → null).
/// </summary>
[Collection("postgres")]
public sealed class KalanMasterTests(PostgresFixture fx)
{
    [Fact]
    public async Task ExpenseCategory_tur_roundtrip()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var svc = scope.ServiceProvider.GetRequiredService<ExpenseCategoryService>();

        var id = await svc.CreateAsync(new ExpenseCategoryInput { Kod = "YKT", Ad = "Yakıt", Tur = "Araç" });
        Assert.Equal("Araç", (await svc.GetAsync(id))!.Tur);

        var id2 = await svc.CreateAsync(new ExpenseCategoryInput { Kod = "KIRA", Ad = "Kira" }); // Tür boş
        Assert.Null((await svc.GetAsync(id2))!.Tur);
    }

    [Fact]
    public async Task CustomCode_turu_roundtrip()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var svc = scope.ServiceProvider.GetRequiredService<CustomCodeService>();

        var id = await svc.CreateAsync(new CustomCodeInput { Kod = "VIP", Ad = "VIP Müşteri", Turu = "Cari" });
        Assert.Equal("Cari", (await svc.GetAsync(id))!.Turu);

        var id2 = await svc.CreateAsync(new CustomCodeInput { Kod = "FLO", Ad = "Filo" }); // Tür boş
        Assert.Null((await svc.GetAsync(id2))!.Turu);
    }
}
