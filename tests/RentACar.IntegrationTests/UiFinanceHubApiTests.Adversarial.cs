using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RentACar.Application.Common;
using RentACar.Application.Finance;
using RentACar.Application.Kur;
using RentACar.Application.Periods;
using RentACar.Domain.Entities;
using RentACar.Domain.Enums;
using RentACar.IntegrationTests.Infrastructure;

namespace RentACar.IntegrationTests;

/// <summary>F8.1a bağımsız adversarial bulgularının kalıcı kilitleri (M1, M2, L1, L3, L4b).</summary>
public sealed partial class UiFinanceHubApiTests
{
    // ------------------------------------------------------------ M1: çözülen kurla baz taşması

    [Fact]
    public async Task M1_resolved_fixed_rate_overflow_is_rejected_and_reports_stay_healthy()
    {
        var e = await SetupAsync();
        var s = await LoginAsync(e, Who.Accountant);
        // Sabit kur servis sınırının (≤ 1.000.000) üstü → 400, API ve servis (Blazor yolu) aynı.
        await Problem(await PostAsync(s, "/kurlar/sabit", new { kod = "XYZ", kur = 1_000_001m }), HttpStatusCode.BadRequest, "dogrulama", "kur");
        await Assert.ThrowsAsync<ValidationException>(() => ReadAsync(e, sp =>
            sp.GetRequiredService<SabitKurService>().UpsertAsync(new SabitKurInput { Kod = "XYZ", Kur = 9_999_999_999_999m })));

        // Eski (sınır öncesi) veri: dev sabit kur doğrudan tabloda. Kursuz işlem ÇÖZÜLEN kurla baz sınırına takılır.
        await DbAsync(e, async db =>
        {
            db.SabitKurlar.Add(new SabitKur { TenantId = e.TenantId, Kod = "XYZ", Kur = 9_999_999_999_999m, Aktif = true });
            return await db.SaveChangesAsync();
        });
        var attack = new { cariId = e.CustomerA, yon = "Borclandir", tutar = 999_999_999_999_999m, doviz = "XYZ" };
        await Problem(await PostAsync(s, "/bakiye-duzeltme", attack, NewKey()), HttpStatusCode.BadRequest, "dogrulama", "tutar");
        await Problem(await PostAsync(s, "/kasa/virman", new { kaynak = "Kasa", hedef = "Banka", tutar = 1000m, doviz = "XYZ" }, NewKey()),
            HttpStatusCode.BadRequest, "dogrulama", "tutar");
        await Problem(await PostAsync(s, "/depozito/iade", new { cariId = e.CustomerA, tutar = 1000m, hesap = "Kasa", doviz = "XYZ" }, NewKey()),
            HttpStatusCode.BadRequest, "dogrulama", "tutar");
        await Problem(await PostAsync(s, "/api/ui/v1/finans/depozito/al", new { cariId = e.CustomerA, tutar = 1000m, hesap = "Kasa", doviz = "XYZ" }, NewKey()),
            HttpStatusCode.BadRequest, "dogrulama", "tutar");

        // Servis son savunması (Blazor dahil tüm yollar): 1000 × 10^13 = 10^16 ≥ 10^15 → yazımdan önce 400, defter boş.
        await Assert.ThrowsAsync<ValidationException>(() => ReadAsync(e, sp =>
            sp.GetRequiredService<BakiyeDuzeltmeService>().AdjustAsync(new BakiyeDuzeltmeInput
            { CariId = e.CustomerA, Tutar = 1000m, Yon = BakiyeDuzeltmeYonu.Borclandir, Doviz = "XYZ" })));
        Assert.Equal(0, await DbAsync(e, db => db.AccountLedgerEntries.CountAsync()));

        foreach (var path in new[] { "/donem-kapanis", "/kasa/ozet", $"/cariler/{e.CustomerA}/bakiye",
                     $"/cariler/{e.CustomerA}/ekstre", $"/cariler/{e.CustomerA}/acik-kalemler" })
            await Ok(await GetAsync(s, path));
    }

    // ------------------------------------------------------------ M2: dönem kilidi İstanbul günü

    [Fact]
    public void M2_period_lock_compares_istanbul_calendar_days()
    {
        // Oracle: İstanbul = UTC+3 (DST yok). Kilit günü D; D+1 00:30 İstanbul = D 21:30 UTC → AÇIK;
        // D 23:59 İstanbul = D 20:59 UTC → KAPALI.
        var utcMidnight = TestZaman.GunSonra(-10, saat: 0);
        var d = DateOnly.FromDateTime(utcMidnight.UtcDateTime);
        var closingIstanbulMidnight = utcMidnight.AddHours(-3);
        foreach (var closing in new[] { utcMidnight, closingIstanbulMidnight })
        {
            Assert.Equal(d, PeriodLock.LocalDay(closing));
            Assert.False(PeriodLock.IsClosed(utcMidnight.AddHours(21).AddMinutes(30), closing));
            Assert.True(PeriodLock.IsClosed(utcMidnight.AddHours(20).AddMinutes(59), closing));
        }
        Assert.Equal(utcMidnight.AddHours(21).AddTicks(-1), PeriodLock.DayEndUtc(d));
    }

