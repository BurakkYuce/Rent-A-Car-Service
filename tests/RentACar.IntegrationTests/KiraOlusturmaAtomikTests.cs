using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using RentACar.Application.Bookings;
using RentACar.Application.Common;
using RentACar.Application.Customers;
using RentACar.Application.RentalAddOns;
using RentACar.Application.VehicleGroups;
using RentACar.Application.Vehicles;
using RentACar.Domain.Enums;
using RentACar.Infrastructure.Persistence;
using RentACar.IntegrationTests.Infrastructure;

namespace RentACar.IntegrationTests;

/// <summary>
/// Kira oluşturma ATOMİK (DEVIR §6): kira satırı + sistem ücret satırları (SYS-*) + fatura dönem planı TEK
/// transaction'da yazılır — doğrudan kira (<see cref="RentalService.CreateDirectAsync"/>; harici API, /api/ui aynı yol)
/// ve rezervasyon→kira (<see cref="ReservationService.ConvertToRentalAsync"/>). Hata noktası, bu kiracıya özel geçici
/// BEFORE INSERT trigger'ıyla (DB hatası) ücret satırı ya da dönem planı adımında kurulur.
/// BAĞIMSIZ ORACLE (elle): grup EKO eşik 25 yaş, genç sürücü 100 NET/gün (KDV %20); 22 yaşında müşteri;
/// 3 gün × 1000 = 3000 baz → genç satırı 300 net / 360 brüt → GenelToplam 3360. Ay başından tam 2 ay → 2 dönem.
/// Hata durumunda beklenen: kira YOK, kalem YOK, plan YOK, rezervasyon dokunulmamış (Rezerv, bağsız).
/// </summary>
[Collection("postgres")]
public sealed class KiraOlusturmaAtomikTests(PostgresFixture fx)
{
    private static readonly DateTimeOffset Start = new DateTimeOffset(DateTime.UtcNow.Date, TimeSpan.Zero).AddDays(5);

    // Ay başı + 2 ay: dönem sayısı ay uzunluğundan bağımsız olarak 2 (elle).
    private static DateTimeOffset MonthStart()
    {
        var today = DateTime.UtcNow.Date;
        return new DateTimeOffset(today.Year, today.Month, 1, 0, 0, 0, TimeSpan.Zero).AddMonths(2);
    }

    [Fact]
    public async Task Direct_rental_fee_line_failure_leaves_nothing()
    {
        var tenant = Guid.NewGuid();
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(tenant);
        var sp = scope.ServiceProvider;
        var (customer, vehicle) = await SeedAsync(sp, "34 KA 01");

        await using (await FailInsertAsync("RentalAddOns", tenant))
            await Assert.ThrowsAnyAsync<Exception>(() => sp.GetRequiredService<RentalService>().CreateDirectAsync(new BookingInput
            { MusteriId = customer, VehicleId = vehicle, BasTar = Start, BitTar = Start.AddDays(3), GunlukUcret = 1000m }));

        Assert.Equal((0, 0, 0), await CountsAsync(sp));

        // Trigger kalkınca aynı istek tam yazılır (yarım kayıt engel olmadı): 3000 + 360 = 3360.
        var id = await sp.GetRequiredService<RentalService>().CreateDirectAsync(new BookingInput
        { MusteriId = customer, VehicleId = vehicle, BasTar = Start, BitTar = Start.AddDays(3), GunlukUcret = 1000m });
        var c = (await sp.GetRequiredService<RentalService>().GetAsync(id))!;
        Assert.Equal(3000m, c.Tutar);
        Assert.Equal(3360m, c.GenelToplam);
        Assert.Equal(3360m, c.Bakiye);
        var row = Assert.Single(await sp.GetRequiredService<RentalAddOnService>().ListAsync(id));
        Assert.Equal(300m, row.NetTutar);
        Assert.Equal(60m, row.KdvTutar);
        Assert.Equal(360m, row.Toplam);
    }

