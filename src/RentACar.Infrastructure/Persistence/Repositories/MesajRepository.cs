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
public sealed class MesajRepository(IDbContextFactory<AppDbContext> factory) : IMesajRepository, IMessageTemplateVersionStore
{
    private readonly IDbContextFactory<AppDbContext> _factory = factory;

    /// <summary>F11.1b — şablon sürümleri (xmin). Kimlikler önce query filter'lı EF sorgusuyla alınır (kiracı kapsamı
    /// RLS'e ek olarak uygulamada da); sürüm her satır için <see cref="SatirSurumu.OkuAsync"/> ile okunur.</summary>
    public async Task<IReadOnlyDictionary<Guid, string>> VersionsAsync(CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var ids = await db.MesajSablonlari.AsNoTracking().Select(x => x.Id).ToListAsync(ct);
        var result = new Dictionary<Guid, string>(ids.Count);
        foreach (var id in ids)
            if (await SatirSurumu.OkuAsync(db, SatirSurumu.MesajSablonlari, id, ct) is { } v)
                result[id] = v;
        return result;
    }

    /// <summary>F11.1b — kilit + sürüm karşılaştırmasıyla (tür, kanal) upsert. Eşzamanlı ilk yazımın kaybedeni 409 alır
    /// (sürümsüz yoldaki "kaybeden mevcut satıra uygular" davranışı burada YOK: bayat form sessizce ezmesin).</summary>
    public async Task UpsertAsync(MesajSablonInput input, string? expectedVersion, CancellationToken ct = default)
    {
        try
        {
            await PgRetry.RunAsync(async () =>
            {
                await using var db = await _factory.CreateDbContextAsync(ct);
                await using var tx = await db.Database.BeginTransactionAsync(ct);
                var id = await db.MesajSablonlari.AsNoTracking()
                    .Where(x => x.Tur == input.Tur && x.Kanal == input.Kanal).Select(x => (Guid?)x.Id).FirstOrDefaultAsync(ct);
                if (id is { } k)
                {
                    await SatirSurumu.KilitleAsync(db, SatirSurumu.MesajSablonlari, k, ct);
                    var current = await SatirSurumu.OkuAsync(db, SatirSurumu.MesajSablonlari, k, ct);
                    if (expectedVersion is null || !string.Equals(current, expectedVersion.Trim(), StringComparison.Ordinal))
                        throw new Application.Common.EszamanliDegisiklikException(Application.Common.EszamanliDegisiklikException.KayitMesaji);
                }
                else if (expectedVersion is not null)
                {
                    throw new Application.Common.EszamanliDegisiklikException(Application.Common.EszamanliDegisiklikException.KayitMesaji);
                }

                var s = await db.MesajSablonlari.FirstOrDefaultAsync(x => x.Tur == input.Tur && x.Kanal == input.Kanal, ct);
                var isNew = s is null;
                s ??= new MesajSablon { Tur = input.Tur, Kanal = input.Kanal };
                s.Konu = string.IsNullOrWhiteSpace(input.Konu) ? null : input.Konu.Trim();
                s.Govde = input.Govde.Trim();
                s.Aktif = input.Aktif;
                s.UpdatedAtUtc = DateTimeOffset.UtcNow;
                if (isNew) db.MesajSablonlari.Add(s);
                await db.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);
            }, ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            throw new Application.Common.EszamanliDegisiklikException(Application.Common.EszamanliDegisiklikException.KayitMesaji);
        }
    }

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
