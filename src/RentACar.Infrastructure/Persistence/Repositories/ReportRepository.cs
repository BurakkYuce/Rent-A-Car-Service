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
    }

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

    public async Task<GunlukFaaliyetDto> GetGunlukFaaliyetAsync(
        DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);

        var yeniRez = await db.Reservations.AsNoTracking()
            .CountAsync(r => r.CreatedAtUtc >= from && r.CreatedAtUtc <= to, ct);
        var yeniKira = await db.Rentals.AsNoTracking()
            .CountAsync(r => r.CreatedAtUtc >= from && r.CreatedAtUtc <= to, ct);
        // Çıkış: o gün başlayan (İptal olmayan) kiralar. Dönüş: o gün gerçek dönüşü yapılan kiralar.
        var cikis = await db.Rentals.AsNoTracking()
            .CountAsync(r => r.Durum != RentalStatus.Iptal && r.BasTar >= from && r.BasTar <= to, ct);
        var donus = await db.Rentals.AsNoTracking()
            .CountAsync(r => r.GercekDonusTar != null && r.GercekDonusTar >= from && r.GercekDonusTar <= to, ct);

        // Tahsilat: ters kayıt hariç; base tutar (Amount×Rate) bellek-içi toplanır.
        var tahsilatlar = await db.CashTransactions.AsNoTracking()
            .Where(c => c.Tip == CashTransactionType.Tahsilat && !c.TersKayitMi && c.Tarih >= from && c.Tarih <= to)
            .Select(c => new { c.Amount.Amount, c.Amount.Rate })
            .ToListAsync(ct);
        var tahsilatTutar = tahsilatlar.Sum(t => t.Amount * t.Rate);

        // Fatura: İptal hariç; GenelToplam base zaten (Currency/Kur ayrı tutulur ama GenelToplam fatura
        // para birimindedir → günlük faaliyet sayacında brüt toplam olarak gösterilir).
        var faturalar = await db.Invoices.AsNoTracking()
            .Where(i => i.Durum != InvoiceStatus.Iptal && i.Tarih >= from && i.Tarih <= to)
            .Select(i => new { i.GenelToplam, i.Kur })
            .ToListAsync(ct);
        var faturaTutar = faturalar.Sum(f => f.GenelToplam * f.Kur);

        return new GunlukFaaliyetDto(
            yeniRez, yeniKira, cikis, donus,
            tahsilatlar.Count, tahsilatTutar, faturalar.Count, faturaTutar);
    }

    public async Task<IReadOnlyList<KdvLineRowDto>> GetKdvLineRowsAsync(
    public async Task<IReadOnlyList<EkHizmetSalesRowDto>> GetEkHizmetSalesRowsAsync(
        DateTimeOffset? from, DateTimeOffset? to, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);

        var inv = db.Invoices.AsNoTracking().Where(i => i.Durum != InvoiceStatus.Iptal);
        if (from is { } f) inv = inv.Where(i => i.Tarih >= f);
        if (to is { } t) inv = inv.Where(i => i.Tarih <= t);

        // Satır tutarları fatura para birimindedir → base para için Kur ile çarp (bellek-içi).
        var raw = await (from l in db.InvoiceLines.AsNoTracking()
                         join i in inv on l.InvoiceId equals i.Id
                         select new { l.KdvOrani, l.SatirNet, l.SatirKdv, l.SatirToplam, i.Kur, InvoiceId = i.Id })
            .ToListAsync(ct);

        return raw
            .Select(r => new KdvLineRowDto(
                r.KdvOrani, r.SatirNet * r.Kur, r.SatirKdv * r.Kur, r.SatirToplam * r.Kur, r.InvoiceId))
            .ToList();
        // İptal kiraların ek hizmetleri satış sayılmaz. Tarih: kalemin eklenme zamanı (CreatedAtUtc).
        var aktifKiraIds = db.Rentals.AsNoTracking().Where(r => r.Durum != RentalStatus.Iptal).Select(r => r.Id);
        var q = db.RentalAddOns.AsNoTracking().Where(a => aktifKiraIds.Contains(a.RentalId));
        if (from is { } f) q = q.Where(a => a.CreatedAtUtc >= f);
        if (to is { } t) q = q.Where(a => a.CreatedAtUtc <= t);

        // RentalAddOn tutarları baz para (TRY) olarak saklanır (Kur yok) → doğrudan kullanılır.
        return await q
            .Select(a => new EkHizmetSalesRowDto(a.Ad, a.Miktar, a.NetTutar, a.KdvTutar, a.Toplam, a.RentalId))
            .ToListAsync(ct);
    }
}
