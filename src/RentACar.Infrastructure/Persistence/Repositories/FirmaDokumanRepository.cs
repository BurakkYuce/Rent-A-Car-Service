using Microsoft.EntityFrameworkCore;
using Npgsql;
using RentACar.Application.Common;
using RentACar.Application.FirmaDokumanlar;
using RentACar.Domain.Entities;

namespace RentACar.Infrastructure.Persistence.Repositories;

/// <summary>
/// <see cref="IFirmaDokumanRepository"/> uygulaması. Tenant izolasyonu iki katmanlı ve ikisi de
/// OTOMATİK: EF global query filter (merkezi <c>OnModelCreating</c> döngüsü) + Postgres RLS.
/// Bu sınıfta elle TenantId yüklemi YOKTUR ve olmamalıdır — olsaydı "unutulabilir" bir kural olurdu.
/// </summary>
public sealed class FirmaDokumanRepository(IDbContextFactory<AppDbContext> factory) : IFirmaDokumanRepository
{
    private readonly IDbContextFactory<AppDbContext> _factory = factory;

    // Projeksiyon TEK yerde — `Bytes` kolonunun kazara SELECT'e sızmasını yapısal olarak engeller.
    private static readonly System.Linq.Expressions.Expression<Func<FirmaDokuman, FirmaDokumanSatiri>> ToSatir =
        d => new FirmaDokumanSatiri(d.Id, d.Baslik, d.Aciklama, d.DosyaAdi, d.Boyut, d.Sira,
            d.YukleyenKullanici, d.CreatedAtUtc);

    public async Task<IReadOnlyList<FirmaDokumanSatiri>> ListeleAsync(CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.FirmaDokumanlari.AsNoTracking()
            .OrderBy(d => d.Sira)
            .Select(ToSatir).ToListAsync(ct);
    }

    public async Task<int> SayAsync(CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.FirmaDokumanlari.AsNoTracking().CountAsync(ct);
    }

    public async Task<Guid> EkleAsync(FirmaDokuman dokuman, int maxYuva, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);

        // Dolu yuvalar (bytea okumadan) → ilk BOŞ yuva. Silinen belgenin yuvası yeniden kullanılır,
        // aksi halde 10 kez yükle-sil yapan firma bir daha belge ekleyemezdi.
        var dolu = await db.FirmaDokumanlari.AsNoTracking().Select(d => d.Sira).ToListAsync(ct);
        var yuva = Enumerable.Range(1, maxYuva).Except(dolu).FirstOrDefault();
        if (yuva == 0)
            throw new ValidationException(
                $"En fazla {maxYuva} doküman saklanabilir. Yeni belge yüklemek için mevcutlardan birini silin.");

        dokuman.Sira = yuva;
        db.FirmaDokumanlari.Add(dokuman);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation or PostgresErrorCodes.CheckViolation
        })
        {
            // Yarış: iki eşzamanlı yükleme aynı boş yuvayı seçti (unique index) ya da yuva aralığı
            // aşıldı (CHECK). İkisi de "sınır dayatıldı" demektir — 11. satır DB'ye yazılamaz.
            throw new ValidationException("Doküman kaydedilemedi — aynı anda başka bir yükleme yapıldı, tekrar deneyin.");
        }
        return dokuman.Id;
    }

    public async Task<bool> SilAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var dokuman = await db.FirmaDokumanlari.FirstOrDefaultAsync(d => d.Id == id, ct);
        if (dokuman is null) return false; // başka tenant'ın belgesi de buraya düşer (query filter)
        db.FirmaDokumanlari.Remove(dokuman);
        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<FirmaDokumanIcerik?> IndirAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.FirmaDokumanlari.AsNoTracking()
            .Where(d => d.Id == id)
            .Select(d => new FirmaDokumanIcerik(d.Bytes, d.ContentType, d.DosyaAdi, d.UpdatedAtUtc ?? d.CreatedAtUtc))
            .FirstOrDefaultAsync(ct);
    }
}
