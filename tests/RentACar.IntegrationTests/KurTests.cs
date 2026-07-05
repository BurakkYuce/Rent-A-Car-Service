using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RentACar.Application.Common;
using RentACar.Application.Kur;
using RentACar.Domain.Entities;
using RentACar.IntegrationTests.Infrastructure;
using RentACar.Infrastructure.Persistence;
using RentACar.Web.Kur;

namespace RentACar.IntegrationTests;

/// <summary>
/// TCMB XML parse — SAF (networksüz). BAĞIMSIZ ORACLE: fixture'daki değerler elle, koddan değil.
/// KRİTİK: ondalık `.` INVARIANT culture (34.2567 → 34.2567, tr-TR'de 342567 DEĞİL).
/// </summary>
public sealed class TcmbKurParserTests
{
    private const string Xml = """
        <Tarih_Date Tarih="04.07.2026" Date="07/04/2026" Bulten_No="2026/126">
          <Currency CrossOrder="0" Kod="USD" CurrencyCode="USD">
            <Unit>1</Unit><Isim>ABD DOLARI</Isim>
            <ForexBuying>34.1234</ForexBuying><ForexSelling>34.2567</ForexSelling>
            <BanknoteBuying>34.1000</BanknoteBuying><BanknoteSelling>34.2800</BanknoteSelling>
          </Currency>
          <Currency CrossOrder="18" Kod="JPY" CurrencyCode="JPY">
            <Unit>100</Unit><Isim>JAPON YENI</Isim>
            <ForexBuying>22.5000</ForexBuying><ForexSelling>22.7000</ForexSelling>
          </Currency>
          <Currency CrossOrder="99" Kod="XDR" CurrencyCode="XDR">
            <Unit>1</Unit><Isim>SDR</Isim>
            <ForexBuying></ForexBuying><ForexSelling></ForexSelling>
          </Currency>
        </Tarih_Date>
        """;

    [Fact]
    public void Parse_dogru_deger_birim_tarih_ve_invariant_ondalik()
    {
        var list = TcmbKurParser.Parse(Xml);

        var usd = list.Single(x => x.Kod == "USD");
        Assert.Equal(34.2567m, usd.ForexSatis);   // INVARIANT: 342567 DEĞİL
        Assert.Equal(34.1234m, usd.ForexAlis);
        Assert.Equal(34.2800m, usd.EfektifSatis);
        Assert.Equal(1, usd.Birim);
        Assert.Equal("ABD DOLARI", usd.Ad);
        // Tarih 04.07.2026 → UTC gün başı
        Assert.Equal(new DateTimeOffset(2026, 7, 4, 0, 0, 0, TimeSpan.Zero), usd.Tarih);

        var jpy = list.Single(x => x.Kod == "JPY");
        Assert.Equal(100, jpy.Birim);
        Assert.Equal(22.7000m, jpy.ForexSatis);

        // Boş forex → null (patlamaz)
        var xdr = list.Single(x => x.Kod == "XDR");
        Assert.Null(xdr.ForexSatis);
        Assert.Null(xdr.ForexAlis);
    }
}

/// <summary>
/// KurService çevirim + sabit-kur çözümü. BAĞIMSIZ ORACLE: beklenen değerler elle senaryodan (100 USD ×
/// 34.50 = 3450), servis kodundan DEĞİL. Para-kritik: kur yönü, Birim, fallback, override, tenant izolasyon.
/// </summary>
[Collection("postgres")]
public sealed class KurServiceTests(PostgresFixture fx)
{
    private static readonly DateTimeOffset Cuma = new(2026, 7, 3, 0, 0, 0, TimeSpan.Zero); // 2026-07-03 Cuma

