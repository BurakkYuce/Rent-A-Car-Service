using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RentACar.Application.Common;
using RentACar.Application.Reporting;
using RentACar.Domain.Common;
using RentACar.Domain.Enums;
using RentACar.Infrastructure.Persistence;
using RentACar.Web.Api.Rezervasyon;

namespace RentACar.Web.Api.Rapor;

/// <summary>Cari raporları: cari bakiye, yaşlandırma, extre özeti, tahsilat-fatura mutabakatı.</summary>
public static partial class ReportApi
{
    private static void MapCustomer(RouteGroupBuilder vr)
    {
        vr.MapGet("/cari-bakiye", CustomerBalances);
        vr.MapGet("/cari-bakiye/yaslandirma", Aging);
        vr.MapGet("/extre-ozeti", Statement);
        vr.MapGet("/tahsilat-fatura", CollectionInvoice);
        vr.MapGet("/tahsilat-fatura/mutabakat", CollectionReconciliation);
        MapInvoicePeriod(vr);
    }

    // ------------------------------------------------------------------ cari bakiye

    public sealed class CustomerBalanceFilter
    {
        /// <summary>Ad / telefon / e-posta / vergi no içinde.</summary>
        [FromQuery(Name = "ara")] public string? Ara { get; set; }
        [FromQuery(Name = "ozelKod")] public string? OzelKod { get; set; }
        [FromQuery(Name = "sinif")] public string? Sinif { get; set; }
        [FromQuery(Name = "doviz")] public string? Doviz { get; set; }
        /// <summary><c>kurumsal</c> | <c>bireysel</c> | boş.</summary>
        [FromQuery(Name = "tip")] public string? Tip { get; set; }
        /// <summary><c>borclu</c> | <c>alacakli</c> | boş.</summary>
        [FromQuery(Name = "bakiye")] public string? Bakiye { get; set; }
        /// <summary>Mutlak bakiye alt sınırı.</summary>
        [FromQuery(Name = "min")] public decimal? Min { get; set; }
    }

    /// <summary>Kart toplamları FİLTREDEN BAĞIMSIZ (firma geneli alacak/borç) + seçenekler tüm carilerden.</summary>
    public sealed record BalanceReportSummary(
        decimal BorcluToplam, decimal AlacakliToplam, IReadOnlyList<string> OzelKodlar,
        IReadOnlyList<string> Siniflar, IReadOnlyList<string> Dovizler);

    private static async Task<Ok<ReportResult<BalanceReportSummary, CariBalanceDto>>> CustomerBalances(
        [AsParameters] CustomerBalanceFilter f, [AsParameters] ReportPageQuery page, ReportService reports,
        ICurrentUser user, IDbContextFactory<AppDbContext> dbf, HttpContext http, CancellationToken ct)
    {
        ReportScope.RequireFirmWide(user);
        if (f.Min is { } m && Math.Abs(m) > Kira.RentalLimits.MaxAmount)
            throw new ValidationException($"Tutar en fazla {Kira.RentalLimits.MaxAmount:N0} olabilir.", "min");
        var tip = F(f.Tip)?.ToLowerInvariant() switch
        {
            null => (bool?)null, "kurumsal" => true, "bireysel" => false,
            _ => throw new ValidationException("Geçersiz tip değeri. İzin verilenler: kurumsal, bireysel.", "tip"),
        };
        var balance = F(f.Bakiye)?.ToLowerInvariant();
        if (balance is not null and not "borclu" and not "alacakli")
            throw new ValidationException("Geçersiz bakiye değeri. İzin verilenler: borclu, alacakli.", "bakiye");

        var rowList = await reports.GetAccountBalancesAsync(new CariBakiyeFilter
        {
            Ara = F(f.Ara), OzelKod = F(f.OzelKod), Sinif = F(f.Sinif), Doviz = F(f.Doviz),
            Kurumsal = tip, BakiyeTuru = balance, MinTutar = f.Min,
        }, ct);
        var all = await reports.GetAccountBalancesAsync(ct: ct);
        var mask = await CustomerMask.LoadAsync(dbf, rowList.Select(s => s.CariId), ct);
        var rows = rowList.Select(s => s with
        {
            Ad = mask.Name(s.CariId, s.Ad), Telefon = mask.Phone(s.CariId, s.Telefon), Email = mask.Email(s.CariId, s.Email),
        }).ToList();
        var summary = new BalanceReportSummary(
            all.Where(b => b.Bakiye > 0).Sum(b => b.Bakiye), -all.Where(b => b.Bakiye < 0).Sum(b => b.Bakiye),
            Distinct(all.Select(b => b.OzelKod?.Trim()), StringComparer.OrdinalIgnoreCase),
            Distinct(all.Select(b => b.Sinif?.Trim()), StringComparer.OrdinalIgnoreCase),
            Distinct(all.Select(b => b.Doviz?.Trim()), StringComparer.OrdinalIgnoreCase));
        var export = ReportExport.Links(http, user, "cari-bakiye",
        [
            ("ara", f.Ara), ("ozelKod", f.OzelKod), ("sinif", f.Sinif), ("doviz", f.Doviz), ("tip", f.Tip),
            ("bakiye", f.Bakiye), ("min", f.Min?.ToString(System.Globalization.CultureInfo.InvariantCulture)),
        ], pdf: true);
        return TypedResults.Ok(new ReportResult<BalanceReportSummary, CariBalanceDto>(
            new ReportPeriodDto(null, null), summary, page.Apply(rows, BalanceMap), export));
    }

