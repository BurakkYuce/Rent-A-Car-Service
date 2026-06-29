using Microsoft.EntityFrameworkCore;
using RentACar.Application.Reporting;
using RentACar.Domain.Entities;
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

    public async Task<IReadOnlyList<PeriyodikServisRow>> GetPeriyodikServisRowsAsync(CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);

        // Her araç için tanımlı en yüksek SonrakiBakimKm (en ileri bakım hedefi).
        var bakim = await db.ServiceRecords.AsNoTracking()
            .Where(r => r.SonrakiBakimKm != null)
            .GroupBy(r => r.VehicleId)
            .Select(g => new { VehicleId = g.Key, Sonraki = g.Max(r => r.SonrakiBakimKm!.Value) })
            .ToListAsync(ct);

        var arac = (await db.Vehicles.AsNoTracking().Select(v => new { v.Id, v.Plaka, v.Km }).ToListAsync(ct))
            .ToDictionary(v => v.Id, v => v);

        return bakim
            .Where(b => arac.ContainsKey(b.VehicleId))
            .Select(b =>
            {
                var v = arac[b.VehicleId];
                return new PeriyodikServisRow(b.VehicleId, v.Plaka, v.Km, b.Sonraki, b.Sonraki - v.Km);
            })
            .OrderBy(r => r.KalanKm)
            .ToList();
    }

    public async Task<IReadOnlyList<KmDetayRow>> GetKmDetayRowsAsync(
        DateTimeOffset? from, DateTimeOffset? to, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);

        var q = db.Rentals.AsNoTracking().Where(r => r.CikisKm != null && r.DonusKm != null);
        if (from is { } f) q = q.Where(r => r.BasTar >= f);
        if (to is { } t) q = q.Where(r => r.BasTar <= t);

        var rows = await q
            .Select(r => new { r.Id, r.SozlesmeNo, r.VehicleId, r.CikisKm, r.DonusKm, r.KmLimit, r.FazlaKm, r.FazlaKmBedeli })
            .ToListAsync(ct);

        var plaka = (await db.Vehicles.AsNoTracking().Select(v => new { v.Id, v.Plaka }).ToListAsync(ct))
            .ToDictionary(v => v.Id, v => v.Plaka);

        return rows
            .Select(r => new KmDetayRow(
                r.Id, r.SozlesmeNo, plaka.TryGetValue(r.VehicleId, out var p) ? p : "(bilinmeyen araç)",
                r.CikisKm!.Value, r.DonusKm!.Value, r.DonusKm!.Value - r.CikisKm!.Value,
                r.KmLimit, r.FazlaKm, r.FazlaKmBedeli))
            .ToList();
    }

    public async Task<IReadOnlyList<RezervasyonKaynakRow>> GetRezervasyonKaynakRowsAsync(
        DateTimeOffset? from, DateTimeOffset? to, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);

        var q = db.Reservations.AsNoTracking();
        if (from is { } f) q = q.Where(r => r.BasTar >= f);
        if (to is { } t) q = q.Where(r => r.BasTar <= t);

        var rows = await q.Select(r => new { r.Kaynak, r.Gun, r.Tutar }).ToListAsync(ct);

        return rows
            .GroupBy(r => string.IsNullOrWhiteSpace(r.Kaynak) ? "(belirtilmemiş)" : r.Kaynak!)
            .Select(g => new RezervasyonKaynakRow(g.Key, g.Count(), g.Sum(r => r.Gun), g.Sum(r => r.Tutar)))
            .OrderByDescending(r => r.ToplamCiro)
            .ToList();
    }

    public async Task<IReadOnlyList<FaturaDonemRow>> GetFaturaDonemRowsAsync(
        DateTimeOffset? from, DateTimeOffset? to, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);

        var q = db.Invoices.AsNoTracking();
        if (from is { } f) q = q.Where(i => i.Tarih >= f);
        if (to is { } t) q = q.Where(i => i.Tarih <= t);

        var rows = await q
            .Select(i => new { i.Id, i.No, i.Tarih, i.VadeTarihi, i.CariId, i.GenelToplam, i.Durum, i.IadeMi })
            .ToListAsync(ct);

        var cust = (await db.Customers.AsNoTracking().ToListAsync(ct)).ToDictionary(c => c.Id, c => c.DisplayName);

        return rows
            .OrderByDescending(i => i.Tarih)
            .Select(i => new FaturaDonemRow(
                i.Id, i.No, i.Tarih, i.VadeTarihi,
                cust.TryGetValue(i.CariId, out var n) ? n : "(bilinmeyen cari)",
                i.GenelToplam, i.Durum.ToString(), i.IadeMi))
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
    }

    public async Task<IReadOnlyList<EkHizmetSalesRowDto>> GetEkHizmetSalesRowsAsync(
        DateTimeOffset? from, DateTimeOffset? to, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);

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

    public async Task<IReadOnlyList<KarlilikSatirDto>> GetKarlilikRowsAsync(
        DateTimeOffset? from, DateTimeOffset? to, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);

        // Gider (Debit) — AccountRef = araç (null = araca bağlanmamış genel gider). Base = Amount×Rate.
        var gq = db.AccountLedgerEntries.AsNoTracking()
            .Where(e => e.AccountType == LedgerAccountType.Gider && e.Direction == LedgerDirection.Debit);
        if (from is { } gf) gq = gq.Where(e => e.EntryDateUtc >= gf);
        if (to is { } gt) gq = gq.Where(e => e.EntryDateUtc <= gt);
        var giderRaw = await gq.Select(e => new { e.AccountRef, A = e.Amount.Amount, R = e.Amount.Rate }).ToListAsync(ct);
        var giderByVeh = giderRaw.GroupBy(x => x.AccountRef ?? Guid.Empty)
            .ToDictionary(g => g.Key, g => g.Sum(x => x.A * x.R));

        // Gelir (Credit) — SourceId(Fatura) → Kira → Araç ile atfedilir; atfedilemeyen → Guid.Empty.
        var lq = db.AccountLedgerEntries.AsNoTracking()
            .Where(e => e.AccountType == LedgerAccountType.Gelir && e.Direction == LedgerDirection.Credit);
        if (from is { } ef) lq = lq.Where(e => e.EntryDateUtc >= ef);
        if (to is { } et) lq = lq.Where(e => e.EntryDateUtc <= et);
        var gelirRaw = await lq.Select(e => new { e.SourceType, e.SourceId, A = e.Amount.Amount, R = e.Amount.Rate }).ToListAsync(ct);

        // Atfetme haritaları: kaynak türüne göre araç çözümü.
        var invMap = (await db.Invoices.AsNoTracking().Where(i => i.RentalId != null)
            .Select(i => new { i.Id, RentalId = i.RentalId!.Value }).ToListAsync(ct))
            .ToDictionary(x => x.Id, x => x.RentalId);
        var rentalToVeh = (await db.Rentals.AsNoTracking().Select(r => new { r.Id, r.VehicleId }).ToListAsync(ct))
            .ToDictionary(x => x.Id, x => x.VehicleId);
        var saleToVeh = (await db.VehicleSales.AsNoTracking().Select(s => new { s.Id, s.VehicleId }).ToListAsync(ct))
            .ToDictionary(x => x.Id, x => x.VehicleId);
        var cezaToVeh = (await db.Penalties.AsNoTracking().Where(p => p.VehicleId != null)
            .Select(p => new { p.Id, VehicleId = p.VehicleId!.Value }).ToListAsync(ct))
            .ToDictionary(x => x.Id, x => x.VehicleId);

        var gelirByVeh = new Dictionary<Guid, decimal>();
        foreach (var e in gelirRaw)
        {
            // Fatura→kira→araç, AracSatis→satış→araç, Ceza→ceza→araç. HGS (plaka-bazlı, araç-Id yok) ve
            // manuel/kaynaksız gelir → (Atanmamış). (roadmap B2 adversarial: satış/ceza geliri artık atfedilir.)
            var veh = Guid.Empty;
            switch (e.SourceType)
            {
                case "Fatura" when invMap.TryGetValue(e.SourceId, out var rid) && rentalToVeh.TryGetValue(rid, out var vid):
                    veh = vid; break;
                case "AracSatis" when saleToVeh.TryGetValue(e.SourceId, out var sv):
                    veh = sv; break;
                case "Ceza" when cezaToVeh.TryGetValue(e.SourceId, out var cv):
                    veh = cv; break;
            }
            gelirByVeh[veh] = gelirByVeh.GetValueOrDefault(veh) + e.A * e.R;
        }

        var vehIds = giderByVeh.Keys.Concat(gelirByVeh.Keys).Where(k => k != Guid.Empty).Distinct().ToList();
        var dims = (await db.Vehicles.AsNoTracking().Where(v => vehIds.Contains(v.Id))
                .Select(v => new { v.Id, v.Plaka, v.Sube, v.Grup }).ToListAsync(ct))
            .ToDictionary(v => v.Id, v => (v.Plaka, v.Sube, v.Grup));

        var rows = new List<KarlilikSatirDto>();
        foreach (var key in giderByVeh.Keys.Concat(gelirByVeh.Keys).Distinct())
        {
            var gelir = gelirByVeh.GetValueOrDefault(key);
            var gider = giderByVeh.GetValueOrDefault(key);
            if (key == Guid.Empty)
                rows.Add(new KarlilikSatirDto(null, "(Atanmamış)", null, null, gelir, gider, gelir - gider));
            else
            {
                var d = dims.TryGetValue(key, out var x) ? x : ("(bilinmeyen araç)", (string?)null, (string?)null);
                rows.Add(new KarlilikSatirDto(key, d.Item1, d.Item2, d.Item3, gelir, gider, gelir - gider));
            }
        }
        return rows;
    }
}
