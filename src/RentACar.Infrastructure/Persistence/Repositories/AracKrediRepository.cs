using Microsoft.EntityFrameworkCore;
using RentACar.Application.AracKredileri;
using RentACar.Domain.Entities;
using RentACar.Domain.Enums;

using RentACar.Domain.Common;

namespace RentACar.Infrastructure.Persistence.Repositories;

/// <summary>Araç kredisi kalıcılığı (roadmap L4). CreateAsync boşluksuz No (KR-000001) tahsis eder.</summary>
public sealed class AracKrediRepository(IDbContextFactory<AppDbContext> factory) : IVehicleLoanRepository
{
    private readonly IDbContextFactory<AppDbContext> _factory = factory;

    public async Task<IReadOnlyList<AracKredi>> ListAsync(CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.AracKredileri.AsNoTracking().OrderByDescending(x => x.CreatedAtUtc).ToListAsync(ct);
    }

    /// <summary>FAZ-13 — filtreli liste. Sıralama ListAsync ile AYNI (en yeni üstte) ki filtre
    /// açıp kapatmak satır sırasını değiştirmesin.</summary>
    public async Task<IReadOnlyList<AracKredi>> SearchAsync(AracKrediFilter filtre, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var q = db.AracKredileri.AsNoTracking();

        if (filtre.CariId is Guid c) q = q.Where(x => x.CariId == c);
        if (filtre.Durum is { } d) q = q.Where(x => x.Durum == d);
        if (filtre.Bas is { } bas) q = q.Where(x => x.BaslangicTarihi >= bas);
        if (filtre.Bit is { } bit) q = q.Where(x => x.BaslangicTarihi <= bit);

        if (!string.IsNullOrWhiteSpace(filtre.DosyaNo))
        {
            var dn = filtre.DosyaNo.Trim();
            q = q.Where(x => x.DosyaNo != null && EF.Functions.ILike(x.DosyaNo, $"%{dn}%"));
        }

        if (!string.IsNullOrWhiteSpace(filtre.Plaka))
        {
            // Kredide plaka kolonu YOK (VehicleId var) → Vehicles alt-sorgusu. Bellekte süzmek
            // tüm kredileri çekmeyi gerektirirdi; ILIKE parça eşleşmesi canlıdaki davranışla aynı.
            // Plaka DB'de boşluksuz-büyük harf saklanır (VehicleService.Normalize) → arama terimi de
            // AYNI kuraldan geçmeli, yoksa listede görülen "34 ABC 01" yazımı hiçbir şey bulmazdı
            // (FAZ-29 M4 dersi: tek kural, kopya yok).
            var p = RentACar.Application.Vehicles.VehicleService.PlateKey(filtre.Plaka);
            q = q.Where(x => x.VehicleId != null
                && db.Vehicles.Any(v => v.Id == x.VehicleId && EF.Functions.ILike(v.Plaka, $"%{p}%")));
        }

        return await q.OrderByDescending(x => x.CreatedAtUtc).ToListAsync(ct);
    }