    private static readonly SortFieldMap<CariBalanceDto> BalanceMap = SortFieldMap<CariBalanceDto>
        .Create(r => r.CariId).Alan("ad", r => r.Ad).Alan("bakiye", r => r.Bakiye).Alan("toplamBorc", r => r.ToplamBorc)
        .Alan("toplamAlacak", r => r.ToplamAlacak).Alan("doviz", r => r.Doviz).Alan("sinif", r => r.Sinif);

    // ------------------------------------------------------------------ yaşlandırma

    public sealed record AgingReportSummary(DateOnly Tarih, ReportAgingBuckets Kovalar, decimal Toplam);

    /// <summary>Cari borç yaşlandırma (brüt borç). <c>tarih</c> = yaşın ölçüldüğü gün (varsayılan bugün).</summary>
    private static async Task<Ok<ReportResult<AgingReportSummary, AgingRowDto>>> Aging(
        DateOnly? tarih, [AsParameters] ReportPageQuery page, ReportService reports, ICurrentUser user,
        IDbContextFactory<AppDbContext> dbf, HttpContext http, CancellationToken ct)
    {
        ReportScope.RequireFirmWide(user);
        var day = ReportPeriod.ValidateDay(tarih, "tarih") ?? ReportPeriod.Today;
        // Servis yaşı UTC takvim günü farkıyla sayar → günün UTC çıpası (gün sonu dahil).
        var rowList = await reports.GetAgingAsync(ReportPeriod.Anchor(day).AddDays(1).AddMicroseconds(-1), ct);
        var mask = await CustomerMask.LoadAsync(dbf, rowList.Select(s => s.CariId), ct);
        var rows = rowList.Select(s => s with { Ad = mask.Name(s.CariId, s.Ad) }).ToList();
        var bucket = new ReportAgingBuckets(rows.Sum(a => a.B0_30), rows.Sum(a => a.B31_60), rows.Sum(a => a.B61_90),
            rows.Sum(a => a.B90Plus));
        return TypedResults.Ok(new ReportResult<AgingReportSummary, AgingRowDto>(new ReportPeriodDto(null, day),
            new AgingReportSummary(day, bucket, rows.Sum(a => a.Toplam)), page.Apply(rows, AgingMap),
            ReportExport.Links(http, user, "yaslandirma", [("asOf", ReportExport.Day(day))], pdf: true)));
    }

    private static readonly SortFieldMap<AgingRowDto> AgingMap = SortFieldMap<AgingRowDto>
        .Create(r => r.CariId).Alan("ad", r => r.Ad).Alan("toplam", r => r.Toplam).Alan("b0_30", r => r.B0_30)
        .Alan("b31_60", r => r.B31_60).Alan("b61_90", r => r.B61_90).Alan("b90Plus", r => r.B90Plus);
}
