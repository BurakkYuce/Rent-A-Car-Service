using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using RentACar.Application.Common;
using RentACar.Application.Kur;
using RentACar.Domain.Entities;
using RentACar.IntegrationTests.Infrastructure;
using RentACar.Infrastructure.Persistence;
using RentACar.Web.Kur;

namespace RentACar.IntegrationTests;

/// <summary>
/// ADVERSARIAL probe — TCMB kur + SabitKur sistemini ÇÜRÜTMEYE çalışır. Bağımsız oracle: beklenenler ELLE.
/// </summary>
public sealed class TcmbKurParserAdversarialProbe
{
    // ---- 5. PARSE KÜLTÜRÜ: tr-TR CurrentCulture altında "." ondalık bozuluyor mu? ----
    [Fact]
    public void P5_Parse_trTR_culture_altinda_invariant_ondalik_korunur()
    {
        var prev = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("tr-TR");
            var xml = """
                <Tarih_Date Tarih="04.07.2026">
                  <Currency Kod="USD"><Unit>1</Unit><Isim>ABD DOLARI</Isim>
                    <ForexBuying>34.1234</ForexBuying><ForexSelling>34.2567</ForexSelling></Currency>
                </Tarih_Date>
                """;
            var usd = TcmbKurParser.Parse(xml).Single(x => x.Kod == "USD");
            // ELLE oracle: 34.2567 (tr-TR CurrentCulture 342567 YAPMAMALI)
            Assert.Equal(34.2567m, usd.ForexSatis);
            Assert.Equal(34.1234m, usd.ForexAlis);
        }
        finally { CultureInfo.CurrentCulture = prev; }
    }

    // ---- 5b. Bozuk/eksik değerler patlatmıyor mu? ----
    [Fact]
    public void P5b_Bozuk_eksik_deger_null_ve_birim_fallback()
    {
        var xml = """
            <Tarih_Date Tarih="04.07.2026">
              <Currency Kod="AAA"><Unit>0</Unit><Isim>SIFIR BIRIM</Isim>
                <ForexSelling>abc</ForexSelling></Currency>
              <Currency Kod="BBB"><Isim>BIRIMSIZ</Isim>
                <ForexSelling>1.2345</ForexSelling></Currency>
              <Currency Kod="CCC"><Unit>1</Unit><Isim>NEGATIF</Isim>
                <ForexSelling>-5.0</ForexSelling></Currency>
            </Tarih_Date>
            """;
        var list = TcmbKurParser.Parse(xml);
        var aaa = list.Single(x => x.Kod == "AAA");
        Assert.Equal(1, aaa.Birim);          // Unit=0 → 1'e fallback (sıfıra bölme yok)
        Assert.Null(aaa.ForexSatis);         // "abc" → null (patlamaz)
        var bbb = list.Single(x => x.Kod == "BBB");
        Assert.Equal(1, bbb.Birim);          // eksik Unit → 1
        var ccc = list.Single(x => x.Kod == "CCC");
        Assert.Equal(-5.0m, ccc.ForexSatis); // negatif PARSE ediliyor (GetRate reddetmeli — ayrı probe)
    }

    // ---- 5c. Tarih attribute eksik → bugüne düşüyor (sessiz) ----
    [Fact]
    public void P5c_Tarih_attribute_eksik_bugune_duser()
    {
        var xml = """
            <Tarih_Date>
              <Currency Kod="USD"><Unit>1</Unit><Isim>X</Isim><ForexSelling>34.0</ForexSelling></Currency>
            </Tarih_Date>
            """;
        var usd = TcmbKurParser.Parse(xml).Single();
        var now = DateTimeOffset.UtcNow;
        Assert.Equal(new DateTimeOffset(now.Year, now.Month, now.Day, 0, 0, 0, TimeSpan.Zero), usd.Tarih);
    }
}

[Collection("postgres")]
public sealed class KurServiceAdversarialProbe(PostgresFixture fx)
{
    private static readonly DateTimeOffset Cuma = new(2026, 7, 3, 0, 0, 0, TimeSpan.Zero);

