using Microsoft.EntityFrameworkCore;
using RentACar.Application.PublicSite;
using RentACar.Domain.Entities;

namespace RentACar.Infrastructure.Persistence.Repositories;

/// <summary>PR-8: IPublicBookingRequestRepository implementasyonu.</summary>
public sealed class PublicBookingRequestRepository(IDbContextFactory<AppDbContext> factory) : IPublicBookingRequestRepository
{
    private readonly IDbContextFactory<AppDbContext> _factory = factory;

    public async Task AddAsync(PublicBookingRequest request, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        db.SiteTalepleri.Add(request);
        await db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<PublicBookingRequest>> ListAsync(CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.SiteTalepleri.AsNoTracking()
            .OrderBy(t => t.Durum)                 // Yeni(0) önce — çalışma kuyruğu
            .ThenByDescending(t => t.CreatedAtUtc)
            .ToListAsync(ct);
    }

    public async Task<PublicBookingRequest?> FindAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.SiteTalepleri.AsNoTracking().FirstOrDefaultAsync(t => t.Id == id, ct);
    }

    public async Task<bool> TryClaimAsync(Guid id, PublicBookingRequestDurum target, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        // TEK atomik UPDATE — yarışta tek kazanan bırakır (oku-kontrol-yaz DEĞİL).
        // PR-17: koşul artık "Durum == Yeni" DEĞİL "terminal değil". Ara durumlar eklenince eski
        // yüklem, personelin "İletişimde" işaretlediği talebi dönüştürülemez hale getiriyordu.
        // `TalepDurumu.Terminal` bir C# metodu ve LINQ'e çevrilemez → yüklem aktif durumların
        // AÇIK listesi olarak yazıldı; testler ikisinin örtüştüğünü doğruluyor.
        var affected = await db.SiteTalepleri
            .Where(t => t.Id == id && (t.Durum == PublicBookingRequestDurum.Yeni
                                    || t.Durum == PublicBookingRequestDurum.Iletisimde
                                    || t.Durum == PublicBookingRequestDurum.TeklifVerildi))
            .ExecuteUpdateAsync(s => s
                .SetProperty(t => t.Durum, target)
                .SetProperty(t => t.UpdatedAtUtc, DateTimeOffset.UtcNow), ct);
        return affected == 1;
    }

    public async Task SetConvertedReservationAsync(Guid id, Guid reservationId, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        await db.SiteTalepleri.Where(t => t.Id == id)
            .ExecuteUpdateAsync(s => s
                .SetProperty(t => t.DonusenReservationId, reservationId)
                .SetProperty(t => t.UpdatedAtUtc, DateTimeOffset.UtcNow), ct);
    }

    public async Task ReleaseClaimAsync(Guid id, PublicBookingRequestDurum previousStatus, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        // PR-17: koşulsuz `Yeni` yazmak, personelin "İletişimde/Teklif verildi" ilerlemesini
        // SESSİZCE silerdi → claim ÖNCESİ durum geri yazılıyor.
        await db.SiteTalepleri.Where(t => t.Id == id)
            .ExecuteUpdateAsync(s => s
                .SetProperty(t => t.Durum, previousStatus)
                .SetProperty(t => t.DonusenReservationId, (Guid?)null)
                .SetProperty(t => t.UpdatedAtUtc, DateTimeOffset.UtcNow), ct);
    }

    // ---- PR-17: filtre/sayfalama, sayaçlar, durum/atama/not ----

    public async Task<(IReadOnlyList<PublicBookingRequest> Satirlar, int Toplam)> PagedAsync(
        PublicBookingRequestDurum? status, string? search, int page, int size, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var q = db.SiteTalepleri.AsNoTracking().AsQueryable();

        if (status is { } d) q = q.Where(t => t.Durum == d);
        if (!string.IsNullOrWhiteSpace(search))
        {
            // Ad ve telefonda arama. Telefon normalizasyonu DB'de ifade edilemediği için ham
            // `Contains` — personel numarayı listede göründüğü gibi yazıyor.
            var k = search.Trim();
            q = q.Where(t => EF.Functions.ILike(t.AdSoyad, $"%{k}%") || t.Telefon.Contains(k));
        }

        var total = await q.CountAsync(ct);
        var rows = await q
            .OrderBy(t => t.Durum == PublicBookingRequestDurum.Yeni ? 0 : 1) // çalışma kuyruğu: Yeni önce
            .ThenByDescending(t => t.CreatedAtUtc)
            .Skip(Math.Max(0, page - 1) * size).Take(size)
            .ToListAsync(ct);
        return (rows, total);
    }

