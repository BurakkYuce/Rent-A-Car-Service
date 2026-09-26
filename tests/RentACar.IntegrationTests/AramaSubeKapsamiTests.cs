using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RentACar.Application.Common;
using RentACar.Application.Customers;
using RentACar.Application.Search;
using RentACar.Application.Secim;
using RentACar.Domain.Entities;
using RentACar.Domain.Enums;
using RentACar.Infrastructure.Persistence;
using RentACar.IntegrationTests.Infrastructure;

namespace RentACar.IntegrationTests;

/// <summary>
/// F1.6 — genel arama ŞUBE KAPSAMI (güvenlik düzeltmesi; Blazor <c>/ara</c> da bu servisi kullanır) ve seçim
/// servisinin servis-katmanı savunması. BAĞIMSIZ ORACLE: A şubesi operatörü A'nın kayıtlarını bulur, B'ninkini bulmaz —
/// elle kurulmuş kayıtlar ve elle yazılmış beklentiler.
/// </summary>
[Collection("postgres")]
public sealed class AramaSubeKapsamiTests(PostgresFixture fx)
{
    private static async Task YazAsync(IServiceScope scope, Action<AppDbContext> yaz)
    {
        var f = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
        await using var db = await f.CreateDbContextAsync();
        yaz(db);
        await db.SaveChangesAsync();
    }

    /// <summary>İki şubeye ait araç/kira/rezervasyon/fatura; hepsinin numarası ortak "F16ARA" önekini taşır.</summary>
    private static async Task IkiSubeKurAsync(TestHost host, Guid tenant)
    {
        using var seed = host.ScopeFor(tenant);
        var kiraA = Guid.NewGuid();
        var kiraB = Guid.NewGuid();
        await YazAsync(seed, db =>
        {
            db.Vehicles.Add(new Vehicle { Plaka = "34 F16ARA A", Sube = "SubeA", Durum = VehicleStatus.Musait });
            db.Vehicles.Add(new Vehicle { Plaka = "06 F16ARA B", Sube = "SubeB", Durum = VehicleStatus.Musait });
            db.Rentals.Add(new RentalContract
            {
                Id = kiraA, SozlesmeNo = "F16ARA-KA", CikisOfisi = "SubeA", MusteriId = Guid.NewGuid(), VehicleId = Guid.NewGuid(),
                BasTar = DateTimeOffset.UtcNow, BitTar = DateTimeOffset.UtcNow.AddDays(1), Doviz = "TRY",
            });
            db.Rentals.Add(new RentalContract
            {
                Id = kiraB, SozlesmeNo = "F16ARA-KB", CikisOfisi = "SubeB", MusteriId = Guid.NewGuid(), VehicleId = Guid.NewGuid(),
                BasTar = DateTimeOffset.UtcNow, BitTar = DateTimeOffset.UtcNow.AddDays(1), Doviz = "TRY",
            });
            db.Reservations.Add(new Reservation { ReservationNo = "F16ARA-RA", CikisOfisi = "SubeA", Durum = ReservationStatus.Rezerv });
            db.Reservations.Add(new Reservation { ReservationNo = "F16ARA-RB", CikisOfisi = "SubeB", Durum = ReservationStatus.Rezerv });
            Invoice Fatura(string no, Guid? kira, Guid? farkKira, string? islemSube) => new()
            {
                No = no, Durum = InvoiceStatus.Kesildi, CariId = Guid.NewGuid(), RentalId = kira, KaynakKiraId = farkKira,
                IslemSube = islemSube, Tarih = DateTimeOffset.UtcNow, NetTutar = 100m, KdvTutar = 20m, GenelToplam = 120m,
                Currency = "TRY", Kur = 1m,
            };
            db.Invoices.Add(Fatura("F16ARA-FA", kiraA, null, null));      // A kirasının faturası
            db.Invoices.Add(Fatura("F16ARA-FB", kiraB, null, null));      // B kirasının faturası
            db.Invoices.Add(Fatura("F16ARA-FFB", null, kiraB, null));     // B kirasının FARK faturası (RentalId null)
            db.Invoices.Add(Fatura("F16ARA-FMA", null, null, "SubeA"));   // kirasız manuel, A işlem şubesi
            db.Invoices.Add(Fatura("F16ARA-FMB", null, null, "SubeB"));   // kirasız manuel, B işlem şubesi
            db.Customers.Add(new Customer { Tip = CustomerType.Bireysel, Ad = "F16ARA", Soyad = "Cari" }); // cari kiracı geneli (şube alanı yok)
        });
    }

    [Fact]
    public async Task A_subesi_operatoru_A_kayitlarini_bulur_B_kayitlarini_bulamaz()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var tenant = Guid.NewGuid();
        await IkiSubeKurAsync(host, tenant);

        using var op = host.ScopeFor(tenant, Guid.NewGuid(), "opa", UserRole.Operator, assignedBranch: "SubeA");
        var hits = await op.ServiceProvider.GetRequiredService<SearchService>().SearchAsync("F16ARA");
        var basliklar = hits.Select(h => h.Baslik).Order(StringComparer.Ordinal).ToArray();

