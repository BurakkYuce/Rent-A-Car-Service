using Microsoft.EntityFrameworkCore;
using Npgsql;
using RentACar.Application.Common;
using RentACar.Application.ServiceRecords;
using RentACar.Domain.Entities;
using RentACar.Domain.Enums;

using RentACar.Domain.Common;

namespace RentACar.Infrastructure.Persistence.Repositories;

/// <summary>
/// Servis kaydı kalıcılığı. Create: boşluksuz no + kalemler. Transition: durum + araç durumu
/// kuplajı TEK transaction. AddLine: kalem + ToplamIscilik yeniden hesabı.
/// </summary>
public sealed class ServiceRecordRepository(IDbContextFactory<AppDbContext> factory) : IServiceRecordRepository
{
    private readonly IDbContextFactory<AppDbContext> _factory = factory;

    public async Task<IReadOnlyList<ServiceRecord>> ListAsync(CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.ServiceRecords.AsNoTracking().Include(r => r.Lines)
            .OrderByDescending(r => r.GirisTarihi).ToListAsync(ct);
    }

    public async Task<ServiceRecord?> FindAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.ServiceRecords.AsNoTracking().Include(r => r.Lines)
            .FirstOrDefaultAsync(r => r.Id == id, ct);
    }

    public async Task CreateAsync(ServiceRecord record, CancellationToken ct = default)
    {
        await PgRetry.RunAsync(async () => // P0-5: deadlock/serialization çakışmasında baştan dene
        {
            await using var db = await _factory.CreateDbContextAsync(ct);
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            record.No = await BelgeNoUretici.UretAsync(db, db.TenantId, BelgeNoTuru.ServisKaydi, ct);
            foreach (var l in record.Lines) l.ServiceRecordId = record.Id;
            record.ToplamIscilik = record.Lines.Sum(l => l.Tutar);
            db.ServiceRecords.Add(record);
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }, ct);
    }

    public async Task<bool> TransitionAsync(
        Guid id, Action<ServiceRecord> apply,
        VehicleStatus? setVehicleTo, VehicleStatus? onlyWhenVehicleIs,
        Func<ServiceRecord, VehicleKmLog>? kmLog = null, CancellationToken ct = default)
    {
        return await PgRetry.RunAsync(async () => // P0-5: deadlock/serialization çakışmasında baştan dene
        {
            await using var db = await _factory.CreateDbContextAsync(ct);
            await using var tx = await db.Database.BeginTransactionAsync(ct);

            var rec = await db.ServiceRecords.FirstOrDefaultAsync(r => r.Id == id, ct);
            if (rec is null) return false;
            apply(rec);
            rec.UpdatedAtUtc = DateTimeOffset.UtcNow;

            // FAZ 2.5: km zaman-serisi — servis geçişiyle AYNI transaction (tamamlamada çıkış km).
            if (kmLog is not null) db.KmLoglari.Add(kmLog(rec));

            if (setVehicleTo is VehicleStatus vs)
            {
                var vehicle = await db.Vehicles.FirstOrDefaultAsync(v => v.Id == rec.VehicleId, ct);
                if (vehicle is not null && (onlyWhenVehicleIs is null || vehicle.Durum == onlyWhenVehicleIs))
                {
                    vehicle.Durum = vs;
                    vehicle.UpdatedAtUtc = DateTimeOffset.UtcNow;
                }
            }

            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return true;
        }, ct);
    }

    public Task<bool> UpdateBilgiAsync(Guid id, Action<ServiceRecord> apply, CancellationToken ct = default)
        // Durum geçişi yok, araç kuplajı yok, km log yok — yalnız alan güncellemesi (FAZ-16 bilgi blokları).
        => TransitionAsync(id, apply, setVehicleTo: null, onlyWhenVehicleIs: null, ct: ct);

    public async Task<bool> AddLineAsync(Guid id, ServiceLine kalem, CancellationToken ct = default)
    {
        return await PgRetry.RunAsync(async () => // P0-5 deadlock retry + kayıp-güncelleme koruması
        {
            await using var db = await _factory.CreateDbContextAsync(ct);
            await using var tx = await db.Database.BeginTransactionAsync(ct);

            // Satır kilidi: eşzamanlı kalem eklemeleri serileşir → ToplamIscilik kayıp-güncellemesi
            // OLMAZ. (Kilitsiz eski hâlde her çağrı kendi tracked koleksiyonundan toplardı → biri
            // kaybolurdu; bu toplam rücu/yansıtma (KusurOrani × ToplamIscilik) ile deftere gidiyor
            // → PARA etkisi.)
            var rec = await db.ServiceRecords
                .FromSqlRaw("SELECT * FROM \"ServiceRecords\" WHERE \"Id\" = {0} FOR UPDATE", id)
                .FirstOrDefaultAsync(ct);
            if (rec is null) return false;
            if (rec.Durum is ServisDurum.Tamamlandi or ServisDurum.Iptal)
                throw new ValidationException("Kapanmış servise kalem eklenemez.");

            kalem.ServiceRecordId = rec.Id;
            db.Set<ServiceLine>().Add(kalem);
            // Toplamı DB'den (kilit altında) yeniden hesapla + bu çağrının yeni kalemi. Eşzamanlı
            // çağrı bu commit'i beklediğinden onun kalemi mevcutToplam'a dahil olur.
            var mevcutToplam = await db.Set<ServiceLine>().Where(l => l.ServiceRecordId == id).SumAsync(l => l.Tutar, ct);
            rec.ToplamIscilik = mevcutToplam + kalem.Tutar;
            rec.UpdatedAtUtc = DateTimeOffset.UtcNow;

            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return true;
        }, ct);
    }

    public async Task PostYansitmaAsync(Guid serviceId, Guid cariId, decimal yansitilanTutar,
        IReadOnlyList<AccountLedgerEntry> entries, CancellationToken ct = default)
    {
        var debit = entries.Where(e => e.Direction == LedgerDirection.Debit).Sum(e => e.Amount.AmountInBase);
        var credit = entries.Where(e => e.Direction == LedgerDirection.Credit).Sum(e => e.Amount.AmountInBase);
        if (debit != credit) throw new ValidationException($"Servis yansıtma defteri dengesiz: borç {debit} ≠ alacak {credit}.");

        await PgRetry.RunAsync(async () => // P0-5: deadlock/serialization çakışmasında baştan dene
        {
            await using var db = await _factory.CreateDbContextAsync(ct);
            await using var tx = await db.Database.BeginTransactionAsync(ct);

            var rec = await db.ServiceRecords.FirstOrDefaultAsync(r => r.Id == serviceId, ct)
                ?? throw new ValidationException("Servis kaydı bulunamadı.");
            if (rec.Yansitildi) throw new ValidationException("Servis maliyeti zaten yansıtıldı.");
            rec.Yansitildi = true;
            rec.YansitilanTutar = yansitilanTutar;
            rec.YansitilanCariId = cariId;
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
                throw new ValidationException("Servis maliyeti zaten yansıtıldı.");
            }
        }, ct);
    }
}
