using Microsoft.EntityFrameworkCore;
using Npgsql;
using RentACar.Application.Common;
using RentACar.Application.Customers;
using RentACar.Domain.Entities;
using RentACar.Domain.Enums;

namespace RentACar.Infrastructure.Persistence.Repositories;

/// <summary>
/// ICustomerRepository: kısa-ömürlü context'ler (factory). Update, audit eski/yeni
/// farkı için entity'yi yükleyip mutasyonu uygular. DB benzersizlik ihlali (23505),
/// constraint adına göre TC/VergiNo ayrımıyla DuplicateCariException'a çevrilir.
/// </summary>
public sealed class CustomerRepository(IDbContextFactory<AppDbContext> factory) : ICustomerRepository
{
    private readonly IDbContextFactory<AppDbContext> _factory = factory;

    public async Task<IReadOnlyList<Customer>> ListAsync(CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.Customers.AsNoTracking()
            .OrderBy(c => c.Tip).ThenBy(c => c.Unvan).ThenBy(c => c.Ad)
            .ToListAsync(ct);
    }

    /// <summary>Ortak filtre (arama + Tip + İYS/uyarı/kara-liste) — SearchAsync ve SearchRowsAsync paylaşır.</summary>
    private static IQueryable<Customer> ApplyFilter(IQueryable<Customer> q, CustomerFilter filter)
    {
        if (!string.IsNullOrWhiteSpace(filter.Query))
        {
            var term = $"%{filter.Query.Trim()}%";
            q = q.Where(c =>
                (c.Ad != null && EF.Functions.ILike(c.Ad, term))
                || (c.Soyad != null && EF.Functions.ILike(c.Soyad, term))
                || (c.Unvan != null && EF.Functions.ILike(c.Unvan, term))
                || (c.TcKimlik != null && EF.Functions.ILike(c.TcKimlik, term))
                || (c.VergiNo != null && EF.Functions.ILike(c.VergiNo, term)));
        }
        if (filter.Tip is { } tip) q = q.Where(c => c.Tip == tip);
        if (filter.IysIzinli is { } iys) q = q.Where(c => c.IysIzinli == iys);
        if (filter.Uyari is { } uy) q = q.Where(c => c.Uyari == uy);
        if (filter.KaraListe is { } kl) q = q.Where(c => c.KaraListe == kl);
        return q;
    }

    public async Task<PagedResult<Customer>> SearchAsync(CustomerFilter filter, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var q = ApplyFilter(db.Customers.AsNoTracking(), filter);

        var total = await q.CountAsync(ct);
        var items = await q
            .OrderBy(c => c.Tip).ThenBy(c => c.Unvan).ThenBy(c => c.Ad)
            .Skip((filter.Page - 1) * filter.PageSize).Take(filter.PageSize)
            .ToListAsync(ct);
        return new PagedResult<Customer>(items, total, filter.Page, filter.PageSize);
    }

    public async Task<PagedResult<CustomerRow>> SearchRowsAsync(CustomerFilter filter, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var q = ApplyFilter(db.Customers.AsNoTracking(), filter);

        var total = await q.CountAsync(ct);
        var page = await q
            .OrderBy(c => c.Tip).ThenBy(c => c.Unvan).ThenBy(c => c.Ad)
            .Skip((filter.Page - 1) * filter.PageSize).Take(filter.PageSize)
            .ToListAsync(ct);

        // Sayfadaki cariler için kira agregaları (İptal hariç): adet, ciro (GenelToplam Σ), son kira tarihi.
        var ids = page.Select(c => c.Id).ToList();
        var aggList = await db.Rentals.AsNoTracking()
            .Where(r => ids.Contains(r.MusteriId) && r.Durum != RentalStatus.Iptal)
            .GroupBy(r => r.MusteriId)
            .Select(g => new { MusteriId = g.Key, Adet = g.Count(), Ciro = g.Sum(r => r.GenelToplam), SonKira = g.Max(r => r.BasTar) })
            .ToListAsync(ct);
        var agg = aggList.ToDictionary(a => a.MusteriId);

        var rows = page.Select(c =>
        {
            agg.TryGetValue(c.Id, out var a);
            return new CustomerRow
            {
                Id = c.Id, Tip = c.Tip, DisplayName = c.DisplayName,
                TcKimlik = c.TcKimlik, VergiNo = c.VergiNo, CepTel = c.CepTel, Email = c.Email,
                Il = c.Il, Kaynak = c.Kaynak,
                KaraListe = c.KaraListe, Pasif = c.Pasif, Uyari = c.Uyari, IysIzinli = c.IysIzinli,
                KiraAdet = a?.Adet ?? 0,
                Ciro = a?.Ciro ?? 0m,
                SonKira = a is null ? null : a.SonKira
            };
        }).ToList();

        return new PagedResult<CustomerRow>(rows, total, filter.Page, filter.PageSize);
    }

    public async Task<Customer?> FindAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.Customers.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id, ct);
    }

    public async Task<bool> TcKimlikExistsAsync(string tcKimlik, Guid? excludeId = null, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.Customers.AsNoTracking()
            .Where(c => c.TcKimlik == tcKimlik && (excludeId == null || c.Id != excludeId))
            .AnyAsync(ct);
    }

    public async Task<bool> VergiNoExistsAsync(string vergiNo, Guid? excludeId = null, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.Customers.AsNoTracking()
            .Where(c => c.VergiNo == vergiNo && (excludeId == null || c.Id != excludeId))
            .AnyAsync(ct);
    }

    public async Task CreateAsync(Customer customer, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        db.Customers.Add(customer);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (AsDuplicate(ex, customer) is { } dup)
        {
            throw dup;
        }
    }

    public async Task<bool> UpdateAsync(Guid id, Action<Customer> apply, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var customer = await db.Customers.FirstOrDefaultAsync(c => c.Id == id, ct);
        if (customer is null) return false;

        apply(customer);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (AsDuplicate(ex, customer) is { } dup)
        {
            throw dup;
        }
        return true;
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var customer = await db.Customers.FirstOrDefaultAsync(c => c.Id == id, ct);
        if (customer is null) return false;

        db.Customers.Remove(customer);
        await db.SaveChangesAsync(ct);
        return true;
    }

    private static DuplicateCariException? AsDuplicate(DbUpdateException ex, Customer c)
    {
        if (ex.InnerException is not PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } pg)
            return null;
        var constraint = pg.ConstraintName ?? string.Empty;
        if (constraint.Contains("TcKimlik", StringComparison.OrdinalIgnoreCase))
            return new DuplicateCariException("TC Kimlik No", c.TcKimlik ?? string.Empty);
        if (constraint.Contains("VergiNo", StringComparison.OrdinalIgnoreCase))
            return new DuplicateCariException("Vergi No", c.VergiNo ?? string.Empty);
        return new DuplicateCariException("kayıt", c.DisplayName);
    }
}
