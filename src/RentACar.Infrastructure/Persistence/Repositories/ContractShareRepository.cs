using Microsoft.EntityFrameworkCore;
using RentACar.Application.Bookings;
using RentACar.Domain.Entities;

namespace RentACar.Infrastructure.Persistence.Repositories;

/// <summary>
/// PR-C — paylaşım linki + anlık görüntü kalıcılığı.
///
/// <para><b>İKİ tabloya, TEK transaction'da yazar.</b> <c>PaylasimLinkler</c> platform tablosudur
/// (RLS yok — anonim token çözümü için zorunlu, bkz. <see cref="PaylasimLink"/>),
/// <c>SozlesmePdfler</c> tenant-owned + RLS'dir. Yarım kalırsa iki bozuk hal doğar: token var ama
/// PDF yok (müşteri 404 alır) ya da PDF var ama token yok (yetim bytea). Bu yüzden ikisi de aynı
/// <c>SaveChangesAsync</c> içinde — EF tek transaction açar.</para>
///
/// <para><b>TenantId elle damgalanır</b> — <see cref="PaylasimLink"/> <c>ITenantOwned</c> DEĞİL,
/// dolayısıyla <c>AuditSaveChangesInterceptor</c>'ın damgalama döngüsü ona dokunmaz. Atlanırsa
/// <c>Guid.Empty</c> yazılır, FK patlar (sessiz sızıntı değil — gürültülü hata, doğru davranış).</para>
/// </summary>
public sealed class ContractShareRepository(IDbContextFactory<AppDbContext> factory)
    : IContractShareRepository
{
    private readonly IDbContextFactory<AppDbContext> _factory = factory;

    public async Task<PaylasimDurum?> ActiveAsync(Guid rentalId, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var link = await Active(db, rentalId).AsNoTracking().FirstOrDefaultAsync(ct);
        return link is null ? null : await StatusAsync(db, link, ct);
    }

    public async Task<PaylasimDurum> CreateAsync(Guid rentalId, string contractNo, string token,
        byte[] pdf, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);

        // Aktif link VARSA aynısını döner: yeni snapshot üretilmez (bytea şişmesi + müşterinin
        // elindeki adresin tıklama başına değişmesi, ikisi de kabul edilemez).
        var existing = await Active(db, rentalId).FirstOrDefaultAsync(ct);
        if (existing is not null) return await StatusAsync(db, existing, ct);

        var now = DateTimeOffset.UtcNow;
        await WritePdfAsync(db, rentalId, pdf, now, ct);
        var link = new PaylasimLink
        {
            TenantId = db.TenantId,          // ITenantOwned değil → interceptor damgalamaz
            RentalId = rentalId,
            Token = token,
            SozlesmeNo = contractNo,
            OlusturmaUtc = now,
            AnlikGoruntuUtc = now,
        };
        db.PaylasimLinkler.Add(link);
        await db.SaveChangesAsync(ct);
        return await StatusAsync(db, link, ct);
    }

    public async Task<PaylasimDurum> NewVersionAsync(Guid rentalId, string contractNo, string token,
        byte[] pdf, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var now = DateTimeOffset.UtcNow;

        // Eski link İPTAL (silinmez — erişim sayacı/geçmişi kanıt). Eski adres bundan sonra 404.
        // Kısmi unique index `NOT "Iptal"` üzerinde olduğundan, iptal edilen satır yeni linke yer açar.
        foreach (var old in await Active(db, rentalId).ToListAsync(ct))
            old.Iptal = true;

        await WritePdfAsync(db, rentalId, pdf, now, ct);
        var link = new PaylasimLink
        {
            TenantId = db.TenantId,
            RentalId = rentalId,
            Token = token,
            SozlesmeNo = contractNo,
            OlusturmaUtc = now,
            AnlikGoruntuUtc = now,
        };
        db.PaylasimLinkler.Add(link);
        await db.SaveChangesAsync(ct);
        return await StatusAsync(db, link, ct);
    }

    public async Task<bool> CancelAsync(Guid rentalId, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var links = await Active(db, rentalId).ToListAsync(ct);
        if (links.Count == 0) return false;

        foreach (var l in links) l.Iptal = true;

        // Anlık görüntü SİLİNİR: iptal edilmiş bir paylaşımın PDF'ini süresiz taşımanın anlamı yok
        // (link zaten 404 dönüyor). Tekrar paylaşılırsa yeni ve GÜNCEL bir görüntü üretilir.
        var pdf = await db.SozlesmePdfler.FirstOrDefaultAsync(x => x.RentalId == rentalId, ct);
        if (pdf is not null) db.SozlesmePdfler.Remove(pdf);

        await db.SaveChangesAsync(ct);
        return true;
    }

    /// <summary>Bu kiranın iptal edilmemiş linkleri. Kısmi unique index en fazla bir tane garanti
    /// eder; sorgu yine de çoğul yazıldı — index'e değil veriye göre davranmak daha güvenli.</summary>
    private static IQueryable<PaylasimLink> Active(AppDbContext db, Guid rentalId)
        => db.PaylasimLinkler.Where(x => x.TenantId == db.TenantId && x.RentalId == rentalId && !x.Iptal);

    /// <summary>Anlık görüntüyü yazar/tazeler (kira başına TEK satır — unique index).</summary>
    private static async Task WritePdfAsync(AppDbContext db, Guid rentalId, byte[] pdf,
        DateTimeOffset now, CancellationToken ct)
    {
        var existing = await db.SozlesmePdfler.FirstOrDefaultAsync(x => x.RentalId == rentalId, ct);
        if (existing is null)
        {
            db.SozlesmePdfler.Add(new SozlesmePdf
            {
                RentalId = rentalId, Bytes = pdf, Boyut = pdf.Length, UretimUtc = now,
            });
            return;
        }
        existing.Bytes = pdf;
        existing.Boyut = pdf.Length;
        existing.UretimUtc = now;
        existing.UpdatedAtUtc = now;
    }

    /// <summary>
    /// Panel görünümü. <b>Bayat</b> = kira, anlık görüntü alındıktan SONRA güncellenmiş.
    /// <c>RentalContract.UpdatedAtUtc</c> null ise kira hiç güncellenmemiş → bayat değil.
    /// </summary>
    private static async Task<PaylasimDurum> StatusAsync(AppDbContext db, PaylasimLink link,
        CancellationToken ct)
    {
        var update = await db.Rentals.AsNoTracking()
            .Where(r => r.Id == link.RentalId)
            .Select(r => r.UpdatedAtUtc)
            .FirstOrDefaultAsync(ct);

        return new PaylasimDurum(link.Token, link.ErisimSayisi, link.SonErisimUtc,
            link.OlusturmaUtc, link.AnlikGoruntuUtc,
            Bayat: update is DateTimeOffset g && g > link.AnlikGoruntuUtc);
    }
}