    [Fact]
    public async Task Direct_rental_period_plan_failure_leaves_nothing()
    {
        var tenant = Guid.NewGuid();
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(tenant);
        var sp = scope.ServiceProvider;
        var (customer, vehicle) = await SeedAsync(sp, "34 KA 02");
        var start = MonthStart();

        await using (await FailInsertAsync("FaturaDonemleri", tenant))
            await Assert.ThrowsAnyAsync<Exception>(() => sp.GetRequiredService<RentalService>().CreateDirectAsync(new BookingInput
            { MusteriId = customer, VehicleId = vehicle, BasTar = start, BitTar = start.AddMonths(2), GunlukUcret = 100m }));

        Assert.Equal((0, 0, 0), await CountsAsync(sp));

        var id = await sp.GetRequiredService<RentalService>().CreateDirectAsync(new BookingInput
        { MusteriId = customer, VehicleId = vehicle, BasTar = start, BitTar = start.AddMonths(2), GunlukUcret = 100m });
        Assert.Equal(2, await DbAsync(sp, db => db.FaturaDonemleri.CountAsync(d => d.RentalId == id)));
        Assert.Equal(1, await DbAsync(sp, db => db.RentalAddOns.CountAsync(a => a.RentalId == id)));
    }

    [Fact]
    public async Task Conversion_fee_line_failure_leaves_reservation_untouched()
    {
        var tenant = Guid.NewGuid();
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(tenant);
        var sp = scope.ServiceProvider;
        var (customer, vehicle) = await SeedAsync(sp, "34 KA 03");
        var reservations = sp.GetRequiredService<ReservationService>();
        var resId = await reservations.CreateAsync(new BookingInput
        { MusteriId = customer, VehicleId = vehicle, BasTar = Start, BitTar = Start.AddDays(3), GunlukUcret = 1000m });

        await using (await FailInsertAsync("RentalAddOns", tenant))
            await Assert.ThrowsAnyAsync<Exception>(() => reservations.ConvertToRentalAsync(resId));

        Assert.Equal((0, 0, 0), await CountsAsync(sp));
        var r = (await reservations.GetAsync(resId))!;
        Assert.Equal(ReservationStatus.Rezerv, r.Durum);
        Assert.Null(r.RentalContractId);

        // Tekrar deneme temiz çevirir: 3000 + 360 (genç satırı) = 3360.
        var rentalId = await reservations.ConvertToRentalAsync(resId);
        Assert.Equal(3360m, (await sp.GetRequiredService<RentalService>().GetAsync(rentalId))!.GenelToplam);
        Assert.Equal(ReservationStatus.KirayaCevrildi, (await reservations.GetAsync(resId))!.Durum);
    }

    [Fact]
    public async Task Conversion_builds_period_plan_in_same_transaction()
    {
        var tenant = Guid.NewGuid();
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(tenant);
        var sp = scope.ServiceProvider;
        var (customer, vehicle) = await SeedAsync(sp, "34 KA 04");
        var reservations = sp.GetRequiredService<ReservationService>();
        var start = MonthStart();
        var resId = await reservations.CreateAsync(new BookingInput
        { MusteriId = customer, VehicleId = vehicle, BasTar = start, BitTar = start.AddMonths(2), GunlukUcret = 100m });

        await using (await FailInsertAsync("FaturaDonemleri", tenant))
            await Assert.ThrowsAnyAsync<Exception>(() => reservations.ConvertToRentalAsync(resId));
        Assert.Equal((0, 0, 0), await CountsAsync(sp));
        Assert.Equal(ReservationStatus.Rezerv, (await reservations.GetAsync(resId))!.Durum);

        // Uzun kira dönüşümde de doğrudan kirayla AYNI plana sahip olur (önceden plan hiç kurulmuyordu).
        var rentalId = await reservations.ConvertToRentalAsync(resId);
        Assert.Equal(2, await DbAsync(sp, db => db.FaturaDonemleri.CountAsync(d => d.RentalId == rentalId)));
    }

