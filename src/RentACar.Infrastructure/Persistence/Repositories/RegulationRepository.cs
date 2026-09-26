using Microsoft.EntityFrameworkCore;
using Npgsql;
using RentACar.Application.Common;
using RentACar.Application.Regulation;
using RentACar.Domain.Entities;
using RentACar.Domain.Enums;

namespace RentACar.Infrastructure.Persistence.Repositories;

/// <summary>Sigorta/MTV/Muayene CRUD + birleşik vade kaynakları.</summary>
public sealed class RegulationRepository(IDbContextFactory<AppDbContext> factory) : IRegulationRepository
{
    private readonly IDbContextFactory<AppDbContext> _factory = factory;

    public async Task<IReadOnlyList<InsurancePolicy>> ListInsuranceAsync(CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.InsurancePolicies.AsNoTracking().OrderByDescending(x => x.Bitis).ToListAsync(ct);
    }

    public async Task<IReadOnlyList<MtvRecord>> ListMtvAsync(CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.MtvRecords.AsNoTracking().OrderByDescending(x => x.Vade).ToListAsync(ct);
    }

    public async Task<IReadOnlyList<InspectionRecord>> ListInspectionAsync(CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.InspectionRecords.AsNoTracking().OrderByDescending(x => x.Bitis).ToListAsync(ct);
    }

    public async Task AddInsuranceAsync(InsurancePolicy policy, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        db.InsurancePolicies.Add(policy);
        await db.SaveChangesAsync(ct);
    }

    public async Task AddMtvAsync(MtvRecord record, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        db.MtvRecords.Add(record);
        await db.SaveChangesAsync(ct);
    }

    public async Task AddInspectionAsync(InspectionRecord record, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        db.InspectionRecords.Add(record);
        await db.SaveChangesAsync(ct);
    }

