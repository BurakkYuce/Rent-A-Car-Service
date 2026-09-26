using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RentACar.Application.Bookings;
using RentACar.Application.Customers;
using RentACar.Application.Finance;
using RentACar.Application.Vehicles;
using RentACar.Domain.Entities;
using RentACar.Domain.Enums;
using RentACar.Infrastructure.Persistence;
using RentACar.IntegrationTests.Infrastructure;

namespace RentACar.IntegrationTests;

/// <summary>
/// F8.1b — <c>/api/ui/v1</c> finans belge uçları (faturalar, cezalar, giderler, gelen e-fatura, araç satışları)
/// GERÇEK Web boru hattında: defter dengesi, KDV satır yuvarlaması, idempotency (önce kayıt → 409 + mevcut),
/// eşzamanlı çift gönderim, ters kayıt (iade), şube kapsamı (başka şube 403, başka kiracı 404), dürüst stub.
/// <para><b>BAĞIMSIZ ORACLE:</b> beklenen tutarlar elle hesaplandı (333,33 × %20 = 66,666 → 66,67, brüt 400,00;
/// 10.000 × %20 = 2.000; 150 + 50,50 = 200,50) — servis/rapor kodundan üretilmez. Kullanıcılar ve parolalar
/// çalışma anında rastgele; tarihler <see cref="TestZaman"/>'dan.</para>
/// </summary>
[Collection("web")]
public sealed partial class UiFinanceDocumentTests(WebFixture fx)
{
    private const string V1 = "/api/ui/v1";

    private enum Who { Admin, Accountant, AccountantNoReverse, OperatorA, OperatorB, OperatorPlain }

    private sealed class Env
    {
        public required Guid TenantId { get; init; }
        public required string Code { get; init; }
        public required string Password { get; init; }
        public required Dictionary<Who, string> Users { get; init; }
        public required Guid Customer { get; init; }
        public required Guid Supplier { get; init; }
        public required Guid Rental { get; init; }        // SubeA, 3 gün × 100 = 300 TRY
        public required Guid RentalVehicle { get; init; } // SubeA aracı
    }

    private sealed record Session(HttpClient C, string Xsrf);

    private static string Random10(string prefix) => prefix + Guid.NewGuid().ToString("N")[..10];
    private static string NewKey() => Guid.NewGuid().ToString("N");

    private async Task<Env> SetupAsync()
    {
        var tenantId = Guid.NewGuid();
        var code = Random10("f81b");
        var password = WebFixture.RandomPassword();
        var users = Enum.GetValues<Who>().ToDictionary(k => k, _ => Random10("u"));

        var opts = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(fx.Pg.OwnerConnectionString).Options;
        await using (var db = new AppDbContext(opts, NullTenantContext.Instance, NullCurrentUser.Instance))
        {
            db.Tenants.Add(new Tenant { Id = tenantId, Code = code, Name = code, IsActive = true });
            var hasher = fx.Web.Services.GetRequiredService<IPasswordHasher<User>>();
            foreach (var (who, name) in users)
            {
                var (role, branch) = who switch
                {
                    Who.Admin => (UserRole.Admin, (string?)null),
                    Who.Accountant or Who.AccountantNoReverse => (UserRole.Muhasebe, null),
                    Who.OperatorA or Who.OperatorPlain => (UserRole.Operator, "SubeA"),
                    _ => (UserRole.Operator, "SubeB"),
                };
                var u = new User { TenantId = tenantId, UserName = name, DisplayName = name, Rol = role, AtanmisSube = branch, IsActive = true };
                u.PasswordHash = hasher.HashPassword(u, password);
                db.Users.Add(u);
                // Operatörlere kullanıcı-bazlı FinanceWrite: şube kapsamını izin kapısından AYRI sınamak için.
                if (who is Who.OperatorA or Who.OperatorB)
                    db.KullaniciIzinIstisnalari.Add(new KullaniciIzinIstisna { TenantId = tenantId, UserId = u.Id, Izin = "FinanceWrite", Ver = true });
                if (who == Who.AccountantNoReverse)
                    db.KullaniciIzinIstisnalari.Add(new KullaniciIzinIstisna { TenantId = tenantId, UserId = u.Id, Izin = "FinanceReverse", Ver = false });
            }
            await db.SaveChangesAsync();
        }
        await fx.MakePilotAsync(tenantId, true);

        return await ReadAsync(tenantId, async sp =>
        {
            var customers = sp.GetRequiredService<CustomerService>();
            var customer = await customers.CreateAsync(new CustomerInput { Tip = CustomerType.Bireysel, Ad = "Belge", Soyad = "Musteri" });
            var supplier = await customers.CreateAsync(new CustomerInput { Tip = CustomerType.Bireysel, Ad = "Belge", Soyad = "Tedarikci" });
            var vehicle = await VehicleAsync(sp, "SubeA");
            var rental = await sp.GetRequiredService<RentalService>().CreateDirectAsync(new BookingInput
            {
                MusteriId = customer, VehicleId = vehicle, BasTar = TestZaman.DaysLater(10),
                BitTar = TestZaman.DaysLater(13), GunlukUcret = 100m, CikisOfisi = "SubeA",
            });
            return new Env
            {
                TenantId = tenantId, Code = code, Password = password, Users = users,
                Customer = customer, Supplier = supplier, Rental = rental, RentalVehicle = vehicle,
            };
        });
    }