    // KurKayitlari PAYLAŞIMLI (RLS yok) → testler arası temizlenmez. İdempotent seed: (Tarih,Kod) varsa sil, ekle.
    private static async Task SeedKurAsync(IServiceScope scope, DateTimeOffset tarih,
        params (string kod, decimal satis, int birim)[] kurlar)
    {
        var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
        await using var db = await factory.CreateDbContextAsync();
        var kodlar = kurlar.Select(k => k.kod).ToList();
        var mevcut = await db.KurKayitlari.Where(x => x.Tarih == tarih && kodlar.Contains(x.Kod)).ToListAsync();
        db.KurKayitlari.RemoveRange(mevcut);
        foreach (var (kod, satis, birim) in kurlar)
            db.KurKayitlari.Add(new KurKaydi
            {
                Tarih = tarih, Kod = kod, Ad = kod, Birim = birim,
                ForexSatis = satis, ForexAlis = satis - 0.5m
            });
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task Cevir_yon_ve_baz()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        await SeedKurAsync(scope, Cuma, ("USD", 34.50m, 1));
        var kur = scope.ServiceProvider.GetRequiredService<KurService>();

        Assert.Equal(34.50m, await kur.GetRateAsync("USD", Cuma));       // 1 birim = Satış/Birim
        Assert.Equal(3450m, await kur.CevirAsync(100m, "USD", "TL", Cuma)); // 100 × 34.50 = 3450
        Assert.True(await kur.CevirAsync(100m, "USD", "TL", Cuma) > 100m);   // YÖN: 100 USD > 100 TL
        Assert.Equal(100m, await kur.CevirAsync(100m, "TL", "TL", Cuma));    // baz→baz
        Assert.Equal(100m, await kur.CevirAsync(100m, "USD", "USD", Cuma));  // aynı döviz
    }

    [Fact]
    public async Task Birim_JPY_100_ve_capraz_TL_bazi()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        // JPY Birim 100: 25.00/100 = 0.25. Çapraz temiz: EUR 45, USD 30 → 100×45/30 = 150.
        await SeedKurAsync(scope, Cuma, ("JPY", 25.00m, 100), ("EUR", 45m, 1), ("USD", 30m, 1));
        var kur = scope.ServiceProvider.GetRequiredService<KurService>();

        Assert.Equal(0.25m, await kur.GetRateAsync("JPY", Cuma));            // Birim 100
        Assert.Equal(150m, await kur.CevirAsync(100m, "EUR", "USD", Cuma));  // TL bazı: 100×45/30
    }

    [Fact]
    public async Task Hafta_sonu_fallback_ve_eksik_doviz()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        await SeedKurAsync(scope, Cuma, ("USD", 34.50m, 1)); // yalnız Cuma
        var kur = scope.ServiceProvider.GetRequiredService<KurService>();

        var pazar = Cuma.AddDays(2); // hafta sonu — kur yok
        Assert.Equal(34.50m, await kur.GetRateAsync("USD", pazar)); // ≤pazar en yeni = Cuma

        await Assert.ThrowsAsync<ValidationException>(() => kur.GetRateAsync("XXX", Cuma)); // eksik → hata (0/1 değil)
    }

    [Fact]
    public async Task SabitKur_override_ve_pencere()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        await SeedKurAsync(scope, Cuma, ("USD", 34.50m, 1));
        var sabit = scope.ServiceProvider.GetRequiredService<SabitKurService>();
        var kur = scope.ServiceProvider.GetRequiredService<KurService>();

        // Süresiz sabit kur → TCMB'yi ezer
        await sabit.UpsertAsync(new SabitKurInput { Kod = "USD", Kur = 40m, Aktif = true });
        Assert.Equal(40m, await kur.GetRateAsync("USD", Cuma));

        // Pencere gelecekte → bugün TCMB'ye düşer
        await sabit.UpsertAsync(new SabitKurInput { Kod = "USD", Kur = 40m, Aktif = true, BasTar = Cuma.AddDays(10) });
        Assert.Equal(34.50m, await kur.GetRateAsync("USD", Cuma));

        // Pasif → TCMB
        await sabit.UpsertAsync(new SabitKurInput { Kod = "USD", Kur = 40m, Aktif = false });
        Assert.Equal(34.50m, await kur.GetRateAsync("USD", Cuma));
    }

    [Fact]
    public async Task SabitKur_tenant_izolasyonu()
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        using var host = new TestHost(fx.AppConnectionString);

        // Paylaşımlı TCMB kuru (her iki tenant görür)
        using (var s0 = host.ScopeFor(tenantA))
            await SeedKurAsync(s0, Cuma, ("USD", 34.50m, 1));

        // A kendi sabit kurunu tanımlar
        using (var sa = host.ScopeFor(tenantA))
            await sa.ServiceProvider.GetRequiredService<SabitKurService>()
                .UpsertAsync(new SabitKurInput { Kod = "USD", Kur = 40m, Aktif = true });

        // A → 40 (kendi sabiti); B → 34.50 (A'nınkini GÖRMEZ, TCMB'ye düşer)
        using (var sa = host.ScopeFor(tenantA))
            Assert.Equal(40m, await sa.ServiceProvider.GetRequiredService<KurService>().GetRateAsync("USD", Cuma));
        using (var sb = host.ScopeFor(tenantB))
            Assert.Equal(34.50m, await sb.ServiceProvider.GetRequiredService<KurService>().GetRateAsync("USD", Cuma));
    }
}
