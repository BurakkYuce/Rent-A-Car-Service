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
        // Tek doğruluk kaynağı (denetim O12b): WhatsApp operasyon özeti de AYNI kaynağı kullanır.
        return await OrtakSorgular.VehicleDurumlariAsync(db, ct);
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
        var faturalar = await fq.Select(i => new { i.GenelToplam, i.Kur, i.IadeMi }).ToListAsync(ct);
        int faturaAdet = faturalar.Count;
        // İade faturası net toplamı DÜŞÜRÜR (mutabakat: fatura vs tahsilat doğru netleşsin).
        decimal faturaToplam = faturalar.Sum(f => f.GenelToplam * f.Kur * (f.IadeMi ? -1m : 1m));

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

        // Kaynak 1: servis kaydındaki elle hedef — her araç için en yüksek SonrakiBakimKm.
        var bakim = (await db.ServiceRecords.AsNoTracking()
            .Where(r => r.SonrakiBakimKm != null)
            .GroupBy(r => r.VehicleId)
            .Select(g => new { VehicleId = g.Key, Sonraki = g.Max(r => r.SonrakiBakimKm!.Value) })
            .ToListAsync(ct)).ToDictionary(b => b.VehicleId, b => b.Sonraki);

        // Kaynak 2 (otomatik): Vehicle.SonBakimKm + ServisTanim.BakimKm (AracTipi ↔ Vehicle.Tip,
        // case-insensitive; birden çok tanım eşleşirse EN KÜÇÜK aralık = en erken uyarı).
        var tanimlar = (await db.ServisTanimlari.AsNoTracking()
                .Where(t => t.Aktif && t.BakimKm > 0).Select(t => new { t.AracTipi, t.BakimKm }).ToListAsync(ct))
            .GroupBy(t => t.AracTipi.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Min(t => t.BakimKm), StringComparer.OrdinalIgnoreCase);

        var araclar = await db.Vehicles.AsNoTracking()
            .Select(v => new { v.Id, v.Plaka, v.Km, v.Tip, v.SonBakimKm }).ToListAsync(ct);

        // Araç bazında birleştir: iki kaynaktan MIN(KalanKm) (çift satır YOK — adversarial inceleme 4);
        // hiçbir kaynağı olmayan araç "tanım yok" satırı (SonrakiBakimKm=null) — sessiz gizleme yok.
        return araclar.Select(v =>
            {
                int? servisHedef = bakim.TryGetValue(v.Id, out var s) ? s : null;
                int? otoHedef = v.SonBakimKm is int son && v.Tip is { } tip
                    && tanimlar.TryGetValue(tip.Trim(), out var aralik) ? son + aralik : null;

                var (hedef, kaynak) = (servisHedef, otoHedef) switch
                {
                    (int sv, int ot) => sv - v.Km <= ot - v.Km ? (sv, "Servis") : (ot, "Tanım"),
                    (int sv, null) => (sv, "Servis"),
                    (null, int ot) => (ot, "Tanım"),
                    _ => ((int?)null, (string?)null)
                };
                return new PeriyodikServisRow(v.Id, v.Plaka, v.Km, hedef, hedef - v.Km, kaynak);
            })
            .OrderBy(r => r.KalanKm ?? int.MaxValue)
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
            .Select(i => new { i.Id, i.No, i.Tarih, i.VadeTarihi, i.CariId, i.GenelToplam, i.Currency, i.Kur, i.Durum, i.IadeMi })
            .ToListAsync(ct);

        var cust = (await db.Customers.AsNoTracking().ToListAsync(ct)).ToDictionary(c => c.Id, c => c.DisplayName);

        return rows
            .OrderByDescending(i => i.Tarih)
            .Select(i => new FaturaDonemRow(
                i.Id, i.No, i.Tarih, i.VadeTarihi,
                cust.TryGetValue(i.CariId, out var n) ? n : "(bilinmeyen cari)",
                i.GenelToplam, i.Currency, i.Kur, i.Durum.ToString(), i.IadeMi))
            .ToList();
    }

    public async Task<IReadOnlyList<AracDurumTakipRow>> GetAracDurumTakipRowsAsync(
        DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);

        var toplam = await db.Vehicles.AsNoTracking().CountAsync(ct);

        var kiralar = await db.Rentals.AsNoTracking()
            .Where(r => r.Durum != RentalStatus.Iptal)
            .Select(r => new { r.BasTar, Bit = r.GercekDonusTar ?? r.BitTar })
            .ToListAsync(ct);

        var servisler = await db.ServiceRecords.AsNoTracking()
            .Select(s => new { s.GirisTarihi, Cikis = s.CikisTarihi })
            .ToListAsync(ct);

        var sonuc = new List<AracDurumTakipRow>();
        for (var d = from.Date; d <= to.Date; d = d.AddDays(1))
        {
            var dolu = kiralar.Count(k => k.BasTar.Date <= d && k.Bit.Date >= d);
            var bakim = servisler.Count(s => s.GirisTarihi.Date <= d && (s.Cikis ?? to).Date >= d);
            var bos = Math.Max(0, toplam - dolu - bakim);
            sonuc.Add(new AracDurumTakipRow(new DateTimeOffset(d, TimeSpan.Zero), toplam, dolu, bakim, bos));
        }
        return sonuc;
    }

    public async Task<IReadOnlyList<MusteriSegmentRow>> GetMusteriSegmentRowsAsync(CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);

        var grup = await db.Rentals.AsNoTracking()
            .Where(r => r.Durum != RentalStatus.Iptal)
            .GroupBy(r => r.MusteriId)
            .Select(g => new { MusteriId = g.Key, KiraSayisi = g.Count(), ToplamCiro = g.Sum(r => r.GenelToplam * r.KurSnapshot), SonIslem = g.Max(r => r.BasTar) }) // TL-baz (O5); VIP eşiği artık anlamlı
            .ToListAsync(ct);

        var cust = (await db.Customers.AsNoTracking().ToListAsync(ct)).ToDictionary(c => c.Id, c => c.DisplayName);

        return grup
            .Select(g => new MusteriSegmentRow(
                g.MusteriId, cust.TryGetValue(g.MusteriId, out var n) ? n : "(bilinmeyen cari)",
                g.KiraSayisi, g.ToplamCiro, g.SonIslem,
                g.ToplamCiro >= 10000m ? "VIP" : g.ToplamCiro > 0m ? "Standart" : "Pasif"))
            .OrderByDescending(r => r.ToplamCiro)
            .ToList();
    }

    public async Task<IReadOnlyList<PersonelCalismaRow>> GetPersonelCalismaRowsAsync(CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);

        var grup = await db.Baflar.AsNoTracking()
            .GroupBy(b => b.PersonelId)
            .Select(g => new { PersonelId = g.Key, TahsisSayisi = g.Count() })
            .ToListAsync(ct);

        var pers = (await db.Personeller.AsNoTracking().ToListAsync(ct))
            .ToDictionary(p => p.Id, p => $"{p.Ad} {p.Soyad}".Trim());

        return grup
            .Select(g => new PersonelCalismaRow(
                g.PersonelId, pers.TryGetValue(g.PersonelId, out var n) ? n : "(bilinmeyen personel)", g.TahsisSayisi))
            .OrderByDescending(r => r.TahsisSayisi)
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

        // Tahsilat: TEK doğruluk kaynağı (denetim O12b — WhatsApp özeti aynı tanımı kullanır; TL-baz Σ Amount×Rate,
        // ters kayıt hariç). Pencere [from, to] kapalı → helper'a to+1tick (davranış birebir korunur).
        var (tahsilatAdet, tahsilatTutar) = await OrtakSorgular.TahsilatTlAsync(db, from, to.AddTicks(1), ct);

        // Fatura: İptal hariç; GenelToplam base zaten (Currency/Kur ayrı tutulur ama GenelToplam fatura
        // para birimindedir → günlük faaliyet sayacında brüt toplam olarak gösterilir).
        var faturalar = await db.Invoices.AsNoTracking()
            .Where(i => i.Durum != InvoiceStatus.Iptal && i.Tarih >= from && i.Tarih <= to)
            .Select(i => new { i.GenelToplam, i.Kur, i.IadeMi })
            .ToListAsync(ct);
        var faturaTutar = faturalar.Sum(f => f.GenelToplam * f.Kur * (f.IadeMi ? -1m : 1m)); // iade net'i düşürür

        return new GunlukFaaliyetDto(
            yeniRez, yeniKira, cikis, donus,
            tahsilatAdet, tahsilatTutar, faturalar.Count, faturaTutar);
    }

    public async Task<IReadOnlyList<KdvLineRowDto>> GetKdvLineRowsAsync(
        DateTimeOffset? from, DateTimeOffset? to, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);

        var inv = db.Invoices.AsNoTracking().Where(i => i.Durum != InvoiceStatus.Iptal);
        if (from is { } f) inv = inv.Where(i => i.Tarih >= f);
        if (to is { } t) inv = inv.Where(i => i.Tarih <= t);

        // Satır tutarları fatura para birimindedir → base para için Kur ile çarp (bellek-içi).
        // İade faturası satırları NEGATİF sayılır (IadeMi) → KDV oran-bazında netleşir.
        var raw = await (from l in db.InvoiceLines.AsNoTracking()
                         join i in inv on l.InvoiceId equals i.Id
                         select new { l.KdvOrani, l.SatirNet, l.SatirKdv, l.SatirToplam, i.Kur, i.IadeMi, InvoiceId = i.Id })
            .ToListAsync(ct);

        return raw
            .Select(r =>
            {
                var s = (r.IadeMi ? -1m : 1m) * r.Kur;
                return new KdvLineRowDto(r.KdvOrani, r.SatirNet * s, r.SatirKdv * s, r.SatirToplam * s, r.InvoiceId);
            })
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

        // Gelir — HER İKİ yön (iade faturası Borç Gelir yazar → SignedBase ile netleşir).
        // SourceId(Fatura/FaturaIade) → Kira → Araç ile atfedilir; atfedilemeyen → Guid.Empty.
        var lq = db.AccountLedgerEntries.AsNoTracking()
            .Where(e => e.AccountType == LedgerAccountType.Gelir);
        if (from is { } ef) lq = lq.Where(e => e.EntryDateUtc >= ef);
        if (to is { } et) lq = lq.Where(e => e.EntryDateUtc <= et);
        var gelirRaw = await lq.Select(e => new { e.SourceType, e.SourceId, e.Direction, A = e.Amount.Amount, R = e.Amount.Rate }).ToListAsync(ct);

        // Atfetme haritaları: kaynak türüne göre araç çözümü. İade faturası RentalId=null taşır →
        // kira bağı KaynakFaturaId üzerinden (iki-hop): iade → kaynak fatura → RentalId ?? KaynakKiraId.
        // FARK faturası da RentalId=null taşır (kira-fatura unique index'ine çarpmasın) → kira bağı
        // KaynakKiraId'dedir (atıf düzeltmesi: fark + iade-of-fark geliri önceden "(Atanmamış)"a düşüyordu).
        var invAll = await db.Invoices.AsNoTracking()
            .Select(i => new { i.Id, i.RentalId, i.KaynakFaturaId, i.KaynakKiraId }).ToListAsync(ct);
        var invById = invAll.ToDictionary(x => x.Id);
        Guid? RentalOf(Guid invId)
        {
            if (!invById.TryGetValue(invId, out var i)) return null;
            if (i.RentalId is Guid r) return r;
            if (i.KaynakKiraId is Guid kk) return kk; // fark faturası
            if (i.KaynakFaturaId is Guid k && invById.TryGetValue(k, out var s)) return s.RentalId ?? s.KaynakKiraId;
            return null;
        }
        var rentalToVeh = (await db.Rentals.AsNoTracking().Select(r => new { r.Id, r.VehicleId }).ToListAsync(ct))
            .ToDictionary(x => x.Id, x => x.VehicleId);
        var saleToVeh = (await db.VehicleSales.AsNoTracking().Select(s => new { s.Id, s.VehicleId }).ToListAsync(ct))
            .ToDictionary(x => x.Id, x => x.VehicleId);
        // Ceza: VehicleId doğrudan; yoksa RentalId → kira → araç fallback (araçsız+kirasız ceza atfedilemez).
        var cezaToVeh = (await db.Penalties.AsNoTracking().Where(p => p.VehicleId != null || p.RentalId != null)
                .Select(p => new { p.Id, p.VehicleId, p.RentalId }).ToListAsync(ct))
            .Select(p => new
            {
                p.Id,
                VehicleId = p.VehicleId
                    ?? (p.RentalId is Guid prid && rentalToVeh.TryGetValue(prid, out var prv) ? prv : (Guid?)null)
            })
            .Where(x => x.VehicleId != null)
            .ToDictionary(x => x.Id, x => x.VehicleId!.Value);
        var servisToVeh = (await db.ServiceRecords.AsNoTracking()
                .Select(s => new { s.Id, s.VehicleId }).ToListAsync(ct))
            .ToDictionary(x => x.Id, x => x.VehicleId);

        var gelirByVeh = new Dictionary<Guid, decimal>();
        foreach (var e in gelirRaw)
        {
            // Fatura/FaturaIade→kira→araç (fark faturası dahil), AracSatis→satış→araç, Ceza→ceza→araç
            // (RentalId fallback'li), ServisYansitma→servis→araç. HGS (plaka-bazlı, kalıcı VehicleId yok) ve
            // manuel/kaynaksız gelir → (Atanmamış). (roadmap B2 adversarial + araç-karne atıf düzeltmesi.)
            var veh = Guid.Empty;
            switch (e.SourceType)
            {
                case "Fatura" or "FaturaIade" when RentalOf(e.SourceId) is Guid rid && rentalToVeh.TryGetValue(rid, out var vid):
                    veh = vid; break;
                case "AracSatis" when saleToVeh.TryGetValue(e.SourceId, out var sv):
                    veh = sv; break;
                case "Ceza" when cezaToVeh.TryGetValue(e.SourceId, out var cv):
                    veh = cv; break;
                case "ServisYansitma" when servisToVeh.TryGetValue(e.SourceId, out var srv):
                    veh = srv; break;
            }
            // İade Borç Gelir → negatif (kârı azaltır); normal Alacak Gelir → pozitif.
            var signed = (e.Direction == LedgerDirection.Credit ? 1m : -1m) * e.A * e.R;
            gelirByVeh[veh] = gelirByVeh.GetValueOrDefault(veh) + signed;
        }

        var vehIds = giderByVeh.Keys.Concat(gelirByVeh.Keys).Where(k => k != Guid.Empty).Distinct().ToList();
        var dims = (await db.Vehicles.AsNoTracking().Where(v => vehIds.Contains(v.Id))
                .Select(v => new { v.Id, v.Plaka, v.Sube, v.Grup, v.Segment }).ToListAsync(ct))
            .ToDictionary(v => v.Id, v => (v.Plaka, v.Sube, v.Grup, v.Segment));

        var rows = new List<KarlilikSatirDto>();
        foreach (var key in giderByVeh.Keys.Concat(gelirByVeh.Keys).Distinct())
        {
            var gelir = gelirByVeh.GetValueOrDefault(key);
            var gider = giderByVeh.GetValueOrDefault(key);
            if (key == Guid.Empty)
                rows.Add(new KarlilikSatirDto(null, "(Atanmamış)", null, null, null, gelir, gider, gelir - gider));
            else
            {
                var d = dims.TryGetValue(key, out var x) ? x : ("(bilinmeyen araç)", (string?)null, (string?)null, (string?)null);
                rows.Add(new KarlilikSatirDto(key, d.Item1, d.Item2, d.Item3, d.Item4, gelir, gider, gelir - gider));
            }
        }
        return rows;
    }

    public async Task<AracKarneRawDto> GetAracKarneRawAsync(
        Guid vehicleId, DateTimeOffset? from, DateTimeOffset? to, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);

        // Araç (query filter + RLS tenant-scope'lu) — yoksa/başka tenant'sa boş paket → servis null → 404.
        var vehicle = await db.Vehicles.AsNoTracking().FirstOrDefaultAsync(v => v.Id == vehicleId, ct);
        if (vehicle is null)
            return new AracKarneRawDto(null, [], [], [], [], [], 0, 0, null, 0m, 0m);

        // ---- GİDER (defterden): AccountRef = araç. Kategori etiketi kaynak varlıktan zenginleşir
        // (SigortaOdeme→poliçe Tip; Gider→Expense.Tip). Base = Amount×Rate (bellekte).
        // Tüm geçmiş çekilir; dönem penceresi BELLEKTE uygulanır — KPI için ömür-boyu toplamlar
        // aynı sorgudan türetilir (adversarial F2: KPI parası dönem filtresinden sızmasın).
        var giderRawTum = await db.AccountLedgerEntries.AsNoTracking()
            .Where(e => e.AccountType == LedgerAccountType.Gider
                        && e.Direction == LedgerDirection.Debit && e.AccountRef == vehicleId)
            .Select(e => new { e.EntryDateUtc, e.SourceType, e.SourceId, A = e.Amount.Amount, R = e.Amount.Rate })
            .ToListAsync(ct);
        var giderRaw = giderRawTum
            .Where(e => (from is not { } gf || e.EntryDateUtc >= gf) && (to is not { } gt || e.EntryDateUtc <= gt))
            .ToList();

        var policeler = await db.InsurancePolicies.AsNoTracking()
            .Where(p => p.VehicleId == vehicleId).ToListAsync(ct);
        var sigortaTip = policeler.ToDictionary(x => x.Id, x => x.Tip);
        var giderKayitlari = await db.Expenses.AsNoTracking()
            .Where(x => x.VehicleId == vehicleId).ToListAsync(ct);
        var giderTip = giderKayitlari.ToDictionary(x => x.Id, x => x.Tip);

        string GiderKategori(string sourceType, Guid sourceId) => sourceType switch
        {
            "MtvOdeme" => "MTV",
            "MuayeneOdeme" => "Muayene",
            "SigortaOdeme" => sigortaTip.TryGetValue(sourceId, out var t) && t == InsuranceType.Kasko
                ? "Kasko" : "Sigorta (Trafik)",
            "Gider" => giderTip.TryGetValue(sourceId, out var g) ? g switch
            {
                ExpenseType.Genel => "Genel Gider",
                ExpenseType.Arac => "Araç Gideri",
                ExpenseType.Personel => "Personel",
                ExpenseType.Sigorta => "Sigorta",
                ExpenseType.Mtv => "MTV",
                ExpenseType.Muayene => "Muayene",
                _ => "Diğer"
            } : "Diğer",
            _ => sourceType
        };
        var giderler = giderRaw
            .Select(e => new AracLedgerGiderRow(
                e.EntryDateUtc.UtcDateTime.Year, GiderKategori(e.SourceType, e.SourceId), e.A * e.R))
            .ToList();

        // ---- GELİR (defterden): GetKarlilikRowsAsync atıf kurallarının araç-scope'lu birebir kopyası
        // (parite testi kilitler). Kaynak id-kümeleri bu araca göre kurulur; atanamayan gelir karnede YOK.
        var kiralar = await db.Rentals.AsNoTracking().Where(r => r.VehicleId == vehicleId)
            .Select(r => new { r.Id, r.SozlesmeNo, r.BasTar, r.BitTar, r.GercekDonusTar, r.Durum, r.GenelToplam, r.CikisKm, r.DonusKm })
            .ToListAsync(ct);
        var rentalIds = kiralar.Select(r => r.Id).ToList();

        // Fatura bağı: RentalId (base) veya KaynakKiraId (fark) bu aracın kirasına işaret eder;
        // iade faturaları KaynakFaturaId ile bu kümeye iki-hop bağlanır.
        var dogrudanInvRows = await db.Invoices.AsNoTracking()
            .Where(i => (i.RentalId != null && rentalIds.Contains(i.RentalId.Value))
                     || (i.KaynakKiraId != null && rentalIds.Contains(i.KaynakKiraId.Value)))
            .Select(i => new { i.Id, i.RentalId, i.KaynakKiraId }).ToListAsync(ct);
        var dogrudanInv = dogrudanInvRows.Select(i => i.Id).ToList();
        var iadeInv = await db.Invoices.AsNoTracking()
            .Where(i => i.KaynakFaturaId != null && dogrudanInv.Contains(i.KaynakFaturaId.Value))
            .Select(i => i.Id).ToListAsync(ct);
        var invIds = dogrudanInv.Concat(iadeInv).ToHashSet();
        // Kira olayı DeftereYansir sinyali: kiranın parası deftere FATURA ile girer (iptal edilse bile
        // kesilmiş fatura defterde kalır — immutable). "İptal değil" durumuna değil buna bakılır (adversarial F3).
        var faturaliKiralar = dogrudanInvRows
            .Select(i => i.RentalId ?? i.KaynakKiraId!.Value).ToHashSet();

        var satislar = await db.VehicleSales.AsNoTracking().Where(s => s.VehicleId == vehicleId)
            .Select(s => new { s.Id, s.No, s.Tarih, s.GenelToplam, s.Durum }).ToListAsync(ct);
        var saleIds = satislar.Select(s => s.Id).ToHashSet();

        // Ceza: VehicleId öncelikli (Karlilik ile aynı) — VehicleId BAŞKA araca işaret ediyorsa buraya sayılmaz;
        // VehicleId=null + RentalId bu aracın kirası → fallback.
        var cezaIds = (await db.Penalties.AsNoTracking()
                .Where(p => p.VehicleId == vehicleId
                         || (p.VehicleId == null && p.RentalId != null && rentalIds.Contains(p.RentalId.Value)))
                .Select(p => p.Id).ToListAsync(ct)).ToHashSet();

        var servisKayitlari = await db.ServiceRecords.AsNoTracking()
            .Where(s => s.VehicleId == vehicleId).ToListAsync(ct);
        var servisIds = servisKayitlari.Select(s => s.Id).ToHashSet();

        var gelirRawTum = await db.AccountLedgerEntries.AsNoTracking()
            .Where(e => e.AccountType == LedgerAccountType.Gelir)
            .Select(e => new { e.EntryDateUtc, e.SourceType, e.SourceId, e.Direction, A = e.Amount.Amount, R = e.Amount.Rate })
            .ToListAsync(ct);
        var gelirRaw = gelirRawTum
            .Where(e => (from is not { } ef || e.EntryDateUtc >= ef) && (to is not { } et || e.EntryDateUtc <= et))
            .ToList();

        string? GelirKaynak(string st, Guid sid) => st switch
        {
            "Fatura" or "FaturaIade" when invIds.Contains(sid) => "Kira/Fatura",
            "AracSatis" when saleIds.Contains(sid) => "Araç Satışı",
            "Ceza" when cezaIds.Contains(sid) => "Ceza Yansıtma",
            "ServisYansitma" when servisIds.Contains(sid) => "Servis Yansıtma",
            _ => null // başka araca/kaynağa ait ya da atanamayan → karnede yok
        };
        var gelirler = gelirRaw
            .Select(e => new { e, K = GelirKaynak(e.SourceType, e.SourceId) })
            .Where(x => x.K != null)
            .Select(x => new AracLedgerGelirRow(
                x.e.EntryDateUtc.UtcDateTime.Year, x.K!,
                (x.e.Direction == LedgerDirection.Credit ? 1m : -1m) * x.e.A * x.e.R))
            .ToList();
        // Ömür-boyu (pencereden bağımsız) toplamlar — KPI/amortisman paydaları bunlardan.
        var omurGider = giderRawTum.Sum(e => e.A * e.R);
        var omurGelir = gelirRawTum
            .Where(e => GelirKaynak(e.SourceType, e.SourceId) != null)
            .Sum(e => (e.Direction == LedgerDirection.Credit ? 1m : -1m) * e.A * e.R);

        // ---- OLAYLAR ("neyi ne zaman") — kaynak varlıktan, BİLGİ amaçlı (tutar brüt/native; P&L'e toplanmaz).
        var mtvler = await db.MtvRecords.AsNoTracking().Where(m => m.VehicleId == vehicleId).ToListAsync(ct);
        var muayeneler = await db.InspectionRecords.AsNoTracking().Where(i => i.VehicleId == vehicleId).ToListAsync(ct);

        var olaylar = new List<AracOlayRow>();
        foreach (var p in policeler)
            olaylar.Add(new AracOlayRow(p.Baslangic,
                p.Tip == InsuranceType.Kasko ? "Kasko" : "Trafik Sigortası",
                $"Poliçe {p.PoliceNo ?? "-"} ({p.Firma ?? "-"}) — bitiş {p.Bitis:dd.MM.yyyy}" + (p.Odendi ? "" : " — ÖDENMEDİ"),
                p.Prim + p.ZeyilPrim, p.Odendi));
        foreach (var m in mtvler)
            olaylar.Add(new AracOlayRow(m.Vade, "MTV",
                $"Dönem {m.Donem}" + (m.Odendi ? "" : " — ÖDENMEDİ"), m.Tutar, m.Odendi));
        foreach (var i in muayeneler)
            olaylar.Add(new AracOlayRow(i.MuayeneTarihi, "Muayene",
                $"Geçerlilik {i.Bitis:dd.MM.yyyy}" + (i.Odendi ? "" : " — ÖDENMEDİ"), i.Ucret + i.Ceza, i.Odendi));
        foreach (var s in servisKayitlari)
            olaylar.Add(new AracOlayRow(s.GirisTarihi, $"Servis ({s.Tip})",
                $"{s.No} — km {s.GirisKm}→{(s.CikisKm?.ToString() ?? "-")} ({s.Durum})"
                + (s.Yansitildi ? $" — rücu {s.YansitilanTutar:N2}" : ""),
                s.ToplamIscilik, false)); // servis maliyeti deftere yazılmaz (mali belge değil)
        foreach (var x in giderKayitlari)
            olaylar.Add(new AracOlayRow(x.Tarih, $"Gider ({x.Tip})",
                $"{x.No}{(string.IsNullOrWhiteSpace(x.Aciklama) ? "" : " — " + x.Aciklama)}", x.GenelToplam, true));
        foreach (var s in satislar)
            olaylar.Add(new AracOlayRow(s.Tarih, "Satış",
                s.No + (s.Durum == SatisDurum.Iptal ? " — İPTAL" : ""), s.GenelToplam,
                s.Durum == SatisDurum.Tamamlandi));
        foreach (var r in kiralar)
            olaylar.Add(new AracOlayRow(r.BasTar, "Kira",
                $"{r.SozlesmeNo} — {r.BasTar:dd.MM.yyyy} → {(r.GercekDonusTar ?? r.BitTar):dd.MM.yyyy} ({r.Durum})"
                + (faturaliKiralar.Contains(r.Id) ? "" : " — faturalanmamış"),
                r.GenelToplam, faturaliKiralar.Contains(r.Id)));
        if (from is { } of) olaylar.RemoveAll(o => o.Tarih < of);
        if (to is { } ot) olaylar.RemoveAll(o => o.Tarih > ot);
        olaylar = olaylar.OrderByDescending(o => o.Tarih).ToList();

        // ---- KPI hamı: kira aralıkları (İptal hariç; efektif bitiş = GercekDonusTar ?? BitTar),
        // servis aralıkları (İptal hariç), katedilen km (çıkış+dönüş dolu kiralar).
        var aktifKiralar = kiralar.Where(r => r.Durum != RentalStatus.Iptal).ToList();
        var kiraAraliklari = aktifKiralar
            .Select(r => new DolulukKiraRowDto(r.BasTar, r.GercekDonusTar ?? r.BitTar)).ToList();
        var servisAraliklari = servisKayitlari.Where(s => s.Durum != ServisDurum.Iptal)
            .Select(s => new AracServisGunRow(s.GirisTarihi, s.CikisTarihi)).ToList();
        var katedilenKm = aktifKiralar.Where(r => r.CikisKm != null && r.DonusKm != null)
            .Sum(r => r.DonusKm!.Value - r.CikisKm!.Value);
        var sonSatis = satislar.Where(s => s.Durum == SatisDurum.Tamamlandi)
            .Select(s => (DateTimeOffset?)s.Tarih).DefaultIfEmpty(null).Max();

        return new AracKarneRawDto(vehicle, gelirler, giderler, olaylar,
            kiraAraliklari, servisAraliklari, aktifKiralar.Count, katedilenKm, sonSatis,
            omurGelir, omurGider);
    }
}