    private static Task<Guid> VehicleAsync(IServiceProvider sp, string branch)
        => sp.GetRequiredService<VehicleService>().CreateAsync(new VehicleInput
        {
            Plaka = "34 FB " + Random.Shared.Next(1000, 9999), Sube = branch,
        });

    private async Task<T> ReadAsync<T>(Guid tenantId, Func<IServiceProvider, Task<T>> read)
    {
        using var host = new TestHost(fx.Pg.AppConnectionString);
        using var s = host.ScopeFor(tenantId, role: UserRole.Admin);
        return await read(s.ServiceProvider);
    }

    private Task<T> DbAsync<T>(Env e, Func<AppDbContext, Task<T>> read)
        => ReadAsync(e.TenantId, async sp =>
        {
            await using var db = await sp.GetRequiredService<IDbContextFactory<AppDbContext>>().CreateDbContextAsync();
            return await read(db);
        });

    private Task<List<AccountLedgerEntry>> LedgerAsync(Env e, Guid sourceId)
        => DbAsync(e, db => db.AccountLedgerEntries.AsNoTracking().Where(x => x.SourceId == sourceId).ToListAsync());

    private Task<decimal> CustomerBalanceAsync(Env e, Guid account)
        => ReadAsync(e.TenantId, sp => sp.GetRequiredService<CashService>().GetAccountBalanceAsync(account));

    /// <summary>Kiracının TÜM defter kümeleri dengeli: her (SourceType, SourceId) için Σ borç(baz) == Σ alacak(baz).</summary>
    private async Task AllLedgerBalancedAsync(Env e)
    {
        var rows = await DbAsync(e, db => db.AccountLedgerEntries.AsNoTracking().ToListAsync());
        Assert.NotEmpty(rows);
        foreach (var set in rows.GroupBy(x => (x.SourceType, x.SourceId)))
        {
            var debit = set.Where(x => x.Direction == LedgerDirection.Debit).Sum(x => x.Amount.Amount * x.Amount.Rate);
            var credit = set.Where(x => x.Direction == LedgerDirection.Credit).Sum(x => x.Amount.Amount * x.Amount.Rate);
            Assert.True(debit == credit, $"Dengesiz küme {set.Key}: borç {debit} ≠ alacak {credit}");
        }
    }

    private static void Line(List<AccountLedgerEntry> set, LedgerAccountType type, Guid? reference, LedgerDirection dir,
        decimal amount, string currency = "TRY", decimal rate = 1m)
        => Assert.Single(set, x => x.AccountType == type && x.AccountRef == reference && x.Direction == dir
                                   && x.Amount.Amount == amount && x.Amount.Currency == currency && x.Amount.Rate == rate);
}