    /// <summary>
    /// #416 adversarial M1: geçici 40P01 (deadlock) PgRetry ile yeniden denenir ve BAŞARIR — önceden doğrudan kirada
    /// ikinci deneme aynı kalem nesnesini iki kez ekleyip 23505 PK_RentalAddOns ile 500 dönüyordu. Hata kira ve kalem
    /// SaveChanges'a girdikten SONRA (plan eklemesinde) ve kalem eklemesinde ayrı ayrı. Sözleşme no boşluksuz.
    /// </summary>
    [Fact]
    public async Task Direct_rental_retries_transient_deadlock_cleanly()
    {
        var tenant = Guid.NewGuid();
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(tenant);
        var sp = scope.ServiceProvider;
        var (customer, vehicle) = await SeedAsync(sp, "34 KA 05");
        var rentals = sp.GetRequiredService<RentalService>();
        var start = MonthStart();

        await using (await DeadlockOnceAsync("FaturaDonemleri", tenant))
            await rentals.CreateDirectAsync(new BookingInput
            { MusteriId = customer, VehicleId = vehicle, BasTar = start, BitTar = start.AddMonths(2), GunlukUcret = 100m });
        Assert.Equal((1, 1, 2), await CountsAsync(sp));

        var second = await sp.GetRequiredService<VehicleService>().CreateAsync(new VehicleInput { Plaka = "34 KA 06", Grup = "EKO" });
        Guid id;
        await using (await DeadlockOnceAsync("RentalAddOns", tenant))
            id = await rentals.CreateDirectAsync(new BookingInput
            { MusteriId = customer, VehicleId = second, BasTar = Start, BitTar = Start.AddDays(3), GunlukUcret = 1000m });
        Assert.Equal((2, 2, 2), await CountsAsync(sp));
        Assert.Equal(3360m, (await rentals.GetAsync(id))!.GenelToplam); // 3000 + 360 — kalem bir kez

        // Boşluksuz no: her gün öneki içinde sıra 1..k (geri alınan deneme numara tüketmez).
        var nos = await DbAsync(sp, db => db.Rentals.Select(r => r.SozlesmeNo).ToListAsync());
        Assert.All(nos.GroupBy(n => n[..^3]), g => Assert.Equal(
            Enumerable.Range(1, g.Count()), g.Select(n => int.Parse(n[^3..])).Order()));
    }

    /// <summary>
    /// #416 adversarial L1: çevirme rezervasyon kilidi + transaction bağlantısını tutarken ek bağlantı AÇMAZ (ücret
    /// girdileri kilitten önce okunur). Önceden havuz=2'de 4 paralel çevirme havuz tükenmesiyle zaman aşımına düşüyordu.
    /// </summary>
    [Fact]
    public async Task Parallel_conversions_do_not_exhaust_small_pool()
    {
        var tenant = Guid.NewGuid();
        var reservationIds = new List<Guid>();
        using (var seedHost = new TestHost(fx.AppConnectionString))
        using (var seed = seedHost.ScopeFor(tenant))
        {
            var sp = seed.ServiceProvider;
            var (customer, first) = await SeedAsync(sp, "34 KA 10");
            for (var i = 0; i < 4; i++)
            {
                var vehicle = i == 0 ? first
                    : await sp.GetRequiredService<VehicleService>().CreateAsync(new VehicleInput { Plaka = $"34 KA 1{i}", Grup = "EKO" });
                reservationIds.Add(await sp.GetRequiredService<ReservationService>().CreateAsync(new BookingInput
                { MusteriId = customer, VehicleId = vehicle, BasTar = Start, BitTar = Start.AddDays(3), GunlukUcret = 1000m }));
            }
        }

        using var host = new TestHost(fx.AppConnectionString + ";Maximum Pool Size=2;Timeout=5");
        var results = await Task.WhenAll(reservationIds.Select(rid => Task.Run(async () =>
        {
            try
            {
                using var s = host.ScopeFor(tenant);
                await s.ServiceProvider.GetRequiredService<ReservationService>().ConvertToRentalAsync(rid);
                return "ok";
            }
            catch (Exception ex) { return ex.GetType().Name + ": " + (ex.InnerException?.Message ?? ex.Message); }
        })));
        Assert.All(results, r => Assert.Equal("ok", r));

        using var check = host.ScopeFor(tenant);
        Assert.Equal((4, 4, 0), await CountsAsync(check.ServiceProvider)); // 4 kira × 1 genç satırı, kısa kira → plan yok
    }

    /// <summary>L1 tutarlılığı: kilit altında okunan sözleşmenin ücret girdisi ön okumadan farklıysa (rezervasyon arada
    /// güncellendi) eski girdiyle ücret yazılmaz — 409 cakisma. Aynı girdide kalemler kilit altındaki kiranın Id'sine bağlanır.</summary>
    [Fact]
    public void Fee_lines_reject_inputs_changed_after_prepare()
    {
        var definition = new Domain.Entities.EkHizmetTanim { Kod = FeeLineService.YoungDriverCode, Ad = "Genç sürücü ücreti", KdvOrani = 0.20m };
        var prepared = new Domain.Entities.RentalContract { VehicleId = Guid.NewGuid(), MusteriId = Guid.NewGuid(), BasTar = Start, Gun = 3 };
        var fees = new ContractFees(FeeInputs.Of(prepared),
            [(definition, new SistemUcretSatiri(FeeLineService.YoungDriverCode, "Genç sürücü ücreti", 100m, 3, null))]);

        var locked = new Domain.Entities.RentalContract { VehicleId = prepared.VehicleId, MusteriId = prepared.MusteriId, BasTar = Start, Gun = 3 };
        var line = Assert.Single(fees.LinesFor(locked));
        Assert.Equal(locked.Id, line.RentalId);
        Assert.Equal(360m, line.Toplam); // 100 × 3 = 300 net + %20

        var changed = new Domain.Entities.RentalContract { VehicleId = prepared.VehicleId, MusteriId = prepared.MusteriId, BasTar = Start, Gun = 4 };
        Assert.Throws<ConcurrentModificationException>(() => fees.LinesFor(changed));
    }

