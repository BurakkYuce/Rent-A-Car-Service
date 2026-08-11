using Microsoft.EntityFrameworkCore;
using RentACar.Application.Baflar;
using RentACar.Domain.Entities;
using RentACar.Domain.Enums;

namespace RentACar.Infrastructure.Persistence.Repositories;

/// <summary>BAF kalıcılığı (roadmap L5). CreateAsync boşluksuz No (BAF-000001) tahsis eder.</summary>
public sealed class BafRepository(IDbContextFactory<AppDbContext> factory) : IBafRepository
{
    private readonly IDbContextFactory<AppDbContext> _factory = factory;

    public async Task<IReadOnlyList<Baf>> ListAsync(RentACar.Application.Authorization.BranchScope.BranchFilter kapsam, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.Baflar.AsNoTracking()
            // C3: Baf'ta SubeId kolonu yok → kural metin dalıyla işler (FK dalı kayıt tarafında tutamaz).
            .Where(x => kapsam.Unrestricted || (kapsam.SubeAd != null && x.Sube != null && x.Sube.Trim() == kapsam.SubeAd))
            .OrderByDescending(x => x.CreatedAtUtc).ToListAsync(ct);
    }

    /// <summary>
    /// FAZ-18 — filtreli liste. Şube kapsamı ListAsync ile AYNI ifadeden geçer (tek kural) ve
    /// filtreden BAĞIMSIZ uygulanır → "Ofis" filtresi kapsamı genişletemez, yalnız daraltır.
    /// </summary>
    public async Task<IReadOnlyList<Baf>> SearchAsync(
        RentACar.Application.Authorization.BranchScope.BranchFilter kapsam,
        RentACar.Application.Baflar.BafFilter filtre, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var q = db.Baflar.AsNoTracking()
            .Where(x => kapsam.Unrestricted || (kapsam.SubeAd != null && x.Sube != null && x.Sube.Trim() == kapsam.SubeAd));

        if (filtre.PersonelId is Guid p) q = q.Where(x => x.PersonelId == p);
        if (filtre.Durum is { } d) q = q.Where(x => x.Durum == d);
        if (filtre.KullanimAmaci is { } ka) q = q.Where(x => x.KullanimAmaci == ka);
        if (filtre.Bas is { } bas) q = q.Where(x => x.CikisTarihi >= bas);
        if (filtre.Bit is { } bit) q = q.Where(x => x.CikisTarihi <= bit);

        if (!string.IsNullOrWhiteSpace(filtre.Ofis))
        {
            var o = filtre.Ofis.Trim();
            q = q.Where(x => x.Sube != null && x.Sube == o);
        }

        if (filtre.Lokasyon is { } lok)
        {
            // Şubelerden biri boşsa kayıt HİÇBİR kovaya girmez — bilinmeyeni "aynı" saymak yanlış bilgi olurdu.
            q = lok == RentACar.Application.Baflar.BafLokasyon.AyniOfis
                ? q.Where(x => x.Sube != null && x.DonusSube != null && x.Sube == x.DonusSube)
                : q.Where(x => x.Sube != null && x.DonusSube != null && x.Sube != x.DonusSube);
        }

        if (!string.IsNullOrWhiteSpace(filtre.Plaka))
        {
            // Baf'ta plaka kolonu YOK (VehicleId var) → Vehicles alt-sorgusu. Plaka DB'de boşluksuz-büyük
            // harf saklanır → arama terimi de AYNI kuraldan geçer (tek kural, kopya yok).
            var pl = RentACar.Application.Vehicles.VehicleService.PlakaAnahtar(filtre.Plaka);
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
            var n = await SequenceAllocator.NextAsync(db, db.TenantId, "BafNo", ct);
            row.No = $"BAF-{n:D6}";
            db.Baflar.Add(row);
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }, ct);
    }

    public async Task<bool> TeslimAlAsync(Guid id, int donusKm, int? donusYakit, DateTimeOffset donusTarihi,
        string? donusSube, TimeOnly? donusSaat, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var row = await db.Baflar.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (row is null) return false;
        row.DonusTarihi = donusTarihi;
        row.DonusKm = donusKm;
        row.DonusYakit = donusYakit;
        // FAZ-18: boş geçilirse MEVCUT değer korunur (kısmi güncelleme sıfırlamaya dönüşmesin).
        if (!string.IsNullOrWhiteSpace(donusSube)) row.DonusSube = donusSube.Trim();
        if (donusSaat is { } ds) row.DonusSaat = ds;
        row.Durum = BafDurum.Kapandi;
        row.UpdatedAtUtc = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<bool> IptalAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var row = await db.Baflar.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (row is null) return false;
        row.Durum = BafDurum.Iptal;
        row.UpdatedAtUtc = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        return true;
    }
}
