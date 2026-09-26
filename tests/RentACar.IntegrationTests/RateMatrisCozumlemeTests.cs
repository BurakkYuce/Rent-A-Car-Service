using Microsoft.Extensions.DependencyInjection;
using RentACar.Application.RateMatrices;
using RentACar.Domain.Enums;
using RentACar.IntegrationTests.Infrastructure;

namespace RentACar.IntegrationTests;

/// <summary>
/// Tarife matrisi ÇÖZÜMLEME (para-etkili fiyat okuma). BAĞIMSIZ ORACLE: elle kurulmuş senaryolar —
/// WEB/Merkez/EKO Gün3=90 → 3 gün toplam 270; spesifik joker'i yener; kademe-clamp (10 gün→Gün7);
/// yalnız Onaylı; tarih penceresi; en-dar-pencere kırıcısı; cross-tenant izole.
/// </summary>
[Collection("postgres")]
public sealed class RateMatrisCozumlemeTests(PostgresFixture fx)
{
    private static readonly DateTimeOffset D = new(2026, 7, 15, 0, 0, 0, TimeSpan.Zero);

    private static RateMatrixInput T(string code, string? channel = null, string? branch = null, string? group = null,
        TariffApprovalStatus approval = TariffApprovalStatus.Onayli, decimal? g1 = null, decimal? g3 = null, decimal? g7 = null,
        DateTimeOffset? start = null, DateTimeOffset? bit = null, string? money = null)
        => new()
        {
            Kod = code, Ad = code, Kanal = channel, Sube = branch, AracGrupKod = group, OnayDurumu = approval, Aktif = true,
            Gun1 = g1, Gun3 = g3, Gun7 = g7, BasTar = start, BitTar = bit, ParaBirimi = money
        };

    [Fact]
    public async Task Kademe_gunluk_ve_toplam_fiyat()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var svc = scope.ServiceProvider.GetRequiredService<RateMatrixService>();
        await svc.CreateAsync(T("WEB-EKO", "WEB", "Merkez", "EKO", g1: 100, g3: 90, g7: 70));

