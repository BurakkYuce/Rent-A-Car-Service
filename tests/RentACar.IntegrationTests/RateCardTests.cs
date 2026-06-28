using Microsoft.Extensions.DependencyInjection;
using RentACar.Application.Common;
using RentACar.Application.Pricing;
using RentACar.Domain.Enums;
using RentACar.IntegrationTests.Infrastructure;

// roadmap A1: RateCardService.GetRateAsync [Obsolete] (RentalQuoteEngine birincil); bu testler eski
// RateCard geriye-uyum fallback'ini bilinçli doğruluyor → CS0618 bu dosyada bastırılır.
#pragma warning disable CS0618

namespace RentACar.IntegrationTests;

/// <summary>
/// Tarife (RateCard) master + fiyat lookup — bağımsız oracle. Beklenen ücretler elle-kurulu
/// senaryodan gelir (servis kodundan değil). Kademe seçimi, en-dar-kademe, dönem önceliği,
/// pasif hariç, grup harf-duyarsız, CRUD/benzersizlik/yetki/izolasyon.
/// </summary>
[Collection("postgres")]
public sealed class RateCardTests(PostgresFixture fx)
{
    private static readonly DateTimeOffset AnyDay = new(2026, 6, 15, 12, 0, 0, TimeSpan.Zero);

    private static RateCardInput Rc(string kod, string grup, int min, int max, decimal ucret,
        DateTimeOffset? bas = null, DateTimeOffset? bit = null, bool aktif = true) => new()
    {
        Kod = kod, Ad = kod, Grup = grup, MinGun = min, MaxGun = max, GunlukUcret = ucret,
        GecerliBas = bas, GecerliBit = bit, Aktif = aktif
    };

    [Fact]
    public async Task Create_normalizes_kod_and_roundtrips()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var svc = scope.ServiceProvider.GetRequiredService<RateCardService>();