    public async Task<MtvRecord?> FindMtvAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.MtvRecords.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
    }

    private const string MtvDuplicate = "Bu MTV ödemesi zaten kaydedilmiş (çift gönderim).";
    private const string InspectionDuplicate = "Bu muayene ödemesi zaten kaydedilmiş (çift gönderim).";

    public async Task<RegulasyonOdemeSonuc> PostMtvPaymentAsync(
        Guid mtvId,
        Func<decimal, int, (MtvOdeme Odeme, IReadOnlyList<AccountLedgerEntry> Entries)> posting,
        CancellationToken ct = default, Guid? operationKey = null)
    {
        return await PgRetry.RunAsync(async () => // P0-5: deadlock/serialization çakışmasında baştan dene
        {
            await using var db = await _factory.CreateDbContextAsync(ct);
            await using var tx = await db.Database.BeginTransactionAsync(ct);

            // SATIR KİLİDİ: kalan bakiye ve sıra numarası kilidin ARKASINDA okunur. Kilitsiz
            // okusaydık iki eşzamanlı ödeme aynı kalanı görüp bakiyeyi birlikte aşabilir
            // (kayıp güncelleme) ve aynı sırayı alabilirdi.
            var rec = await db.MtvRecords
                .FromSqlRaw("SELECT * FROM \"MtvRecords\" WHERE \"Id\" = {0} FOR UPDATE", mtvId)
                .FirstOrDefaultAsync(ct)
                ?? throw new ValidationException("MTV kaydı bulunamadı.");
            // F1.4 — ANAHTAR ÖNCE (satır kilidinin arkasında): aynı anahtarlı ikinci gönderim, "zaten
            // ödendi"/"bakiye yok" çitlerinden ÖNCE mükerrer sayılır → sonuç ilk ödemenin tutarına bağlı değil.
            if (operationKey is Guid key && key != Guid.Empty &&
                await db.MtvOdemeleri.AsNoTracking().AnyAsync(x => x.IslemAnahtari == key, ct))
                throw new DuplicateOperationException(MtvDuplicate);
            if (rec.Odendi) throw new ValidationException("MTV zaten ödendi.");
            if (rec.Kalan <= 0m) throw new ValidationException("MTV kaydında ödenecek bakiye yok.");

            var order = await db.MtvOdemeleri.CountAsync(x => x.MtvId == mtvId, ct) + 1;
            var (payment, entries) = posting(rec.Kalan, order);
            CheckBalanced(entries, payment.Tutar, "MTV");

            rec.Kalan = decimal.Round(rec.Kalan - payment.Tutar, 4, MidpointRounding.AwayFromZero);
            if (rec.Kalan <= 0m) { rec.Kalan = 0m; rec.Odendi = true; }
            rec.UpdatedAtUtc = DateTimeOffset.UtcNow;
            payment.KalanSonrasi = rec.Kalan;

            db.MtvOdemeleri.Add(payment);
            db.AccountLedgerEntries.AddRange(entries);

            try
            {
                await db.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);
            }
            catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
            {
                // (MtvId, Sira) veya IslemAnahtari kısmi unique index: çift gönderim → HER ŞEY geri
                // alınır (tek tx), bakiye DEĞİŞMEZ.
                await tx.RollbackAsync(ct);
                throw IdempotencyConstraint.Red(ex, MtvDuplicate);
            }

            return new RegulasyonOdemeSonuc(payment.Id, payment.Sira, payment.Tutar, rec.Kalan, rec.Odendi);
        }, ct);
    }

    public async Task<IReadOnlyList<MtvOdeme>> ListMtvPaymentsAsync(Guid mtvId, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.MtvOdemeleri.AsNoTracking()
            .Where(x => x.MtvId == mtvId).OrderBy(x => x.Sira).ToListAsync(ct);
    }

    public async Task<IReadOnlyList<MtvOdeme>> ListAllMtvPaymentsAsync(CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.MtvOdemeleri.AsNoTracking()
            .OrderBy(x => x.MtvId).ThenBy(x => x.Sira).ToListAsync(ct);
    }

    /// <summary>
    /// Her kısmi adım KENDİ İÇİNDE dengeli olmalı — kapanışta toplam denkleşmesi YETMEZ.
    /// Adversarial L6: denge tek başına yetmez; boş küme de (0 == 0) dengelidir ve defter YAZILMADAN
    /// bakiye düşerdi. Bu yüzden borç toplamının ÖDEME TUTARINA eşitliği de burada zorlanır —
    /// çağıran delege ile bakiye arasındaki bağ repo tarafından garanti edilir.
    /// </summary>
    private static void CheckBalanced(IReadOnlyList<AccountLedgerEntry> entries, decimal amount, string ne)
    {
        if (entries.Count == 0)
            throw new ValidationException($"{ne} ödemesi defter kaydı olmadan yazılamaz.");
        var debit = entries.Where(e => e.Direction == LedgerDirection.Debit).Sum(e => e.Amount.AmountInBase);
        var credit = entries.Where(e => e.Direction == LedgerDirection.Credit).Sum(e => e.Amount.AmountInBase);
        if (debit != credit)
            throw new ValidationException($"{ne} ödeme defteri dengesiz: borç {debit} ≠ alacak {credit}.");
        var debitNative = entries.Where(e => e.Direction == LedgerDirection.Debit).Sum(e => e.Amount.Amount);
        if (debitNative != amount)
            throw new ValidationException(
                $"{ne} ödemesi defterle uyuşmuyor: ödeme {amount} ≠ defter borcu {debitNative}.");
    }

    public async Task<InspectionRecord?> FindInspectionAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.InspectionRecords.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
    }

    public async Task<RegulasyonOdemeSonuc> PostInspectionPaymentAsync(
        Guid inspectionId,
        Func<decimal, int, (MuayeneOdeme Odeme, IReadOnlyList<AccountLedgerEntry> Entries)> posting,
        CancellationToken ct = default, Guid? operationKey = null)
    {
        return await PgRetry.RunAsync(async () =>
        {
            await using var db = await _factory.CreateDbContextAsync(ct);
            await using var tx = await db.Database.BeginTransactionAsync(ct);

            var rec = await db.InspectionRecords
                .FromSqlRaw("SELECT * FROM \"InspectionRecords\" WHERE \"Id\" = {0} FOR UPDATE", inspectionId)
                .FirstOrDefaultAsync(ct)
                ?? throw new ValidationException("Muayene kaydı bulunamadı.");
            // F1.4 — ANAHTAR ÖNCE (bkz. PostMtvOdemeAsync).
            if (operationKey is Guid key && key != Guid.Empty &&
                await db.MuayeneOdemeleri.AsNoTracking().AnyAsync(x => x.IslemAnahtari == key, ct))
                throw new DuplicateOperationException(InspectionDuplicate);
            if (rec.Odendi) throw new ValidationException("Muayene zaten ödendi.");

            var order = await db.MuayeneOdemeleri.CountAsync(x => x.InspectionId == inspectionId, ct) + 1;
            var (payment, entries) = posting(rec.Kalan, order);
            CheckBalanced(entries, payment.Tutar, "Muayene");

            // Ceza BORCU ARTIRIR: önce kalana eklenir, sonra ödenen düşülür.
            rec.Ceza = decimal.Round(rec.Ceza + payment.Ceza, 4, MidpointRounding.AwayFromZero);
            rec.Kalan = decimal.Round(rec.Kalan + payment.Ceza - payment.Tutar, 4, MidpointRounding.AwayFromZero);
            if (rec.Kalan <= 0m) { rec.Kalan = 0m; rec.Odendi = true; }
            rec.UpdatedAtUtc = DateTimeOffset.UtcNow;
            payment.KalanSonrasi = rec.Kalan;

            db.MuayeneOdemeleri.Add(payment);
            db.AccountLedgerEntries.AddRange(entries);

            try
            {
                await db.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);
            }
            catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
            {
                await tx.RollbackAsync(ct);
                throw IdempotencyConstraint.Red(ex, InspectionDuplicate);
            }

            return new RegulasyonOdemeSonuc(payment.Id, payment.Sira, payment.Tutar, rec.Kalan, rec.Odendi);
        }, ct);
    }

    public async Task<IReadOnlyList<MuayeneOdeme>> ListInspectionPaymentsAsync(Guid inspectionId, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.MuayeneOdemeleri.AsNoTracking()
            .Where(x => x.InspectionId == inspectionId).OrderBy(x => x.Sira).ToListAsync(ct);
    }

    public async Task<IReadOnlyList<MuayeneOdeme>> ListAllInspectionPaymentsAsync(CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.MuayeneOdemeleri.AsNoTracking()
            .OrderBy(x => x.InspectionId).ThenBy(x => x.Sira).ToListAsync(ct);
    }

    public async Task<InsurancePolicy?> FindInsuranceAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.InsurancePolicies.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
    }

    public async Task PostInsurancePaymentAsync(Guid policyId, decimal endorsementPremium, IReadOnlyList<AccountLedgerEntry> entries, CancellationToken ct = default)
    {
        var debit = entries.Where(e => e.Direction == LedgerDirection.Debit).Sum(e => e.Amount.AmountInBase);
        var credit = entries.Where(e => e.Direction == LedgerDirection.Credit).Sum(e => e.Amount.AmountInBase);
        if (debit != credit) throw new ValidationException($"Sigorta ödeme defteri dengesiz: borç {debit} ≠ alacak {credit}.");

        await PgRetry.RunAsync(async () => // P0-5: deadlock/serialization çakışmasında baştan dene
        {
            await using var db = await _factory.CreateDbContextAsync(ct);
            await using var tx = await db.Database.BeginTransactionAsync(ct);

            // F9.1: SATIR KİLİDİ — "zaten ödendi" çiti kilidin arkasında okunur; eşzamanlı ikinci ödeme ilkinin
            // commit'ini bekler ve Odendi=true görür (unique index yalnız son savunma).
            var rec = await db.InsurancePolicies
                .FromSqlRaw("SELECT * FROM \"InsurancePolicies\" WHERE \"Id\" = {0} FOR UPDATE", policyId)
                .FirstOrDefaultAsync(ct)
                ?? throw new ValidationException("Sigorta poliçesi bulunamadı.");
            if (rec.Odendi) throw new ValidationException("Sigorta zaten ödendi.");
            rec.Odendi = true;
            rec.ZeyilPrim = endorsementPremium;
            // FAZ-15: Kalan BİLGİ alanı; ödeme poliçenin tamamını (prim + zeyil ek prim) kapattığı
            // için 0'a düşer. Defter kaydıyla AYNI transaction'da yazılır → ekrandaki bakiye ile
            // defter arasında yarış penceresi kalmaz.
            rec.Kalan = 0m;
            rec.UpdatedAtUtc = DateTimeOffset.UtcNow;
            db.AccountLedgerEntries.AddRange(entries);

            try
            {
                await db.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);
            }
            catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
            {
                await tx.RollbackAsync(ct);
                throw new ValidationException("Sigorta zaten ödendi.");
            }
        }, ct);
    }

    // ---- FAZ-15 zeyil (poliçe eki): saf CRUD, defter YOK ----

    public async Task<IReadOnlyList<InsurancePolicyZeyil>> ListEndorsementsAsync(Guid policyId, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.InsurancePolicyZeyilleri.AsNoTracking()
            .Where(x => x.PolicyId == policyId)
            .OrderBy(x => x.Tarih).ThenBy(x => x.ZeyilNo).ToListAsync(ct);
    }

    public async Task<IReadOnlyList<InsurancePolicyZeyil>> ListAllEndorsementsAsync(CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.InsurancePolicyZeyilleri.AsNoTracking()
            .OrderBy(x => x.PolicyId).ThenBy(x => x.Tarih).ThenBy(x => x.ZeyilNo).ToListAsync(ct);
    }

    public async Task AddEndorsementAsync(InsurancePolicyZeyil endorsement, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        db.InsurancePolicyZeyilleri.Add(endorsement);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // (TenantId, PolicyId, ZeyilNo) unique — çift gönderim/mükerrer no 500 değil temiz red.
            throw new ValidationException($"Bu poliçede '{endorsement.ZeyilNo}' numaralı zeyil zaten var.");
        }
    }

    public async Task<bool> DeleteEndorsementAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        // Tenant sınırı: global query filter + RLS → başka tenant'ın satırı BULUNAMAZ (false).
        var rec = await db.InsurancePolicyZeyilleri.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (rec is null) return false;
        db.InsurancePolicyZeyilleri.Remove(rec);
        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<IReadOnlyList<VadeSource>> GetDueSourcesAsync(CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        // Tek doğruluk kaynağı (denetim O12a): bildirim job'ı da AYNI birleşimi kullanır → sessizce ayrışamaz.
        return await SharedQueries.DueSourcesAsync(db, ct);
    }
}
