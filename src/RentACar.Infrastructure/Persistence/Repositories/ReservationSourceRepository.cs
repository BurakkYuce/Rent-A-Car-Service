using Microsoft.EntityFrameworkCore;
using RentACar.Application.ReservationSources;
using RentACar.Domain.Entities;

namespace RentACar.Infrastructure.Persistence.Repositories;

/// <summary>Rezervasyon kaynağı repo — gövdenin çoğu generic <see cref="MasterDefinitionRepository{T}"/>'den (O12d);
/// burada yalnız FAZ-24 toplu oran yansıtması var.</summary>
public sealed class ReservationSourceRepository(IDbContextFactory<AppDbContext> factory)
    : MasterDefinitionRepository<ReservationSource>(factory, "rezervasyon kaynağı"), IReservationSourceRepository
{
    private readonly IDbContextFactory<AppDbContext> _factory = factory;

    public async Task<int> ReflectRatesAsync(Guid excludedId, decimal? rentalRate, decimal? serviceRate,
        decimal? dropRate, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);

        // ExecuteUpdateAsync BİLİNÇLİ OLARAK kullanılmıyor: denetim kaydı SaveChanges
        // interceptor'ında üretiliyor (AuditSaveChangesInterceptor). Toplu oran değişikliği
        // izsiz kalmamalı — tablo master sözlük boyutunda, izlenen entity maliyeti önemsiz.
        var targets = await db.ReservationSources
            .Where(x => x.Aktif && x.Id != excludedId).ToListAsync(ct);

        foreach (var h in targets)
        {
            h.KiraOrani = rentalRate;
            h.HizmetOrani = serviceRate;
            h.DropOrani = dropRate;
            h.UpdatedAtUtc = DateTimeOffset.UtcNow;
        }

        await db.SaveChangesAsync(ct);   // tek transaction: yarım yansıtma kalmaz
        return targets.Count;
    }
}
