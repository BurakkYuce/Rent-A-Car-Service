using Microsoft.EntityFrameworkCore;
using RentACar.Application.FaturaDonemleri;
using RentACar.Domain.Entities;
using RentACar.Domain.Enums;

namespace RentACar.Infrastructure.Persistence.Repositories;

/// <summary>IFaturaDonemRepository implementasyonu (FAZ 4.2-B1).</summary>
public sealed class InvoicePeriodRepository(IDbContextFactory<AppDbContext> factory) : IInvoicePeriodRepository
{
    private readonly IDbContextFactory<AppDbContext> _factory = factory;

    public async Task<IReadOnlyList<FaturaDonemi>> ListForRentalAsync(Guid rentalId, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.FaturaDonemleri.AsNoTracking()
            .Where(d => d.RentalId == rentalId)
            .OrderBy(d => d.DonemSira).ToListAsync(ct);
    }

    public async Task<IReadOnlyList<OtomatikTahsilatAdayi>> CandidatesAsync(
        OtomatikTahsilatFiltre filter, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var now = DateTimeOffset.UtcNow;

        // ADAY = vadesi GELMİŞ (DonemBit <= now) + PLANLANDI dönem × KİRADA sözleşme.
        // "Vadesi gelmemiş" dönemi listelemek, kullanıcıyı zamanından önce kesime davet ederdi.
        var q = from d in db.FaturaDonemleri.AsNoTracking()
                join r in db.Rentals.AsNoTracking() on d.RentalId equals r.Id
                join c in db.Customers.AsNoTracking() on r.MusteriId equals c.Id
                where d.Durum == InvoicePeriodStatus.Planlandi && d.DonemBit <= now
                      && r.Durum == RentalStatus.Kirada && r.DonemselFaturalama
                select new { d, r, c };

        if (!string.IsNullOrWhiteSpace(filter.SozlesmeNo))
        {
            var no = filter.SozlesmeNo.Trim();
            q = q.Where(x => x.r.SozlesmeNo.Contains(no));
        }
        if (filter.VadeMin is { } min) q = q.Where(x => x.d.DonemBit >= min);
        if (filter.VadeMax is { } max) q = q.Where(x => x.d.DonemBit <= max);

        // Şube kapsamı: FK dolu satırda FK TEK BAŞINA karar verir (FAZ-5 C5 daraltması);
        // FK'sız eski satırlarda metin yoluna düşülür.
        if (filter.SubeIdler is { Count: > 0 } ids)
        {
            var name = filter.SubeAdi;
            q = q.Where(x => x.r.CikisSubeId != null
                ? ids.Contains(x.r.CikisSubeId.Value)
                : (name != null && x.r.CikisOfisi == name));
        }
        else if (!string.IsNullOrWhiteSpace(filter.SubeAdi))
        {
            // FK'sız claim (eski oturum): yalnız metin yolu kalır.
            var name = filter.SubeAdi.Trim();
            q = q.Where(x => x.r.CikisOfisi == name);
        }

        var raw = await q.OrderBy(x => x.d.DonemBit).ThenBy(x => x.r.SozlesmeNo)
            .Select(x => new
            {
                x.r.Id, x.r.SozlesmeNo, x.d.DonemSira, x.d.DonemBas, x.d.DonemBit,
                CariId = x.c.Id, x.c.Tip, x.c.Ad, x.c.Soyad, x.c.Unvan,
                x.r.CikisOfisi, x.r.CikisSubeId, x.r.Doviz, x.r.Tutar
            }).ToListAsync(ct);
        if (raw.Count == 0) return [];

        // Cari bakiye TEK sorguda (satır başına sorgu N+1 yapardı). Bakiye = Σ SignedBase.
        var customers = raw.Select(x => x.CariId).Distinct().ToList();
        var balances = await db.AccountLedgerEntries.AsNoTracking()
            .Where(e => e.AccountType == LedgerAccountType.Cari && e.AccountRef != null
                        && customers.Contains(e.AccountRef.Value))
            .GroupBy(e => e.AccountRef!.Value)
            .Select(g => new
            {
                CariId = g.Key,
                Bakiye = g.Sum(e => e.Direction == LedgerDirection.Debit
                    ? e.Amount.Amount * e.Amount.Rate
                    : -(e.Amount.Amount * e.Amount.Rate))
            })
            .ToDictionaryAsync(x => x.CariId, x => x.Bakiye, ct);

        var result = raw.Select(x => new OtomatikTahsilatAdayi(
            x.Id, x.SozlesmeNo, x.DonemSira, x.DonemBas, x.DonemBit, x.CariId,
            x.Tip == CustomerType.Bireysel ? $"{x.Ad} {x.Soyad}".Trim() : (x.Unvan ?? string.Empty),
            x.CikisOfisi, x.CikisSubeId,
            // TRY kiralarda Rental.Doviz NULL gelir; boş etiket ekranda "· " gibi görünür ve
            // döviz kırılımını bozardı → baz para koduna normalize edilir.
            string.IsNullOrWhiteSpace(x.Doviz) ? "TRY" : x.Doviz.Trim().ToUpperInvariant(),
            x.Tutar,
            balances.TryGetValue(x.CariId, out var b) ? b : 0m)).ToList();

        // "Sadece bakiyeli" BELLEKTE süzülür: bakiye türetilmiş bir toplam, SQL'de tekrar
        // hesaplatmak aynı kuralın ikinci kopyası olurdu.
        return filter.SadeceBakiyeli ? result.Where(x => x.CariBakiye > 0m).ToList() : result;
    }

    public async Task<bool> MarkSkippedAsync(Guid periodId, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var d = await db.FaturaDonemleri.FirstOrDefaultAsync(x => x.Id == periodId, ct);
        if (d is null || d.Durum != InvoicePeriodStatus.Planlandi) return false;
        d.Durum = InvoicePeriodStatus.Atlandi;
        d.UpdatedAtUtc = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task ReplacePlannedAsync(
        Guid rentalId, IReadOnlyList<FaturaDonemi> newPlans, CancellationToken ct = default)
    {
        await PgRetry.RunAsync(async () => // deadlock/serialization çakışmasında baştan dene
        {
            await using var db = await _factory.CreateDbContextAsync(ct);
            await using var tx = await db.Database.BeginTransactionAsync(ct);

            var oldOnes = await db.FaturaDonemleri
                .Where(d => d.RentalId == rentalId && d.Durum == InvoicePeriodStatus.Planlandi)
                .ToListAsync(ct);
            db.FaturaDonemleri.RemoveRange(oldOnes);
            db.FaturaDonemleri.AddRange(newPlans);

            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }, ct);
    }
}