    [Fact]
    public async Task M2_locked_day_boundary_follows_istanbul_day_in_api()
    {
        var e = await SetupAsync();
        var s = await LoginAsync(e, Who.Accountant);
        var today = PeriodLock.LocalDay(DateTimeOffset.UtcNow);
        var locked = today.AddDays(-2);
        Assert.Equal(HttpStatusCode.NoContent, (await PostAsync(s, "/donem-kapanis/kilitle", new { kapanisTarihi = locked })).StatusCode);
        Assert.Equal(locked.ToString("yyyy-MM-dd"), (await Ok(await GetAsync(s, "/donem-kapanis"))).GetProperty("kapanisTarihi").GetString());

        // Ertesi gün 00:30 İstanbul (+03:00) — UTC'de hâlâ kilitli gün → önce reddediliyordu, artık 200.
        var nextDayEarly = new DateTimeOffset(locked.AddDays(1).ToDateTime(new TimeOnly(0, 30)), TimeSpan.FromHours(3));
        await IdOf(await PostAsync(s, "/bakiye-duzeltme",
            new { cariId = e.CustomerA, yon = "Borclandir", tutar = 5m, tarih = nextDayEarly }, NewKey()));
        // Kilitli günün 23:59'u İstanbul → 400.
        var lockedLate = new DateTimeOffset(locked.ToDateTime(new TimeOnly(23, 59)), TimeSpan.FromHours(3));
        await Problem(await PostAsync(s, "/bakiye-duzeltme",
            new { cariId = e.CustomerA, yon = "Borclandir", tutar = 5m, tarih = lockedLate }, NewKey()), HttpStatusCode.BadRequest, "dogrulama");
        Assert.Equal(5m, await BalanceAsync(e, e.CustomerA));
    }

    // ------------------------------------------------------------ L1 / L3 / L4b

    [Fact]
    public async Task L1_auto_collection_extreme_dates_are_400()
    {
        var e = await SetupAsync();
        var s = await LoginAsync(e, Who.Accountant);
        await Problem(await GetAsync(s, "/otomatik-tahsilat?vadeMax=9999-12-31"), HttpStatusCode.BadRequest, "dogrulama", "vadeMax");
    }

    [Fact]
    public async Task L3_close_items_same_key_other_selection_is_not_same_content()
    {
        var e = await SetupAsync();
        var s = await LoginAsync(e, Who.Accountant);
        await IdOf(await PostAsync(s, "/bakiye-duzeltme", new { cariId = e.CustomerA, yon = "Borclandir", tutar = 100m }, NewKey()));
        await IdOf(await PostAsync(s, "/bakiye-duzeltme", new { cariId = e.CustomerA, yon = "Borclandir", tutar = 200m }, NewKey()));
        var items = (await Ok(await GetAsync(s, $"/cariler/{e.CustomerA}/acik-kalemler"))).GetProperty("kalemler")
            .EnumerateArray().Select(x => x.GetProperty("id").GetGuid()).ToList();
        var key = NewKey();
        await Ok(await PostAsync(s, $"/cariler/{e.CustomerA}/toplu-kapat",
            new { secim = new object[] { new { kalemId = items[0] } }, hesap = "Kasa" }, key));
        var other = await Problem(await PostAsync(s, $"/cariler/{e.CustomerA}/toplu-kapat",
            new { secim = new object[] { new { kalemId = items[1] } }, hesap = "Kasa" }, key), HttpStatusCode.Conflict, "mukerrer");
        Assert.False(other.GetProperty("mevcut").GetProperty("ayniIcerik").GetBoolean());
        var partial = await Problem(await PostAsync(s, $"/cariler/{e.CustomerA}/toplu-kapat",
            new { secim = new object[] { new { kalemId = items[0], tutar = 1m } }, hesap = "Kasa" }, key), HttpStatusCode.Conflict, "mukerrer");
        Assert.False(partial.GetProperty("mevcut").GetProperty("ayniIcerik").GetBoolean());
        Assert.Equal(1, await CashCountAsync(e));
    }

    [Fact]
    public async Task L4b_scoped_operator_cannot_reverse_tenant_wide_cash_transaction()
    {
        var e = await SetupAsync();
        var admin = await LoginAsync(e, Who.Admin);
        var collection = await IdOf(await PostAsync(admin, "/api/ui/v1/finans/tahsilat",
            new { cariId = e.CustomerB, tutar = 40m, hesap = "Kasa" }, NewKey()));
        var scoped = await LoginAsync(e, Who.OperatorB);
        await Problem(await PostAsync(scoped, $"/kasa/islemler/{collection}/ters", null), HttpStatusCode.Forbidden, "yetki_yok");
        Assert.Equal(-40m, await BalanceAsync(e, e.CustomerB));
        await IdOf(await PostAsync(admin, $"/kasa/islemler/{collection}/ters", null));
        Assert.Equal(0m, await BalanceAsync(e, e.CustomerB));
    }
}
