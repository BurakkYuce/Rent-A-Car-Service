using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using RentACar.Application.Common;
using RentACar.Application.Finance;
using RentACar.Domain.Entities;
using RentACar.Domain.Enums;

namespace RentACar.Infrastructure.Persistence.Repositories;

/// <summary>
/// Fatura kalıcılığı. PostAsync: No tahsisi + fatura + satırlar + DENGELİ defter kümesi
/// → TEK transaction. Fatura/satır/defter immutable (DB trigger).
/// </summary>
public sealed class InvoiceRepository(IDbContextFactory<AppDbContext> factory) : IInvoiceRepository
{
    private readonly IDbContextFactory<AppDbContext> _factory = factory;

    public async Task<IReadOnlyList<Invoice>> ListAsync(CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.Invoices.AsNoTracking().OrderByDescending(i => i.Tarih).ToListAsync(ct);
    }

    public async Task<Invoice?> FindAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.Invoices.AsNoTracking().Include(i => i.Lines).FirstOrDefaultAsync(i => i.Id == id, ct);
    }

    /// <summary>
    /// FAZ-52 — fatura satırı × fatura × cari × kira × araç × rezervasyon birleşimi.
    ///
    /// <para>Kira/araç/rezervasyon bağı OPSİYONELDİR: serbest (manuel) faturanın kirası yoktur →
    /// LEFT JOIN. Bunları INNER JOIN yapmak manuel faturaları listeden sessizce düşürürdü.</para>
    ///
    /// <para>Para alanları satırdan OLDUĞU GİBİ okunur (yeniden hesap yok).</para>
    /// </summary>
    public async Task<IReadOnlyList<FaturaSatirDto>> ListLinesAsync(
        FaturaSatirFilter? filter = null, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);

        var q =
            from l in db.InvoiceLines.AsNoTracking()
            join i in db.Invoices.AsNoTracking() on l.InvoiceId equals i.Id
            join c in db.Customers.AsNoTracking() on i.CariId equals c.Id into cg
            from c in cg.DefaultIfEmpty()
            join r in db.Rentals.AsNoTracking() on i.RentalId equals (Guid?)r.Id into rg
            from r in rg.DefaultIfEmpty()
            join v in db.Vehicles.AsNoTracking() on (Guid?)r.VehicleId equals (Guid?)v.Id into vg
            from v in vg.DefaultIfEmpty()
            join rez in db.Reservations.AsNoTracking() on r.ReservationId equals (Guid?)rez.Id into rezg
            from rez in rezg.DefaultIfEmpty()
            select new { l, i, c, r, v, rez };

        if (filter is not null)
        {
            if (filter.CariId is { } cid) q = q.Where(x => x.i.CariId == cid);
            if (filter.Bas is { } b) q = q.Where(x => x.i.Tarih >= b);
            if (filter.Bit is { } t) q = q.Where(x => x.i.Tarih <= t);
            if (filter.IptalleriGizle) q = q.Where(x => x.i.Durum != InvoiceStatus.Iptal);
            if (!string.IsNullOrWhiteSpace(filter.Ofis))
            {
                var o = filter.Ofis.Trim();
                q = q.Where(x => x.r != null && x.r.CikisOfisi != null && x.r.CikisOfisi.Trim() == o);
            }
            if (!string.IsNullOrWhiteSpace(filter.Ara))
            {
                var a = filter.Ara.Trim();
                q = q.Where(x => EF.Functions.ILike(x.i.No, $"%{a}%")
                              || EF.Functions.ILike(x.l.Aciklama, $"%{a}%")
                              || (x.r != null && EF.Functions.ILike(x.r.SozlesmeNo, $"%{a}%")));
            }
            if (!string.IsNullOrWhiteSpace(filter.Plaka))
            {
                // Plakalar normalize saklanıyor ("34AA01"); arama terimi de normalize edilmeli (FAZ-63 dersi).
                var p = filter.Plaka.Trim().ToUpperInvariant().Replace(" ", string.Empty);
                q = q.Where(x => x.v != null && EF.Functions.ILike(x.v.Plaka, $"%{p}%"));
            }
        }

        var limit = Math.Clamp(filter?.EnFazla ?? 2000, 1, 20000);
        var rows = await q
            .OrderByDescending(x => x.i.Tarih).ThenBy(x => x.i.No)
            .Take(limit)
            .Select(x => new
            {
                x.i.Id, x.i.No, x.i.Tarih, x.i.VadeTarihi, x.i.Durum, x.i.IadeMi, x.i.ManuelMi,
                x.i.Currency, x.i.Kur, x.i.CariId,
                CariAd = x.c == null ? null : (x.c.Tip == CariType.Bireysel
                    ? ((x.c.Ad ?? "") + " " + (x.c.Soyad ?? "")) : x.c.Unvan),
                CariSehir = x.c == null ? null : x.c.Il,
                CariEmail = x.c == null ? null : x.c.Email,
                CariVergiNo = x.c == null ? null : x.c.VergiNo,
                x.l.Aciklama, x.l.Miktar, x.l.BirimNetFiyat, x.l.KdvOrani,
                x.l.SatirNet, x.l.SatirKdv, x.l.SatirToplam,
                RentalId = (Guid?)(x.r == null ? null : x.r.Id),
                SozlesmeNo = x.r == null ? null : x.r.SozlesmeNo,
                Plaka = x.v == null ? null : x.v.Plaka,
                CikisOfisi = x.r == null ? null : x.r.CikisOfisi,
                RezKaynak = x.rez == null ? null : x.rez.Kaynak
            })
            .ToListAsync(ct);

        return rows.Select(x => new FaturaSatirDto(
            x.Id, x.No, x.Tarih, x.VadeTarihi, x.Durum, x.IadeMi, x.ManuelMi, x.Currency, x.Kur,
            x.CariId, string.IsNullOrWhiteSpace(x.CariAd) ? "(bilinmeyen cari)" : x.CariAd!.Trim(),
            x.CariSehir, x.CariEmail, x.CariVergiNo,
            x.Aciklama, x.Miktar, x.BirimNetFiyat, x.KdvOrani, x.SatirNet, x.SatirKdv, x.SatirToplam,
            x.RentalId, x.SozlesmeNo, x.Plaka, x.CikisOfisi, x.RezKaynak)).ToList();
    }

    public async Task<IReadOnlyList<Invoice>> ListByRentalAsync(Guid rentalId, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        // base (RentalId) + fark (KaynakKiraId) — GetFarkStateAsync ile aynı kapsam; burada iptal/iade de
        // listelenir (görsel liste, filtre yok). İadeler kaynak fatura üzerinden dolaylı bağlı olduğundan
        // ikinci sorguyla eklenir.
        var kiraFaturalari = await db.Invoices.AsNoTracking()
            .Where(i => i.RentalId == rentalId || i.KaynakKiraId == rentalId)
            .ToListAsync(ct);
        var ids = kiraFaturalari.Select(x => x.Id).ToList();
        var iadeler = ids.Count == 0
            ? []
            : await db.Invoices.AsNoTracking()
                .Where(i => i.KaynakFaturaId != null && ids.Contains(i.KaynakFaturaId.Value))
                .ToListAsync(ct);
        return kiraFaturalari.Concat(iadeler.Where(i => !ids.Contains(i.Id)))
            .OrderByDescending(i => i.Tarih).ThenByDescending(i => i.No).ToList();
    }

    public async Task<bool> IadeExistsForAsync(Guid kaynakFaturaId, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.Invoices.AsNoTracking().AnyAsync(i => i.KaynakFaturaId == kaynakFaturaId, ct);
    }

    public async Task<(decimal FaturalananBrut, int FarkSayisi)> GetFarkStateAsync(Guid rentalId, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        // TOCTOU koruması (adversarial Kritik-1): faturalanan + fark-sayısı AYNI snapshot'tan okunur → eşzamanlı
        // fark isteklerinde tutarlı sıra. RepeatableRead: tek tutarlı görüntü; salt-okuma → rollback.
        await using var tx = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead, ct);
        try
        {
            return await FarkStateHesaplaAsync(db, rentalId, ct);
        }
        finally { await tx.RollbackAsync(ct); }
    }

    /// <summary>Fark-state (iade-netli faturalanan brüt + fark sayısı) — GetFarkStateAsync ile
    /// posting TX-içi yeniden doğrulaması (adversarial B2-Kritik-1) AYNI sorgudan geçer (tek kopya).</summary>
    private static Task<(decimal FaturalananBrut, int FarkSayisi)> FarkStateHesaplaAsync(
        AppDbContext db, Guid rentalId, CancellationToken ct)
        => OrtakSorgular.FarkStateAsync(db, rentalId, ct); // tek kopya (job üreticisiyle ortak)

    /// <summary>Kira-fatura advisory kilidi (adversarial B2-Kritik-1): base/fark/dönem posting'leri
    /// aynı kira üzerinde SERİLEŞİR — iki farklı unique-index'e yazan yollar (base: (TenantId,RentalId);
    /// fark/dönem: (TenantId,KaynakKiraId,Sıra)) birbirini görmeden commit edemez.</summary>
    private static async Task KiraFaturaKilidiAsync(AppDbContext db, Guid rentalId, CancellationToken ct)
    {
        var conn = db.Database.GetDbConnection();
        await using var cmd = conn.CreateCommand();
        cmd.Transaction = db.Database.CurrentTransaction?.GetDbTransaction();
        cmd.CommandText = "SELECT pg_advisory_xact_lock(hashtextextended(@k, 42))";
        var p = cmd.CreateParameter();
        p.ParameterName = "k";
        p.Value = $"fatura:{db.TenantId}:{rentalId}";
        cmd.Parameters.Add(p);
        await cmd.ExecuteScalarAsync(ct);
    }

    public async Task PostAsync(Invoice invoice, IReadOnlyList<AccountLedgerEntry> entries, CancellationToken ct = default)
    {
        var debit = entries.Where(e => e.Direction == LedgerDirection.Debit).Sum(e => e.Amount.AmountInBase);
        var credit = entries.Where(e => e.Direction == LedgerDirection.Credit).Sum(e => e.Amount.AmountInBase);
        if (debit != credit)
            throw new ValidationException($"Fatura defteri dengesiz: borç {debit} ≠ alacak {credit}.");

        await PgRetry.RunAsync(async () => // P0-5: deadlock/serialization çakışmasında baştan dene
        {
            await using var db = await _factory.CreateDbContextAsync(ct);
            await using var tx = await db.Database.BeginTransactionAsync(ct);

            // Adversarial B2-Kritik-1: kira-bağlı faturada advisory kilit + TX-İÇİ yeniden doğrulama.
            // BASE yolu yalnız HİÇ fatura yokken çalışır (IsRentalInvoiced kapısı) — kilit altında
            // yeniden bakılır; bu arada dönem/fark faturası commit ettiyse base TAM tutarı ikinci kez
            // keserdi (çift faturalama) → temiz red (operatör fark yoluna düşer).
            var kiraBagi = invoice.RentalId ?? invoice.KaynakKiraId;
            if (kiraBagi is Guid kb && !invoice.IadeMi)
            {
                await KiraFaturaKilidiAsync(db, kb, ct);
                if (invoice.RentalId is Guid rid &&
                    await db.Invoices.AsNoTracking().AnyAsync(i => i.RentalId == rid || i.KaynakKiraId == rid, ct))
                    throw new ValidationException("Kira bu sırada faturalandı (eşzamanlı istek) — kalan tutar için 'Fatura Kes' fark yolunu kullanın.");
            }

            var n = await SequenceAllocator.NextAsync(db, db.TenantId, "InvoiceNo", ct);
            invoice.No = $"FT-{n:D6}";
            // No defter açıklamasında kullanıldığından satırların ait olduğu fatura no'yu yansıt.
            // İade satırları cari ekstrede "İade" etiketiyle görünsün (adversarial Low: eskiden
            // hepsi "Fatura" yazılıyordu).
            var etiket = invoice.IadeMi ? "İade" : "Fatura";
            foreach (var entry in entries)
                entry.Description = $"{etiket} {invoice.No}";

            db.Invoices.Add(invoice);          // satırlar cascade
            db.AccountLedgerEntries.AddRange(entries);

            try
            {
                await db.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);
            }
            catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
            {
                // Kısmi unique index (eşzamanlı çift fatura/iade) → idempotent reddet. İade yarışında
                // (TenantId,KaynakFaturaId) index'i tetiklenir → doğru ifadeyle reddet.
                await tx.RollbackAsync(ct);
                // Fark faturası (KaynakKiraId): eşzamanlı/çift istek aynı hedefe çarptı → idempotent reddet.
                throw new ValidationException(
                    invoice.KaynakKiraId is not null ? "Kira farkı zaten faturalanmış (eşzamanlı istek)." :
                    invoice.IadeMi ? "Bu fatura zaten iade edilmiş." : "Kira zaten faturalanmış.");
            }
        }, ct);
    }

    public async Task PostDonemAsync(Invoice invoice, IReadOnlyList<AccountLedgerEntry> entries,
        Guid donemId, decimal kesilenTutar, decimal beklenenFaturalanan, CancellationToken ct = default)
    {
        var debit = entries.Where(e => e.Direction == LedgerDirection.Debit).Sum(e => e.Amount.AmountInBase);
        var credit = entries.Where(e => e.Direction == LedgerDirection.Credit).Sum(e => e.Amount.AmountInBase);
        if (debit != credit)
            throw new ValidationException($"Dönem faturası defteri dengesiz: borç {debit} ≠ alacak {credit}.");

        await PgRetry.RunAsync(async () => // deadlock/serialization çakışmasında baştan dene
        {
            await using var db = await _factory.CreateDbContextAsync(ct);
            await using var tx = await db.Database.BeginTransactionAsync(ct);

            // Adversarial B2-Kritik-1: advisory kilit + FATURALANAN TX-İÇİ yeniden doğrulama —
            // servis kesileceği kilit DIŞINDA hesapladı; bu arada base/fark/dönem faturası commit
            // ettiyse tutar bayattır → temiz red (çağıran güncel durumla yeniden dener).
            await KiraFaturaKilidiAsync(db, invoice.KaynakKiraId!.Value, ct);
            var (guncelFaturalanan, _) = await FarkStateHesaplaAsync(db, invoice.KaynakKiraId.Value, ct);
            if (guncelFaturalanan != beklenenFaturalanan)
                throw new ValidationException("Kira faturaları bu sırada değişti (eşzamanlı istek) — dönem kesimini yeniden deneyin.");

            // FAZ 4.2-B2: dönem satırı fatura+defterle AYNI transaction'da Kesildi'ye geçer — yarım
            // durum imkânsız (fatura var/dönem Planlandi ya da tersi olamaz). TX-içi yarış çiti:
            // Planlandi DIŞI her durum reddedilir (Kesildi/Atlandi yarışı; unique index ikinci savunma).
            var donem = await db.FaturaDonemleri.FirstOrDefaultAsync(d => d.Id == donemId, ct)
                ?? throw new ValidationException("Fatura dönemi bulunamadı.");
            if (donem.Durum != FaturaDonemDurum.Planlandi)
                throw new ValidationException("Dönem bu sırada kesilmiş/atlanmış (eşzamanlı istek).");
            donem.Durum = FaturaDonemDurum.Kesildi;
            donem.InvoiceId = invoice.Id;
            donem.KesilenTutar = kesilenTutar;
            donem.UpdatedAtUtc = DateTimeOffset.UtcNow;

            var n = await SequenceAllocator.NextAsync(db, db.TenantId, "InvoiceNo", ct);
            invoice.No = $"FT-{n:D6}";
            foreach (var entry in entries)
                entry.Description = $"Fatura {invoice.No}";

            db.Invoices.Add(invoice);
            db.AccountLedgerEntries.AddRange(entries);

            try
            {
                await db.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);
            }
            catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
            {
                await tx.RollbackAsync(ct);
                throw new ValidationException("Dönem faturası zaten kesilmiş (eşzamanlı istek).");
            }
        }, ct);
    }
}