    // Tam-kontrollü TCMB seed (idempotent; paylaşımlı tablo).
    private static async Task SeedRawAsync(IServiceScope scope, params KurKaydi[] kayitlar)
    {
        var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
        await using var db = await factory.CreateDbContextAsync();
        foreach (var k in kayitlar)
        {
            var mev = await db.KurKayitlari.Where(x => x.Tarih == k.Tarih && x.Kod == k.Kod).ToListAsync();
            db.KurKayitlari.RemoveRange(mev);
        }
        db.KurKayitlari.AddRange(kayitlar);
        await db.SaveChangesAsync();
    }

    private static KurKaydi K(string kod, decimal? satis, int birim = 1, decimal? alis = null)
        => new() { Tarih = Cuma, Kod = kod, Ad = kod, Birim = birim, ForexSatis = satis, ForexAlis = alis };

    // ---- 1. YÖN + ÇAPRAZ (bağımsız oracle) ----
    [Fact]
    public async Task P1_yon_ve_capraz_TL_bazi()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        // ELLE oracle: USD=34.50, EUR=45, USD>TL, EUR→USD = 45/30*100 (ama USD 34.50 kullanalım)
        await SeedRawAsync(scope, K("USD", 34.50m), K("EUR", 45m));
        var kur = scope.ServiceProvider.GetRequiredService<ExchangeRateService>();

