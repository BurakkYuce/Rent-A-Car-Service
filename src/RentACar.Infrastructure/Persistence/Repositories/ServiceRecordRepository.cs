using Microsoft.EntityFrameworkCore;
using Npgsql;
using RentACar.Application.Common;
using RentACar.Application.ServiceRecords;
using RentACar.Domain.Entities;
using RentACar.Domain.Enums;

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
        await using var db = await _factory.CreateDbContextAsync(ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var n = await SequenceAllocator.NextAsync(db, db.TenantId, "ServiceNo", ct);
        record.No = $"SRV-{n:D6}";
        foreach (var l in record.Lines) l.ServiceRecordId = record.Id;
        record.ToplamIscilik = record.Lines.Sum(l => l.Tutar);
        db.ServiceRecords.Add(record);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }

    public async Task<bool> TransitionAsync(
        Guid id, Action<ServiceRecord> apply,
        VehicleStatus? setVehicleTo, VehicleStatus? onlyWhenVehicleIs, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        var rec = await db.ServiceRecords.FirstOrDefaultAsync(r => r.Id == id, ct);
        if (rec is null) return false;
        apply(rec);
        rec.UpdatedAtUtc = DateTimeOffset.UtcNow;

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
    }

    public async Task<bool> AddLineAsync(Guid id, string aciklama, decimal tutar, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var rec = await db.ServiceRecords.Include(r => r.Lines).FirstOrDefaultAsync(r => r.Id == id, ct);
        if (rec is null) return false;
        if (rec.Durum is ServisDurum.Tamamlandi or ServisDurum.Iptal)
            throw new ValidationException("Kapanmış servise kalem eklenemez.");

        rec.Lines.Add(new ServiceLine { ServiceRecordId = rec.Id, Aciklama = aciklama, Tutar = tutar });
        rec.ToplamIscilik = rec.Lines.Sum(l => l.Tutar);
        rec.UpdatedAtUtc = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task PostYansitmaAsync(Guid serviceId, Guid cariId, decimal yansitilanTutar,
        IReadOnlyList<AccountLedgerEntry> entries, CancellationToken ct = default)
    {
        var debit = entries.Where(e => e.Direction == LedgerDirection.Debit).Sum(e => e.Amount.AmountInBase);
        var credit = entries.Where(e => e.Direction == LedgerDirection.Credit).Sum(e => e.Amount.AmountInBase);
        if (debit != credit) throw new ValidationException($"Servis yansıtma defteri dengesiz: borç {debit} ≠ alacak {credit}.");

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
    }
}
