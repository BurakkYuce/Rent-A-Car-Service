using System.Net;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using RentACar.Domain.Entities;
using RentACar.Domain.Enums;
using RentACar.IntegrationTests.Infrastructure;

namespace RentACar.IntegrationTests;

/// <summary>
/// #286 adversarial M1 — ceza iptali ödeme/yansıtmayla AYNI kilit altında. Sıra DIŞ bir <c>FOR UPDATE</c> ile zorlanır:
/// dış işlem satırı tutarken ilk istek gönderilir ve kilitte beklediği görülür, sonra ikinci istek; dış kilit
/// bırakılınca istekler kuyruk sırasıyla işlenir. Beklenen son durum elle kurulmuş senaryodan (bağımsız oracle).
/// </summary>
public sealed partial class UiFinanceDocumentTests
{
    /// <summary>Kiracı bağlamında (racar_app + RLS) satırı kilitleyen dış işlem.</summary>
    private async Task<(NpgsqlConnection Conn, NpgsqlTransaction Tx)> HoldRowLockAsync(Env e, string table, Guid id)
    {
        var conn = new NpgsqlConnection(fx.Pg.AppConnectionString);
        await conn.OpenAsync();
        var tx = await conn.BeginTransactionAsync();
        await using (var set = new NpgsqlCommand("SELECT set_config('app.tenant_id', @t, true)", conn, tx))
        {
            set.Parameters.AddWithValue("t", e.TenantId.ToString());
            await set.ExecuteScalarAsync();
        }
        await using var lk = new NpgsqlCommand($"SELECT 1 FROM \"{table}\" WHERE \"Id\" = @id FOR UPDATE", conn, tx);
        lk.Parameters.AddWithValue("id", id);
        Assert.NotNull(await lk.ExecuteScalarAsync());
        return (conn, tx);
    }

    /// <summary>Bu veritabanında kilit bekleyen oturum sayısı en az <paramref name="n"/> olana dek bekler (en çok 10 sn).</summary>
    private async Task WaitForLockWaitersAsync(int n)
    {
        await using var conn = new NpgsqlConnection(fx.Pg.AppConnectionString);
        await conn.OpenAsync();
        for (var i = 0; i < 200; i++)
        {
            await using var cmd = new NpgsqlCommand(
                "SELECT count(*) FROM pg_stat_activity WHERE datname = current_database() AND wait_event_type = 'Lock'", conn);
            if ((long)(await cmd.ExecuteScalarAsync())! >= n) return;
            await Task.Delay(50);
        }
        Assert.Fail($"{n} kilit bekleyen oturum görülmedi.");
    }

    private async Task<(HttpResponseMessage First, HttpResponseMessage Second)> OrderedAsync(
        Env e, string table, Guid rowId, Func<Task<HttpResponseMessage>> first, Func<Task<HttpResponseMessage>> second)
    {
        var (conn, tx) = await HoldRowLockAsync(e, table, rowId);
        try
        {
            var a = first();
            await WaitForLockWaitersAsync(1);
            var b = second();
            await WaitForLockWaitersAsync(2);
            await tx.RollbackAsync();
            return (await a, await b);
        }
        finally { await conn.DisposeAsync(); }
    }

    private Task<Penalty> PenaltyRowAsync(Env e, Guid id) => DbAsync(e, db => db.Penalties.AsNoTracking().SingleAsync(p => p.Id == id));

    [Fact]
    public async Task Penalty_payment_then_cancel_cancel_is_rejected_and_payment_stands()
    {
        var e = await SetupAsync();
        var s = await LoginAsync(e, Who.Admin);
        var (id, line1, _) = await PenaltyAsync(e, s);
        var (pay, cancel) = await OrderedAsync(e, "Penalties", id,
            () => PostAsync(s, $"/cezalar/{id}/odeme", new { satirId = line1, tutar = 100m, hesap = "Kasa" }, NewKey()),
            () => PostAsync(s, $"/cezalar/{id}/iptal", null));
        await Ok(pay);
        await Problem(cancel, HttpStatusCode.BadRequest, "dogrulama");
        var p = await PenaltyRowAsync(e, id);
        Assert.Equal(PenaltyStatus.Kismi, p.Durum);           // iptal edilmedi, ödeme durumu korundu
        Assert.Equal(100m, p.OdenenTutar);
        Assert.Equal(100.50m, p.Kalan);                    // 200,50 − 100
        await AllLedgerBalancedAsync(e);
    }

    [Fact]
    public async Task Penalty_reflect_then_cancel_cancel_is_rejected_and_customer_debt_matches_state()
    {
        var e = await SetupAsync();
        var s = await LoginAsync(e, Who.Admin);
        var (id, _, _) = await PenaltyAsync(e, s);
        var (reflect, cancel) = await OrderedAsync(e, "Penalties", id,
            () => PostAsync(s, $"/cezalar/{id}/yansit", null),
            () => PostAsync(s, $"/cezalar/{id}/iptal", null));
        await Ok(reflect);
        await Problem(cancel, HttpStatusCode.BadRequest, "dogrulama");
        Assert.Equal(PenaltyStatus.Yansitildi, (await PenaltyRowAsync(e, id)).Durum);
        Assert.Equal(200.50m, await CustomerBalanceAsync(e, e.Customer)); // borç yansıtılmış cezaya ait, iptal değil
        await AllLedgerBalancedAsync(e);
    }

    [Fact]
    public async Task Penalty_cancel_then_payment_payment_is_rejected_and_state_stays_cancelled()
    {
        var e = await SetupAsync();
        var s = await LoginAsync(e, Who.Admin);
        var (id, line1, _) = await PenaltyAsync(e, s);
        var (cancel, pay) = await OrderedAsync(e, "Penalties", id,
            () => PostAsync(s, $"/cezalar/{id}/iptal", null),
            () => PostAsync(s, $"/cezalar/{id}/odeme", new { satirId = line1, tutar = 100m, hesap = "Kasa" }, NewKey()));
        await Ok(cancel);
        await Problem(pay, HttpStatusCode.BadRequest, "dogrulama");
        var p = await PenaltyRowAsync(e, id);
        Assert.Equal(PenaltyStatus.Iptal, p.Durum);
        Assert.Equal(0m, p.OdenenTutar);
        Assert.Equal(0, await DbAsync(e, db => db.PenaltyOdemeleri.CountAsync()));
        Assert.Equal(0, await DbAsync(e, db => db.AccountLedgerEntries.CountAsync(x => x.SourceType == "CezaOdeme")));
        await Problem(await PostAsync(s, $"/cezalar/{id}/yansit", null), HttpStatusCode.BadRequest, "dogrulama");
    }
}
