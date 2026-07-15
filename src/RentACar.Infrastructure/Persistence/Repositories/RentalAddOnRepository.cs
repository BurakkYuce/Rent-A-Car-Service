using Microsoft.EntityFrameworkCore;
using RentACar.Application.Bookings;
using RentACar.Application.Common;
using RentACar.Application.RentalAddOns;
using RentACar.Domain.Entities;

namespace RentACar.Infrastructure.Persistence.Repositories;

/// <summary>
/// Kira ek hizmet kalemi deposu. Ekleme/silme ile parent kira GenelToplam/Bakiye'sini AYNI
/// transaction'da yeniden hesaplar (RentalTotals — tek doğruluk kaynağı). Faturalanmış kirada
/// değişiklik reddedilir. Tenant izolasyonu RLS + query filter ile otomatik.
/// </summary>
public sealed class RentalAddOnRepository(IDbContextFactory<AppDbContext> factory) : IRentalAddOnRepository
{
    private readonly IDbContextFactory<AppDbContext> _factory = factory;

    public async Task<IReadOnlyList<RentalAddOn>> ListForRentalAsync(Guid rentalId, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.RentalAddOns.AsNoTracking()
            .Where(a => a.RentalId == rentalId)
            .OrderBy(a => a.CreatedAtUtc)
            .ToListAsync(ct);
    }

    public async Task<RentalAddOn?> FindAsync(Guid addOnId, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.RentalAddOns.AsNoTracking().FirstOrDefaultAsync(a => a.Id == addOnId, ct);
    }

    public async Task<bool> IsRentalInvoicedAsync(Guid rentalId, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        // FAZ 4.2-B2: DÖNEM faturaları fark formatındadır (RentalId=null, KaynakKiraId=kira) ve base
        // fatura OLMADAN var olabilirler — yalnız RentalId'ye bakmak dönemsel kirada normal "Fatura
        // Kes"i BASE yoluna sokup TAM tutarı İKİNCİ KEZ kestirirdi (çift faturalama). Her iki bağ da
        // "faturalanmış" sayılır; addon/drop dondurma guard'ları da ilk dönem kesiminden itibaren
        // tutarlı biçimde devreye girer (defter snapshot ilkesiyle aynı).
        return await db.Invoices.AsNoTracking()
            .AnyAsync(i => i.RentalId == rentalId || i.KaynakKiraId == rentalId, ct);
    }

    public async Task AddAsync(RentalAddOn addOn, CancellationToken ct = default)
    {
        await PgRetry.RunAsync(async () => // P0-5: deadlock/serialization çakışmasında baştan dene
        {
            await using var db = await _factory.CreateDbContextAsync(ct);
            await using var tx = await db.Database.BeginTransactionAsync(ct);

            // Lost-update koruması (CRITICAL): RecomputeAsync mutlak SUM okuyup GenelToplam'ı yazar.
            // SUM'dan ÖNCE parent kira satırını kilitle → eşzamanlı ek hizmet ekleme/silme serileşir,
            // SUM tüm commit'li kalemleri görür (READ COMMITTED'da stale-okuma → eksik faturalama engellenir).
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT 1 FROM \"Rentals\" WHERE \"Id\" = {addOn.RentalId} FOR UPDATE", ct);

            var rental = await db.Rentals.FirstOrDefaultAsync(r => r.Id == addOn.RentalId, ct)
                ?? throw new ValidationException("Kira sözleşmesi bulunamadı.");
            if (await db.Invoices.AnyAsync(i => i.RentalId == addOn.RentalId, ct))
                throw new ValidationException("Faturalanmış kiraya ek hizmet eklenemez.");
            // K2/O2 (denetim): ek hizmet tutarları TL girilir; FX kirada kira dövizine karışıp faturada ×Kur
            // çarpılırdı (500 TL koltuk → "500 EUR" satırı → 17.500 TL defter). v1 sınırı: FX kirada ek hizmet YOK.
            if (RentACar.Application.Kur.KurService.NormalizeKod(rental.Doviz) != "TRY")
                throw new ValidationException("Dövizli kirada ek hizmet v1'de desteklenmiyor (tutar birimleri karışır).");

            db.RentalAddOns.Add(addOn);
            await db.SaveChangesAsync(ct);

            await RecomputeAsync(db, rental, ct);
            await tx.CommitAsync(ct);
        }, ct);
    }

    public async Task<bool> RemoveAsync(Guid addOnId, CancellationToken ct = default)
    {
        return await PgRetry.RunAsync(async () => // P0-5: deadlock/serialization çakışmasında baştan dene
        {
            await using var db = await _factory.CreateDbContextAsync(ct);
            await using var tx = await db.Database.BeginTransactionAsync(ct);

            var addOn = await db.RentalAddOns.FirstOrDefaultAsync(a => a.Id == addOnId, ct);
            if (addOn is null) return false;

            // Lost-update koruması (CRITICAL) — bkz. AddAsync: SUM yeniden-hesabından önce kira satırını kilitle.
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT 1 FROM \"Rentals\" WHERE \"Id\" = {addOn.RentalId} FOR UPDATE", ct);

            var rental = await db.Rentals.FirstOrDefaultAsync(r => r.Id == addOn.RentalId, ct);
            if (rental is not null && await db.Invoices.AnyAsync(i => i.RentalId == rental.Id, ct))
                throw new ValidationException("Faturalanmış kiranın ek hizmeti silinemez.");

            db.RentalAddOns.Remove(addOn);
            await db.SaveChangesAsync(ct);

            if (rental is not null)
                await RecomputeAsync(db, rental, ct);
            await tx.CommitAsync(ct);
            return true;
        }, ct);
    }

    private static async Task RecomputeAsync(AppDbContext db, RentalContract rental, CancellationToken ct)
    {
        var sum = await db.RentalAddOns
            .Where(a => a.RentalId == rental.Id)
            .SumAsync(a => a.Toplam, ct);
        RentalTotals.Recompute(rental, sum);
        rental.UpdatedAtUtc = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
    }
}
