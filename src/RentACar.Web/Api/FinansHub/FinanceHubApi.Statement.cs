using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using RentACar.Application.Bookings;
using RentACar.Application.Common;
using RentACar.Application.Finance;
using RentACar.Domain.Entities;
using RentACar.Domain.Enums;
using RentACar.Infrastructure.Persistence;
using RentACar.Web.Api.Finans;
using RentACar.Web.Api.Rezervasyon;

namespace RentACar.Web.Api.FinansHub;

/// <summary>Cari ekstre (Blazor <c>/cariler/{id}/ekstre</c>) ve tek cari açık borç kalemleri (<c>/tek-cari-toplu</c>).</summary>
public static partial class FinanceHubApi
{
    private static void MapStatement(RouteGroupBuilder write, RouteGroupBuilder financeRead)
    {
        financeRead.MapGet("/cariler/{cariId:guid}/ekstre", GetStatement);
        write.MapGet("/cariler/{cariId:guid}/acik-kalemler", GetOpenItems);
        write.MapPost("/cariler/{cariId:guid}/toplu-kapat", PostCloseItems)
            .Produces<Api.UiHata.MukerrerProblemi>(StatusCodes.Status409Conflict, "application/problem+json");
    }

    /// <summary>
    /// Ekstre: satırlar + devir + yürüyen bakiye (sunucuda). <c>bakiye</c> filtresizdir. <c>mod=ozet</c> ay (İstanbul) ×
    /// kaynak kırılımını da döner (detayla aynı veri). Döviz/kaynak seçenekleri TÜM hareketlerden türetilir.
    /// </summary>
    private static async Task<Results<Ok<CustomerStatement>, ProblemHttpResult>> GetStatement(
        Guid cariId, DateOnly? bas, DateOnly? bit, string? doviz, string? kaynak, string? kiraDurum, string? mod,
        CashService cash, IDbContextFactory<AppDbContext> f, CancellationToken ct)
    {
        var summary = mod?.Trim().ToLowerInvariant() switch
        {
            null or "" or "detay" => false,
            "ozet" => true,
            _ => throw new ValidationException("Görünüm 'detay' ya da 'ozet' olmalıdır.", "mod"),
        };
        FinansApi.Metin(doviz, 8, "doviz");
        FinansApi.Metin(kaynak, 64, "kaynak");
        var rentalStatus = F5Ortak.EnumAdi<RentalStatus>(kiraDurum, "kiraDurum");
        var names = await F5Ortak.CarilerAsync(f, [cariId], ct);
        if (!names.ContainsKey(cariId)) return F5Ortak.Bulunamadi("Cari bulunamadı.");

        var balance = await cash.GetCariBalanceAsync(cariId, ct);
        var all = (await cash.GetStatementAsync(cariId, null, ct)).Satirlar;
        var (min, max) = F5Ortak.GunAraligi(bas, bit);
        var filter = new CariEkstreFilter
        {
            Bas = min, Bit = max, Doviz = F5Ortak.Nz(doviz), SourceType = F5Ortak.Nz(kaynak), KiraDurum = rentalStatus,
        };
        var result = await cash.GetStatementAsync(cariId, filter, ct);

        var running = result.Devir;
        var lines = new List<CustomerStatementLine>(result.Satirlar.Count);
        foreach (var e in result.Satirlar)
        {
            running += e.SignedBase;
            var isDebit = e.Direction == LedgerDirection.Debit;
            lines.Add(new CustomerStatementLine(
                e.Id, e.EntryDateUtc, e.Description, e.SourceType, e.SourceId, e.Amount.Currency, e.Amount.Amount,
                e.Amount.Rate, isDebit ? e.Amount.AmountInBase : 0m, isDebit ? 0m : e.Amount.AmountInBase, running,
                e.SourceType is "Tahsilat" or "Odeme" ? e.SourceId : null));
        }

        IReadOnlyList<CustomerStatementSummaryLine>? byMonth = summary ? Summarize(result.Satirlar) : null;
        return TypedResults.Ok(new CustomerStatement(
            cariId, F5Ortak.CariAdi(names, cariId), balance, result.Devir, !filter.TarihDisiDaraltmaVar, lines, byMonth,
            lines.Sum(l => l.Borc), lines.Sum(l => l.Alacak),
            [.. all.Select(e => e.Amount.Currency).Where(c => !string.IsNullOrWhiteSpace(c))
                .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(c => c, StringComparer.Ordinal)],
            [.. all.Select(e => e.SourceType).Where(s => !string.IsNullOrWhiteSpace(s))
                .Distinct(StringComparer.Ordinal).OrderBy(s => s, StringComparer.Ordinal)]));
    }

    /// <summary>Ay (İstanbul takvimi) × kaynak. Devir özete girmez (hareket değil, açılış bakiyesi).</summary>
    private static List<CustomerStatementSummaryLine> Summarize(IEnumerable<AccountLedgerEntry> lines)
        => [.. lines
            .GroupBy(e => (Month: TimeZoneInfo.ConvertTime(e.EntryDateUtc, TenantGun.Dilim).ToString("yyyy-MM"), e.SourceType))
            .Select(g =>
            {
                var debit = g.Where(e => e.Direction == LedgerDirection.Debit).Sum(e => e.Amount.AmountInBase);
                var credit = g.Where(e => e.Direction == LedgerDirection.Credit).Sum(e => e.Amount.AmountInBase);
                return new CustomerStatementSummaryLine(g.Key.Month, g.Key.SourceType, g.Count(), debit, credit, debit - credit);
            })
            .OrderBy(o => o.Ay, StringComparer.Ordinal).ThenBy(o => o.Kaynak, StringComparer.Ordinal)];

    /// <summary>Carinin BORÇ kalemleri + kapanan (tahsis) tutarları — servisin çit kurarken kullandığı kaynaktan.</summary>
    private static async Task<Results<Ok<CustomerOpenItems>, ProblemHttpResult>> GetOpenItems(
        Guid cariId, CashService cash, IDbContextFactory<AppDbContext> f, CancellationToken ct)
    {
        var names = await F5Ortak.CarilerAsync(f, [cariId], ct);
        if (!names.ContainsKey(cariId)) return F5Ortak.Bulunamadi("Cari bulunamadı.");
        var debts = (await cash.GetStatementAsync(cariId, null, ct)).Satirlar
            .Where(x => x.Direction == LedgerDirection.Debit).OrderBy(x => x.EntryDateUtc).ToList();
        var closed = await cash.KapatilanTutarlarAsync([.. debts.Select(x => x.Id)], ct);
        var items = debts.Select(s =>
        {
            var done = closed.GetValueOrDefault(s.Id);
            var rest = s.Amount.AmountInBase - done;
            var isClosed = rest <= 0.005m;
            return new CustomerOpenItem(s.Id, s.EntryDateUtc, s.SourceType, s.Description, s.Amount.Amount,
                s.Amount.Currency, s.Amount.AmountInBase, done, isClosed ? 0m : rest, isClosed);
        }).ToList();
        return TypedResults.Ok(new CustomerOpenItems(cariId, F5Ortak.CariAdi(names, cariId),
            await cash.GetCariBalanceAsync(cariId, ct), items.Where(i => !i.Kapali).Sum(i => i.Kalan), items));
    }
}
