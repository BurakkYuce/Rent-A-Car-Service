using Microsoft.EntityFrameworkCore;
using Npgsql;
using RentACar.Application.Notifications;
using RentACar.Domain.Entities;
using RentACar.Domain.Enums;

namespace RentACar.Infrastructure.Persistence.Repositories;

/// <summary>
/// Mesaj şablonları + giden mesaj kaydı. Tenant izolasyonu query filter + RLS ile otomatik.
///
/// <para><b>İdempotency burada:</b> <see cref="MesajEkleAsync"/> benzersiz index ihlalini (23505)
/// YUTAR ve <c>false</c> döner — "başka bir çağrı bu olayı zaten yazdı" demektir, hata değil.
/// Çağıran bunu görüp ikinci mesajı GÖNDERMEZ.</para>
/// </summary>
public sealed class MesajRepository(IDbContextFactory<AppDbContext> factory) : IMesajRepository
{
    private readonly IDbContextFactory<AppDbContext> _factory = factory;

    public async Task<IReadOnlyList<MesajSablonRow>> SablonListAsync(CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.MesajSablonlari.AsNoTracking()
            .OrderBy(x => x.Tur).ThenBy(x => x.Kanal)
            .Select(x => new MesajSablonRow(x.Id, x.Tur, x.Kanal, x.Konu, x.Govde, x.Aktif))
            .ToListAsync(ct);
    }

    public async Task<MesajSablon?> SablonBulAsync(MesajTuru tur, MesajKanal kanal, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.MesajSablonlari.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Tur == tur && x.Kanal == kanal, ct);
    }

    public async Task SablonUpsertAsync(MesajSablonInput input, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var s = await db.MesajSablonlari
            .FirstOrDefaultAsync(x => x.Tur == input.Tur && x.Kanal == input.Kanal, ct);
        var yeni = s is null;
        s ??= new MesajSablon { Tur = input.Tur, Kanal = input.Kanal };

        s.Konu = string.IsNullOrWhiteSpace(input.Konu) ? null : input.Konu.Trim();
        s.Govde = input.Govde.Trim();
        s.Aktif = input.Aktif;
        s.UpdatedAtUtc = DateTimeOffset.UtcNow;
        if (yeni) db.MesajSablonlari.Add(s);

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // Eşzamanlı ilk yazım: diğer taraf aynı (tür,kanal) satırını oluşturdu. Kaybeden taraf
            // kendi değerlerini mevcut satıra uygular — kullanıcı "kaydettim ama gitmedi" görmesin.
            db.ChangeTracker.Clear();
            var mevcut = await db.MesajSablonlari
                .FirstAsync(x => x.Tur == input.Tur && x.Kanal == input.Kanal, ct);
            mevcut.Konu = string.IsNullOrWhiteSpace(input.Konu) ? null : input.Konu.Trim();
            mevcut.Govde = input.Govde.Trim();
            mevcut.Aktif = input.Aktif;
            mevcut.UpdatedAtUtc = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(ct);
        }
    }

    public async Task<GidenMesaj?> MesajBulAsync(string anahtar, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.GidenMesajlar.AsNoTracking().FirstOrDefaultAsync(x => x.Anahtar == anahtar, ct);
    }

    public async Task<bool> MesajEkleAsync(GidenMesaj mesaj, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        db.GidenMesajlar.Add(mesaj);
        try
        {
            await db.SaveChangesAsync(ct);
            return true;
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            db.ChangeTracker.Clear();
            return false; // aynı olay zaten kayıtlı → çift mesaj YOK
        }
    }

    public async Task MesajGuncelleAsync(Guid id, Action<GidenMesaj> apply, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var m = await db.GidenMesajlar.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (m is null) return;
        apply(m);
        await db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<GidenMesajRow>> MesajListAsync(
        GidenMesajFilter? filter = null, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var q = db.GidenMesajlar.AsNoTracking().AsQueryable();

        if (filter?.Durum is { } d) q = q.Where(x => x.Durum == d);
        if (filter?.Kanal is { } k) q = q.Where(x => x.Kanal == k);
        if (!string.IsNullOrWhiteSpace(filter?.Tur)) q = q.Where(x => x.Tur == filter.Tur);

        var take = Math.Clamp(filter?.Take ?? 100, 1, 500);
        return await q.OrderByDescending(x => x.OlusturmaUtc).Take(take)
            .Select(x => new GidenMesajRow(
                x.Id, x.Tur, x.Kanal, x.Alici, x.Konu, x.Durum, x.Hata, x.DenemeSayisi,
                x.OlusturmaUtc, x.GonderimUtc, x.KaynakTur, x.KaynakId))
            .ToListAsync(ct);
    }

    /// <summary>
    /// Yeniden denenecek kayıtlar: kuyrukta, deneme hakkı dolmamış ve çok eski değil.
    /// Job doğrudan-context yolundan çağırır (repository değil) — bu yüzden statiktir.
    /// </summary>
    public static Task<List<GidenMesaj>> KuyruktakilerAsync(
        AppDbContext db, DateTimeOffset now, int maxDeneme, CancellationToken ct = default)
    {
        // 3 günden eski kuyruk kaydı yeniden DENENMEZ: geç gelen bir "yarın aracınızı teslim alın"
        // mesajı yardımcı değil zararlıdır. Kayıt kalır, operatör ekranda görür.
        var sinir = now.AddDays(-3);
        return db.GidenMesajlar
            .Where(x => x.Durum == GidenMesajDurum.Kuyrukta
                        && x.DenemeSayisi < maxDeneme
                        && x.OlusturmaUtc >= sinir)
            .OrderBy(x => x.OlusturmaUtc)
            .Take(200)
            .ToListAsync(ct);
    }
}