    public async Task<AracKredi?> FindAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.AracKredileri.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
    }

    public async Task CreateAsync(AracKredi row, CancellationToken ct = default)
    {
        await PgRetry.RunAsync(async () => // P0-5: deadlock/serialization çakışmasında baştan dene
        {
            await using var db = await _factory.CreateDbContextAsync(ct);
            await using var tx = await db.Database.BeginTransactionAsync(ct); // No tahsisi atomik (boşluksuz)
            row.No = await BelgeNoUretici.UretAsync(db, db.TenantId, DocumentNoType.AracKredi, ct);
            db.AracKredileri.Add(row);
            try
            {
                await db.SaveChangesAsync(ct);
            }
            // FAZ-13: silinmiş/başka tenant'ın carisi ya da aracı seçilirse composite FK ihlali gelir;
            // uç yalnız ValidationException yakalıyor → aksi halde kullanıcı 500 görürdü.
            catch (Microsoft.EntityFrameworkCore.DbUpdateException ex)
                when (ex.InnerException is Npgsql.PostgresException { SqlState: Npgsql.PostgresErrorCodes.ForeignKeyViolation })
            {
                await tx.RollbackAsync(ct);
                throw new RentACar.Application.Common.ValidationException(
                    "Seçilen cari ya da araç bulunamadı (silinmiş olabilir); listeyi yenileyip tekrar deneyin.");
            }
            // F6.1b — Id = işlem anahtarı (yalnız /api/ui): aynı anahtarla eşzamanlı ikinci oluşturma PK'ye çarpar.
            catch (Microsoft.EntityFrameworkCore.DbUpdateException ex) when (PkIhlali.Mi(ex))
            {
                await tx.RollbackAsync(ct);
                throw new RentACar.Application.Common.DuplicateOperationException(PkIhlali.Mesaj);
            }
            await tx.CommitAsync(ct);
        }, ct);
    }

    private const string TaksitMukerrer = "Bu taksit ödemesi zaten kaydedilmiş (çift gönderim).";

    public async Task<bool> PayInstallmentAsync(Guid id,
        Func<int, (Expense Expense, IReadOnlyList<AccountLedgerEntry> Entries)>? posting = null,
        CancellationToken ct = default, Guid? islemAnahtari = null, int? beklenenSira = null)
    {
        return await PgRetry.RunAsync(async () => // P0-5 deadlock retry + sayaç yarışı koruması
        {
            await using var db = await _factory.CreateDbContextAsync(ct);
            await using var tx = await db.Database.BeginTransactionAsync(ct);

            // Satır kilidi: eşzamanlı taksit ödemeleri serileşir → OdenenTaksit sayaç yarışı
            // (kayıp artırım = kaybolan ödeme) OLMAZ.
            var row = await db.AracKredileri
                .FromSqlRaw("SELECT * FROM \"AracKredileri\" WHERE \"Id\" = {0} FOR UPDATE", id)
                .FirstOrDefaultAsync(ct);
            if (row is null) return false;
            // F1.4 — ANAHTAR ÖNCE (satır kilidinin arkasında): aynı anahtarla ikinci gönderim, "tüm taksitler
            // ödendi" (sessiz false) çitinden ÖNCE mükerrer sayılır. Yoksa son taksidin tekrarı sessiz false,
            // ara taksidin tekrarı kısıt reddi alıyordu — sonuç taksidin sırasına bağlıydı.
            if (islemAnahtari is Guid anahtar && anahtar != Guid.Empty &&
                await db.Expenses.AsNoTracking().AnyAsync(e => e.IslemAnahtari == anahtar, ct))
                throw new RentACar.Application.Common.DuplicateOperationException(TaksitMukerrer);
            // Adversarial 1.3 M1: Durum çiti KİLİDİN ARKASINDA — iptal-yarışında iptal krediye para
            // yazılıp İptal'in Kapandi ile ezilmesi imkânsızlaşır (servis ön-kontrolü yarışa açıktı).
            if (row.Durum == LoanStatus.Iptal)
                throw new RentACar.Application.Common.ValidationException("İptal kredinin taksiti ödenemez.");
            // F6.1b — BAYATLIK (anahtar kontrolünden SONRA, kilit altında): istemci hangi taksidi ödediğini söyler.
            // İki sekme farklı anahtarla aynı ekrandan "Taksit Öde"ye basarsa ikincisi #n+1'i DEĞİL 409 cakisma alır
            // (aksi halde kullanıcının niyeti olmayan bir sonraki taksit de ödenirdi).
            if (beklenenSira is int beklenen && row.OdenenTaksit + 1 != beklenen)
                throw new RentACar.Application.Common.ConcurrentModificationException(
                    $"Kredinin ödenen taksit sayısı bu ekran açıldıktan sonra değişti (şu an {row.OdenenTaksit}/{row.TaksitSayisi}); " +
                    "taksit ödenmedi. Güncel planı kontrol edip yeniden deneyin.");
            if (row.OdenenTaksit >= row.TaksitSayisi) return false; // tüm taksitler ödendi
            row.OdenenTaksit++;
            if (row.OdenenTaksit >= row.TaksitSayisi) row.Durum = LoanStatus.Kapandi;
            row.UpdatedAtUtc = DateTimeOffset.UtcNow;

            // FAZ 1.3: taksit GİDERİ sayaçla AYNI transaction'da — biri olmadan diğeri asla yazılmaz.
            if (posting is not null)
            {
                var (expense, entries) = posting(row.OdenenTaksit);
                var debit = entries.Where(e => e.Direction == LedgerDirection.Debit).Sum(e => e.Amount.AmountInBase);
                var credit = entries.Where(e => e.Direction == LedgerDirection.Credit).Sum(e => e.Amount.AmountInBase);
                if (debit != credit)
                    throw new RentACar.Application.Common.ValidationException($"Defter dengesiz: borç {debit} ≠ alacak {credit}.");
                expense.No = await BelgeNoUretici.UretAsync(db, db.TenantId, DocumentNoType.Gider, ct);
                db.Expenses.Add(expense);
                db.AccountLedgerEntries.AddRange(entries);
            }

            try
            {
                await db.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);
            }
            catch (Microsoft.EntityFrameworkCore.DbUpdateException ex)
                when (ex.InnerException is Npgsql.PostgresException { SqlState: Npgsql.PostgresErrorCodes.UniqueViolation })
            {
                // IslemAnahtari kısmi unique index: çift-submit → sayaç DA geri alınır (tek tx) → net red.
                // F1.4: kısıt adına göre sınıflandırılır (F1.1 deseni) — idempotency kısıtı → Mukerrer (409),
                // belge no çakışması gibi diğerleri eskisi gibi düz ValidationException.
                await tx.RollbackAsync(ct);
                throw IdempotencyKisiti.Red(ex, TaksitMukerrer);
            }
            return true;
        }, ct);
    }

    /// <summary>FAZ-13 — toplu iptal. Kilit sırası ID'ye göre SABİT: iki eşzamanlı toplu iptal
    /// kesişen kümelerde birbirini deadlock'a sokmaz. Aktif olmayan satır sessizce atlanır.</summary>
    public async Task<int> BulkCancelAsync(IReadOnlyList<Guid> ids, CancellationToken ct = default)
    {
        if (ids.Count == 0) return 0;
        return await PgRetry.RunAsync(async () =>
        {
            await using var db = await _factory.CreateDbContextAsync(ct);
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            var sayac = 0;
            foreach (var id in ids.Distinct().OrderBy(x => x))
            {
                var row = await db.AracKredileri
                    .FromSqlRaw("SELECT * FROM \"AracKredileri\" WHERE \"Id\" = {0} FOR UPDATE", id)
                    .FirstOrDefaultAsync(ct);
                // Kapanmış (tüm taksitleri ödenmiş) ya da zaten iptal olan kredi ATLANIR — toplu
                // seçimde tek satır yüzünden işlem patlamasın, ödenmiş taksitlerin defteri de
                // hiçbir şekilde geri alınmasın.
                if (row is null || row.Durum != LoanStatus.Aktif) continue;
                row.Durum = LoanStatus.Iptal;
                row.UpdatedAtUtc = DateTimeOffset.UtcNow;
                sayac++;
            }
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return sayac;
        }, ct);
    }

    public async Task<bool> SetStatusAsync(Guid id, LoanStatus durum, CancellationToken ct = default)
    {
        return await PgRetry.RunAsync(async () =>
        {
            await using var db = await _factory.CreateDbContextAsync(ct);
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            // Aynı satır kilidi (M1 simetrisi): iptal, süren taksit ödemesiyle serileşir.
            var row = await db.AracKredileri
                .FromSqlRaw("SELECT * FROM \"AracKredileri\" WHERE \"Id\" = {0} FOR UPDATE", id)
                .FirstOrDefaultAsync(ct);
            if (row is null) return false;
            row.Durum = durum;
            row.UpdatedAtUtc = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return true;
        }, ct);
    }
}
