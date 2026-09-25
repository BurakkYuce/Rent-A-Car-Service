using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using RentACar.Application.Baflar;
using RentACar.Application.Bookings;
using RentACar.Application.Common;
using RentACar.Domain.Common;
using RentACar.Domain.Entities;
using RentACar.Infrastructure.Persistence;
using RentACar.Infrastructure.Migrations;
using RentACar.IntegrationTests.Infrastructure;

namespace RentACar.IntegrationTests;

/// <summary>
/// Karar (3), 2026-09-25 — TEK iç yakıt ölçeği 0–12. Harici JWT API (<c>/api/v1/rentals</c>) yüzde sözleşmesini
/// korur ve sınırda çevirir; servisler 0–12 dışını reddeder; mevcut veri migration'la çevrilir.
/// BAĞIMSIZ ORACLE: tüm beklenen değerler elle hesaplandı (v × 12 / 100 ve t × 100 / 12, en yakına).
/// </summary>
[Collection("postgres")]
public sealed class YakitOlcegiTests(PostgresFixture fx)
{
    private static readonly DateTimeOffset Bas = new(2026, 9, 1, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Bit = new(2026, 9, 5, 9, 0, 0, TimeSpan.Zero);

    // ---- saf çeviri tablosu (elle) ----
    [Theory]
    [InlineData(0, 0)]      // 0
    [InlineData(4, 0)]      // 0,48 → 0
    [InlineData(5, 1)]      // 0,60 → 1
    [InlineData(25, 3)]     // 3,00
    [InlineData(45, 5)]     // 5,40 → 5
    [InlineData(50, 6)]     // 6,00
    [InlineData(80, 10)]    // 9,60 → 10
    [InlineData(100, 12)]   // 12,00
    public void Yuzde_on_ikiye(int yuzde, int beklenen) => Assert.Equal(beklenen, YakitOlcegi.YuzdedenOnIkiye(yuzde));

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 8)]      // 8,33 → 8
    [InlineData(3, 25)]     // 25,00
    [InlineData(5, 42)]     // 41,67 → 42
    [InlineData(6, 50)]     // 50,00
    [InlineData(10, 83)]    // 83,33 → 83
    [InlineData(12, 100)]
    public void On_ikiden_yuzdeye(int onIkide, int beklenen) => Assert.Equal(beklenen, YakitOlcegi.OnIkidenYuzdeye(onIkide));

    // Kira servisi müşteri/araç varlığını girişte doğrular (#328) → her kira kiracıda GERÇEK araç + cari ile.
    private static async Task<BookingInput> InputAsync(IServiceProvider sp, decimal yakitBirim)
        => Input(await TestArac.YeniAsync(sp), await TestCari.YeniAsync(sp), yakitBirim);

    private static BookingInput Input(Guid vehicle, Guid cari, decimal yakitBirim) => new()
    {
        MusteriId = cari, VehicleId = vehicle, BasTar = Bas, BitTar = Bit,
        GunlukUcret = 100m, KmLimit = 0, FazlaKmUcret = 0m, YakitBirimUcret = yakitBirim
    };

    // ---- eksik yakıt bedeli 0–12 ölçeğinde (para) ----
    [Fact]
    public async Task Eksik_yakit_bedeli_on_ikide_bir_birimle()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var svc = scope.ServiceProvider.GetRequiredService<RentalService>();

        var id = await svc.CreateDirectAsync(await InputAsync(scope.ServiceProvider, yakitBirim: 150m));
        Assert.True(await svc.DeliverAsync(id, cikisKm: 1000, cikisYakit: 12)); // dolu depo
        Assert.True(await svc.ReturnAsync(id, donusKm: 1000, donusYakit: 7, gercekDonus: Bit));

        var c = (await svc.GetAsync(id))!;
        Assert.Equal(5, c.EksikYakit);          // 12 − 7
        Assert.Equal(750m, c.YakitBedeli);      // 5 × 150
        Assert.Equal(1150m, c.GenelToplam);     // 4 gün × 100 = 400 + 750
    }

    [Fact]
    public async Task Servis_12_ustunu_ve_negatifi_reddeder()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var svc = scope.ServiceProvider.GetRequiredService<RentalService>();

        var id = await svc.CreateDirectAsync(await InputAsync(scope.ServiceProvider, yakitBirim: 10m));
        await Assert.ThrowsAsync<ValidationException>(() => svc.DeliverAsync(id, 1000, 13)); // eskiden 0–100 geçiyordu
        await Assert.ThrowsAsync<ValidationException>(() => svc.DeliverAsync(id, 1000, -1));
        Assert.True(await svc.DeliverAsync(id, 1000, 12));
        await Assert.ThrowsAsync<ValidationException>(() => svc.ReturnAsync(id, 1100, 13, Bit));
        Assert.False((await svc.PreviewReturnAsync(id, 1100, 13, Bit)).Ok);
    }

    [Fact]
    public async Task Baf_servisi_0_12_disini_reddeder()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var svc = scope.ServiceProvider.GetRequiredService<BafService>();

        await Assert.ThrowsAsync<ValidationException>(() => svc.CreateAsync(new BafInput
            { PersonelId = Guid.NewGuid(), VehicleId = Guid.NewGuid(), CikisKm = 100, CikisYakit = 13 }));
        var id = await svc.CreateAsync(new BafInput
            { PersonelId = Guid.NewGuid(), VehicleId = Guid.NewGuid(), CikisKm = 100, CikisYakit = 12 });
        await Assert.ThrowsAsync<ValidationException>(() => svc.TeslimAlAsync(id, 150, donusYakit: 13));
        await Assert.ThrowsAsync<ValidationException>(() => svc.TeslimAlKilitliAsync(id, 150, 13, null, null, null));
        Assert.True(await svc.TeslimAlAsync(id, 150, donusYakit: 0));
        Assert.Equal(0, (await svc.GetAsync(id))!.DonusYakit);
    }

    // ---- harici API: yüzde sözleşmesi, sınırda çeviri ----
    private static async Task<Guid> IdAsync(HttpResponseMessage r)
    {
        Assert.Equal(HttpStatusCode.Created, r.StatusCode);
        using var doc = JsonDocument.Parse(await r.Content.ReadAsStringAsync());
        return doc.RootElement.GetProperty("id").GetGuid();
    }

    private async Task<(int? Cikis, int? Donus)> HamYakitAsync(Guid tenant, Guid rentalId)
    {
        await using var con = new NpgsqlConnection(fx.OwnerConnectionString);
        await con.OpenAsync();
        await using var tx = await con.BeginTransactionAsync();
        await using (var set = new NpgsqlCommand("SELECT set_config('app.tenant_id', @t, true)", con, tx))
        {
            set.Parameters.AddWithValue("t", tenant.ToString());
            await set.ExecuteScalarAsync();
        }
        await using var cmd = new NpgsqlCommand("SELECT \"CikisYakit\", \"DonusYakit\" FROM \"Rentals\" WHERE \"Id\" = @id", con, tx);
        cmd.Parameters.AddWithValue("id", rentalId);
        await using var rd = await cmd.ExecuteReaderAsync();
        Assert.True(await rd.ReadAsync());
        return (rd.IsDBNull(0) ? null : rd.GetInt32(0), rd.IsDBNull(1) ? null : rd.GetInt32(1));
    }

    [Fact]
    public async Task Harici_api_yuzde_alir_on_ikide_bir_saklar_yuzde_dondurur()
    {
        var code = $"yk{Guid.NewGuid():N}";
        await ApiSeed.TenantUserAsync(fx.OwnerConnectionString, code, "umit", "p");
        using var api = new ApiFactory(fx.AppConnectionString);
        var c = await api.LoginClientAsync(code, "umit", "p");
        var tenant = await TenantIdAsync(code);

        var vId = await IdAsync(await c.PostAsJsonAsync("/api/v1/vehicles",
            new { plaka = "34YK" + Random.Shared.Next(100, 999), durum = "Musait", km = 0, yakit = "Benzin" }));
        var mId = await IdAsync(await c.PostAsJsonAsync("/api/v1/customers", new { tip = "Bireysel", ad = "Yakıt" }));
        var bas = DateTimeOffset.UtcNow.AddDays(2);
        var bit = bas.AddDays(3);
        var kira = await IdAsync(await c.PostAsJsonAsync("/api/v1/rentals",
            new { musteriId = mId, vehicleId = vId, basTar = bas, bitTar = bit, gunlukUcret = 100m, yakitBirimUcret = 100m }));
        // Birim ücret sınırda yüzde puanı başından on ikide bir başına: 100 × 100/12 = 833,3333 (4 hane)
        Assert.Equal(833.3333m, await HamBirimAsync(tenant, kira));
        var olustu = await c.GetFromJsonAsync<JsonElement>($"/api/v1/rentals/{kira}");
        Assert.Equal(100m, olustu.GetProperty("yakitBirimUcret").GetDecimal()); // 833,3333 × 12/100 = 99,999996 → 100,00

        // 101 / -1 → 400; hiçbir şey yazılmaz
        Assert.Equal(HttpStatusCode.BadRequest,
            (await c.PostAsJsonAsync($"/api/v1/rentals/{kira}/deliver", new { cikisKm = 1000, cikisYakit = 101 })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await c.PostAsJsonAsync($"/api/v1/rentals/{kira}/deliver", new { cikisKm = 1000, cikisYakit = -1 })).StatusCode);
        Assert.Equal((null, null), await HamYakitAsync(tenant, kira));

        // %50 → 6 saklanır → %50 okunur
        var teslim = await c.PostAsJsonAsync($"/api/v1/rentals/{kira}/deliver", new { cikisKm = 1000, cikisYakit = 50 });
        Assert.Equal(HttpStatusCode.OK, teslim.StatusCode);
        Assert.Equal((6, (int?)null), await HamYakitAsync(tenant, kira));
        var oku = await c.GetFromJsonAsync<JsonElement>($"/api/v1/rentals/{kira}");
        Assert.Equal(50, oku.GetProperty("cikisYakit").GetInt32());

        Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsJsonAsync($"/api/v1/rentals/{kira}/return",
            new { donusKm = 1000, donusYakit = 101, gercekDonus = bit })).StatusCode);

        // ESKİ SÖZLEŞME ORACLE'I: %50 → %25 = 25 puan eksik × 100 = 2500,00.
        // (İç: 6 − 3 = 3 × 833,3333 = 2499,9999 → satır 2 haneye → 2500,00.)
        var donus = await c.PostAsJsonAsync($"/api/v1/rentals/{kira}/return", new { donusKm = 1000, donusYakit = 25, gercekDonus = bit });
        Assert.Equal(HttpStatusCode.OK, donus.StatusCode);
        Assert.Equal(((int?)6, (int?)3), await HamYakitAsync(tenant, kira));
        var j = await donus.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(50, j.GetProperty("cikisYakit").GetInt32());
        Assert.Equal(25, j.GetProperty("donusYakit").GetInt32());
        Assert.Equal(25, j.GetProperty("eksikYakit").GetInt32());        // yüzde puanı (3/12)
        Assert.Equal(100m, j.GetProperty("yakitBirimUcret").GetDecimal());
        Assert.Equal(2500.00m, j.GetProperty("yakitBedeli").GetDecimal());

        // İkinci senaryo: birim 10, %100 → %50 = 50 puan × 10 = 500,00.
        // (İç: 10 → 83,3333; 12 − 6 = 6 × 83,3333 = 499,9998 → 500,00.)
        var vId2 = await IdAsync(await c.PostAsJsonAsync("/api/v1/vehicles",
            new { plaka = "34YL" + Random.Shared.Next(100, 999), durum = "Musait", km = 0, yakit = "Benzin" }));
        var kira2 = await IdAsync(await c.PostAsJsonAsync("/api/v1/rentals",
            new { musteriId = mId, vehicleId = vId2, basTar = bas, bitTar = bit, gunlukUcret = 100m, yakitBirimUcret = 10m }));
        Assert.Equal(83.3333m, await HamBirimAsync(tenant, kira2));
        Assert.Equal(HttpStatusCode.OK,
            (await c.PostAsJsonAsync($"/api/v1/rentals/{kira2}/deliver", new { cikisKm = 1000, cikisYakit = 100 })).StatusCode);
        var j2 = await (await c.PostAsJsonAsync($"/api/v1/rentals/{kira2}/return",
            new { donusKm = 1000, donusYakit = 50, gercekDonus = bit })).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(50, j2.GetProperty("eksikYakit").GetInt32());
        Assert.Equal(10m, j2.GetProperty("yakitBirimUcret").GetDecimal());
        Assert.Equal(500.00m, j2.GetProperty("yakitBedeli").GetDecimal());

        // Rezervasyon da aynı sınırdan geçer (kiraya çevrilince birim ücret kopyalanır).
        var rez = await c.PostAsJsonAsync("/api/v1/reservations", new
        {
            musteriId = mId, vehicleId = vId, basTar = bit.AddDays(5), bitTar = bit.AddDays(7), gunlukUcret = 50m, yakitBirimUcret = 10m
        });
        Assert.Equal(HttpStatusCode.Created, rez.StatusCode);
        Assert.Equal(10m, (await rez.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("yakitBirimUcret").GetDecimal());

        // Taşma çiti: decimal/numeric(19,4) taşması 500 değil 400.
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsJsonAsync("/api/v1/rentals", new
        {
            musteriId = mId, vehicleId = vId2, basTar = bit.AddDays(10), bitTar = bit.AddDays(11), gunlukUcret = 100m,
            yakitBirimUcret = 10_000_000_000m
        })).StatusCode);
    }

    private async Task<decimal> HamBirimAsync(Guid tenant, Guid rentalId)
    {
        await using var con = new NpgsqlConnection(fx.OwnerConnectionString);
        await con.OpenAsync();
        await using var tx = await con.BeginTransactionAsync();
        await using (var set = new NpgsqlCommand("SELECT set_config('app.tenant_id', @t, true)", con, tx))
        {
            set.Parameters.AddWithValue("t", tenant.ToString());
            await set.ExecuteScalarAsync();
        }
        await using var cmd = new NpgsqlCommand("SELECT \"YakitBirimUcret\" FROM \"Rentals\" WHERE \"Id\" = @id", con, tx);
        cmd.Parameters.AddWithValue("id", rentalId);
        return (decimal)(await cmd.ExecuteScalarAsync())!;
    }

    private async Task<Guid> TenantIdAsync(string code)
    {
        await using var con = new NpgsqlConnection(fx.OwnerConnectionString);
        await con.OpenAsync();
        await using var cmd = new NpgsqlCommand("SELECT \"Id\" FROM \"Tenants\" WHERE \"Code\" = @c", con);
        cmd.Parameters.AddWithValue("c", code);
        return (Guid)(await cmd.ExecuteScalarAsync())!;
    }

    // ---- veri migration'ı: GERÇEK SQL, FORCE RLS altında racar_owner ile ----
    private async Task HamYazAsync(Guid tenant, string sql, Guid id)
    {
        await using var con = new NpgsqlConnection(fx.OwnerConnectionString);
        await con.OpenAsync();
        await using var tx = await con.BeginTransactionAsync();
        await using (var set = new NpgsqlCommand("SELECT set_config('app.tenant_id', @t, true)", con, tx))
        {
            set.Parameters.AddWithValue("t", tenant.ToString());
            await set.ExecuteScalarAsync();
        }
        await using var cmd = new NpgsqlCommand(sql, con, tx);
        cmd.Parameters.AddWithValue("id", id);
        Assert.Equal(1, await cmd.ExecuteNonQueryAsync());
        await tx.CommitAsync();
    }

    [Fact]
    public async Task Migration_yuzdeleri_on_ikide_bire_cevirir()
    {
        var tenant = Guid.NewGuid();
        var opts = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(fx.OwnerConnectionString).Options;
        await using (var db = new AppDbContext(opts, NullTenantContext.Instance, NullCurrentUser.Instance))
        {
            var kod = $"ym{Guid.NewGuid():N}";
            db.Tenants.Add(new Tenant { Id = tenant, Code = kod, Name = kod, IsActive = true });
            await db.SaveChangesAsync();
        }

        Guid yuzdeKira, onIkiKira, kapaliKira, baf;
        using (var host = new TestHost(fx.AppConnectionString))
        using (var scope = host.ScopeFor(tenant))
        {
            var rentals = scope.ServiceProvider.GetRequiredService<RentalService>();
            yuzdeKira = await rentals.CreateDirectAsync(await InputAsync(scope.ServiceProvider, 10m));
            onIkiKira = await rentals.CreateDirectAsync(await InputAsync(scope.ServiceProvider, 10m));
            kapaliKira = await rentals.CreateDirectAsync(await InputAsync(scope.ServiceProvider, 10m));
            baf = await scope.ServiceProvider.GetRequiredService<BafService>().CreateAsync(new BafInput
                { PersonelId = Guid.NewGuid(), VehicleId = Guid.NewGuid(), CikisKm = 100 });
        }
        // Eski yollarla yazılmış olabilecek değerler (harici API yüzdesi / BAF yüzdesi / L6 öncesi negatif):
        await HamYazAsync(tenant, "UPDATE \"Rentals\" SET \"CikisYakit\" = 80, \"DonusYakit\" = 45 WHERE \"Id\" = @id", yuzdeKira);
        await HamYazAsync(tenant, "UPDATE \"Rentals\" SET \"CikisYakit\" = 8, \"DonusYakit\" = -3 WHERE \"Id\" = @id", onIkiKira);
        await HamYazAsync(tenant, "UPDATE \"Rentals\" SET \"CikisYakit\" = 80, \"DonusYakit\" = 40, \"Durum\" = 1 WHERE \"Id\" = @id", kapaliKira);
        await HamYazAsync(tenant, "UPDATE \"Baflar\" SET \"CikisYakit\" = 80, \"DonusYakit\" = 8 WHERE \"Id\" = @id", baf);

        // Migration'ın KENDİ SQL'i; tenant döngüsü yalnız bu testin kiracısına daraltılır (paylaşımlı DB).
        var sql = Assert.Single(new YakitOlcegiOnIki().UpOperations.OfType<SqlOperation>()).Sql;
        const string dongu = "FOR t IN SELECT \"Id\" FROM \"Tenants\" LOOP";
        Assert.Contains(dongu, sql);
        sql = sql.Replace(dongu, $"FOR t IN SELECT '{tenant}'::uuid LOOP");
        await using (var con = new NpgsqlConnection(fx.OwnerConnectionString))
        {
            await con.OpenAsync();
            await using var cmd = new NpgsqlCommand(sql, con);
            await cmd.ExecuteNonQueryAsync();
        }

        Assert.Equal(((int?)10, (int?)5), await HamYakitAsync(tenant, yuzdeKira)); // 80→9,6→10; 45→5,4→5
        Assert.Equal(((int?)8, (int?)0), await HamYakitAsync(tenant, onIkiKira));  // 0–12 dokunulmaz; negatif → 0
        Assert.Equal(((int?)10, (int?)5), await HamYakitAsync(tenant, kapaliKira)); // 40 → 4,8 → 5
        // Birim ücret: YALNIZ açık (Kirada) + yüzdeyle teslim edilmiş kirada çevrilir: 10 × 100/12 = 83,3333.
        Assert.Equal(83.3333m, await HamBirimAsync(tenant, yuzdeKira));
        Assert.Equal(10m, await HamBirimAsync(tenant, onIkiKira));   // zaten on ikide bir ölçeği
        Assert.Equal(10m, await HamBirimAsync(tenant, kapaliKira));  // kapanmış: faturalanmış geçmiş, dokunulmaz
        using (var host = new TestHost(fx.AppConnectionString))
        using (var scope = host.ScopeFor(tenant))
        {
            var b = (await scope.ServiceProvider.GetRequiredService<BafService>().GetAsync(baf))!;
            Assert.Equal(10, b.CikisYakit);  // BAF tamamı yüzdeydi: 80 → 10
            Assert.Equal(1, b.DonusYakit);   // 8 → 0,96 → 1
        }
    }
}
