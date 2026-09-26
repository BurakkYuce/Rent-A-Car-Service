using Microsoft.EntityFrameworkCore;
using RentACar.Application.Baflar;
using RentACar.Domain.Entities;
using RentACar.Domain.Enums;

using RentACar.Domain.Common;

namespace RentACar.Infrastructure.Persistence.Repositories;

/// <summary>BAF kalıcılığı (roadmap L5). CreateAsync boşluksuz No (BAF-000001) tahsis eder.</summary>
public sealed class BafRepository(IDbContextFactory<AppDbContext> factory) : IBafRepository
{
    private readonly IDbContextFactory<AppDbContext> _factory = factory;

    public async Task<IReadOnlyList<Baf>> ListAsync(RentACar.Application.Authorization.BranchScope.BranchFilter scope, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.Baflar.AsNoTracking()
            // C3: Baf'ta SubeId kolonu yok → kural metin dalıyla işler (FK dalı kayıt tarafında tutamaz).
            .Where(x => scope.Unrestricted || (scope.SubeAd != null && x.Sube != null && x.Sube.Trim() == scope.SubeAd))
            .OrderByDescending(x => x.CreatedAtUtc).ToListAsync(ct);
    }

    /// <summary>
    /// FAZ-18 — filtreli liste. Şube kapsamı ListAsync ile AYNI ifadeden geçer (tek kural) ve
    /// filtreden BAĞIMSIZ uygulanır → "Ofis" filtresi kapsamı genişletemez, yalnız daraltır.
    /// </summary>
    public async Task<IReadOnlyList<Baf>> SearchAsync(
        RentACar.Application.Authorization.BranchScope.BranchFilter scope,
        RentACar.Application.Baflar.BafFilter filter, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var q = db.Baflar.AsNoTracking()
            .Where(x => scope.Unrestricted || (scope.SubeAd != null && x.Sube != null && x.Sube.Trim() == scope.SubeAd));

        if (filter.PersonelId is Guid p) q = q.Where(x => x.PersonelId == p);
        if (filter.Durum is { } d) q = q.Where(x => x.Durum == d);
        if (filter.KullanimAmaci is { } ka) q = q.Where(x => x.KullanimAmaci == ka);
        if (filter.Bas is { } start) q = q.Where(x => x.CikisTarihi >= start);
        if (filter.Bit is { } bit) q = q.Where(x => x.CikisTarihi <= bit);

        if (!string.IsNullOrWhiteSpace(filter.Ofis))
        {
            var o = filter.Ofis.Trim();
            q = q.Where(x => x.Sube != null && x.Sube == o);
        }

        if (filter.Lokasyon is { } lok)
        {
            // Şubelerden biri boşsa kayıt HİÇBİR kovaya girmez — bilinmeyeni "aynı" saymak yanlış bilgi olurdu.
            q = lok == RentACar.Application.Baflar.BafLocation.AyniOfis
                ? q.Where(x => x.Sube != null && x.DonusSube != null && x.Sube == x.DonusSube)
                : q.Where(x => x.Sube != null && x.DonusSube != null && x.Sube != x.DonusSube);
        }

        if (!string.IsNullOrWhiteSpace(filter.Plaka))
        {
            // Baf'ta plaka kolonu YOK (VehicleId var) → Vehicles alt-sorgusu. Plaka DB'de boşluksuz-büyük
            // harf saklanır → arama terimi de AYNI kuraldan geçer (tek kural, kopya yok).
            var pl = RentACar.Application.Vehicles.VehicleService.PlateKey(filter.Plaka);
            q = q.Where(x => db.Vehicles.Any(v => v.Id == x.VehicleId && EF.Functions.ILike(v.Plaka, $"%{pl}%")));
        }

        return await q.OrderByDescending(x => x.CreatedAtUtc).ToListAsync(ct);
    }

    public async Task<Baf?> FindAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.Baflar.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
    }

    public async Task CreateAsync(Baf row, CancellationToken ct = default)
    {
        await PgRetry.RunAsync(async () => // P0-5: deadlock/serialization çakışmasında baştan dene
        {
            await using var db = await _factory.CreateDbContextAsync(ct);
            await using var tx = await db.Database.BeginTransactionAsync(ct); // No tahsisi atomik (boşluksuz)
            row.No = await DocumentNoGenerator.GenerateAsync(db, db.TenantId, DocumentNoType.Baf, ct);
            db.Baflar.Add(row);
            try { await db.SaveChangesAsync(ct); }
            catch (DbUpdateException ex) when (PkViolation.Is(ex)) // F6.1b: Id = işlem anahtarı → çift gönderim
            {
                await tx.RollbackAsync(ct);
                throw new RentACar.Application.Common.DuplicateOperationException(PkViolation.Message);
            }
            await tx.CommitAsync(ct);
        }, ct);
    }

    public async Task<bool> ReceiveAsync(Guid id, int returnKm, int? returnFuel, DateTimeOffset returnDate,
        string? returnBranch, TimeOnly? returnHour, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var row = await db.Baflar.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (row is null) return false;
        row.DonusTarihi = returnDate;
        row.DonusKm = returnKm;
        row.DonusYakit = returnFuel;
        // FAZ-18: boş geçilirse MEVCUT değer korunur (kısmi güncelleme sıfırlamaya dönüşmesin).
        if (!string.IsNullOrWhiteSpace(returnBranch)) row.DonusSube = returnBranch.Trim();
        if (returnHour is { } ds) row.DonusSaat = ds;
        row.Durum = BafStatus.Kapandi;
        row.UpdatedAtUtc = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<bool> CancelAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var row = await db.Baflar.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (row is null) return false;
        row.Durum = BafStatus.Iptal;
        row.UpdatedAtUtc = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        return true;
    }

    public Task<bool> UpdateLockedAsync(Guid id, Action<Baf> apply, CancellationToken ct = default)
        => RowVersionSql.UpdateAsync(_factory, RowVersionSql.Bafs, id, expectedVersion: null,
            (db, k, c) => db.Baflar.FirstOrDefaultAsync(x => x.Id == k, c), apply, ct);
}
