using Microsoft.Extensions.DependencyInjection;
using RentACar.Application.Common;
using RentACar.Application.Customers;
using RentACar.Application.Finance;
using RentACar.Domain.Enums;
using RentACar.IntegrationTests.Infrastructure;

namespace RentACar.IntegrationTests;

/// <summary>
/// Pre-launch adversarial M5 — tekil tahsilat/ödeme (kasiyerin en sık işlemi) artık idempotency anahtarı taşır:
/// çift-submit (çift-tık/retry/geri-butonu) aynı IslemAnahtari ile ikinci kez yazılamaz (mevcut kısmi unique index).
/// Önceden korumasızdı → 5 eşzamanlı 100 TL tahsilat cari'yi −500 yapıyordu (kanıtlı).
/// </summary>
[Collection("postgres")]
public sealed class TahsilatIdempotencyTests(PostgresFixture fx)
{
    private static async Task<Guid> CariAsync(IServiceProvider sp)
        => await sp.GetRequiredService<CustomerService>().CreateAsync(new CustomerInput { Tip = CustomerType.Bireysel, Ad = "A", Soyad = "B" });

    [Fact]
    public async Task Ayni_islemanahtari_cift_submit_tek_yazar()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var tenant = Guid.NewGuid();
        Guid cari;
        using (var seed = host.ScopeFor(tenant)) cari = await CariAsync(seed.ServiceProvider);

        var token = Guid.NewGuid(); // aynı form-token → çift-submit
        var basari = 0;
        await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => Task.Run(async () =>
        {
            using var s = host.ScopeFor(tenant);
            try
            {
                await s.ServiceProvider.GetRequiredService<CashService>()
                    .CollectAsync(new CashInput { CariId = cari, Tutar = 100m, IslemAnahtari = token });
                Interlocked.Increment(ref basari);
            }
            catch (ValidationException) { /* idempotent red beklenir */ }
        })));

        using var check = host.ScopeFor(tenant);
        Assert.Equal(1, basari);  // yalnız 1 tahsilat başardı (5 DEĞİL)
        Assert.Equal(-100m, await check.ServiceProvider.GetRequiredService<CashService>().GetAccountBalanceAsync(cari)); // −500 DEĞİL
    }

    [Fact]
    public async Task Farkli_token_iki_mesru_tahsilat_yazilir()
    {
        // Regresyon: farklı token (farklı işlem) → ikisi de yazılır (idempotency meşru tahsilatı bloklamaz).
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var cari = await CariAsync(sp);
        var cash = sp.GetRequiredService<CashService>();
        await cash.CollectAsync(new CashInput { CariId = cari, Tutar = 100m, IslemAnahtari = Guid.NewGuid() });
        await cash.CollectAsync(new CashInput { CariId = cari, Tutar = 100m, IslemAnahtari = Guid.NewGuid() });
        Assert.Equal(-200m, await cash.GetAccountBalanceAsync(cari));
    }
}
