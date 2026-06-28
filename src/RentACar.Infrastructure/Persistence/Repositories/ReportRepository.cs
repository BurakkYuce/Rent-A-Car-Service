using Microsoft.EntityFrameworkCore;
using RentACar.Application.Reporting;
using RentACar.Domain.Enums;

namespace RentACar.Infrastructure.Persistence.Repositories;

/// <summary>
/// Salt-okunur defter sorgusu. Verilen hesap türleri + tarih aralığındaki AccountLedgerEntry
/// satırlarını base tutarıyla (Amount×Rate) döndürür. Tenant izolasyonu query filter + RLS ile
/// otomatik. Karmaşık-tip (Money) alanları ham kolon olarak çekilir; base bellek-içi hesaplanır.
/// </summary>
public sealed class ReportRepository(IDbContextFactory<AppDbContext> factory) : IReportRepository
{
    private readonly IDbContextFactory<AppDbContext> _factory = factory;

    public async Task<IReadOnlyList<LedgerRowDto>> GetLedgerRowsAsync(
        IReadOnlyCollection<LedgerAccountType> accountTypes,
        DateTimeOffset? from, DateTimeOffset? to, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);

        var q = db.AccountLedgerEntries.AsNoTracking().Where(e => accountTypes.Contains(e.AccountType));
        if (from is { } f) q = q.Where(e => e.EntryDateUtc >= f);
        if (to is { } t) q = q.Where(e => e.EntryDateUtc <= t);

        var raw = await q
            .Select(e => new
            {
                e.EntryDateUtc, e.AccountType, e.Direction, e.SourceType, e.Description,
                Amount = e.Amount.Amount, Rate = e.Amount.Rate
            })
            .ToListAsync(ct);

        return raw
            .Select(r => new LedgerRowDto(
                r.EntryDateUtc, r.AccountType, r.Direction, r.SourceType, r.Description, r.Amount * r.Rate))
            .ToList();
    }

    public async Task<IReadOnlyList<CariLedgerRowDto>> GetCariLedgerRowsAsync(
        DateTimeOffset? asOf, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);

        var q = db.AccountLedgerEntries.AsNoTracking().Where(e => e.AccountType == LedgerAccountType.Cari);
        if (asOf is { } a) q = q.Where(e => e.EntryDateUtc <= a);

        var raw = await q
            .Select(e => new { e.AccountRef, e.Direction, Amount = e.Amount.Amount, Rate = e.Amount.Rate, e.EntryDateUtc })
            .ToListAsync(ct);

        // Cari adları: DisplayName mapped değil (computed) → Customers bellek-içi çekilip eşlenir.
        var names = (await db.Customers.AsNoTracking().ToListAsync(ct))
            .ToDictionary(c => c.Id, c => c.DisplayName);

        return raw
            .Select(r =>
            {
                var id = r.AccountRef ?? Guid.Empty;
                return new CariLedgerRowDto(
                    id, names.TryGetValue(id, out var n) && !string.IsNullOrWhiteSpace(n) ? n : "(bilinmeyen cari)",
                    r.Direction, r.Amount * r.Rate, r.EntryDateUtc);
            })
            .ToList();
    }

    public async Task<IReadOnlyList<VehicleStatus>> GetVehicleStatusesAsync(CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.Vehicles.AsNoTracking().Select(v => v.Durum).ToListAsync(ct);
    }

    public async Task<IReadOnlyList<DolulukKiraRowDto>> GetRentalIntervalsAsync(
        DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);

        // İptal hariç; efektif bitiş = gerçek dönüş ?? planlı bitiş. Dönemle çakışanlar:
        // efektifBitiş >= from AND Bas <= to. (?? → COALESCE; EF çevirir.)
        var raw = await db.Rentals.AsNoTracking()
            .Where(r => r.Durum != RentalStatus.Iptal)
            .Select(r => new { r.BasTar, Bit = r.GercekDonusTar ?? r.BitTar })
            .Where(r => r.Bit >= from && r.BasTar <= to)
            .ToListAsync(ct);

        return raw.Select(r => new DolulukKiraRowDto(r.BasTar, r.Bit)).ToList();
    public async Task<TahsilatFaturaDto> GetTahsilatFaturaAsync(
        DateTimeOffset? from, DateTimeOffset? to, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);

        // Fatura: İptal hariç; base = GenelToplam × Kur (Kur düz kolon, bellek-içi çarpılır).
        var fq = db.Invoices.AsNoTracking().Where(i => i.Durum != InvoiceStatus.Iptal);
        if (from is { } ff) fq = fq.Where(i => i.Tarih >= ff);
        if (to is { } ft) fq = fq.Where(i => i.Tarih <= ft);
        var faturalar = await fq.Select(i => new { i.GenelToplam, i.Kur }).ToListAsync(ct);
        int faturaAdet = faturalar.Count;
        decimal faturaToplam = faturalar.Sum(f => f.GenelToplam * f.Kur);

        // Tahsilat: Tip=Tahsilat, ters kayıt hariç; base = Amount × Rate.
        var tq = db.CashTransactions.AsNoTracking()
            .Where(c => c.Tip == CashTransactionType.Tahsilat && !c.TersKayitMi);
        if (from is { } tf) tq = tq.Where(c => c.Tarih >= tf);
        if (to is { } tt) tq = tq.Where(c => c.Tarih <= tt);
        var tahsilatlar = await tq
            .Select(c => new { Amount = c.Amount.Amount, Rate = c.Amount.Rate }).ToListAsync(ct);
        int tahsilatAdet = tahsilatlar.Count;
        decimal tahsilatToplam = tahsilatlar.Sum(t => t.Amount * t.Rate);

        return new TahsilatFaturaDto(
            faturaAdet, faturaToplam, tahsilatAdet, tahsilatToplam, faturaToplam - tahsilatToplam);
    }

    public async Task<int> GetActiveRentalCountAsync(CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.Rentals.AsNoTracking().CountAsync(r => r.Durum == RentalStatus.Kirada, ct);
    }

    public async Task<IReadOnlyList<ServiceCostRowDto>> GetServiceCostRowsAsync(
        DateTimeOffset? from, DateTimeOffset? to, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);

        var q = db.ServiceRecords.AsNoTracking().Where(r => r.Durum == ServisDurum.Tamamlandi);
        if (from is { } f) q = q.Where(r => r.CikisTarihi >= f);
        if (to is { } t) q = q.Where(r => r.CikisTarihi <= t);

        var rows = await q
            .Select(r => new { r.VehicleId, r.Tip, r.ToplamIscilik })
            .ToListAsync(ct);

        var plaka = (await db.Vehicles.AsNoTracking().Select(v => new { v.Id, v.Plaka }).ToListAsync(ct))
            .ToDictionary(v => v.Id, v => v.Plaka);

        return rows
            .Select(r => new ServiceCostRowDto(
                r.VehicleId, plaka.TryGetValue(r.VehicleId, out var p) ? p : "(bilinmeyen araç)", r.Tip, r.ToplamIscilik))
            .ToList();
    }
}