        var id = await svc.CreateAsync(Rc("b-std", "B", 1, 3, 100m));
        var got = await svc.GetAsync(id);
        Assert.Equal("B-STD", got!.Kod);   // normalize: büyük harf
        Assert.Equal("B", got.Grup);
        Assert.Equal(100m, got.GunlukUcret);
    }

    [Fact]
    public async Task Duplicate_kod_rejected_case_insensitive()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var svc = scope.ServiceProvider.GetRequiredService<RateCardService>();

        await svc.CreateAsync(Rc("B-STD", "B", 1, 3, 100m));
        await Assert.ThrowsAsync<ValidationException>(() => svc.CreateAsync(Rc("b-std", "B", 4, 9, 80m)));
    }

    [Fact]
    public async Task Lookup_selects_correct_day_tier()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var svc = scope.ServiceProvider.GetRequiredService<RateCardService>();

        await svc.CreateAsync(Rc("B1", "B", 1, 3, 100m));   // 1–3 gün: 100
        await svc.CreateAsync(Rc("B2", "B", 4, 9999, 80m)); // 4+ gün: 80

        Assert.Equal(100m, (await svc.GetRateAsync("B", 1, AnyDay))!.GunlukUcret);
        Assert.Equal(100m, (await svc.GetRateAsync("B", 3, AnyDay))!.GunlukUcret);
        Assert.Equal(80m,  (await svc.GetRateAsync("B", 4, AnyDay))!.GunlukUcret);
        Assert.Equal(80m,  (await svc.GetRateAsync("B", 30, AnyDay))!.GunlukUcret);
        Assert.Null(await svc.GetRateAsync("C", 2, AnyDay));   // grup yok
    }

    [Fact]
    public async Task Lookup_prefers_most_specific_tier_on_overlap()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var svc = scope.ServiceProvider.GetRequiredService<RateCardService>();

        await svc.CreateAsync(Rc("D-GEN", "D", 1, 9999, 200m));  // genel
        await svc.CreateAsync(Rc("D-LONG", "D", 7, 9999, 150m)); // uzun dönem (daha dar alt sınır)

        // 3 gün: yalnız genel kapsar → 200.
        Assert.Equal(200m, (await svc.GetRateAsync("D", 3, AnyDay))!.GunlukUcret);
        // 10 gün: ikisi de kapsar → en yüksek MinGun (7) kazanır → 150.
        Assert.Equal(150m, (await svc.GetRateAsync("D", 10, AnyDay))!.GunlukUcret);
    }

    [Fact]
    public async Task Lookup_prefers_seasonal_period_when_in_range()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var svc = scope.ServiceProvider.GetRequiredService<RateCardService>();

        await svc.CreateAsync(Rc("E-BASE", "E", 1, 9999, 100m)); // dönemsiz taban
        await svc.CreateAsync(Rc("E-YAZ", "E", 1, 9999, 300m,
            bas: new DateTimeOffset(2026, 7, 1, 0, 0, 0, TimeSpan.Zero),
            bit: new DateTimeOffset(2026, 8, 31, 23, 59, 0, TimeSpan.Zero))); // yaz sezonu

        var temmuz = new DateTimeOffset(2026, 7, 15, 12, 0, 0, TimeSpan.Zero);
        var aralik = new DateTimeOffset(2026, 12, 15, 12, 0, 0, TimeSpan.Zero);

        // Yaz: sezon dönemi kapsar, aynı kademe → en güncel başlangıç (sezon) kazanır → 300.
        Assert.Equal(300m, (await svc.GetRateAsync("E", 5, temmuz))!.GunlukUcret);
        // Aralık: sezon kapsamaz → tabana düşer → 100.
        Assert.Equal(100m, (await svc.GetRateAsync("E", 5, aralik))!.GunlukUcret);
    }

    [Fact]
    public async Task Lookup_excludes_inactive_and_is_group_case_insensitive()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var svc = scope.ServiceProvider.GetRequiredService<RateCardService>();

        await svc.CreateAsync(Rc("F-OFF", "F", 1, 9999, 50m, aktif: false));
        Assert.Null(await svc.GetRateAsync("F", 2, AnyDay)); // pasif → eşleşme yok

        await svc.CreateAsync(Rc("G1", "G", 1, 9999, 120m));
        Assert.Equal(120m, (await svc.GetRateAsync("g", 2, AnyDay))!.GunlukUcret); // "g" == "G"
    }

    [Fact]
    public async Task Validation_rejects_bad_inputs()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var svc = scope.ServiceProvider.GetRequiredService<RateCardService>();

        await Assert.ThrowsAsync<ValidationException>(() => svc.CreateAsync(Rc("", "B", 1, 3, 100m)));        // kod yok
        await Assert.ThrowsAsync<ValidationException>(() => svc.CreateAsync(Rc("X", "", 1, 3, 100m)));        // grup yok
        await Assert.ThrowsAsync<ValidationException>(() => svc.CreateAsync(Rc("X", "B", 5, 3, 100m)));       // max<min
        await Assert.ThrowsAsync<ValidationException>(() => svc.CreateAsync(Rc("X", "B", 1, 3, -1m)));        // negatif ücret
        await Assert.ThrowsAsync<ValidationException>(() => svc.CreateAsync(Rc("X", "B", 1, 3, 100m,
            bas: new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.Zero),
            bit: new DateTimeOffset(2026, 7, 1, 0, 0, 0, TimeSpan.Zero))));                                   // bit<bas
    }

    [Fact]
    public async Task NonOperations_user_cannot_manage()
    {
        using var host = new TestHost(fx.AppConnectionString);
        // Muhasebe: FinanceWrite var, OperationsWrite YOK → tarife yönetemez.
        using var scope = host.ScopeFor(Guid.NewGuid(), Guid.NewGuid(), "muh", UserRole.Muhasebe);
        var svc = scope.ServiceProvider.GetRequiredService<RateCardService>();
        await Assert.ThrowsAsync<ValidationException>(() => svc.CreateAsync(Rc("B1", "B", 1, 3, 100m)));
    }

    [Fact]
    public async Task RateCards_are_tenant_isolated()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var t1 = Guid.NewGuid();
        var t2 = Guid.NewGuid();

        using (var s1 = host.ScopeFor(t1))
            await s1.ServiceProvider.GetRequiredService<RateCardService>().CreateAsync(Rc("B1", "B", 1, 9999, 100m));

        using var s2 = host.ScopeFor(t2);
        var svc2 = s2.ServiceProvider.GetRequiredService<RateCardService>();
        Assert.Empty(await svc2.ListAsync());
        Assert.Null(await svc2.GetRateAsync("B", 2, AnyDay)); // t1 tarifesi t2'ye görünmez
    }
}