    private static async Task<(Guid Customer, Guid Vehicle)> SeedAsync(IServiceProvider sp, string plate)
    {
        await sp.GetRequiredService<VehicleGroupService>().CreateAsync(new VehicleGroupInput
        { Kod = "EKO", Ad = "Ekonomik", GencSurucuYas = 25, GencSurucuUcretGunluk = 100m });
        var vehicle = await sp.GetRequiredService<VehicleService>().CreateAsync(new VehicleInput { Plaka = plate, Grup = "EKO" });
        var customer = await sp.GetRequiredService<CustomerService>().CreateAsync(new CustomerInput
        { Tip = CustomerType.Bireysel, Ad = "Genc", Soyad = "KA", DogumTarihi = DateTimeOffset.UtcNow.AddYears(-22) });
        return (customer, vehicle);
    }

    private static async Task<T> DbAsync<T>(IServiceProvider sp, Func<AppDbContext, Task<T>> query)
    {
        await using var db = await sp.GetRequiredService<IDbContextFactory<AppDbContext>>().CreateDbContextAsync();
        return await query(db);
    }

    private static async Task<(int Rentals, int AddOns, int Periods)> CountsAsync(IServiceProvider sp)
    {
        await using var db = await sp.GetRequiredService<IDbContextFactory<AppDbContext>>().CreateDbContextAsync();
        return (await db.Rentals.CountAsync(), await db.RentalAddOns.CountAsync(), await db.FaturaDonemleri.CountAsync());
    }

    /// <summary>Bu kiracının <paramref name="table"/> eklemesini DB'de patlatan geçici trigger; dispose kaldırır.</summary>
    private async Task<IAsyncDisposable> FailInsertAsync(string table, Guid tenant)
    {
        var name = "test_kira_atomik_" + tenant.ToString("N")[..12];
        await ExecAsync(
            $"CREATE OR REPLACE FUNCTION {name}() RETURNS trigger AS $$ BEGIN IF NEW.\"TenantId\" = '{tenant}' THEN RAISE EXCEPTION 'test: {table}'; END IF; RETURN NEW; END; $$ LANGUAGE plpgsql; " +
            $"CREATE TRIGGER {name} BEFORE INSERT ON \"{table}\" FOR EACH ROW EXECUTE FUNCTION {name}();");
        return new Drop(() => ExecAsync($"DROP TRIGGER IF EXISTS {name} ON \"{table}\"; DROP FUNCTION IF EXISTS {name}();"));
    }

    /// <summary>Bu kiracının ilk <paramref name="table"/> eklemesinde YALNIZ BİR KEZ sentetik 40P01 (deadlock).</summary>
    private async Task<IAsyncDisposable> DeadlockOnceAsync(string table, Guid tenant)
    {
        var name = "test_kira_dl_" + tenant.ToString("N")[..12];
        await ExecAsync(
            $"CREATE SEQUENCE {name}_s; " +
            $"CREATE FUNCTION {name}() RETURNS trigger SECURITY DEFINER AS $$ BEGIN IF NEW.\"TenantId\" = '{tenant}' THEN " +
            $"IF nextval('{name}_s') = 1 THEN RAISE EXCEPTION 'test deadlock' USING ERRCODE = '40P01'; END IF; END IF; RETURN NEW; END; $$ LANGUAGE plpgsql; " +
            $"CREATE TRIGGER {name} BEFORE INSERT ON \"{table}\" FOR EACH ROW EXECUTE FUNCTION {name}();");
        return new Drop(() => ExecAsync($"DROP TRIGGER IF EXISTS {name} ON \"{table}\"; DROP FUNCTION IF EXISTS {name}(); DROP SEQUENCE IF EXISTS {name}_s;"));
    }

    private async Task ExecAsync(string sql)
    {
        await using var owner = new NpgsqlConnection(fx.OwnerConnectionString);
        await owner.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, owner);
        await cmd.ExecuteNonQueryAsync();
    }

    private sealed class Drop(Func<Task> drop) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync() => await drop();
    }
}
