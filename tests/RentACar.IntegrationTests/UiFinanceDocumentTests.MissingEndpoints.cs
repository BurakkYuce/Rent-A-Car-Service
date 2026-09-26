using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RentACar.Application.Bookings;
using RentACar.Application.ExpenseCategories;
using RentACar.Application.VehicleSales;
using RentACar.Domain.Entities;
using RentACar.Domain.Enums;
using RentACar.Infrastructure.Persistence;
using RentACar.IntegrationTests.Infrastructure;

namespace RentACar.IntegrationTests;

/// <summary>
/// #300 eksik uçlar (DEVIR §6): FinanceWrite'lı kira seçimi + gider "Sözleşme" sütunu, gider kategorisi seçimi, fatura döviz
/// özeti, satılabilir araç seçimi ve ceza ödemesinde <c>mevcut.belgeNo</c>. Beklenen değerler elle kurulan senaryodan
/// (fatura tutarları aşağıda satır satır toplandı); başka şube süzülür/403, başka kiracı görünmez/400.
/// </summary>
public sealed partial class UiFinanceDocumentTests
{
    /// <summary>Tek izin istisnalı ek kullanıcı (ör. Muhasebe + FinanceWrite YASAK → ne OW ne FW).</summary>
    private async Task<Session> ExtraLoginAsync(Env e, UserRole role, string? branch, string permission, bool grant)
    {
        var name = Random10("x");
        var opts = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(fx.Pg.OwnerConnectionString).Options;
        await using (var db = new AppDbContext(opts, NullTenantContext.Instance, NullCurrentUser.Instance))
        {
            var u = new User { TenantId = e.TenantId, UserName = name, DisplayName = name, Rol = role, AtanmisSube = branch, IsActive = true };
            u.PasswordHash = fx.Web.Services.GetRequiredService<IPasswordHasher<User>>().HashPassword(u, e.Password);
            db.Users.Add(u);
            db.KullaniciIzinIstisnalari.Add(new KullaniciIzinIstisna { TenantId = e.TenantId, UserId = u.Id, Izin = permission, Ver = grant });
            await db.SaveChangesAsync();
        }
        return await LoginAsync(e, name, name);
    }

    private Task<Session> NoOpsNoFinanceAsync(Env e) => ExtraLoginAsync(e, UserRole.Muhasebe, null, "FinanceWrite", false);

    private static async Task<List<Guid>> IdsAsync(HttpResponseMessage r)
        => (await Ok(r)).EnumerateArray().Select(x => x.GetProperty("id").GetGuid()).ToList();

    private Task<Guid> BranchBRentalAsync(Env e)
        => ReadAsync(e.TenantId, async sp => await sp.GetRequiredService<RentalService>().CreateDirectAsync(new BookingInput
        {
            MusteriId = e.Customer, VehicleId = await VehicleAsync(sp, "SubeB"), BasTar = TestZaman.DaysLater(20),
            BitTar = TestZaman.DaysLater(22), GunlukUcret = 100m, CikisOfisi = "SubeB",
        }));

    [Fact]
    public async Task Rental_pick_is_open_to_finance_write_and_keeps_branch_scope()
    {
        var e = await SetupAsync();
        var rentalB = await BranchBRentalAsync(e);
        var contract = await DbAsync(e, db => db.Rentals.Where(r => r.Id == e.Rental).Select(r => r.SozlesmeNo).SingleAsync());

        var accountant = await LoginAsync(e, Who.Accountant); // FinanceWrite, OperationsWrite YOK
        var all = await Ok(await GetAsync(accountant, "/crm/secim/kira"));
        var mine = all.EnumerateArray().Single(x => x.GetProperty("id").GetGuid() == e.Rental);
        Assert.Equal(contract, mine.GetProperty("sozlesmeNo").GetString());
        Assert.Contains(rentalB, all.EnumerateArray().Select(x => x.GetProperty("id").GetGuid()));

        // Şube kapsamı korunur: SubeB operatörü SubeA kirasını görmez.
        var opB = await IdsAsync(await GetAsync(await LoginAsync(e, Who.OperatorB), "/crm/secim/kira"));
        Assert.Contains(rentalB, opB);
        Assert.DoesNotContain(e.Rental, opB);
        // Ne OperationsWrite ne FinanceWrite → 403.
        await Problem(await GetAsync(await NoOpsNoFinanceAsync(e), "/crm/secim/kira"), HttpStatusCode.Forbidden, "yetki_yok");
        // Başka kiracı: kira hiç görünmez.
        var other = await SetupAsync();
        var foreign = await IdsAsync(await GetAsync(await LoginAsync(other, Who.Accountant), "/crm/secim/kira"));
        Assert.DoesNotContain(e.Rental, foreign);
        Assert.DoesNotContain(rentalB, foreign);
    }