        Assert.Equal(3450m, await kur.ConvertAsync(100m, "USD", "TL", Cuma));      // 100×34.50
        Assert.Equal(100m, await kur.ConvertAsync(3450m, "TL", "USD", Cuma));      // ters: 3450/34.50
        // EUR→USD: 100 EUR = 4500 TL = 4500/34.50 USD ≈ 130.4347826...
        var eurUsd = await kur.ConvertAsync(100m, "EUR", "USD", Cuma);
        Assert.Equal(4500m / 34.50m, eurUsd);
        Assert.True(eurUsd > 100m); // EUR TL'de USD'den değerli → >100 USD
    }

    // ---- 2. BİRİM=0 DB'de (sıfıra bölme?) ----
    [Fact]
    public async Task P2_birim_sifir_DB_sifira_bolme_yok()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        await SeedRawAsync(scope, K("ZZZ", 30m, birim: 0)); // bozuk birim
        var kur = scope.ServiceProvider.GetRequiredService<ExchangeRateService>();
        Assert.Equal(30m, await kur.GetRateAsync("ZZZ", Cuma)); // 30 / (0→1) = 30, patlamaz
    }

    // ---- 3. NEGATİF/0 TCMB → ValidationException (sessiz leak yok) ----
    [Fact]
    public async Task P3_negatif_ve_sifir_TCMB_reddedilir()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        await SeedRawAsync(scope, K("NEG", -5m), K("ZER", 0m));
        var kur = scope.ServiceProvider.GetRequiredService<ExchangeRateService>();
        await Assert.ThrowsAsync<ValidationException>(() => kur.GetRateAsync("NEG", Cuma));
        await Assert.ThrowsAsync<ValidationException>(() => kur.GetRateAsync("ZER", Cuma));
    }

    // ---- 3b. Alis türü doğru kolonu seçiyor mu? ----
    [Fact]
    public async Task P3b_alis_satis_kolon_secimi()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        await SeedRawAsync(scope, K("USD", satis: 34.50m, alis: 34.00m));
        var kur = scope.ServiceProvider.GetRequiredService<ExchangeRateService>();
        Assert.Equal(34.50m, await kur.GetRateAsync("USD", Cuma, ExchangeRateType.Satis));
        Assert.Equal(34.00m, await kur.GetRateAsync("USD", Cuma, ExchangeRateType.Alis));
    }

    // ---- 4. GELECEK TARİH → en yeni ≤ tarih ----
    [Fact]
    public async Task P4_gelecek_tarih_en_yeni_kur()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        await SeedRawAsync(scope, K("USD", 34.50m));
        var kur = scope.ServiceProvider.GetRequiredService<ExchangeRateService>();
        Assert.Equal(34.50m, await kur.GetRateAsync("USD", Cuma.AddYears(1))); // gelecek → en son bilinen
    }

    // ---- 6. SabitKur pencere GÜN-DAHİL: BasTar günü başından BitTar günü SONUNA kadar geçerli ----
    [Fact]
    public async Task P6_pencere_gun_dahil()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        await SeedRawAsync(scope, K("USD", 34.50m)); // TCMB 07-03
        var sabit = scope.ServiceProvider.GetRequiredService<FixedExchangeRateService>();
        var kur = scope.ServiceProvider.GetRequiredService<ExchangeRateService>();

        var bas = new DateTimeOffset(2026, 7, 4, 0, 0, 0, TimeSpan.Zero);
        var bit = new DateTimeOffset(2026, 7, 6, 0, 0, 0, TimeSpan.Zero);
        await sabit.UpsertAsync(new SabitKurInput { Kod = "USD", Kur = 40m, Aktif = true, BasTar = bas, BitTar = bit });

        Assert.Equal(40m, await kur.GetRateAsync("USD", bas)); // BasTar günü başı — dahil
        Assert.Equal(40m, await kur.GetRateAsync("USD", new DateTimeOffset(2026, 7, 6, 23, 0, 0, TimeSpan.Zero))); // BitTar günü öğleden sonra — DAHİL (fix)
        Assert.Equal(34.50m, await kur.GetRateAsync("USD", new DateTimeOffset(2026, 7, 3, 23, 0, 0, TimeSpan.Zero))); // BasTar öncesi gün → TCMB
        Assert.Equal(34.50m, await kur.GetRateAsync("USD", new DateTimeOffset(2026, 7, 7, 0, 0, 0, TimeSpan.Zero)));  // BitTar sonrası gün → TCMB
    }

    // ---- 6b. BitTar GÜN-DAHİL (Medium fix): date-picker (gün) BitTar, aynı gün öğleden sonra HÂLÂ geçerli ----
    [Fact]
    public async Task P6b_bittar_gun_dahil_ayni_gun_gecerli()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        await SeedRawAsync(scope, K("USD", 34.50m));
        var sabit = scope.ServiceProvider.GetRequiredService<FixedExchangeRateService>();
        var kur = scope.ServiceProvider.GetRequiredService<ExchangeRateService>();

        // Kullanıcı "Bitiş = 2026-07-03" seçer (date-picker → gün başı UTC).
        var bitGun = new DateTimeOffset(2026, 7, 3, 0, 0, 0, TimeSpan.Zero);
        var ogledenSonra = new DateTimeOffset(2026, 7, 3, 14, 0, 0, TimeSpan.Zero);

        await sabit.UpsertAsync(new SabitKurInput { Kod = "USD", Kur = 40m, Aktif = true, BitTar = bitGun });
        Assert.Equal(40m, await kur.GetRateAsync("USD", ogledenSonra)); // son gün öğleden sonra DAHİL (eskiden 34.50 sızardı)

        // "Bugün için sabitle" (Bas=Bit=aynı gün) → o gün TÜMÜYLE geçerli.
        await sabit.UpsertAsync(new SabitKurInput { Kod = "USD", Kur = 40m, Aktif = true, BasTar = bitGun, BitTar = bitGun });
        Assert.Equal(40m, await kur.GetRateAsync("USD", ogledenSonra));
        // Ertesi gün → pencere-dışı → TCMB.
        Assert.Equal(34.50m, await kur.GetRateAsync("USD", new DateTimeOffset(2026, 7, 4, 10, 0, 0, TimeSpan.Zero)));
    }

    // ---- 9. SCALE/ROUND: numeric(19,6) round-trip ve büyük tutar ----
    [Fact]
    public async Task P9_scale_ve_buyuk_tutar()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        await SeedRawAsync(scope, K("USD", 34.505050m));
        var kur = scope.ServiceProvider.GetRequiredService<ExchangeRateService>();
        Assert.Equal(34.505050m, await kur.GetRateAsync("USD", Cuma)); // 6 dp korunur
        // Büyük tutar taşmıyor
        Assert.Equal(1_000_000m * 34.505050m, await kur.ConvertAsync(1_000_000m, "USD", "TL", Cuma));
    }
}

// ---- 7. CROSS-TENANT (KRİTİK) — racar_app + RLS FORCE ----
[Collection("postgres")]
public sealed class SabitKurCrossTenantProbe(PostgresFixture fx)
{
    private static readonly DateTimeOffset Cuma = new(2026, 7, 3, 0, 0, 0, TimeSpan.Zero);

