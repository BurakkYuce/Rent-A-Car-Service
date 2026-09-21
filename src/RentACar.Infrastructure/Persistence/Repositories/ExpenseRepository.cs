using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using RentACar.Application.Common;
using RentACar.Application.Expenses;
using RentACar.Domain.Entities;
using RentACar.Domain.Enums;

using RentACar.Domain.Common;

namespace RentACar.Infrastructure.Persistence.Repositories;

/// <summary>
/// Gider kalıcılığı. PostAsync: No tahsisi + gider + DENGELİ defter kümesi → TEK transaction.
/// Gider/defter immutable (DB trigger).
/// </summary>
public sealed class ExpenseRepository(IDbContextFactory<AppDbContext> factory) : IExpenseRepository
{
    private readonly IDbContextFactory<AppDbContext> _factory = factory;

    public async Task<IReadOnlyList<Expense>> ListAsync(
        RentACar.Application.Authorization.BranchScope.BranchFilter kapsam,
        RentACar.Application.Expenses.ExpenseFilter? filter = null, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var q = db.Expenses.AsNoTracking();
        // C3 ŞABLON (BranchScope.InScope ile birebir): FK-eşit VEYA metin-eşit (Ordinal).
        // ÖNCE kapsam, SONRA kullanıcı filtresi — filtre kapsamı genişletemez.
        if (!kapsam.Unrestricted)
        {
            var kid = kapsam.SubeId; var kad = kapsam.SubeAd;
            q = q.Where(x => (kid != null && x.SubeId == kid)
                          || ((kid == null || x.SubeId == null) && kad != null && x.Sube != null && x.Sube.Trim() == kad)); // C5
        }

        if (filter is not null)
        {
            if (!string.IsNullOrWhiteSpace(filter.Ara))
            {
                var t = filter.Ara.Trim();
                q = q.Where(x => EF.Functions.ILike(x.No, $"%{t}%")
                              || (x.EvrakNo != null && EF.Functions.ILike(x.EvrakNo, $"%{t}%"))
                              || (x.Aciklama != null && EF.Functions.ILike(x.Aciklama, $"%{t}%")));
            }
            if (filter.CariId is { } cid) q = q.Where(x => x.CariId == cid);
            if (filter.Tip is { } tip) q = q.Where(x => x.Tip == tip);
            if (!string.IsNullOrWhiteSpace(filter.Sube))
            {
                var s = filter.Sube.Trim();
                q = q.Where(x => x.Sube != null && x.Sube.Trim() == s);
            }
            if (filter.Bas is { } b) q = q.Where(x => x.Tarih >= b);
            if (filter.Bit is { } t2) q = q.Where(x => x.Tarih <= t2);
            if (!string.IsNullOrWhiteSpace(filter.Plaka))
            {
                // Plaka Expense'te YOK → araç tablosundan alt-sorgu (tenant filtresi orada da geçerli).
                // Plakalar DB'de normalize saklanır (büyük harf, boşluksuz: "34AA01"); kullanıcı ise
                // "34 AA 01" yazar. Arama terimi AYNI normalizasyondan geçmezse hiçbir şey bulunmaz.
                var p = filter.Plaka.Trim().ToUpperInvariant().Replace(" ", string.Empty);
                q = q.Where(x => x.VehicleId != null && db.Vehicles
                    .Where(v => EF.Functions.ILike(v.Plaka, $"%{p}%"))
                    .Select(v => (Guid?)v.Id).Contains(x.VehicleId));
            }
        }

        return await q.OrderByDescending(x => x.Tarih).ToListAsync(ct);
    }

