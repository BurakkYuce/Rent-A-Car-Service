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
/// KVKK/F2: tüm okuma yolları PII cipher'larını BELLEKTE çözer (TcKimlik/EhliyetNo/
/// PasaportNo alanlarına) — tüketiciler (UI/API/servisler) değişmeden düz değeri görür;
/// DB'de yalnız cipher + blind-index durur.
/// </summary>
public sealed class CustomerRepository(IDbContextFactory<AppDbContext> factory, ISecretProtector secrets)
    : ICustomerRepository
{
    private readonly IDbContextFactory<AppDbContext> _factory = factory;
    private readonly ISecretProtector _secrets = secrets;

    /// <summary>Cipher'ları bellek-içi düz alanlara çözer (DB'ye YAZILMAZ — okuma yolu AsNoTracking).</summary>
    private Customer Decrypt(Customer c)
    {
        c.TcKimlik = _secrets.Unprotect(c.TcKimlikEnc) ?? c.TcKimlik;       // ?? eski (backfill öncesi) satır
        c.EhliyetNo = _secrets.Unprotect(c.EhliyetNoEnc) ?? c.EhliyetNo;
        c.PasaportNo = _secrets.Unprotect(c.PasaportNoEnc) ?? c.PasaportNo;
        return c;
    }

    public async Task<IReadOnlyList<Customer>> ListAsync(CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var items = await db.Customers.AsNoTracking()
            .OrderBy(c => c.Tip).ThenBy(c => c.Unvan).ThenBy(c => c.Ad)
            .ToListAsync(ct);
        foreach (var c in items) Decrypt(c);
        return items;
    }

    /// <summary>Seçim listesi — PII kolonlarına HİÇ dokunmaz (Decrypt çağrılmaz, cipher okunmaz).
    /// Görünen ad kuralı tek kaynaktan gelsin diye projeksiyon geçici Customer'a sarılıp
    /// <c>DisplayName</c> okunur (kural kopyalanmaz).</summary>
    public async Task<IReadOnlyList<CariSecim>> ListSecimAsync(CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var rows = await db.Customers.AsNoTracking()
            .OrderBy(c => c.Tip).ThenBy(c => c.Unvan).ThenBy(c => c.Ad)
            .Select(c => new { c.Id, c.Tip, c.Unvan, c.Ad, c.Soyad })
            .ToListAsync(ct);
        return rows
            .Select(r => new CariSecim(r.Id,
                new Customer { Tip = r.Tip, Unvan = r.Unvan, Ad = r.Ad, Soyad = r.Soyad }.DisplayName))
            .ToList();
    }

    /// <summary>F1.6 sınırlı seçim araması — PII kolonlarına HİÇ dokunmaz; Türkçe katlamalı
    /// (<see cref="TrSql"/>) ad+soyad+ünvan araması; en çok <paramref name="limit"/> satır.</summary>
    public async Task<IReadOnlyList<CariSecimSatiri>> SecimAraAsync(string katlanmisTerim, int limit, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var q = db.Customers.AsNoTracking();
        if (katlanmisTerim.Length > 0)
            q = q.Where(TrSql.Icerir<Customer>(
                c => (c.Ad ?? "") + " " + (c.Soyad ?? "") + " " + (c.Unvan ?? ""), katlanmisTerim));
        var rows = await q
            .OrderBy(c => c.Tip).ThenBy(c => c.Unvan).ThenBy(c => c.Ad).ThenBy(c => c.Soyad).ThenBy(c => c.Id)
            .Take(limit)
            .Select(c => new { c.Id, c.Tip, c.Unvan, c.Ad, c.Soyad })
            .ToListAsync(ct);
        return rows
            .Select(r => new CariSecimSatiri(r.Id,
                new Customer { Tip = r.Tip, Unvan = r.Unvan, Ad = r.Ad, Soyad = r.Soyad }.DisplayName, r.Tip))
            .ToList();
    }

    /// <summary>F4.3b kimlikle tek seçim satırı — PII kolonlarına dokunmaz (bkz. <see cref="SecimAraAsync"/>).</summary>
    public async Task<CariSecimSatiri?> SecimGetirAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var r = await db.Customers.AsNoTracking()
            .Where(c => c.Id == id)
            .Select(c => new { c.Id, c.Tip, c.Unvan, c.Ad, c.Soyad })
            .FirstOrDefaultAsync(ct);
        return r is null ? null
            : new CariSecimSatiri(r.Id, new Customer { Tip = r.Tip, Unvan = r.Unvan, Ad = r.Ad, Soyad = r.Soyad }.DisplayName, r.Tip);
    }

    /// <summary>Ortak filtre (arama + Tip + İYS/uyarı/kara-liste) — SearchAsync ve SearchRowsAsync paylaşır.
    /// TC araması yalnız TAM eşleşme (blind-index, filter.TcHash) — şifreli kolonda ILike anlamsız.</summary>
    private static IQueryable<Customer> ApplyFilter(IQueryable<Customer> q, CustomerFilter filter)
    {
        if (!string.IsNullOrWhiteSpace(filter.Query))
        {
            var term = $"%{filter.Query.Trim()}%";
            var tcHash = filter.TcHash;
            q = q.Where(c =>
                (c.Ad != null && EF.Functions.ILike(c.Ad, term))
                || (c.Soyad != null && EF.Functions.ILike(c.Soyad, term))
                || (c.Unvan != null && EF.Functions.ILike(c.Unvan, term))
                || (tcHash != null && c.TcKimlikHash == tcHash)
                || (c.VergiNo != null && EF.Functions.ILike(c.VergiNo, term)));
        }
        if (filter.Tip is { } tip) q = q.Where(c => c.Tip == tip);
        if (filter.IysIzinli is { } iys) q = q.Where(c => c.IysIzinli == iys);
        if (filter.Uyari is { } uy) q = q.Where(c => c.Uyari == uy);
        if (filter.KaraListe is { } kl) q = q.Where(c => c.KaraListe == kl);
        // FAZ-40: Pasif filtresi YOKTU — pasif cariler listeden ayıklanamıyordu.
        if (filter.Pasif is { } pf) q = q.Where(c => c.Pasif == pf);
        if (filter.AracVerilmez is { } av) q = q.Where(c => c.AracVerilmez == av);
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
        foreach (var c in items) Decrypt(c);
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
            .Select(g => new { MusteriId = g.Key, Adet = g.Count(), Ciro = g.Sum(r => r.GenelToplam * r.KurSnapshot), SonKira = g.Max(r => r.BasTar) }) // TL-baz (O5: FX+TL düz toplanmaz)
            .ToListAsync(ct);
        var agg = aggList.ToDictionary(a => a.MusteriId);

        var rows = page.Select(c =>
        {
            Decrypt(c);
            agg.TryGetValue(c.Id, out var a);
            return new CustomerRow
            {
                Id = c.Id, Tip = c.Tip, DisplayName = c.DisplayName,
                TcKimlik = c.TcKimlik, VergiNo = c.VergiNo, CepTel = c.CepTel, Email = c.Email,
                Il = c.Il, Kaynak = c.Kaynak,
                KaraListe = c.KaraListe, Pasif = c.Pasif, Uyari = c.Uyari, IysIzinli = c.IysIzinli,
                KiraAdet = a?.Adet ?? 0,
                Ciro = a?.Ciro ?? 0m,
                SonKira = a is null ? null : a.SonKira,
                // FAZ-40 (D3): alanlar entity'de zaten vardı, projeksiyona girmiyordu.
                MusteriTemsilcisi = c.MusteriTemsilcisi,
                DogumTarihi = c.DogumTarihi,
                VadeGun = c.VadeGun,
                UyariNedeni = c.UyariNedeni,
                Gsm2 = c.Gsm2,
                Adres = c.Adres,
                Ilce = c.Ilce,
                EntegrasyonKodu = c.EntegrasyonKodu,
                OzelKod = c.OzelKod,
                Ulke = c.Ulke,
                Sinif = c.Sinif,
                AracVerilmez = c.AracVerilmez
            };
        }).ToList();

        return new PagedResult<CustomerRow>(rows, total, filter.Page, filter.PageSize);
    }

    public async Task<Customer?> FindAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var c = await db.Customers.AsNoTracking()
            .Include(x => x.Kisiler.OrderBy(k => k.Sira)) // PR-E: yetkili kişiler (edit formu doldurur), sıralı
            .FirstOrDefaultAsync(x => x.Id == id, ct);
        return c is null ? null : Decrypt(c);
    }

    public async Task<bool> TcKimlikHashExistsAsync(string tcHash, Guid? excludeId = null, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.Customers.AsNoTracking()
            .Where(c => c.TcKimlikHash == tcHash && (excludeId == null || c.Id != excludeId))
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
        var customer = await db.Customers
            .Include(c => c.Kisiler.OrderBy(k => k.Sira)) // PR-E: mevcut child'lar yüklü olmalı ki clear+add EF farkı doğru cascade etsin
            .FirstOrDefaultAsync(c => c.Id == id, ct);
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

    private DuplicateCariException? AsDuplicate(DbUpdateException ex, Customer c)
    {
        if (ex.InnerException is not PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } pg)
            return null;
        var constraint = pg.ConstraintName ?? string.Empty;
        if (constraint.Contains("TcKimlik", StringComparison.OrdinalIgnoreCase)) // TcKimlikHash indexi de eşleşir
            return new DuplicateCariException("TC Kimlik No", string.Empty); // L2: TC decrypt EDİLMEZ (mesajda gösterilmiyor)
        if (constraint.Contains("VergiNo", StringComparison.OrdinalIgnoreCase))
            return new DuplicateCariException("Vergi No", c.VergiNo ?? string.Empty);
        return new DuplicateCariException("kayıt", c.DisplayName);
    }
}
