using Microsoft.EntityFrameworkCore;
using RentACar.Application.Authorization;
using RentACar.Application.Search;

namespace RentACar.Infrastructure.Persistence.Repositories;

/// <summary>
/// ISearchRepository (roadmap C4): cross-module ILIKE araması. Her DbSet tenant query filter + RLS ile
/// otomatik kapsamlı. Tür başına perTypeLimit ile sınırlı (sessiz kırpma — UI "ilk N" gösterir).
/// <para>F1.6 ŞUBE KAPSAMI (liste ekranlarının C3/C4/C5 şablonu, <see cref="BranchScope.InScope"/> ile birebir):
/// araç (SubeId/Sube), kira ve rezervasyon (CikisSubeId/CikisOfisi), fatura (bağlı kira — RentalId ya da fark
/// faturasında KaynakKiraId — kapsamda; kirasız manuel faturada IslemSube metni). Cari kapsamsız (şube alanı yok).</para>
/// </summary>
public sealed class SearchRepository(IDbContextFactory<AppDbContext> factory) : ISearchRepository
{
    private readonly IDbContextFactory<AppDbContext> _factory = factory;

    public async Task<IReadOnlyList<SearchHit>> SearchAsync(
        string q, int perTypeLimit, BranchScope.BranchFilter scope, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var like = $"%{q}%";
        var hits = new List<SearchHit>();
        var kid = scope.SubeId;
        var kad = scope.SubeAd;
        var unlimited = scope.Unrestricted;

        var vehicleQuery = db.Vehicles.AsNoTracking();
        if (!unlimited)
            vehicleQuery = vehicleQuery.Where(v => (kid != null && v.SubeId == kid)
                || ((kid == null || v.SubeId == null) && kad != null && v.Sube != null && v.Sube.Trim() == kad)); // C5
        var rentalQuery = db.Rentals.AsNoTracking();
        if (!unlimited)
            rentalQuery = rentalQuery.Where(r => (kid != null && r.CikisSubeId == kid)
                || ((kid == null || r.CikisSubeId == null) && kad != null && r.CikisOfisi != null && r.CikisOfisi.Trim() == kad)); // C5
        var resQuery = db.Reservations.AsNoTracking();
        if (!unlimited)
            resQuery = resQuery.Where(r => (kid != null && r.CikisSubeId == kid)
                || ((kid == null || r.CikisSubeId == null) && kad != null && r.CikisOfisi != null && r.CikisOfisi.Trim() == kad)); // C5
        var invoiceQuery = db.Invoices.AsNoTracking();
        if (!unlimited)
        {
            var comprehensiveRentals = rentalQuery.Select(r => r.Id);
            invoiceQuery = invoiceQuery.Where(i =>
                (i.RentalId != null && comprehensiveRentals.Contains(i.RentalId.Value))
                || (i.KaynakKiraId != null && comprehensiveRentals.Contains(i.KaynakKiraId.Value))
                || (i.RentalId == null && i.KaynakKiraId == null && kad != null && i.IslemSube != null && i.IslemSube.Trim() == kad));
        }

        var vehicles = await vehicleQuery
            .Where(v => EF.Functions.ILike(v.Plaka, like) || (v.Marka != null && EF.Functions.ILike(v.Marka, like)))
            .OrderBy(v => v.Plaka).Take(perTypeLimit)
            .Select(v => new { v.Id, v.Plaka, v.Marka }).ToListAsync(ct);
        hits.AddRange(vehicles.Select(v => new SearchHit("Araç", v.Plaka, v.Marka, $"/araclar/{v.Id}")));

        // F11.1b güvenlik M5 (KVKK, #280 kuralı): adı anonimleştirilmiş cari GERÇEK adıyla eşleşmez — yalnız görünen
        // etiketiyle; başlık etiket, alt satır boş, sıra da etiketten (sıra gerçek adı sızdırmasın).
        var label = Application.Customers.CustomerAnonymity.NameLabel;
        var customers = await db.Customers.AsNoTracking()
            .Where(c => c.AnonimAd
                ? EF.Functions.ILike(label, like)
                : (c.Ad != null && EF.Functions.ILike(c.Ad, like)) || (c.Unvan != null && EF.Functions.ILike(c.Unvan, like)))
            .OrderBy(c => c.AnonimAd ? label : c.Ad).ThenBy(c => c.Id).Take(perTypeLimit)
            .Select(c => new { c.Id, c.Ad, c.Unvan, c.AnonimAd }).ToListAsync(ct);
        hits.AddRange(customers.Select(c => c.AnonimAd
            ? new SearchHit("Cari", label, null, $"/cariler/{c.Id}")
            : new SearchHit("Cari", c.Ad ?? c.Unvan ?? "(isimsiz)", c.Unvan, $"/cariler/{c.Id}")));

            // "En yeni" NUMARAYA göre sıralanamaz: yeni belge no'su yyyyddMM taşır (kullanıcı
            // kararı) ve alfabetik sıra kronolojik DEĞİLDİR — 20262608 (26 Ağu) ile 20260109
            // (1 Eyl) ters düşer. Ayrıca eski (KS-000001) ve yeni numaralar bir arada yaşıyor.
            // CreatedAtUtc her iki formatta da doğru sonucu verir.
        var rentals = await rentalQuery
            .Where(r => EF.Functions.ILike(r.SozlesmeNo, like))
            .OrderByDescending(r => r.CreatedAtUtc).Take(perTypeLimit)
            .Select(r => new { r.Id, r.SozlesmeNo }).ToListAsync(ct);
        hits.AddRange(rentals.Select(r => new SearchHit("Kira", r.SozlesmeNo, null, $"/kiralar/{r.Id}")));

        var reservations = await resQuery
            .Where(r => EF.Functions.ILike(r.ReservationNo, like))
            .OrderByDescending(r => r.CreatedAtUtc).Take(perTypeLimit)
            .Select(r => new { r.ReservationNo }).ToListAsync(ct);
        hits.AddRange(reservations.Select(r => new SearchHit("Rezervasyon", r.ReservationNo, null, "/rezervasyonlar")));

        var invoices = await invoiceQuery
            .Where(i => EF.Functions.ILike(i.No, like))
            .OrderByDescending(i => i.CreatedAtUtc).Take(perTypeLimit)
            .Select(i => new { i.No }).ToListAsync(ct);
        hits.AddRange(invoices.Select(i => new SearchHit("Fatura", i.No, null, "/faturalar")));

        return hits;
    }
}
