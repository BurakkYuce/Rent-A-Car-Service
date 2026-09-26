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

    /// <summary>
    /// FAZ-54 — süzgeçli fatura listesi (canlı fatura_islem_listesi.aspx).
    /// Cari adı/özel kodu/vergi künyesi ve kira üzerinden plaka/sözleşme/ofis çözülür.
    /// PII ÇÖZÜLMEZ: DisplayName girdileri, OzelKod, VergiDairesi/VergiNo, Ulke düz-metin kolonlar.
    /// Metin araması bellek-içi ve ORDINAL — SQL'e lower() itmek Türkçe I/İ çiftinde sessizce
    /// eşleşmez (rezervasyon tarafında öğrenilen ders).
    /// </summary>
    public async Task<IReadOnlyList<InvoiceRow>> SearchAsync(
        InvoiceFilter? filter = null, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var f = filter ?? new InvoiceFilter();
        var limit = Math.Clamp(f.EnFazla, 1, 5000);

        var q = db.Invoices.AsNoTracking();
        if (f.CariId is { } c) q = q.Where(i => i.CariId == c);
        if (f.Durum is { } d) q = q.Where(i => i.Durum == d);
        if (f.Iptal is { } ip)
            q = ip ? q.Where(i => i.Durum == InvoiceStatus.Iptal) : q.Where(i => i.Durum != InvoiceStatus.Iptal);
        if (f.Bas is { } b) q = q.Where(i => i.Tarih >= b);
        if (f.Bit is { } t) q = q.Where(i => i.Tarih <= t);
        if (!string.IsNullOrWhiteSpace(f.Doviz)) { var dv = f.Doviz.Trim(); q = q.Where(i => i.Currency == dv); }

        var invoices = await q.OrderByDescending(i => i.Tarih).Take(limit).ToListAsync(ct);
        if (invoices.Count == 0) return [];

        var customerIds = invoices.Select(i => i.CariId).Distinct().ToList();
        var customers = (await db.Customers.AsNoTracking().Where(x => customerIds.Contains(x.Id))
                .Select(x => new { x.Id, x.Tip, x.Unvan, x.Ad, x.Soyad, x.OzelKod, x.VergiDairesi, x.VergiNo, x.Ulke })
                .ToListAsync(ct))
            .ToDictionary(x => x.Id);

        var rentalIds = invoices.Where(i => i.RentalId != null).Select(i => i.RentalId!.Value).Distinct().ToList();
        var rentals = (await db.Rentals.AsNoTracking().Where(r => rentalIds.Contains(r.Id))
            .Select(r => new { r.Id, r.SozlesmeNo, r.VehicleId, r.CikisOfisi }).ToListAsync(ct))
            .ToDictionary(r => r.Id);
        var vehicleIds = rentals.Values.Select(r => r.VehicleId).Distinct().ToList();
        var plates = (await db.Vehicles.AsNoTracking().Where(v => vehicleIds.Contains(v.Id))
            .Select(v => new { v.Id, v.Plaka }).ToListAsync(ct)).ToDictionary(v => v.Id, v => v.Plaka);

        IEnumerable<InvoiceRow> rows = invoices.Select(i =>
        {
            customers.TryGetValue(i.CariId, out var c);
            var rental = i.RentalId is { } rid && rentals.TryGetValue(rid, out var k) ? k : null;
            return new InvoiceRow(
                i,
                c is null ? "—" : new Customer { Tip = c.Tip, Unvan = c.Unvan, Ad = c.Ad, Soyad = c.Soyad }.DisplayName,
                c?.OzelKod, c?.VergiDairesi, c?.VergiNo, c?.Ulke,
                rental is null ? null : plates.GetValueOrDefault(rental.VehicleId),
                rental?.SozlesmeNo, rental?.CikisOfisi);
        });

        if (!string.IsNullOrWhiteSpace(f.Ofis))
        {
            var office = f.Ofis.Trim();
            rows = rows.Where(x => x.Ofis != null
                && string.Equals(x.Ofis.Trim(), office, StringComparison.OrdinalIgnoreCase));
        }
        if (!string.IsNullOrWhiteSpace(f.Ara))
        {
            var a = f.Ara.Trim();
            // Plaka DB'de BOŞLUKSUZ saklanıyor: kullanıcı "34 FL 02" yazdığında da bulunsun diye
            // terim ayrıca harf/rakama indirgenip DENENİR (kira listesindeki desenin aynısı).
            // Yalnız GENİŞLETİR — ham eşleşme aynen korunur.
            var plateTerm = new string([.. a.Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant)]);
            rows = rows.Where(x =>
                x.Fatura.No.Contains(a, StringComparison.OrdinalIgnoreCase)
                || x.CariAd.Contains(a, StringComparison.OrdinalIgnoreCase)
                || (x.CariOzelKod?.Contains(a, StringComparison.OrdinalIgnoreCase) ?? false)
                || (x.Plaka?.Contains(a, StringComparison.OrdinalIgnoreCase) ?? false)
                || (plateTerm.Length > 0 && (x.Plaka?.Contains(plateTerm, StringComparison.OrdinalIgnoreCase) ?? false))
                || (x.SozlesmeNo?.Contains(a, StringComparison.OrdinalIgnoreCase) ?? false)
                || (x.VergiNo?.Contains(a, StringComparison.OrdinalIgnoreCase) ?? false));
        }
        return [.. rows];
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
            join res in db.Reservations.AsNoTracking() on r.ReservationId equals (Guid?)res.Id into rezg
            from res in rezg.DefaultIfEmpty()
            select new { l, i, c, r, v, rez = res };

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
            .OrderByDescending(x => x.i.Tarih).ThenBy(x => x.i.CreatedAtUtc)
            .Take(limit)
            .Select(x => new
            {
                x.i.Id, x.i.No, x.i.Tarih, x.i.VadeTarihi, x.i.Durum, x.i.IadeMi, x.i.ManuelMi,
                x.i.Currency, x.i.Kur, x.i.CariId,
                CariAd = x.c == null ? null : (x.c.Tip == CustomerType.Bireysel
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
        var rentalInvoices = await db.Invoices.AsNoTracking()
            .Where(i => i.RentalId == rentalId || i.KaynakKiraId == rentalId)
            .ToListAsync(ct);
        var ids = rentalInvoices.Select(x => x.Id).ToList();
        var refunds = ids.Count == 0
            ? []
            : await db.Invoices.AsNoTracking()
                .Where(i => i.KaynakFaturaId != null && ids.Contains(i.KaynakFaturaId.Value))
                .ToListAsync(ct);
        return rentalInvoices.Concat(refunds.Where(i => !ids.Contains(i.Id)))
            .OrderByDescending(i => i.Tarih).ThenByDescending(i => i.CreatedAtUtc).ToList();
    }

    public async Task<bool> RefundExistsForAsync(Guid sourceInvoiceId, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.Invoices.AsNoTracking().AnyAsync(i => i.KaynakFaturaId == sourceInvoiceId, ct);
    }

    public async Task<(decimal FaturalananBrut, int FarkSayisi)> GetDifferenceStateAsync(Guid rentalId, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        // TOCTOU koruması (adversarial Kritik-1): faturalanan + fark-sayısı AYNI snapshot'tan okunur → eşzamanlı
        // fark isteklerinde tutarlı sıra. RepeatableRead: tek tutarlı görüntü; salt-okuma → rollback.
        await using var tx = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead, ct);
        try
        {
            return await CalculateDifferenceStateAsync(db, rentalId, ct);
        }
        finally { await tx.RollbackAsync(ct); }
    }

    /// <summary>Fark-state (iade-netli faturalanan brüt + fark sayısı) — GetFarkStateAsync ile
    /// posting TX-içi yeniden doğrulaması (adversarial B2-Kritik-1) AYNI sorgudan geçer (tek kopya).</summary>
    private static Task<(decimal FaturalananBrut, int FarkSayisi)> CalculateDifferenceStateAsync(
        AppDbContext db, Guid rentalId, CancellationToken ct)
        => SharedQueries.DifferenceStateAsync(db, rentalId, ct); // tek kopya (job üreticisiyle ortak)

    /// <summary>Kira-fatura advisory kilidi (adversarial B2-Kritik-1): base/fark/dönem posting'leri
    /// aynı kira üzerinde SERİLEŞİR — iki farklı unique-index'e yazan yollar (base: (TenantId,RentalId);
    /// fark/dönem: (TenantId,KaynakKiraId,Sıra)) birbirini görmeden commit edemez.</summary>
    private static Task RentalInvoiceLockAsync(AppDbContext db, Guid rentalId, CancellationToken ct)
        => RentalLocks.InvoiceAsync(db, rentalId, ct); // F4.1: tek kopya (kira iptali de aynı kilidi alır)

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
            var rentalLink = invoice.RentalId ?? invoice.KaynakKiraId;
            if (rentalLink is Guid kb && !invoice.IadeMi)
            {
                await RentalInvoiceLockAsync(db, kb, ct);
                // F4.1 adversarial M3: iptal ile kesim yarışı — iptal AYNI kilidi alıp açık fatura yokken
                // iptal eder; kesim de kilit altında kiranın hâlâ iptal olmadığını doğrular.
                await RentalLocks.CancelledRentalHasNoInvoiceAsync(db, kb, ct);
                // F4.1 adversarial N2: BASE faturanın tutarı servis tarafından kilit DIŞINDA hesaplandı. Bu arada
                // ek hizmet eklendi/silindi ya da dönüş bedeli yazıldıysa tutar bayattır → fatura sözleşmeden sapardı
                // (P17: fatura 420 / sözleşme 360 — fazla faturalama). Kilit altında aynı formülle yeniden hesapla.
                if (invoice.RentalId is Guid baseRental)
                    await RentalLocks.IsBaseInvoiceCurrentAsync(db, baseRental, invoice.GenelToplam, ct);
                if (invoice.RentalId is Guid rid &&
                    await db.Invoices.AsNoTracking().AnyAsync(i => i.RentalId == rid || i.KaynakKiraId == rid, ct))
                    throw new ValidationException("Kira bu sırada faturalandı (eşzamanlı istek) — kalan tutar için 'Fatura Kes' fark yolunu kullanın.");
            }

            invoice.No = await DocumentNoGenerator.InvoiceAsync(db, db.TenantId, ct);
            // No defter açıklamasında kullanıldığından satırların ait olduğu fatura no'yu yansıt.
            // İade satırları cari ekstrede "İade" etiketiyle görünsün (adversarial Low: eskiden
            // hepsi "Fatura" yazılıyordu).
            var label = invoice.IadeMi ? "İade" : "Fatura";
            foreach (var entry in entries)
                entry.Description = $"{label} {invoice.No}";

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

                // F1.4 — MANUEL FATURA (IslemAnahtari = Id): sıralı ikinci gönderim servis ön-kontrolünde
                // SESSİZ başarı alıyor (mevcut id döner). Yarışı kaybeden ikinci gönderim ise PK'ye çarpıp
                // "Kira zaten faturalanmış." (yanlış metin, 400) alıyordu → sonuç zamanlamaya bağlıydı.
                // Aynı Id'li fatura BU kiracıda görünüyorsa aynı sessiz başarı; görünmüyorsa (başka kiracının
                // Id'si — PK kiracı-global) sessiz yutmak geliri kaybettirirdi → net red (depozito deseni).
                if (invoice.ManuelMi && !invoice.IadeMi && invoice.RentalId is null && invoice.KaynakKiraId is null
                    && (ex.InnerException as PostgresException)?.ConstraintName == "PK_Invoices")
                {
                    var existing = await db.Invoices.AsNoTracking().FirstOrDefaultAsync(i => i.Id == invoice.Id, ct)
                        ?? throw new ValidationException("İşlem anahtarı başka bir kayıtla çakıştı — yeni anahtarla tekrar deneyin.");
                    // Adversarial MEDIUM-1: sessiz başarı YALNIZ aynı cari + aynı tutarlar için (servis
                    // ön-kontrolüyle aynı kural); farklıysa ikinci fatura kesilmedi → 409.
                    if (existing.ManuelMi && !existing.IadeMi && existing.RentalId is null && existing.KaynakKiraId is null
                        && existing.CariId == invoice.CariId && existing.NetTutar == invoice.NetTutar
                        && existing.KdvTutar == invoice.KdvTutar
                        && string.Equals(existing.Currency, invoice.Currency, StringComparison.OrdinalIgnoreCase))
                        return;
                    throw DuplicateOperationException.DifferentContent();
                }
                // Fark faturası (KaynakKiraId): eşzamanlı/çift istek aynı hedefe çarptı → idempotent reddet.
                throw new ValidationException(
                    invoice.KaynakKiraId is not null ? "Kira farkı zaten faturalanmış (eşzamanlı istek)." :
                    invoice.IadeMi ? "Bu fatura zaten iade edilmiş." : "Kira zaten faturalanmış.");
            }
        }, ct);
    }

    public async Task<Guid> PostPeriodAsync(Invoice invoice, IReadOnlyList<AccountLedgerEntry> entries,
        Guid periodId, decimal issuedAmount, decimal expectedInvoiced, CancellationToken ct = default)
    {
        var debit = entries.Where(e => e.Direction == LedgerDirection.Debit).Sum(e => e.Amount.AmountInBase);
        var credit = entries.Where(e => e.Direction == LedgerDirection.Credit).Sum(e => e.Amount.AmountInBase);
        if (debit != credit)
            throw new ValidationException($"Dönem faturası defteri dengesiz: borç {debit} ≠ alacak {credit}.");

        return await PgRetry.RunAsync(async () => // deadlock/serialization çakışmasında baştan dene
        {
            await using var db = await _factory.CreateDbContextAsync(ct);
            await using var tx = await db.Database.BeginTransactionAsync(ct);

            // Adversarial B2-Kritik-1: advisory kilit + FATURALANAN TX-İÇİ yeniden doğrulama —
            // servis kesileceği kilit DIŞINDA hesapladı; bu arada base/fark/dönem faturası commit
            // ettiyse tutar bayattır → temiz red (çağıran güncel durumla yeniden dener).
            await RentalInvoiceLockAsync(db, invoice.KaynakKiraId!.Value, ct);
            await RentalLocks.CancelledRentalHasNoInvoiceAsync(db, invoice.KaynakKiraId!.Value, ct); // F4.1 M3

            // F1.4 — AYNI dönemin çift gönderimi: sıralı ikinci istek serviste "Kesildi → mevcut
            // InvoiceId" sessiz başarısı alıyor. Yarışı kaybeden ikinci istek ise aşağıdaki faturalanan
            // kontrolüne takılıp 400 alıyordu → sonuç zamanlamaya bağlıydı. Dönem kilidin arkasında
            // Kesildi ise AYNI sessiz başarı: mevcut fatura id'si döner, hiçbir şey yazılmaz.
            var periodStatus = await db.FaturaDonemleri.AsNoTracking()
                .Where(d => d.Id == periodId).Select(d => new { d.Durum, d.InvoiceId }).FirstOrDefaultAsync(ct);
            if (periodStatus is { Durum: InvoicePeriodStatus.Kesildi, InvoiceId: Guid existingInvoice })
            {
                await tx.RollbackAsync(ct);
                return existingInvoice;
            }

            var (currentInvoiced, _) = await CalculateDifferenceStateAsync(db, invoice.KaynakKiraId.Value, ct);
            if (currentInvoiced != expectedInvoiced)
                throw new ValidationException("Kira faturaları bu sırada değişti (eşzamanlı istek) — dönem kesimini yeniden deneyin.");

            // FAZ 4.2-B2: dönem satırı fatura+defterle AYNI transaction'da Kesildi'ye geçer — yarım
            // durum imkânsız (fatura var/dönem Planlandi ya da tersi olamaz). TX-içi yarış çiti:
            // Planlandi DIŞI her durum reddedilir (Kesildi/Atlandi yarışı; unique index ikinci savunma).
            var period = await db.FaturaDonemleri.FirstOrDefaultAsync(d => d.Id == periodId, ct)
                ?? throw new ValidationException("Fatura dönemi bulunamadı.");
            if (period.Durum != InvoicePeriodStatus.Planlandi)
                throw new ValidationException("Dönem bu sırada kesilmiş/atlanmış (eşzamanlı istek).");
            period.Durum = InvoicePeriodStatus.Kesildi;
            period.InvoiceId = invoice.Id;
            period.KesilenTutar = issuedAmount;
            period.UpdatedAtUtc = DateTimeOffset.UtcNow;

            invoice.No = await DocumentNoGenerator.InvoiceAsync(db, db.TenantId, ct);
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
            return invoice.Id;
        }, ct);
    }
}