    private static async Task SeedUsdAsync(IServiceScope scope)
    {
        var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
        await using var db = await factory.CreateDbContextAsync();
        var mev = await db.KurKayitlari.Where(x => x.Tarih == Cuma && x.Kod == "USD").ToListAsync();
        db.KurKayitlari.RemoveRange(mev);
        db.KurKayitlari.Add(new KurKaydi { Tarih = Cuma, Kod = "USD", Ad = "USD", Birim = 1, ForexSatis = 34.50m });
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task P7_B_A_nin_sabit_kurunu_okuyamaz_silemez_guncelleyemez()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        using var host = new TestHost(fx.AppConnectionString);
        using (var s0 = host.ScopeFor(a)) await SeedUsdAsync(s0);

        Guid aId;
        using (var sa = host.ScopeFor(a))
            aId = await sa.ServiceProvider.GetRequiredService<FixedExchangeRateService>()
                .UpsertAsync(new SabitKurInput { Kod = "USD", Kur = 40m, Aktif = true });

        using (var sb = host.ScopeFor(b))
        {
            var repoB = sb.ServiceProvider.GetRequiredService<IPinnedRateRepository>();
            var svcB = sb.ServiceProvider.GetRequiredService<FixedExchangeRateService>();

            // OKUMA: B, A'nın id'sini bulamaz
            Assert.Null(await repoB.FindAsync(aId));
            Assert.Empty(await repoB.ListAsync());
            Assert.Null(await repoB.GetActiveAsync("USD", Cuma));

            // SİLME: B, A'nın id'sini silemez (bulunamaz → false)
            Assert.False(await svcB.DeleteAsync(aId));

            // GÜNCELLEME: B, A'nın entity'sini güncelleyemez (bulunamaz → false)
            var sahte = new SabitKur { Id = aId, TenantId = a, Kod = "USD", Kur = 999m, Aktif = true };
            Assert.False(await repoB.UpdateAsync(sahte));

            // ÇEVİRİM: B, A'nın 40'ını görmez → TCMB 34.50
            Assert.Equal(34.50m, await sb.ServiceProvider.GetRequiredService<ExchangeRateService>().GetRateAsync("USD", Cuma));
        }

        // A hâlâ 40 (B hiçbir şey bozamadı)
        using (var sa = host.ScopeFor(a))
        {
            var s = await sa.ServiceProvider.GetRequiredService<IPinnedRateRepository>().FindAsync(aId);
            Assert.NotNull(s);
            Assert.Equal(40m, s!.Kur);
            Assert.Equal(40m, await sa.ServiceProvider.GetRequiredService<ExchangeRateService>().GetRateAsync("USD", Cuma));
        }
    }

    // Ham SQL ile RLS FORCE kanıtı: B'nin GUC'uyla A'nın satırı görünmez.
    [Fact]
    public async Task P7b_raw_sql_RLS_FORCE_izolasyon()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        using var host = new TestHost(fx.AppConnectionString);
        using (var s0 = host.ScopeFor(a)) await SeedUsdAsync(s0);
        using (var sa = host.ScopeFor(a))
            await sa.ServiceProvider.GetRequiredService<FixedExchangeRateService>()
                .UpsertAsync(new SabitKurInput { Kod = "USD", Kur = 40m, Aktif = true });

        // racar_app bağlantısı + B'nin GUC'u → A'nın satırı görünmemeli
        await using var conn = new NpgsqlConnection(fx.AppConnectionString);
        await conn.OpenAsync();
        await using (var set = new NpgsqlCommand("select set_config('app.tenant_id', @t, false)", conn))
        {
            set.Parameters.AddWithValue("t", b.ToString());
            await set.ExecuteScalarAsync();
        }
        await using var cmd = new NpgsqlCommand("select count(*) from \"SabitKurlar\" where \"TenantId\" = @a", conn);
        cmd.Parameters.AddWithValue("a", a);
        var n = (long)(await cmd.ExecuteScalarAsync())!;
        Assert.Equal(0L, n); // RLS FORCE: B, A'nın satırını sayamaz bile

        // B kendi GUC'uyla A'nın satırını UPDATE/DELETE edemez (0 satır etkilenir)
        await using var upd = new NpgsqlCommand("update \"SabitKurlar\" set \"Kur\" = 1 where \"TenantId\" = @a", conn);
        upd.Parameters.AddWithValue("a", a);
        Assert.Equal(0, await upd.ExecuteNonQueryAsync());
        await using var del = new NpgsqlCommand("delete from \"SabitKurlar\" where \"TenantId\" = @a", conn);
        del.Parameters.AddWithValue("a", a);
        Assert.Equal(0, await del.ExecuteNonQueryAsync());
    }
}