    [Fact]
    public async Task Expense_contract_field_is_listed_with_contract_number_and_scoped()
    {
        var e = await SetupAsync();
        var contract = await DbAsync(e, db => db.Rentals.Where(r => r.Id == e.Rental).Select(r => r.SozlesmeNo).SingleAsync());
        var accountant = await LoginAsync(e, Who.Accountant);
        var id = await IdOf(await PostAsync(accountant, "/giderler", new
        {
            tip = "Genel", netTutar = 10m, kdvOrani = 0m, odemeYontemi = "Nakit", kiraId = e.Rental,
        }, NewKey()));
        var row = (await Ok(await GetAsync(accountant, "/giderler"))).GetProperty("kayitlar").EnumerateArray()
            .Single(x => x.GetProperty("id").GetGuid() == id);
        Assert.Equal(e.Rental, row.GetProperty("kiraId").GetGuid());
        Assert.Equal(contract, row.GetProperty("sozlesmeNo").GetString());
        var plain = await IdOf(await PostAsync(accountant, "/giderler", new
        {
            tip = "Genel", netTutar = 5m, kdvOrani = 0m, odemeYontemi = "Nakit",
        }, NewKey()));
        var plainRow = (await Ok(await GetAsync(accountant, $"/giderler/{plain}"))).GetProperty("gider");
        Assert.Equal(JsonValueKind.Null, plainRow.GetProperty("sozlesmeNo").ValueKind);

        // Başka şubenin kirası: 403 (kendi şubesine yazsa da); başka kiracının kirası: 400 errors[kiraId].
        await Problem(await PostAsync(await LoginAsync(e, Who.OperatorB), "/giderler", new
        {
            tip = "Genel", netTutar = 10m, kdvOrani = 0m, odemeYontemi = "Nakit", sube = "SubeB", kiraId = e.Rental,
        }, NewKey()), HttpStatusCode.Forbidden, "yetki_yok");
        var other = await SetupAsync();
        await Problem(await PostAsync(await LoginAsync(other, Who.Accountant), "/giderler", new
        {
            tip = "Genel", netTutar = 10m, kdvOrani = 0m, odemeYontemi = "Nakit", kiraId = e.Rental,
        }, NewKey()), HttpStatusCode.BadRequest, "dogrulama", "kiraId");
        Assert.Equal(2, await DbAsync(e, db => db.Expenses.CountAsync()));
    }

