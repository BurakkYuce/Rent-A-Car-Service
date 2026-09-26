using Microsoft.EntityFrameworkCore;
using Npgsql;
using RentACar.Application.AracSiparisleri;
using RentACar.Application.Common;
using RentACar.Domain.Entities;
using RentACar.Domain.Enums;

using RentACar.Domain.Common;

namespace RentACar.Infrastructure.Persistence.Repositories;

/// <summary>Araç sipariş kalıcılığı (roadmap L3). CreateAsync boşluksuz No (SP-000001) tahsis eder.</summary>
public sealed class AracSiparisRepository(IDbContextFactory<AppDbContext> factory) : IVehicleOrderRepository
{
    private readonly IDbContextFactory<AppDbContext> _factory = factory;

    public async Task<IReadOnlyList<AracSiparis>> ListAsync(CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.AracSiparisleri.AsNoTracking().OrderByDescending(x => x.CreatedAtUtc).ToListAsync(ct);
    }

    /// <summary>FAZ-17 — filtreli liste. Sıralama <see cref="ListAsync"/> ile AYNI (en yeni üstte)
    /// ki filtre açıp kapatmak satır sırasını değiştirmesin.</summary>
    public async Task<IReadOnlyList<AracSiparis>> SearchAsync(AracSiparisFilter filtre, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var q = db.AracSiparisleri.AsNoTracking();

        if (filtre.CariId is Guid c) q = q.Where(x => x.TedarikciCariId == c);
        if (filtre.Durum is { } d) q = q.Where(x => x.Durum == d);
        if (filtre.Bas is { } bas) q = q.Where(x => x.SiparisTarihi >= bas);
        if (filtre.Bit is { } bit) q = q.Where(x => x.SiparisTarihi <= bit);

        if (!string.IsNullOrWhiteSpace(filtre.DosyaNo))
        {
            var dn = filtre.DosyaNo.Trim();
            q = q.Where(x => x.DosyaNo != null && EF.Functions.ILike(x.DosyaNo, $"%{dn}%"));
        }

        if (!string.IsNullOrWhiteSpace(filtre.Ara))
        {
            // Ad-Soyad araması İKİ yolu birden tarar: serbest metin tedarikçi alanı VE bağlı carinin
            // adı/soyadı/unvanı (cari bağı olmayan eski kayıtlar da bulunabilsin). Cari adı sipariş
            // satırına SNAPSHOT'lanmaz — cari yeniden adlandırılırsa arama doğru kalır.
            var a = filtre.Ara.Trim();
            q = q.Where(x => EF.Functions.ILike(x.Tedarikci, $"%{a}%")
                || (x.TedarikciCariId != null && db.Customers.Any(cu => cu.Id == x.TedarikciCariId
                    && ((cu.Ad != null && EF.Functions.ILike(cu.Ad, $"%{a}%"))
                        || (cu.Soyad != null && EF.Functions.ILike(cu.Soyad, $"%{a}%"))
                        || (cu.Unvan != null && EF.Functions.ILike(cu.Unvan, $"%{a}%"))))));
        }

        if (!string.IsNullOrWhiteSpace(filtre.Arac))
        {
            // Siparişte filo plakası YOK (araç teslimde doğar) → TSB/geçici plaka kaydı + araç
            // tanımı metinleri taranır (bkz. AracSiparisFilter.Arac notu).
            var p = filtre.Arac.Trim();
            q = q.Where(x => (x.TsbKayitNo != null && EF.Functions.ILike(x.TsbKayitNo, $"%{p}%"))
                || (x.Marka != null && EF.Functions.ILike(x.Marka, $"%{p}%"))
                || (x.Tip != null && EF.Functions.ILike(x.Tip, $"%{p}%"))
                || (x.Grup != null && EF.Functions.ILike(x.Grup, $"%{p}%"))
                || (x.Versiyon != null && EF.Functions.ILike(x.Versiyon, $"%{p}%")));
        }

        return await q.OrderByDescending(x => x.CreatedAtUtc).ToListAsync(ct);
    }

    public async Task<AracSiparis?> FindAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.AracSiparisleri.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
    }

    public async Task CreateAsync(AracSiparis row, CancellationToken ct = default)
    {
        await PgRetry.RunAsync(async () => // P0-5: deadlock/serialization çakışmasında baştan dene
        {
            await using var db = await _factory.CreateDbContextAsync(ct);
            await using var tx = await db.Database.BeginTransactionAsync(ct); // No tahsisi atomik (boşluksuz)
            row.No = await BelgeNoUretici.UretAsync(db, db.TenantId, DocumentNoType.AracSiparis, ct);
            db.AracSiparisleri.Add(row);
            try
            {
                await db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException ex) when (FkIhlali(ex))
            {
                // FAZ-17: silinmiş/başka tenant'ın carisi ya da kredisi seçilirse composite FK ihlali
                // gelir; uç yalnız ValidationException yakalıyor → aksi halde kullanıcı 500 görürdü.
                await tx.RollbackAsync(ct);
                throw new ValidationException(SecimBulunamadi);
            }
            catch (DbUpdateException ex) when (PkIhlali.Mi(ex)) // F6.1b: Id = işlem anahtarı → çift gönderim
            {
                await tx.RollbackAsync(ct);
                throw new DuplicateOperationException(PkIhlali.Mesaj);
            }
            await tx.CommitAsync(ct);
        }, ct);
    }

    public async Task<bool> UpdateAsync(Guid id, Action<AracSiparis> apply, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var row = await db.AracSiparisleri.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (row is null) return false;

        apply(row);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (FkIhlali(ex))
        {
            throw new ValidationException(SecimBulunamadi);
        }
        return true;
    }

    public async Task<bool> SetStatusAsync(Guid id, OrderStatus durum, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var row = await db.AracSiparisleri.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (row is null) return false;
        row.Durum = durum;
        row.UpdatedAtUtc = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<bool> UpdateLockedAsync(Guid id, string? beklenenSurum, Action<AracSiparis> apply,
        CancellationToken ct = default)
    {
        try
        {
            return await SatirSurumu.GuncelleAsync(_factory, SatirSurumu.AracSiparisleri, id, beklenenSurum,
                (db, k, c) => db.AracSiparisleri.FirstOrDefaultAsync(x => x.Id == k, c), apply, ct);
        }
        catch (DbUpdateException ex) when (FkIhlali(ex))
        {
            throw new ValidationException(SecimBulunamadi);
        }
    }

    public async Task<string?> VersionAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await SatirSurumu.OkuAsync(db, SatirSurumu.AracSiparisleri, id, ct);
    }

    private const string SecimBulunamadi =
        "Seçilen cari ya da kredi bulunamadı (silinmiş olabilir); listeyi yenileyip tekrar deneyin.";

    private static bool FkIhlali(DbUpdateException ex)
        => ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.ForeignKeyViolation };
}
