using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using RentACar.Application.Bookings;
using RentACar.Application.Common;
using RentACar.Application.Customers;
using RentACar.Application.DropDefinitions;
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

    /// <summary>Kilit altındaki saf hesap: kalemler kilit altındaki kiranın Id'sine ve kilit altında okunan tanımın KDV'sine
    /// bağlanır; gereken SYS tanımı kilit altında yoksa (ön adımdan sonra ana veri değişti) tahmin yerine 409 cakisma.</summary>
    [Fact]
    public void Fee_lines_use_locked_definition_and_reject_missing_one()
    {
        var group = new Domain.Entities.VehicleGroup { Kod = "EKO", Ad = "Ekonomik", Aktif = true, GencSurucuYas = 25, GencSurucuUcretGunluk = 100m };
        var definition = new Domain.Entities.EkHizmetTanim { Kod = FeeLineService.YoungDriverCode, Ad = "Genç sürücü ücreti", KdvOrani = 0.10m };
        var rental = new Domain.Entities.RentalContract { VehicleId = Guid.NewGuid(), MusteriId = Guid.NewGuid(), BasTar = Start, Gun = 3 };
        ContractFeeData Data(params Domain.Entities.EkHizmetTanim[] definitions) => new("EKO", [group], Start.AddYears(-22), [], definitions);

        var line = Assert.Single(FeeLineService.LinesFor(rental, Data(definition)));
        Assert.Equal(rental.Id, line.RentalId);
        Assert.Equal(definition.Id, line.EkHizmetTanimId);
        Assert.Equal(330m, line.Toplam); // 100 × 3 = 300 net + tanımın %10'u

        var ex = Assert.Throws<ConcurrentModificationException>(() => FeeLineService.LinesFor(rental, Data()));
        Assert.Equal("Ücret tanımları bu sırada değişti; işlem yazılmadı, tekrar deneyin.", ex.Message); // genel "güncel hâli yüklendi" değil
    }

    // ---- #416 L2: ücret ana verisi kilit ALTINDA, transaction'ın kendi bağlantısından okunur ----
    // Oracle (elle): baz 3 gün × 1000 = 3000; drop İSTANBUL→İZMİR 400 net → 480 brüt; genç (22 yaş < 25) 3 × 100 = 300 net →
    // 360 brüt (SYS tanımları KDV %20). Değişiklik yapılmasa GenelToplam 3840 olurdu (eski kod her durumda bunu yazıyordu).

    [Fact]
    public async Task Conversion_uses_birth_date_corrected_while_waiting_for_lock()
    {
        var (host, tenant, rid, customer, _, _) = await LockSeedAsync("34 KL 01");
        using var _h = host;
        var total = await ConvertWhileLockedAsync(host, tenant, rid, (sp, _) => SetBirthDateAsync(sp, customer, Start.AddYears(-40)));
        Assert.Equal(3480m, total); // 40 yaş → genç ücreti yok: 3000 + 480
    }

    [Fact]
    public async Task Conversion_uses_group_fee_changed_while_waiting_for_lock()
    {
        var (host, tenant, rid, _, group, _) = await LockSeedAsync("34 KL 02");
        using var _h = host;
        var total = await ConvertWhileLockedAsync(host, tenant, rid,
            (sp, _) => sp.GetRequiredService<VehicleGroupService>().UpdateAsync(group, GroupInput(200m)));
        Assert.Equal(4200m, total); // 3000 + 480 + 3 × 200 = 600 net → 720
    }

    [Fact]
    public async Task Conversion_uses_drop_definition_changed_while_waiting_for_lock()
    {
        var (host, tenant, rid, _, _, drop) = await LockSeedAsync("34 KL 03");
        using var _h = host;
        var total = await ConvertWhileLockedAsync(host, tenant, rid, (sp, _) => sp.GetRequiredService<DropDefinitionService>()
            .UpdateAsync(drop, new DropDefinitionInput { Lokasyon = "IZMIR", Sube = "ISTANBUL", Ucret = 500m }));
        Assert.Equal(3960m, total); // 3000 + 500 net → 600 + 360
    }

    [Fact]
    public async Task Conversion_uses_system_definition_vat_changed_while_waiting_for_lock()
    {
        var (host, tenant, rid, _, _, _) = await LockSeedAsync("34 KL 04");
        using var _h = host;
        var total = await ConvertWhileLockedAsync(host, tenant, rid, (sp, _) => DbAsync(sp, async db =>
        {
            (await db.EkHizmetTanimlari.SingleAsync(t => t.Kod == FeeLineService.YoungDriverCode)).KdvOrani = 0.10m;
            return await db.SaveChangesAsync();
        }));
        Assert.Equal(3810m, total); // 3000 + 480 + 300 net × 1,10 = 330
    }

    /// <summary>Rezervasyonun kendi ücret girdisi kilit beklenirken değişirse de taze değer kullanılır (önceden 409 cakisma
    /// dönüyordu, kullanıcı yeniden denemek zorundaydı). Rezervasyon kilitli olduğundan değişiklik kilidi tutan işlemde.</summary>
    [Fact]
    public async Task Conversion_uses_reservation_drop_waiver_made_while_waiting_for_lock()
    {
        var (host, tenant, rid, _, _, _) = await LockSeedAsync("34 KL 05");
        using var _h = host;
        var total = await ConvertWhileLockedAsync(host, tenant, rid, async (_, holder) =>
        {
            (await holder.Reservations.SingleAsync(r => r.Id == rid)).DropUcreti = 0m; // açık 0 = muafiyet
            await holder.SaveChangesAsync();
        });
        Assert.Equal(3360m, total); // 3000 + 360
    }

    /// <summary>Doğrudan kira: ana veri sözleşme no tahsisinden SONRA, aynı transaction'da okunur — günün sayaç satırı
    /// başka bağlantıda tutulurken düzeltilen doğum tarihi kayda girer.</summary>
    [Fact]
    public async Task Direct_rental_uses_birth_date_corrected_while_waiting_for_number()
    {
        var tenant = Guid.NewGuid();
        using var host = new TestHost(fx.AppConnectionString);
        Guid customer, vehicle;
        using (var seed = host.ScopeFor(tenant))
        {
            var sp = seed.ServiceProvider;
            (customer, var first) = await SeedAsync(sp, "34 KL 06");
            // Isınma: günün sayaç satırı ve SYS-GENC tanımı oluşur (sonraki kira sayaç satırında bekleyebilsin).
            await sp.GetRequiredService<RentalService>().CreateDirectAsync(new BookingInput
            { MusteriId = customer, VehicleId = first, BasTar = Start, BitTar = Start.AddDays(3), GunlukUcret = 1000m });
            vehicle = await sp.GetRequiredService<VehicleService>().CreateAsync(new VehicleInput { Plaka = "34 KL 07", Grup = "EKO" });
        }

        var total = await ActWhileLockedAsync(host, tenant,
            $"SELECT 1 FROM \"TenantSequences\" WHERE \"TenantId\" = {tenant} FOR UPDATE", "%INSERT INTO \"TenantSequences\"%",
            sp => sp.GetRequiredService<RentalService>().CreateDirectAsync(new BookingInput
            { MusteriId = customer, VehicleId = vehicle, BasTar = Start, BitTar = Start.AddDays(3), GunlukUcret = 1000m }),
            (sp, _) => SetBirthDateAsync(sp, customer, Start.AddYears(-40)));
        Assert.Equal(3000m, total); // genç ücreti yok (eski kod bayat 360'ı yazıp 3360 kaydediyordu)
    }

    private Task<decimal> ConvertWhileLockedAsync(TestHost host, Guid tenant, Guid rid, Func<IServiceProvider, AppDbContext, Task> mutate)
        => ActWhileLockedAsync(host, tenant,
            $"SELECT 1 FROM \"Reservations\" WHERE \"Id\" = {rid} FOR UPDATE", "%\"Reservations\"%FOR UPDATE%",
            sp => sp.GetRequiredService<ReservationService>().ConvertToRentalAsync(rid), mutate);

    /// <summary>
    /// <paramref name="lockSql"/> başka bir bağlantıda (holder) tutulurken <paramref name="act"/> başlar ve kilitte bekler
    /// (kilitten önceki adımı — SYS tanımlarının yaratılması — çoktan geçmiştir; <c>pg_stat_activity</c> ile doğrulanır);
    /// beklerken <paramref name="mutate"/> ana veriyi değiştirir; sonra kilit bırakılır. Oluşan kiranın GenelToplam'ı döner.
    /// </summary>
    private async Task<decimal> ActWhileLockedAsync(TestHost host, Guid tenant, FormattableString lockSql, string waitingQuery,
        Func<IServiceProvider, Task<Guid>> act, Func<IServiceProvider, AppDbContext, Task> mutate)
    {
        using var hs = host.ScopeFor(tenant);
        await using var holder = await hs.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>().CreateDbContextAsync();
        await using var tx = await holder.Database.BeginTransactionAsync();
        await holder.Database.ExecuteSqlInterpolatedAsync(lockSql);
        var running = Task.Run(async () =>
        {
            using var s = host.ScopeFor(tenant);
            return await act(s.ServiceProvider);
        });
        Assert.True(await WaitForLockAsync(waitingQuery), "işlem kilide ulaşmadı — yarış kurulamadı");
        await mutate(hs.ServiceProvider, holder);
        await tx.CommitAsync();
        var rentalId = await running;
        return (await hs.ServiceProvider.GetRequiredService<RentalService>().GetAsync(rentalId))!.GenelToplam;
    }

    private async Task<bool> WaitForLockAsync(string queryPattern)
    {
        await using var conn = new NpgsqlConnection(fx.AppConnectionString);
        await conn.OpenAsync();
        for (var i = 0; i < 100; i++)
        {
            await using var cmd = new NpgsqlCommand(
                "SELECT count(*) FROM pg_stat_activity WHERE datname = current_database() AND wait_event_type = 'Lock' AND query LIKE @q", conn);
            cmd.Parameters.AddWithValue("q", queryPattern);
            if ((long)(await cmd.ExecuteScalarAsync())! > 0) return true;
            await Task.Delay(100);
        }
        return false;
    }

    private static Task SetBirthDateAsync(IServiceProvider sp, Guid customer, DateTimeOffset birth) => DbAsync(sp, async db =>
    {
        (await db.Customers.SingleAsync(c => c.Id == customer)).DogumTarihi = birth;
        return await db.SaveChangesAsync();
    });

    private async Task<(TestHost Host, Guid Tenant, Guid Reservation, Guid Customer, Guid Group, Guid Drop)> LockSeedAsync(string plate)
    {
        var tenant = Guid.NewGuid();
        var host = new TestHost(fx.AppConnectionString);
        using var s = host.ScopeFor(tenant);
        var sp = s.ServiceProvider;
        var group = await sp.GetRequiredService<VehicleGroupService>().CreateAsync(GroupInput(100m));
        var drop = await sp.GetRequiredService<DropDefinitionService>().CreateAsync(new DropDefinitionInput { Lokasyon = "IZMIR", Sube = "ISTANBUL", Ucret = 400m });
        var customer = await sp.GetRequiredService<CustomerService>().CreateAsync(new CustomerInput
        { Tip = CustomerType.Bireysel, Ad = "Genc", Soyad = "KL", DogumTarihi = Start.AddYears(-22) });
        var vehicle = await sp.GetRequiredService<VehicleService>().CreateAsync(new VehicleInput { Plaka = plate, Grup = "EKO" });
        var rid = await sp.GetRequiredService<ReservationService>().CreateAsync(new BookingInput
        {
            MusteriId = customer, VehicleId = vehicle, BasTar = Start, BitTar = Start.AddDays(3),
            GunlukUcret = 1000m, CikisOfisi = "ISTANBUL", DonusOfisi = "IZMIR"
        });
        return (host, tenant, rid, customer, group, drop);
    }

    private static VehicleGroupInput GroupInput(decimal young) =>
        new() { Kod = "EKO", Ad = "Ekonomik", GencSurucuYas = 25, GencSurucuUcretGunluk = young };

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
