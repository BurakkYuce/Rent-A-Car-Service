using Microsoft.EntityFrameworkCore;
using Npgsql;
using RentACar.Application.Notifications;
using RentACar.Application.TenantSettings;
using RentACar.Domain.Entities;
using RentACar.Domain.Enums;
using Ayar = RentACar.Domain.Entities.TenantSettings;

namespace RentACar.Infrastructure.Persistence;

/// <summary>
/// Scheduler'ın DOĞRUDAN-CONTEXT yolu için repository uyarlamaları.
///
/// <para><b>Neden gerekli:</b> arka plan işleri DI scope'u olmadan, tenant'a elle kapsanmış tek bir
/// <see cref="AppDbContext"/> ile çalışır (bkz. <c>VadeBildirimJob</c>). Uygulama servisleri ise
/// <c>IDbContextFactory</c> üzerinden KENDİ context'ini açan repository'lere bağlıdır — job içinde
/// o factory doğru tenant'a kapsanmış olmaz. Bu iki uyarlama, aynı repository sözleşmesini job'ın
/// elindeki context üzerinden karşılar; böylece <see cref="MusteriBildirimService"/>'in mantığı
/// (şablon çözümü, idempotency, deneme sayacı, izin kuralı) job yolunda DA aynen kullanılır —
/// kopyalanmaz.</para>
/// </summary>
public sealed class DogrudanMesajRepository(AppDbContext db, Guid tenantId) : IMesajRepository
{
    public Task<IReadOnlyList<MesajSablonRow>> SablonListAsync(CancellationToken ct = default)
        => throw new NotSupportedException("Şablon listesi job yolunda kullanılmaz.");

    public Task<MesajSablon?> SablonBulAsync(MesajTuru tur, MesajKanal kanal, CancellationToken ct = default)
        => db.MesajSablonlari.AsNoTracking().FirstOrDefaultAsync(x => x.Tur == tur && x.Kanal == kanal, ct);

    public Task SablonUpsertAsync(MesajSablonInput input, CancellationToken ct = default)
        => throw new NotSupportedException("Şablon yazımı job yolunda kullanılmaz.");

    public Task<GidenMesaj?> MesajBulAsync(string anahtar, CancellationToken ct = default)
        => db.GidenMesajlar.AsNoTracking().FirstOrDefaultAsync(x => x.Anahtar == anahtar, ct);

    public async Task<bool> MesajEkleAsync(GidenMesaj mesaj, CancellationToken ct = default)
    {
        mesaj.TenantId = tenantId; // interceptor'sız job yolu → açık damga (VadeBildirimUretici deseni)
        db.GidenMesajlar.Add(mesaj);
        try
        {
            await db.SaveChangesAsync(ct);
            return true;
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // Başka bir replika/koşu aynı olayı yazdı → idempotent no-op. Batch geri alındığı için
            // izleyici temizlenir, aksi halde sonraki SaveChanges aynı hatayı tekrarlardı.
            db.ChangeTracker.Clear();
            return false;
        }
    }

    public async Task MesajGuncelleAsync(Guid id, Action<GidenMesaj> apply, CancellationToken ct = default)
    {
        var m = await db.GidenMesajlar.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (m is null) return;
        apply(m);
        await db.SaveChangesAsync(ct);
    }

    public Task<IReadOnlyList<GidenMesajRow>> MesajListAsync(
        GidenMesajFilter? filter = null, CancellationToken ct = default)
        => throw new NotSupportedException("Giden mesaj listesi job yolunda kullanılmaz.");
}

/// <summary>Job yolunda tenant ayarını elindeki context'ten okuyan uyarlama (yalnız okuma).</summary>
public sealed class DogrudanAyarRepository(AppDbContext db) : ITenantSettingsRepository
{
    public Task<Ayar?> GetAsync(CancellationToken ct = default)
        => db.TenantSettings.AsNoTracking().FirstOrDefaultAsync(ct);

    public Task UpsertAsync(Action<Ayar> apply, CancellationToken ct = default)
        => throw new NotSupportedException("Ayar yazımı job yolunda kullanılmaz.");

    public Task<IReadOnlyList<WhatsAppGonderim>> ListWhatsAppGonderimAsync(int take = 7, CancellationToken ct = default)
        => throw new NotSupportedException("WhatsApp gönderim listesi job yolunda kullanılmaz.");
}
