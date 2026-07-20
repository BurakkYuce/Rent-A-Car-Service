using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RentACar.Domain.Entities;
using RentACar.IntegrationTests.Infrastructure;
using RentACar.Infrastructure.Persistence;
using RentACar.Web.Jobs;

namespace RentACar.IntegrationTests;

/// <summary>
/// Ops-watchdog KARAR MANTIĞI — SAF (DB'siz), BAĞIMSIZ ORACLE: beklenen değerler elle kurulur, koddan değil.
/// </summary>
public sealed class OpsWatchdogPolicyTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 20, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    // phone, template, enabled -> aktif?
    [InlineData(null, "ops", null, false)]          // telefon yok
    [InlineData("", "ops", null, false)]            // telefon boş
    [InlineData("  ", "ops", null, false)]          // telefon whitespace
    [InlineData("+905551112233", null, null, false)] // şablon yok
    [InlineData("+905551112233", "ops", null, true)] // ikisi var → aktif
    [InlineData("+905551112233", "ops", "true", true)]
    [InlineData("+905551112233", "ops", "false", false)] // açıkça kapatıldı
    [InlineData("+905551112233", "ops", "FALSE", false)] // büyük/küçük harf duyarsız
    public void Aktif_kapisi(string? phone, string? template, string? enabled, bool beklenen)
        => Assert.Equal(beklenen, OpsWatchdog.Aktif(phone, template, enabled));

    [Fact]
    public void KurBayatMi_null_kayit_bayat_degil()   // henüz kur çekilmemiş → soğuk-başlangıç gürültüsü yok
        => Assert.False(OpsWatchdog.KurBayatMi(null, Now, 3));

    [Fact]
    public void KurBayatMi_bir_gunluk_taze()          // 1 gün < 3 eşik
        => Assert.False(OpsWatchdog.KurBayatMi(Now.AddDays(-1), Now, 3));

    [Fact]
    public void KurBayatMi_dort_gun_bayat()           // 4 gün > 3 eşik
        => Assert.True(OpsWatchdog.KurBayatMi(Now.AddDays(-4), Now, 3));

    [Fact]
    public void KurBayatMi_tam_esik_sinirda_bayat_degil() // tam 3.0 gün → > değil
        => Assert.False(OpsWatchdog.KurBayatMi(Now.AddDays(-3), Now, 3));

    [Theory]
    // guncelToplam, sonAlarm, esik -> alarm?
    [InlineData(5, 3, 2, true)]   // delta 2 >= 2
    [InlineData(4, 3, 2, false)]  // delta 1 < 2
    [InlineData(10, 0, 2, true)]  // sıfırdan 10 hata
    [InlineData(5, 5, 2, false)]  // delta 0
    [InlineData(5, 3, 0, false)]  // eşik 0 → alarm yok
    [InlineData(7, 3, 4, true)]   // delta 4 >= 4
    public void JobHataAlarmi_delta(long guncel, long sonAlarm, int esik, bool beklenen)
        => Assert.Equal(beklenen, OpsWatchdog.JobHataAlarmi(guncel, sonAlarm, esik));

    [Fact]
    public void KurMesaji_gun_ve_tarih_icerir()
    {
        // now - sonTarih = 4.5 gün → floor 4; tarih 2026-07-16 (elle).
        var mesaj = OpsWatchdog.KurMesaji(new DateTimeOffset(2026, 7, 16, 0, 0, 0, TimeSpan.Zero), Now);
        Assert.Contains("4 gündür", mesaj);
        Assert.Contains("2026-07-16", mesaj);
    }

    [Fact]
    public void JobMesaji_ad_ve_sayi_icerir()
    {
        var mesaj = OpsWatchdog.JobMesaji("tcmb-kur", 3);
        Assert.Contains("'tcmb-kur'", mesaj);
        Assert.Contains("3 kez", mesaj);
    }
}

/// <summary>
/// Ops-watchdog kur-okuma DB yolu — gerçek PostgreSQL. En yeni kur kaydını okur (ulusal tablo, GUC gerekmez).
/// </summary>
[Collection("postgres")]
public sealed class OpsWatchdogKurTests(PostgresFixture fx)
{
    [Fact]
    public async Task SonKurTarihi_en_yeniyi_okur_ve_bayatligi_belirler()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();

        // Paylaşımlı ulusal tablo → deterministiklik için temizle, sonra elle 2 kayıt (whole-second: CI tick tuzağı yok).
        // Kod "WDG" (watchdog) BİLEREK benzersiz: bıraktığımız satırlar diğer kur testlerinin USD/EUR aramasına sızmasın.
        var now = new DateTimeOffset(2026, 7, 20, 12, 0, 0, TimeSpan.Zero);
        await using (var seed = await factory.CreateDbContextAsync())
        {
            seed.KurKayitlari.RemoveRange(await seed.KurKayitlari.ToListAsync());
            seed.KurKayitlari.Add(new KurKaydi { Tarih = now.AddDays(-10), Kod = "WDG", Ad = "WDG", Birim = 1, ForexSatis = 30m });
            seed.KurKayitlari.Add(new KurKaydi { Tarih = now.AddDays(-5), Kod = "WDG", Ad = "WDG", Birim = 1, ForexSatis = 35m });
            await seed.SaveChangesAsync();
        }

        await using var db = await factory.CreateDbContextAsync();
        var son = await OpsWatchdog.SonKurTarihiAsync(db);

        Assert.Equal(now.AddDays(-5), son);                    // en yeni (-5), -10 değil
        Assert.True(OpsWatchdog.KurBayatMi(son, now, 3));      // 5 gün > 3 → bayat
        Assert.False(OpsWatchdog.KurBayatMi(son, now, 7));     // 5 gün < 7 → değil
    }

    [Fact]
    public async Task SonKurTarihi_bos_tabloda_null()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();

        await using (var seed = await factory.CreateDbContextAsync())
        {
            seed.KurKayitlari.RemoveRange(await seed.KurKayitlari.ToListAsync());
            await seed.SaveChangesAsync();
        }

        await using var db = await factory.CreateDbContextAsync();
        Assert.Null(await OpsWatchdog.SonKurTarihiAsync(db));  // kayıt yok → null → bayat DEĞİL
    }
}
