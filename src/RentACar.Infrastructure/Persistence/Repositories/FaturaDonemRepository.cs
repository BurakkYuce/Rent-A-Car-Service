using Microsoft.EntityFrameworkCore;
using RentACar.Application.FaturaDonemleri;
using RentACar.Domain.Entities;
using RentACar.Domain.Enums;

namespace RentACar.Infrastructure.Persistence.Repositories;

/// <summary>IFaturaDonemRepository implementasyonu (FAZ 4.2-B1).</summary>
public sealed class FaturaDonemRepository(IDbContextFactory<AppDbContext> factory) : IFaturaDonemRepository
{
    private readonly IDbContextFactory<AppDbContext> _factory = factory;

    public async Task<IReadOnlyList<FaturaDonemi>> ListForRentalAsync(Guid rentalId, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.FaturaDonemleri.AsNoTracking()
            .Where(d => d.RentalId == rentalId)
            .OrderBy(d => d.DonemSira).ToListAsync(ct);
    }

    public async Task<IReadOnlyList<OtomatikTahsilatAdayi>> AdaylarAsync(
        OtomatikTahsilatFiltre filtre, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var simdi = DateTimeOffset.UtcNow;

        // ADAY = vadesi GELMİŞ (DonemBit <= now) + PLANLANDI dönem × KİRADA sözleşme.
        // "Vadesi gelmemiş" dönemi listelemek, kullanıcıyı zamanından önce kesime davet ederdi.
        var q = from d in db.FaturaDonemleri.AsNoTracking()
                join r in db.Rentals.AsNoTracking() on d.RentalId equals r.Id
                join c in db.Customers.AsNoTracking() on r.MusteriId equals c.Id
                where d.Durum == FaturaDonemDurum.Planlandi && d.DonemBit <= simdi
                      && r.Durum == RentalStatus.Kirada && r.DonemselFaturalama
                select new { d, r, c };

        if (!string.IsNullOrWhiteSpace(filtre.SozlesmeNo))
        {
            var no = filtre.SozlesmeNo.Trim();
            q = q.Where(x => x.r.SozlesmeNo.Contains(no));
        }
        if (filtre.VadeMin is { } min) q = q.Where(x => x.d.DonemBit >= min);
        if (filtre.VadeMax is { } max) q = q.Where(x => x.d.DonemBit <= max);

        // Şube kapsamı: FK dolu satırda FK TEK BAŞINA karar verir (FAZ-5 C5 daraltması);
        // FK'sız eski satırlarda metin yoluna düşülür.
        if (filtre.SubeIdler is { Count: > 0 } idler)
        {
            var ad = filtre.SubeAdi;
            q = q.Where(x => x.r.CikisSubeId != null
                ? idler.Contains(x.r.CikisSubeId.Value)
                : (ad != null && x.r.CikisOfisi == ad));
        }
        else if (!string.IsNullOrWhiteSpace(filtre.SubeAdi))
        {
            // FK'sız claim (eski oturum): yalnız metin yolu kalır.
            var ad = filtre.SubeAdi.Trim();
            q = q.Where(x => x.r.CikisOfisi == ad);
        }

        var ham = await q.OrderBy(x => x.d.DonemBit).ThenBy(x => x.r.SozlesmeNo)
            .Select(x => new
            {
                x.r.Id, x.r.SozlesmeNo, x.d.DonemSira, x.d.DonemBas, x.d.DonemBit,
                CariId = x.c.Id, x.c.Tip, x.c.Ad, x.c.Soyad, x.c.Unvan,
                x.r.CikisOfisi, x.r.CikisSubeId, x.r.Doviz, x.r.Tutar
            }).ToListAsync(ct);
        if (ham.Count == 0) return [];

        // Cari bakiye TEK sorguda (satır başına sorgu N+1 yapardı). Bakiye = Σ SignedBase.
        var cariler = ham.Select(x => x.CariId).Distinct().ToList();
        var bakiyeler = await db.AccountLedgerEntries.AsNoTracking()
            .Where(e => e.AccountType == LedgerAccountType.Cari && e.AccountRef != null
                        && cariler.Contains(e.AccountRef.Value))
            .GroupBy(e => e.AccountRef!.Value)
            .Select(g => new
            {
                CariId = g.Key,
                Bakiye = g.Sum(e => e.Direction == LedgerDirection.Debit
                    ? e.Amount.Amount * e.Amount.Rate
                    : -(e.Amount.Amount * e.Amount.Rate))
            })
            .ToDictionaryAsync(x => x.CariId, x => x.Bakiye, ct);

        var sonuc = ham.Select(x => new OtomatikTahsilatAdayi(
            x.Id, x.SozlesmeNo, x.DonemSira, x.DonemBas, x.DonemBit, x.CariId,
            x.Tip == CariType.Bireysel ? $"{x.Ad} {x.Soyad}".Trim() : (x.Unvan ?? string.Empty),
            x.CikisOfisi, x.CikisSubeId,
            // TRY kiralarda Rental.Doviz NULL gelir; boş etiket ekranda "· " gibi görünür ve
            // döviz kırılımını bozardı → baz para koduna normalize edilir.
            string.IsNullOrWhiteSpace(x.Doviz) ? "TRY" : x.Doviz.Trim().ToUpperInvariant(),
            x.Tutar,
            bakiyeler.TryGetValue(x.CariId, out var b) ? b : 0m)).ToList();

        // "Sadece bakiyeli" BELLEKTE süzülür: bakiye türetilmiş bir toplam, SQL'de tekrar
        // hesaplatmak aynı kuralın ikinci kopyası olurdu.
        return filtre.SadeceBakiyeli ? sonuc.Where(x => x.CariBakiye > 0m).ToList() : sonuc;
    }

    public async Task<bool> AtlandiIsaretleAsync(Guid donemId, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var d = await db.FaturaDonemleri.FirstOrDefaultAsync(x => x.Id == donemId, ct);
        if (d is null || d.Durum != FaturaDonemDurum.Planlandi) return false;
        d.Durum = FaturaDonemDurum.Atlandi;
        d.UpdatedAtUtc = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task ReplacePlannedAsync(
        Guid rentalId, IReadOnlyList<FaturaDonemi> yeniPlanlar, CancellationToken ct = default)
    {
        await PgRetry.RunAsync(async () => // deadlock/serialization çakışmasında baştan dene
        {
            await using var db = await _factory.CreateDbContextAsync(ct);
            await using var tx = await db.Database.BeginTransactionAsync(ct);

            var eskiler = await db.FaturaDonemleri
                .Where(d => d.RentalId == rentalId && d.Durum == FaturaDonemDurum.Planlandi)
                .ToListAsync(ct);
            db.FaturaDonemleri.RemoveRange(eskiler);
            db.FaturaDonemleri.AddRange(yeniPlanlar);

            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }, ct);
    }
}