        var s = await svc.ResolveAsync(new RateMatrisSorgu("WEB", "Merkez", "EKO", D, 3));
        Assert.NotNull(s);
        Assert.Equal(90m, s!.GunlukFiyat);   // Gün3 kademesi
        Assert.Equal(270m, s.ToplamFiyat);   // 3 × 90 (oracle)
    }

    [Fact]
    public async Task Spesifik_jokeri_yener_ve_joker_diger_sorguyu_kapsar()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var svc = scope.ServiceProvider.GetRequiredService<RateMatrixService>();
        await svc.CreateAsync(T("JOKER", null, null, null, g3: 200));                 // her şeye uyar
        await svc.CreateAsync(T("WEB-EKO", "WEB", "Merkez", "EKO", g3: 90));           // spesifik

        var specific = await svc.ResolveAsync(new RateMatrisSorgu("WEB", "Merkez", "EKO", D, 3));
        Assert.Equal("WEB-EKO", specific!.Kod);  // 3 alan eşleşen spesifik kazandı
        Assert.Equal(90m, specific.GunlukFiyat);

        var wildcardPath = await svc.ResolveAsync(new RateMatrisSorgu("ACENTA", "Ankara", "LUX", D, 3));
        Assert.Equal("JOKER", wildcardPath!.Kod);    // spesifik uymuyor → joker
        Assert.Equal(200m, wildcardPath.GunlukFiyat);
    }

    [Fact]
    public async Task Kademe_clamp_ve_gecersiz_gun()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var svc = scope.ServiceProvider.GetRequiredService<RateMatrixService>();
        await svc.CreateAsync(T("EKO", "WEB", g1: 100, g7: 70)); // Gün3 YOK

        Assert.Equal(100m, (await svc.ResolveAsync(new RateMatrisSorgu("WEB", null, null, D, 1)))!.GunlukFiyat);  // 1→Gün1
        Assert.Equal(70m, (await svc.ResolveAsync(new RateMatrisSorgu("WEB", null, null, D, 10)))!.GunlukFiyat);  // 10→Gün7
        Assert.Null(await svc.ResolveAsync(new RateMatrisSorgu("WEB", null, null, D, 3)));                        // Gün3 null → yok
        Assert.Null(await svc.ResolveAsync(new RateMatrisSorgu("WEB", null, null, D, 0)));                        // 0 gün → null
    }

    [Fact]
    public async Task Yalnizca_onayli_ve_tarih_penceresi()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var svc = scope.ServiceProvider.GetRequiredService<RateMatrixService>();
        await svc.CreateAsync(T("BEKLE", "WEB", approval: TariffApprovalStatus.Bekliyor, g3: 50));  // onaysız
        Assert.Null(await svc.ResolveAsync(new RateMatrisSorgu("WEB", null, null, D, 3)));   // çözülmez

        await svc.CreateAsync(T("YAZ", "ACENTA", g3: 90,
            start: new(2026, 6, 1, 0, 0, 0, TimeSpan.Zero), bit: new(2026, 8, 31, 0, 0, 0, TimeSpan.Zero)));
        Assert.NotNull(await svc.ResolveAsync(new RateMatrisSorgu("ACENTA", null, null, D, 3)));                   // 15 Tem içinde
        Assert.Null(await svc.ResolveAsync(new RateMatrisSorgu("ACENTA", null, null, new(2026, 10, 1, 0, 0, 0, TimeSpan.Zero), 3))); // dışında
    }

    [Fact]
    public async Task En_dar_pencere_kirici()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var svc = scope.ServiceProvider.GetRequiredService<RateMatrixService>();
        await svc.CreateAsync(T("ACIK", "WEB", g3: 95));                                        // açık pencere
        await svc.CreateAsync(T("DAR", "WEB", g3: 88, start: D.AddDays(-5), bit: D.AddDays(5)));  // dar, tarihi kapsar

        var s = await svc.ResolveAsync(new RateMatrisSorgu("WEB", null, null, D, 3));
        Assert.Equal("DAR", s!.Kod);          // aynı spesifiklik → en dar pencere kazandı
        Assert.Equal(88m, s.GunlukFiyat);
    }

    [Fact]
    public async Task Para_birimi_dogru_filtreler_yabanci_sizmaz()
    {
        // Adversarial Medium: para-null sorgu YALNIZ varsayılan (null-döviz) tarifeyi çözmeli;
        // yabancı-döviz tarife recency ile sızmamalı. Özel döviz sorgusu o dövizi çözer.
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var svc = scope.ServiceProvider.GetRequiredService<RateMatrixService>();
        await svc.CreateAsync(T("DEFAULT", "WEB", g3: 90));               // ParaBirimi null (varsayılan)
        await svc.CreateAsync(T("EURTAR", "WEB", g3: 30, money: "EUR"));   // EUR (sonra yaratıldı → daha yeni)

        var moneyNull = await svc.ResolveAsync(new RateMatrisSorgu("WEB", null, null, D, 3));
        Assert.Equal("DEFAULT", moneyNull!.Kod);   // yabancı-döviz SIZMADI (recency'ye rağmen)
        Assert.Equal(90m, moneyNull.GunlukFiyat);

        var euro = await svc.ResolveAsync(new RateMatrisSorgu("WEB", null, null, D, 3, ParaBirimi: "eur"));
        Assert.Equal("EURTAR", euro!.Kod);         // EUR sorgu → EUR tarife (case-insensitive)
        Assert.Equal(30m, euro.GunlukFiyat);
        Assert.Equal("EUR", euro.ParaBirimi);
    }

    [Fact]
    public async Task Cross_tenant_izole()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var sA = host.ScopeFor(Guid.NewGuid());
        await sA.ServiceProvider.GetRequiredService<RateMatrixService>()
            .CreateAsync(T("WEB-EKO", "WEB", g3: 90));

        using var sB = host.ScopeFor(Guid.NewGuid()); // B, A'nın tarifesini görmez
        Assert.Null(await sB.ServiceProvider.GetRequiredService<RateMatrixService>()
            .ResolveAsync(new RateMatrisSorgu("WEB", null, null, D, 3)));
    }
}