        Assert.Equal(new[] { "34 F16ARA A", "F16ARA", "F16ARA-FA", "F16ARA-FMA", "F16ARA-KA", "F16ARA-RA" }, basliklar);
    }

    [Fact]
    public async Task Admin_ve_subesiz_operator_tum_subeleri_bulur()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var tenant = Guid.NewGuid();
        await IkiSubeKurAsync(host, tenant);

        using (var admin = host.ScopeFor(tenant, Guid.NewGuid(), "ad", UserRole.Admin, assignedBranch: "SubeA"))
            Assert.Equal(12, (await admin.ServiceProvider.GetRequiredService<SearchService>().SearchAsync("F16ARA")).Count);

        using var subesiz = host.ScopeFor(tenant, Guid.NewGuid(), "op", UserRole.Operator, assignedBranch: null);
        Assert.Equal(12, (await subesiz.ServiceProvider.GetRequiredService<SearchService>().SearchAsync("F16ARA")).Count);
    }

    [Fact]
    public async Task B_subesi_operatoru_B_kayitlarini_bulur_fark_faturasi_dahil()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var tenant = Guid.NewGuid();
        await IkiSubeKurAsync(host, tenant);

        using var op = host.ScopeFor(tenant, Guid.NewGuid(), "opb", UserRole.Operator, assignedBranch: "SubeB");
        var basliklar = (await op.ServiceProvider.GetRequiredService<SearchService>().SearchAsync("F16ARA"))
            .Select(h => h.Baslik).Order(StringComparer.Ordinal).ToArray();

        Assert.Equal(new[] { "06 F16ARA B", "F16ARA", "F16ARA-FB", "F16ARA-FFB", "F16ARA-FMB", "F16ARA-KB", "F16ARA-RB" }, basliklar);
    }

    // ------------------------------------------------------------ seçim servisi: servis-katmanı savunması

    [Fact]
    public async Task Secim_servisi_izinsiz_rolde_YetkiYok_firlatir()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var muh = host.ScopeFor(Guid.NewGuid(), Guid.NewGuid(), "muh", UserRole.Muhasebe);
        var s = muh.ServiceProvider.GetRequiredService<SelectionService>();

        // Operasyonel seçimler Muhasebe'ye kapalı.
        await Assert.ThrowsAsync<NoPermissionException>(() => s.VehicleAsync(null, null));
        await Assert.ThrowsAsync<NoPermissionException>(() => s.LocationAsync(null, null));
        await Assert.ThrowsAsync<NoPermissionException>(() => s.StaffAsync(null, null));
        // F4.4: müşteri ve kur seçimi FinanceWrite ile de açık (sabit finans paneli; PII'sız alanlar).
        Assert.NotNull(await s.CustomerAsync(null, null));
        Assert.NotNull(await s.ExchangeRateAsync(null, null));
        Assert.NotNull(await muh.ServiceProvider.GetRequiredService<CustomerService>().SearchSelectionAsync(null, 20));
    }

    [Fact]
    public async Task Musteri_secim_aramasi_turkce_katlamali_ve_sinirli()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var tenant = Guid.NewGuid();
        using (var seed = host.ScopeFor(tenant))
            await YazAsync(seed, db =>
            {
                db.Customers.Add(new Customer { Tip = CustomerType.Kurumsal, Unvan = "ÇİĞDEM Şirketi" });
                db.Customers.Add(new Customer { Tip = CustomerType.Bireysel, Ad = "Çiğdem", Soyad = "Öztürk" });
                db.Customers.Add(new Customer { Tip = CustomerType.Bireysel, Ad = "Yüzde%", Soyad = "Alt_Çizgi" });
                for (var i = 0; i < 30; i++) db.Customers.Add(new Customer { Tip = CustomerType.Bireysel, Ad = $"Dolgu{i:00}" });
            });

        using var op = host.ScopeFor(tenant, Guid.NewGuid(), "op", UserRole.Operator);
        var s = op.ServiceProvider.GetRequiredService<SelectionService>();

        foreach (var q in new[] { "çiğdem", "ÇİĞDEM", "cigdem", "CIGDEM" })
            Assert.Equal(new[] { "Çiğdem Öztürk", "ÇİĞDEM Şirketi" },
                (await s.CustomerAsync(q, null)).Select(x => x.Etiket).Order(StringComparer.Ordinal).ToArray());
        Assert.Equal(new[] { "Çiğdem Öztürk" }, (await s.CustomerAsync("ÖZTÜRK", null)).Select(x => x.Etiket).ToArray());
        // % ve _ joker DEĞİL, düz metin.
        Assert.Equal(new[] { "Yüzde% Alt_Çizgi" }, (await s.CustomerAsync("%", null)).Select(x => x.Etiket).ToArray());
        Assert.Equal(new[] { "Yüzde% Alt_Çizgi" }, (await s.CustomerAsync("t_c", null)).Select(x => x.Etiket).ToArray());
        Assert.Equal(20, (await s.CustomerAsync(null, 500)).Count);
        Assert.Equal(3, (await s.CustomerAsync("dolgu", 3)).Count);
    }

    [Theory]
    [InlineData(null, 20)]
    [InlineData(0, 20)]
    [InlineData(-1, 20)]
    [InlineData(1, 1)]
    [InlineData(20, 20)]
    [InlineData(21, 20)]
    [InlineData(500, 20)]
    public void Limit_normalizasyonu(int? limit, int beklenen) => Assert.Equal(beklenen, SelectionService.Limit(limit));
}
