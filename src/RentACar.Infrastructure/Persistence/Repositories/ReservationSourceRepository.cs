using Microsoft.EntityFrameworkCore;
using RentACar.Application.ReservationSources;
using RentACar.Domain.Entities;

namespace RentACar.Infrastructure.Persistence.Repositories;

/// <summary>Rezervasyon kaynağı repo — gövdenin çoğu generic <see cref="MasterTanimRepository{T}"/>'den (O12d);
/// burada yalnız FAZ-24 toplu oran yansıtması var.</summary>
public sealed class ReservationSourceRepository(IDbContextFactory<AppDbContext> factory)
    : MasterTanimRepository<ReservationSource>(factory, "rezervasyon kaynağı"), IReservationSourceRepository
{
    private readonly IDbContextFactory<AppDbContext> _factory = factory;

    public async Task<int> ReflectRatesAsync(Guid haricId, decimal? kiraOrani, decimal? hizmetOrani,
        decimal? dropOrani, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);

        // ExecuteUpdateAsync BİLİNÇLİ OLARAK kullanılmıyor: denetim kaydı SaveChanges
        // interceptor'ında üretiliyor (AuditSaveChangesInterceptor). Toplu oran değişikliği
        // izsiz kalmamalı — tablo master sözlük boyutunda, izlenen entity maliyeti önemsiz.
        var hedefler = await db.ReservationSources
            .Where(x => x.Aktif && x.Id != haricId).ToListAsync(ct);

        foreach (var h in hedefler)
        {
            h.KiraOrani = kiraOrani;
            h.HizmetOrani = hizmetOrani;
            h.DropOrani = dropOrani;
            h.UpdatedAtUtc = DateTimeOffset.UtcNow;
        }

        await db.SaveChangesAsync(ct);   // tek transaction: yarım yansıtma kalmaz
        return hedefler.Count;
    }
}