    public async Task<int> NewCountAsync(CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.SiteTalepleri.AsNoTracking()
            .CountAsync(t => t.Durum == PublicBookingRequestDurum.Yeni, ct);
    }

    public async Task<DateTimeOffset?> OldestNewAsync(CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.SiteTalepleri.AsNoTracking()
            .Where(t => t.Durum == PublicBookingRequestDurum.Yeni)
            .MinAsync(t => (DateTimeOffset?)t.CreatedAtUtc, ct);
    }

    public async Task<bool> ChangeStatusAsync(Guid id, PublicBookingRequestDurum target, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        // Terminal satır DEĞİŞMEZ (atomik — oku-kontrol-yaz değil). Claim yüklemiyle aynı liste.
        var affected = await db.SiteTalepleri
            .Where(t => t.Id == id && (t.Durum == PublicBookingRequestDurum.Yeni
                                    || t.Durum == PublicBookingRequestDurum.Iletisimde
                                    || t.Durum == PublicBookingRequestDurum.TeklifVerildi))
            .ExecuteUpdateAsync(s => s
                .SetProperty(t => t.Durum, target)
                .SetProperty(t => t.UpdatedAtUtc, DateTimeOffset.UtcNow), ct);
        return affected == 1;
    }

    public async Task<bool> AssignAsync(Guid id, Guid? userId, string? name, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var affected = await db.SiteTalepleri.Where(t => t.Id == id)
            .ExecuteUpdateAsync(s => s
                .SetProperty(t => t.AtananKullaniciId, userId)
                .SetProperty(t => t.AtananAd, name)
                .SetProperty(t => t.UpdatedAtUtc, DateTimeOffset.UtcNow), ct);
        return affected == 1;
    }

    public async Task AddNoteAsync(TalepNotu not, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        db.TalepNotlari.Add(not);
        await db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<TalepNotu>> NotesAsync(Guid requestId, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.TalepNotlari.AsNoTracking()
            .Where(n => n.TalepId == requestId)
            .OrderByDescending(n => n.ZamanUtc)
            .ToListAsync(ct);
    }

    public async Task<Dictionary<Guid, int>> NoteCountsAsync(
        IReadOnlyCollection<Guid> requestIds, CancellationToken ct = default)
    {
        if (requestIds.Count == 0) return [];
        await using var db = await _factory.CreateDbContextAsync(ct);
        // TEK sorgu: satır başına COUNT çağırmak liste ekranında N+1 üretirdi.
        return await db.TalepNotlari.AsNoTracking()
            .Where(n => requestIds.Contains(n.TalepId))
            .GroupBy(n => n.TalepId)
            .Select(g => new { g.Key, Adet = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Adet, ct);
    }

    public async Task<Guid?> FindCustomerIdByPhoneAsync(string phone, CancellationToken ct = default)
    {
        var target = OnlyDigits(phone);
        if (target.Length < 7) return null; // anlamlı eşleşme için çok kısa — yeni cari açılsın

        await using var db = await _factory.CreateDbContextAsync(ct);
        // Normalize karşılaştırma DB'de ifade edilemez (regexp_replace EF'e çevrilmiyor) → aday kümesi
        // bellekte süzülür. Cari sayısı tenant başına makul; CepTel/Gsm2 dolu olanlarla sınırlanır.
        var candidates = await db.Customers.AsNoTracking()
            .Where(c => c.CepTel != null || c.Gsm2 != null)
            .Select(c => new { c.Id, c.CepTel, c.Gsm2 })
            .ToListAsync(ct);

        return candidates.FirstOrDefault(c =>
            OnlyDigits(c.CepTel) == target || OnlyDigits(c.Gsm2) == target)?.Id;
    }

    /// <summary>"0555 111 22 33" → "05551112233" (boşluk/tire/parantez/+ atılır).</summary>
    private static string OnlyDigits(string? s)
        => string.IsNullOrWhiteSpace(s) ? string.Empty : new string(s.Where(char.IsAsciiDigit).ToArray());
}
