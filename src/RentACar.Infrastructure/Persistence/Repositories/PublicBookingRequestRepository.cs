using Microsoft.EntityFrameworkCore;
using RentACar.Application.PublicSite;
using RentACar.Domain.Entities;

namespace RentACar.Infrastructure.Persistence.Repositories;

/// <summary>PR-8: IPublicBookingRequestRepository implementasyonu.</summary>
public sealed class PublicBookingRequestRepository(IDbContextFactory<AppDbContext> factory) : IPublicBookingRequestRepository
{
    private readonly IDbContextFactory<AppDbContext> _factory = factory;

    public async Task AddAsync(PublicBookingRequest talep, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        db.SiteTalepleri.Add(talep);
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

    public async Task<bool> TryClaimAsync(Guid id, PublicBookingRequestDurum hedef, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        // TEK atomik UPDATE — yarışta tek kazanan bırakır (oku-kontrol-yaz DEĞİL).
        // PR-17: koşul artık "Durum == Yeni" DEĞİL "terminal değil". Ara durumlar eklenince eski
        // yüklem, personelin "İletişimde" işaretlediği talebi dönüştürülemez hale getiriyordu.
        // `TalepDurumu.Terminal` bir C# metodu ve LINQ'e çevrilemez → yüklem aktif durumların
        // AÇIK listesi olarak yazıldı; testler ikisinin örtüştüğünü doğruluyor.
        var etkilenen = await db.SiteTalepleri
            .Where(t => t.Id == id && (t.Durum == PublicBookingRequestDurum.Yeni
                                    || t.Durum == PublicBookingRequestDurum.Iletisimde
                                    || t.Durum == PublicBookingRequestDurum.TeklifVerildi))
            .ExecuteUpdateAsync(s => s
                .SetProperty(t => t.Durum, hedef)
                .SetProperty(t => t.UpdatedAtUtc, DateTimeOffset.UtcNow), ct);
        return etkilenen == 1;
    }

    public async Task SetDonusenReservationAsync(Guid id, Guid reservationId, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        await db.SiteTalepleri.Where(t => t.Id == id)
            .ExecuteUpdateAsync(s => s
                .SetProperty(t => t.DonusenReservationId, reservationId)
                .SetProperty(t => t.UpdatedAtUtc, DateTimeOffset.UtcNow), ct);
    }

    public async Task ReleaseClaimAsync(Guid id, PublicBookingRequestDurum oncekiDurum, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        // PR-17: koşulsuz `Yeni` yazmak, personelin "İletişimde/Teklif verildi" ilerlemesini
        // SESSİZCE silerdi → claim ÖNCESİ durum geri yazılıyor.
        await db.SiteTalepleri.Where(t => t.Id == id)
            .ExecuteUpdateAsync(s => s
                .SetProperty(t => t.Durum, oncekiDurum)
                .SetProperty(t => t.DonusenReservationId, (Guid?)null)
                .SetProperty(t => t.UpdatedAtUtc, DateTimeOffset.UtcNow), ct);
    }

    // ---- PR-17: filtre/sayfalama, sayaçlar, durum/atama/not ----

    public async Task<(IReadOnlyList<PublicBookingRequest> Satirlar, int Toplam)> SayfaliAsync(
        PublicBookingRequestDurum? durum, string? ara, int sayfa, int boyut, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var q = db.SiteTalepleri.AsNoTracking().AsQueryable();

        if (durum is { } d) q = q.Where(t => t.Durum == d);
        if (!string.IsNullOrWhiteSpace(ara))
        {
            // Ad ve telefonda arama. Telefon normalizasyonu DB'de ifade edilemediği için ham
            // `Contains` — personel numarayı listede göründüğü gibi yazıyor.
            var k = ara.Trim();
            q = q.Where(t => EF.Functions.ILike(t.AdSoyad, $"%{k}%") || t.Telefon.Contains(k));
        }

        var toplam = await q.CountAsync(ct);
        var satirlar = await q
            .OrderBy(t => t.Durum == PublicBookingRequestDurum.Yeni ? 0 : 1) // çalışma kuyruğu: Yeni önce
            .ThenByDescending(t => t.CreatedAtUtc)
            .Skip(Math.Max(0, sayfa - 1) * boyut).Take(boyut)
            .ToListAsync(ct);
        return (satirlar, toplam);
    }

    public async Task<int> YeniSayisiAsync(CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.SiteTalepleri.AsNoTracking()
            .CountAsync(t => t.Durum == PublicBookingRequestDurum.Yeni, ct);
    }

    public async Task<DateTimeOffset?> EnEskiYeniAsync(CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.SiteTalepleri.AsNoTracking()
            .Where(t => t.Durum == PublicBookingRequestDurum.Yeni)
            .MinAsync(t => (DateTimeOffset?)t.CreatedAtUtc, ct);
    }

    public async Task<bool> DurumDegistirAsync(Guid id, PublicBookingRequestDurum hedef, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        // Terminal satır DEĞİŞMEZ (atomik — oku-kontrol-yaz değil). Claim yüklemiyle aynı liste.
        var etkilenen = await db.SiteTalepleri
            .Where(t => t.Id == id && (t.Durum == PublicBookingRequestDurum.Yeni
                                    || t.Durum == PublicBookingRequestDurum.Iletisimde
                                    || t.Durum == PublicBookingRequestDurum.TeklifVerildi))
            .ExecuteUpdateAsync(s => s
                .SetProperty(t => t.Durum, hedef)
                .SetProperty(t => t.UpdatedAtUtc, DateTimeOffset.UtcNow), ct);
        return etkilenen == 1;
    }

    public async Task<bool> AtaAsync(Guid id, Guid? kullaniciId, string? ad, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var etkilenen = await db.SiteTalepleri.Where(t => t.Id == id)
            .ExecuteUpdateAsync(s => s
                .SetProperty(t => t.AtananKullaniciId, kullaniciId)
                .SetProperty(t => t.AtananAd, ad)
                .SetProperty(t => t.UpdatedAtUtc, DateTimeOffset.UtcNow), ct);
        return etkilenen == 1;
    }

    public async Task NotEkleAsync(TalepNotu not, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        db.TalepNotlari.Add(not);
        await db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<TalepNotu>> NotlarAsync(Guid talepId, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.TalepNotlari.AsNoTracking()
            .Where(n => n.TalepId == talepId)
            .OrderByDescending(n => n.ZamanUtc)
            .ToListAsync(ct);
    }

    public async Task<Dictionary<Guid, int>> NotSayilariAsync(
        IReadOnlyCollection<Guid> talepIdler, CancellationToken ct = default)
    {
        if (talepIdler.Count == 0) return [];
        await using var db = await _factory.CreateDbContextAsync(ct);
        // TEK sorgu: satır başına COUNT çağırmak liste ekranında N+1 üretirdi.
        return await db.TalepNotlari.AsNoTracking()
            .Where(n => talepIdler.Contains(n.TalepId))
            .GroupBy(n => n.TalepId)
            .Select(g => new { g.Key, Adet = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Adet, ct);
    }

    public async Task<Guid?> FindCustomerIdByPhoneAsync(string telefon, CancellationToken ct = default)
    {
        var hedef = OnlyDigits(telefon);
        if (hedef.Length < 7) return null; // anlamlı eşleşme için çok kısa — yeni cari açılsın

        await using var db = await _factory.CreateDbContextAsync(ct);
        // Normalize karşılaştırma DB'de ifade edilemez (regexp_replace EF'e çevrilmiyor) → aday kümesi
        // bellekte süzülür. Cari sayısı tenant başına makul; CepTel/Gsm2 dolu olanlarla sınırlanır.
        var adaylar = await db.Customers.AsNoTracking()
            .Where(c => c.CepTel != null || c.Gsm2 != null)
            .Select(c => new { c.Id, c.CepTel, c.Gsm2 })
            .ToListAsync(ct);

        return adaylar.FirstOrDefault(c =>
            OnlyDigits(c.CepTel) == hedef || OnlyDigits(c.Gsm2) == hedef)?.Id;
    }

    /// <summary>"0555 111 22 33" → "05551112233" (boşluk/tire/parantez/+ atılır).</summary>
    private static string OnlyDigits(string? s)
        => string.IsNullOrWhiteSpace(s) ? string.Empty : new string(s.Where(char.IsAsciiDigit).ToArray());
}
