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
/// F8.1a — <c>/api/ui/v1/finans/*</c> finans ekranları (kasa hub, bakiye düzeltme, cari virman, tek/çok cari toplu
/// tahsilat, toplu gider, depozito iade/mahsup, cari ekstre, ters kayıt, dönem kapanışı, otomatik tahsilat, kurlar)
/// GERÇEK Web boru hattında. <b>BAĞIMSIZ ORACLE:</b> beklenen tutarlar elle kurulmuş senaryodan (ör. borç 100 + 900,
/// 100 tam + 400 kısmi kapatma = 500 tahsilat → bakiye 500); servis/rapor kodundan ÜRETİLMEZ. Kullanıcı adları ve
/// parolalar çalışma anında rastgele; tarihler <see cref="TestZaman"/>.
/// </summary>
[Collection("web")]
public sealed partial class UiFinanceHubApiTests(WebFixture fx)
{
    private const string V1 = "/api/ui/v1/finans";

    private enum Who { Admin, Accountant, AccountantNoReverse, OperatorA, OperatorB, OperatorPlain }

    private sealed class Env
    {
        public required Guid TenantId { get; init; }
        public required string Code { get; init; }
        public required string Password { get; init; }
        public required Dictionary<Who, string> Users { get; init; }
        public required Guid CustomerA { get; init; }
        public required Guid CustomerB { get; init; }
        public required Guid Rental { get; init; }       // SubeA, müşteri A
    }

    private sealed record Session(HttpClient C, string Xsrf);

    private static string RandomName(string prefix) => prefix + Guid.NewGuid().ToString("N")[..10];
    private static string NewKey() => Guid.NewGuid().ToString("N");

    private async Task<Env> SetupAsync()
    {
        var tenantId = Guid.NewGuid();
        var code = RandomName("f81");
        var password = WebFixture.RandomPassword();
        var users = Enum.GetValues<Who>().ToDictionary(k => k, _ => RandomName("u"));
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
                // Şubeli operatörlere kullanıcı-bazlı finans izni: şube kapsamını izin kapısından AYRI sınamak için.
                if (who is Who.OperatorA or Who.OperatorB)
                    foreach (var perm in new[] { "FinanceWrite", "FinanceReverse" })
                        db.KullaniciIzinIstisnalari.Add(new KullaniciIzinIstisna { TenantId = tenantId, UserId = u.Id, Izin = perm, Ver = true });
                if (who == Who.AccountantNoReverse)
                    db.KullaniciIzinIstisnalari.Add(new KullaniciIzinIstisna { TenantId = tenantId, UserId = u.Id, Izin = "FinanceReverse", Ver = false });
            }
            await db.SaveChangesAsync();
        }
        await fx.MakePilotAsync(tenantId, true);

        using var host = new TestHost(fx.Pg.AppConnectionString);
        using var s = host.ScopeFor(tenantId, role: UserRole.Admin);
        var sp = s.ServiceProvider;
        var customers = sp.GetRequiredService<CustomerService>();
        var a = await customers.CreateAsync(new CustomerInput { Tip = CustomerType.Bireysel, Ad = "Hub", Soyad = "Alfa" });
        var b = await customers.CreateAsync(new CustomerInput { Tip = CustomerType.Bireysel, Ad = "Hub", Soyad = "Beta" });
        var vehicle = await sp.GetRequiredService<VehicleService>().CreateAsync(new VehicleInput { Plaka = "34 FH " + Random.Shared.Next(1000, 9999) });
        var start = TestZaman.DaysLater(1);
        var rental = await sp.GetRequiredService<RentalService>().CreateDirectAsync(new BookingInput
        {
            MusteriId = a, VehicleId = vehicle, BasTar = start, BitTar = start.AddDays(3), GunlukUcret = 100m, CikisOfisi = "SubeA",
        });
        return new Env { TenantId = tenantId, Code = code, Password = password, Users = users, CustomerA = a, CustomerB = b, Rental = rental };
    }

    /// <summary>Başka kiracı + cari (izolasyon: bu kiracının kullanıcısı o cariyi göremez).</summary>
    private async Task<Guid> ForeignCustomerAsync()
    {
        var other = Guid.NewGuid();
        var opts = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(fx.Pg.OwnerConnectionString).Options;
        await using (var db = new AppDbContext(opts, NullTenantContext.Instance, NullCurrentUser.Instance))
        {
            var code = RandomName("f8x");
            db.Tenants.Add(new Tenant { Id = other, Code = code, Name = code, IsActive = true });
            await db.SaveChangesAsync();
        }
        using var host = new TestHost(fx.Pg.AppConnectionString);
        using var s = host.ScopeFor(other, role: UserRole.Admin);
        return await s.ServiceProvider.GetRequiredService<CustomerService>()
            .CreateAsync(new CustomerInput { Tip = CustomerType.Bireysel, Ad = "Yabanci", Soyad = "Cari" });
    }

    private async Task<T> ReadAsync<T>(Env e, Func<IServiceProvider, Task<T>> read)
    {
        using var host = new TestHost(fx.Pg.AppConnectionString);
        using var s = host.ScopeFor(e.TenantId, role: UserRole.Admin);
        return await read(s.ServiceProvider);
    }

    private Task<T> DbAsync<T>(Env e, Func<AppDbContext, Task<T>> read)
        => ReadAsync(e, async sp =>
        {
            await using var db = await sp.GetRequiredService<IDbContextFactory<AppDbContext>>().CreateDbContextAsync();
            return await read(db);
        });

    private Task<decimal> BalanceAsync(Env e, Guid customer)
        => ReadAsync(e, sp => sp.GetRequiredService<CashService>().GetAccountBalanceAsync(customer));
}
