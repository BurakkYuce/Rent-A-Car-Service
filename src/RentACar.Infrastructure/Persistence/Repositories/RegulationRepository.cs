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

    private const string MtvMukerrer = "Bu MTV ödemesi zaten kaydedilmiş (çift gönderim).";
    private const string MuayeneMukerrer = "Bu muayene ödemesi zaten kaydedilmiş (çift gönderim).";

    public async Task<RegulasyonOdemeSonuc> PostMtvOdemeAsync(
        Guid mtvId,
        Func<decimal, int, (MtvOdeme Odeme, IReadOnlyList<AccountLedgerEntry> Entries)> posting,
        CancellationToken ct = default, Guid? islemAnahtari = null)
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
            if (islemAnahtari is Guid anahtar && anahtar != Guid.Empty &&
                await db.MtvOdemeleri.AsNoTracking().AnyAsync(x => x.IslemAnahtari == anahtar, ct))
                throw new MukerrerIslemException(MtvMukerrer);
            if (rec.Odendi) throw new ValidationException("MTV zaten ödendi.");
            if (rec.Kalan <= 0m) throw new ValidationException("MTV kaydında ödenecek bakiye yok.");

            var sira = await db.MtvOdemeleri.CountAsync(x => x.MtvId == mtvId, ct) + 1;
            var (odeme, entries) = posting(rec.Kalan, sira);
            DengeKontrol(entries, odeme.Tutar, "MTV");

            rec.Kalan = decimal.Round(rec.Kalan - odeme.Tutar, 4, MidpointRounding.AwayFromZero);
            if (rec.Kalan <= 0m) { rec.Kalan = 0m; rec.Odendi = true; }
            rec.UpdatedAtUtc = DateTimeOffset.UtcNow;
            odeme.KalanSonrasi = rec.Kalan;

            db.MtvOdemeleri.Add(odeme);
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
                throw IdempotencyKisiti.Red(ex, MtvMukerrer);
            }

            return new RegulasyonOdemeSonuc(odeme.Id, odeme.Sira, odeme.Tutar, rec.Kalan, rec.Odendi);
        }, ct);
    }

    public async Task<IReadOnlyList<MtvOdeme>> ListMtvOdemeAsync(Guid mtvId, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.MtvOdemeleri.AsNoTracking()
            .Where(x => x.MtvId == mtvId).OrderBy(x => x.Sira).ToListAsync(ct);
    }

    public async Task<IReadOnlyList<MtvOdeme>> ListMtvOdemeHepsiAsync(CancellationToken ct = default)
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
    private static void DengeKontrol(IReadOnlyList<AccountLedgerEntry> entries, decimal tutar, string ne)
    {
        if (entries.Count == 0)
            throw new ValidationException($"{ne} ödemesi defter kaydı olmadan yazılamaz.");
        var debit = entries.Where(e => e.Direction == LedgerDirection.Debit).Sum(e => e.Amount.AmountInBase);
        var credit = entries.Where(e => e.Direction == LedgerDirection.Credit).Sum(e => e.Amount.AmountInBase);
        if (debit != credit)
            throw new ValidationException($"{ne} ödeme defteri dengesiz: borç {debit} ≠ alacak {credit}.");
        var borcNative = entries.Where(e => e.Direction == LedgerDirection.Debit).Sum(e => e.Amount.Amount);
        if (borcNative != tutar)
            throw new ValidationException(
                $"{ne} ödemesi defterle uyuşmuyor: ödeme {tutar} ≠ defter borcu {borcNative}.");
    }

    public async Task<InspectionRecord?> FindInspectionAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.InspectionRecords.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
    }

    public async Task<RegulasyonOdemeSonuc> PostMuayeneOdemeAsync(
        Guid inspectionId,
        Func<decimal, int, (MuayeneOdeme Odeme, IReadOnlyList<AccountLedgerEntry> Entries)> posting,
        CancellationToken ct = default, Guid? islemAnahtari = null)
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
            if (islemAnahtari is Guid anahtar && anahtar != Guid.Empty &&
                await db.MuayeneOdemeleri.AsNoTracking().AnyAsync(x => x.IslemAnahtari == anahtar, ct))
                throw new MukerrerIslemException(MuayeneMukerrer);
            if (rec.Odendi) throw new ValidationException("Muayene zaten ödendi.");

            var sira = await db.MuayeneOdemeleri.CountAsync(x => x.InspectionId == inspectionId, ct) + 1;
            var (odeme, entries) = posting(rec.Kalan, sira);
            DengeKontrol(entries, odeme.Tutar, "Muayene");

            // Ceza BORCU ARTIRIR: önce kalana eklenir, sonra ödenen düşülür.
            rec.Ceza = decimal.Round(rec.Ceza + odeme.Ceza, 4, MidpointRounding.AwayFromZero);
            rec.Kalan = decimal.Round(rec.Kalan + odeme.Ceza - odeme.Tutar, 4, MidpointRounding.AwayFromZero);
            if (rec.Kalan <= 0m) { rec.Kalan = 0m; rec.Odendi = true; }
            rec.UpdatedAtUtc = DateTimeOffset.UtcNow;
            odeme.KalanSonrasi = rec.Kalan;

            db.MuayeneOdemeleri.Add(odeme);
            db.AccountLedgerEntries.AddRange(entries);

            try
            {
                await db.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);
            }
            catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
            {
                await tx.RollbackAsync(ct);
                throw IdempotencyKisiti.Red(ex, MuayeneMukerrer);
            }

            return new RegulasyonOdemeSonuc(odeme.Id, odeme.Sira, odeme.Tutar, rec.Kalan, rec.Odendi);
        }, ct);
    }

    public async Task<IReadOnlyList<MuayeneOdeme>> ListMuayeneOdemeAsync(Guid inspectionId, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.MuayeneOdemeleri.AsNoTracking()
            .Where(x => x.InspectionId == inspectionId).OrderBy(x => x.Sira).ToListAsync(ct);
    }

    public async Task<IReadOnlyList<MuayeneOdeme>> ListMuayeneOdemeHepsiAsync(CancellationToken ct = default)
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

    public async Task PostSigortaOdemeAsync(Guid policyId, decimal zeyilPrim, IReadOnlyList<AccountLedgerEntry> entries, CancellationToken ct = default)
    {
        var debit = entries.Where(e => e.Direction == LedgerDirection.Debit).Sum(e => e.Amount.AmountInBase);
        var credit = entries.Where(e => e.Direction == LedgerDirection.Credit).Sum(e => e.Amount.AmountInBase);
        if (debit != credit) throw new ValidationException($"Sigorta ödeme defteri dengesiz: borç {debit} ≠ alacak {credit}.");

        await PgRetry.RunAsync(async () => // P0-5: deadlock/serialization çakışmasında baştan dene
        {
            await using var db = await _factory.CreateDbContextAsync(ct);
            await using var tx = await db.Database.BeginTransactionAsync(ct);

            var rec = await db.InsurancePolicies.FirstOrDefaultAsync(x => x.Id == policyId, ct)
                ?? throw new ValidationException("Sigorta poliçesi bulunamadı.");
            if (rec.Odendi) throw new ValidationException("Sigorta zaten ödendi.");
            rec.Odendi = true;
            rec.ZeyilPrim = zeyilPrim;
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

    public async Task<IReadOnlyList<InsurancePolicyZeyil>> ListZeyilAsync(Guid policyId, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.InsurancePolicyZeyilleri.AsNoTracking()
            .Where(x => x.PolicyId == policyId)
            .OrderBy(x => x.Tarih).ThenBy(x => x.ZeyilNo).ToListAsync(ct);
    }

    public async Task<IReadOnlyList<InsurancePolicyZeyil>> ListZeyilHepsiAsync(CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.InsurancePolicyZeyilleri.AsNoTracking()
            .OrderBy(x => x.PolicyId).ThenBy(x => x.Tarih).ThenBy(x => x.ZeyilNo).ToListAsync(ct);
    }

    public async Task AddZeyilAsync(InsurancePolicyZeyil zeyil, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        db.InsurancePolicyZeyilleri.Add(zeyil);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // (TenantId, PolicyId, ZeyilNo) unique — çift gönderim/mükerrer no 500 değil temiz red.
            throw new ValidationException($"Bu poliçede '{zeyil.ZeyilNo}' numaralı zeyil zaten var.");
        }
    }

    public async Task<bool> DeleteZeyilAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        // Tenant sınırı: global query filter + RLS → başka tenant'ın satırı BULUNAMAZ (false).
        var rec = await db.InsurancePolicyZeyilleri.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (rec is null) return false;
        db.InsurancePolicyZeyilleri.Remove(rec);
        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<IReadOnlyList<VadeSource>> GetVadeSourcesAsync(CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        // Tek doğruluk kaynağı (denetim O12a): bildirim job'ı da AYNI birleşimi kullanır → sessizce ayrışamaz.
        return await OrtakSorgular.VadeKaynaklariAsync(db, ct);
    }
}