    public async Task<Expense?> FindAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.Expenses.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
    }

    /// <summary>
    /// FAZ-64 — kısmi ödeme kaydı. Kontrol + sıra tahsisi + yazma AYNI transaction'da ve
    /// <c>(tenant, gider)</c> danışma kilidinin ARKASINDA; kilitsiz "önce oku sonra yaz" TOCTOU'dur
    /// (depozito/ceza deseniyle aynı). Aynı işlem anahtarıyla ikinci gönderim sessizce yutulur.
    /// </summary>
    public async Task<GiderOdeme?> OdemeEkleAsync(
        Guid expenseId, decimal? tutar, DateTimeOffset tarih, string? makbuzNo, string? aciklama,
        string? islemYapan, Guid? islemAnahtari, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        await KilitleAsync(db, $"gider:{db.TenantId}:{expenseId}", ct);

        // F1.4 — ANAHTAR ÖNCE (kilidin arkasında): aynı anahtarlı ikinci gönderim kalan kontrolünden ÖNCE
        // sessizce yutulur (null) — yoksa ilk ödeme kalanın tamamını kapattıysa ikinci "kalanı yok" (400)
        // alıyor, kısmi ödemenin tekrarı sessiz geçiyordu (sonuç tutara bağlıydı).
        // Adversarial MEDIUM-1: sessizlik YALNIZ aynı gider + aynı tutar içindir; aynı anahtar başka
        // gider/tutarla gelirse 409 (önceden sessiz null dönüp ikinci ödemeyi YAZMIYORDU).
        if (await GiderOdemeMevcutMuAsync(db, islemAnahtari, expenseId, tutar, ct))
        {
            await tx.RollbackAsync(ct);
            return null;
        }

        var gider = await db.Expenses.AsNoTracking().FirstOrDefaultAsync(x => x.Id == expenseId, ct)
            ?? throw new ValidationException("Gider bulunamadı.");
        if (gider.OdemeYontemi != OdemeYontemi.AcikHesap)
            throw new ValidationException(
                "Bu gider kayıt anında ödendi (nakit/banka); kısmi ödeme takibi yalnız açık hesap giderlerinde yapılır.");

        // Kalan KİLİDİN ARKASINDA okunur — eşzamanlı iki ödeme birlikte geçemez.
        var odenen = await db.GiderOdemeleri.Where(o => o.ExpenseId == expenseId).SumAsync(o => (decimal?)o.Tutar, ct) ?? 0m;
        var kalan = gider.GenelToplam - odenen;
        if (kalan <= 0m) throw new ValidationException("Bu giderin kalanı yok; tamamı ödenmiş.");

        var odenecek = tutar ?? kalan;
        odenecek = decimal.Round(odenecek, 2, MidpointRounding.ToZero);
        if (odenecek <= 0m) throw new ValidationException("Ödeme tutarı pozitif olmalıdır.");
        if (odenecek > kalan)
            throw new ValidationException($"Ödeme tutarı kalanı aşamaz (kalan {kalan:N2}).");

        var sira = await db.GiderOdemeleri.Where(o => o.ExpenseId == expenseId).CountAsync(ct) + 1;
        var kayit = new GiderOdeme
        {
            ExpenseId = expenseId, Sira = sira, Tutar = odenecek, KalanSonrasi = kalan - odenecek,
            Tarih = tarih, MakbuzNo = makbuzNo, Aciklama = aciklama, IslemYapan = islemYapan,
            // MONOTON bileşen = o gider için ödeme sırası. Değer-anlık-görüntüsünden (tutar+tarih)
            // türetilen anahtar zamansal çakışır: aynı tutarlı iki meşru ödeme birbirini yutardı.
            Anahtar = string.Create(System.Globalization.CultureInfo.InvariantCulture,
                $"gider:{expenseId}:odeme:{sira}"),
            IslemAnahtari = islemAnahtari is { } k && k != Guid.Empty ? k : null
        };
        db.GiderOdemeleri.Add(kayit);
        try
        {
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return kayit;
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // Çift-submit: ikinci gönderim yutulur, bakiye DEĞİŞMEZ. F1.4: farklı giderlerin kilitleri
            // aynı anahtarla yarışabilir → içerik farklıysa 409 (GiderOdemeMevcutMuAsync fırlatır).
            await tx.RollbackAsync(ct);
            await GiderOdemeMevcutMuAsync(db, islemAnahtari, expenseId, tutar, ct);
            return null;
        }
    }

    /// <summary>
    /// F1.4 — bu anahtarla yazılmış gider ödemesi var mı? Yoksa false. Varsa ve AYNI gidere AYNI tutarla
    /// yazılmışsa true (sessiz). Tutar null ("kalanın tamamı") yalnız kayıtlı ödeme o gideri KAPATTIYSA
    /// aynı istek sayılır. Başka gider ya da tutar → <see cref="MukerrerIslemException"/>.
    /// </summary>
    private static async Task<bool> GiderOdemeMevcutMuAsync(
        AppDbContext db, Guid? islemAnahtari, Guid expenseId, decimal? tutar, CancellationToken ct)
    {
        if (islemAnahtari is not { } k || k == Guid.Empty) return false;
        var mevcut = await db.GiderOdemeleri.AsNoTracking()
            .Where(o => o.IslemAnahtari == k)
            .Select(o => new { o.ExpenseId, o.Tutar, o.KalanSonrasi })
            .FirstOrDefaultAsync(ct);
        if (mevcut is null) return false;
        var ayni = mevcut.ExpenseId == expenseId && (tutar is { } t
            ? decimal.Round(t, 2, MidpointRounding.ToZero) == mevcut.Tutar
            : mevcut.KalanSonrasi == 0m);
        if (!ayni) throw MukerrerIslemException.FarkliIcerik();
        return true;
    }

    public async Task<Dictionary<Guid, decimal>> OdenenToplamlariAsync(
        IReadOnlyCollection<Guid> expenseIds, CancellationToken ct = default)
    {
        if (expenseIds.Count == 0) return [];
        await using var db = await _factory.CreateDbContextAsync(ct);
        var idler = expenseIds.Distinct().ToList();
        return await db.GiderOdemeleri.AsNoTracking()
            .Where(o => idler.Contains(o.ExpenseId))
            .GroupBy(o => o.ExpenseId)
            .Select(g => new { g.Key, Toplam = g.Sum(x => x.Tutar) })
            .ToDictionaryAsync(x => x.Key, x => x.Toplam, ct);
    }

    public async Task<IReadOnlyList<GiderOdeme>> ListOdemelerAsync(Guid expenseId, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.GiderOdemeleri.AsNoTracking()
            .Where(o => o.ExpenseId == expenseId).OrderBy(o => o.Sira).ToListAsync(ct);
    }

    /// <summary>Gider-kapsamlı serileştirme kilidi (depozito/ceza deseniyle aynı; tx bitince bırakılır).</summary>
    private static async Task KilitleAsync(AppDbContext db, string anahtar, CancellationToken ct)
    {
        await db.Database.OpenConnectionAsync(ct);
        await using var cmd = db.Database.GetDbConnection().CreateCommand();
        cmd.Transaction = db.Database.CurrentTransaction?.GetDbTransaction();
        cmd.CommandText = "SELECT pg_advisory_xact_lock(hashtextextended(@k, 42))";
        var p = cmd.CreateParameter(); p.ParameterName = "k"; p.Value = anahtar;
        cmd.Parameters.Add(p);
        await cmd.ExecuteScalarAsync(ct);
    }

    public async Task PostAsync(Expense expense, IReadOnlyList<AccountLedgerEntry> entries, CancellationToken ct = default)
    {
        var debit = entries.Where(e => e.Direction == LedgerDirection.Debit).Sum(e => e.Amount.AmountInBase);
        var credit = entries.Where(e => e.Direction == LedgerDirection.Credit).Sum(e => e.Amount.AmountInBase);
        if (debit != credit)
            throw new ValidationException($"Gider defteri dengesiz: borç {debit} ≠ alacak {credit}.");

        await PgRetry.RunAsync(async () => // P0-5: deadlock/serialization çakışmasında baştan dene
        {
            await using var db = await _factory.CreateDbContextAsync(ct);
            await using var tx = await db.Database.BeginTransactionAsync(ct);

            expense.No = await BelgeNoUretici.UretAsync(db, db.TenantId, BelgeNoTuru.Gider, ct);

            db.Expenses.Add(expense);
            db.AccountLedgerEntries.AddRange(entries);

            try
            {
                await db.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);
            }
            catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
            {
                // F1.4: tekil gider artık IslemAnahtari taşıyabiliyor (başlıktan türetilen anahtar) →
                // aynı anahtarla ikinci gönderim (TenantId, IslemAnahtari) kısmi unique'ine çarpar: 409.
                await tx.RollbackAsync(ct);
                throw IdempotencyKisiti.Red(ex, "Bu gider zaten kaydedilmiş (çift gönderim).");
            }
        }, ct);
    }

    public async Task PostBatchAsync(IReadOnlyList<ExpensePosting> items, CancellationToken ct = default)
    {
        if (items.Count == 0) throw new ValidationException("Toplu gider en az bir kalem içermelidir.");

        foreach (var it in items)
        {
            var d = it.Entries.Where(e => e.Direction == LedgerDirection.Debit).Sum(e => e.Amount.AmountInBase);
            var c = it.Entries.Where(e => e.Direction == LedgerDirection.Credit).Sum(e => e.Amount.AmountInBase);
            if (d != c) throw new ValidationException($"Gider defteri dengesiz: borç {d} ≠ alacak {c}.");
        }

        await PgRetry.RunAsync(async () => // P0-5: deadlock/serialization çakışmasında baştan dene
        {
            await using var db = await _factory.CreateDbContextAsync(ct);
            await using var tx = await db.Database.BeginTransactionAsync(ct);

            // ATOMİK: tüm kalemler TEK transaction'da. No'lar boşluksuz; rollback olursa sıra geri alınır.
            foreach (var it in items)
            {
                it.Expense.No = await BelgeNoUretici.UretAsync(db, db.TenantId, BelgeNoTuru.Gider, ct);
                db.Expenses.Add(it.Expense);
                db.AccountLedgerEntries.AddRange(it.Entries);
            }

            try
            {
                await db.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);
            }
            catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
            {
                await tx.RollbackAsync(ct);
                throw IdempotencyKisiti.Red(ex, "Bu toplu gider zaten kaydedilmiş.");
            }
            // FAZ-29 adversarial M1: geçersiz/silinmiş hesap ya da araç referansı FK ihlali üretiyor
            // ve uç yalnız ValidationException yakaladığı için 500 dönüyordu — 500 satırlık parti
            // anlaşılmaz bir hata sayfasıyla kayboluyordu. Temiz redde çevir.
            catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.ForeignKeyViolation })
            {
                await tx.RollbackAsync(ct);
                throw new ValidationException("Seçilen hesap ya da araç bulunamadı (silinmiş olabilir); listeyi yenileyip tekrar deneyin.");
            }
        }, ct);
    }
}