    [Fact]
    public async Task Expense_category_pick_is_readable_with_finance_write_active_only()
    {
        var e = await SetupAsync();
        var (active, passive) = await ReadAsync(e.TenantId, async sp =>
        {
            var svc = sp.GetRequiredService<ExpenseCategoryService>();
            return (await svc.CreateAsync(new ExpenseCategoryInput { Kod = "YKT", Ad = "Yakıt", Aktif = true }),
                await svc.CreateAsync(new ExpenseCategoryInput { Kod = "ESK", Ad = "Eski kalem", Aktif = false }));
        });

        var accountant = await LoginAsync(e, Who.Accountant);
        var items = await Ok(await GetAsync(accountant, "/secim/gider-kategorisi?q=yak"));
        var only = Assert.Single(items.EnumerateArray());
        Assert.Equal(active, only.GetProperty("id").GetGuid());
        Assert.Equal("Yakıt", only.GetProperty("etiket").GetString());
        Assert.Equal("YKT", only.GetProperty("kod").GetString());
        Assert.DoesNotContain(passive, await IdsAsync(await GetAsync(accountant, "/secim/gider-kategorisi")));
        // Tanım ekranı (yazma yüzeyi) değişmedi: Muhasebe'ye hâlâ kapalı.
        await Problem(await GetAsync(accountant, "/gider-turleri"), HttpStatusCode.Forbidden, "yetki_yok");
        Assert.Contains(active, await IdsAsync(await GetAsync(await LoginAsync(e, Who.OperatorPlain), "/secim/gider-kategorisi")));
        await Problem(await GetAsync(await NoOpsNoFinanceAsync(e), "/secim/gider-kategorisi"), HttpStatusCode.Forbidden, "yetki_yok");
        var other = await SetupAsync();
        Assert.DoesNotContain(active, await IdsAsync(await GetAsync(await LoginAsync(other, Who.Accountant), "/secim/gider-kategorisi")));

        // Gelen e-faturaya bağlanan kategori: satırda adı döner (bağlama formunun seçim etiketi).
        var incoming = await IdOf(await PostAsync(accountant, "/gelen-efatura", new
        {
            ettn = Guid.NewGuid().ToString(), gonderenVkn = "1234567890", gonderenUnvan = "Akaryakıt A.Ş.", netTutar = 100m,
            kdvTutar = 20m, genelToplam = 120m,
        }));
        await Ok(await PostAsync(accountant, $"/gelen-efatura/{incoming}/onayla", null));
        var version = (await Ok(await GetAsync(accountant, $"/gelen-efatura/{incoming}"))).GetProperty("surum").GetString();
        await Ok(await SendAsync(accountant, HttpMethod.Put, $"/gelen-efatura/{incoming}/bag",
            new { surum = version, kdv20Matrah = 100m, kdv20 = 20m, giderKategoriId = active }, null));
        var linked = (await Ok(await GetAsync(accountant, $"/gelen-efatura/{incoming}"))).GetProperty("fatura");
        Assert.Equal(active, linked.GetProperty("giderKategoriId").GetGuid());
        Assert.Equal("Yakıt", linked.GetProperty("giderKategoriAd").GetString());
    }

    [Fact]
    public async Task Sellable_vehicle_pick_excludes_sold_and_other_branch()
    {
        var e = await SetupAsync();
        var (free, sold, branchB) = await ReadAsync(e.TenantId, async sp =>
        {
            var a = await VehicleAsync(sp, "SubeA");
            var s = await VehicleAsync(sp, "SubeA");
            var b = await VehicleAsync(sp, "SubeB");
            await sp.GetRequiredService<VehicleSaleService>().CreateAsync(new VehicleSaleInput
            {
                VehicleId = s, AliciCariId = e.Customer, SatisNet = 1000m, KdvOrani = 0.20m,
            });
            return (a, s, b);
        });

        var admin = await IdsAsync(await GetAsync(await LoginAsync(e, Who.Admin), "/secim/satilabilir-arac"));
        Assert.Equal(new[] { e.RentalVehicle, free, branchB }.OrderBy(x => x), admin.OrderBy(x => x));
        var opA = await IdsAsync(await GetAsync(await LoginAsync(e, Who.OperatorA), "/secim/satilabilir-arac"));
        Assert.Equal(new[] { e.RentalVehicle, free }.OrderBy(x => x), opA.OrderBy(x => x));
        Assert.DoesNotContain(sold, opA);
        // Satış FinanceWrite ister; seçim de öyle (yalnız OperationsWrite'lı operatör 403).
        await Problem(await GetAsync(await LoginAsync(e, Who.OperatorPlain), "/secim/satilabilir-arac"), HttpStatusCode.Forbidden, "yetki_yok");
        var other = await SetupAsync();
        var foreign = await IdsAsync(await GetAsync(await LoginAsync(other, Who.Admin), "/secim/satilabilir-arac"));
        Assert.DoesNotContain(free, foreign);
        Assert.Equal([other.RentalVehicle], foreign);
    }
}
