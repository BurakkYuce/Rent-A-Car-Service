using Microsoft.EntityFrameworkCore;
using Npgsql;
using RentACar.Application.Branches;
using RentACar.Application.Common;
using RentACar.Domain.Entities;

namespace RentACar.Infrastructure.Persistence.Repositories;

/// <summary>
/// IBranchRepository: kısa-ömürlü context'ler (factory). Tenant izolasyonu RLS + global
/// query filter ile otomatik. Kod benzersizliği DB'de kısmi/normal unique index ile
/// güvencededir; yarış ihlali (23505) ValidationException'a çevrilir.
/// </summary>
public sealed class BranchRepository(IDbContextFactory<AppDbContext> factory) : IBranchRepository
{
    private readonly IDbContextFactory<AppDbContext> _factory = factory;

    public async Task<IReadOnlyList<Branch>> ListAsync(CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.Branches.AsNoTracking().OrderBy(b => b.Kod).ToListAsync(ct);
    }

    public async Task<IReadOnlyList<Branch>> ListActiveAsync(CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.Branches.AsNoTracking().Where(b => b.Aktif).OrderBy(b => b.Kod).ToListAsync(ct);
    }

    public async Task<Branch?> FindAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.Branches.AsNoTracking().FirstOrDefaultAsync(b => b.Id == id, ct);
    }

    public async Task<Branch?> FindByAdAsync(string ad, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        // Tenant-scoped (query filter + RLS) + EXACT case-insensitive eşleşme (adversarial M2: ILike pattern
        // "M_rkez"/"%" yanlış eşleşiyordu + backfill exact ile uyumsuzdu). lower()=lower() backfill ile birebir.
        // Kod sırası → aynı adlı şubede deterministik seçim (L2).
        var norm = ad.Trim().ToLowerInvariant();
        return await db.Branches.AsNoTracking()
            .Where(b => b.Ad.ToLower() == norm)
            .OrderBy(b => b.Kod)
            .FirstOrDefaultAsync(ct);
    }

    public async Task<bool> KodExistsAsync(string kod, Guid? excludeId = null, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var k = kod.Trim().ToUpperInvariant();
        return await db.Branches.AsNoTracking()
            .Where(b => b.Kod == k && (excludeId == null || b.Id != excludeId))
            .AnyAsync(ct);
    }

    public async Task CreateAsync(Branch branch, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        db.Branches.Add(branch);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            throw new ValidationException($"'{branch.Kod}' kodlu şube zaten var.");
        }
    }

    public async Task<bool> UpdateAsync(Guid id, Action<Branch> apply, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var branch = await db.Branches.FirstOrDefaultAsync(b => b.Id == id, ct);
        if (branch is null) return false;

        apply(branch);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            throw new ValidationException($"'{branch.Kod}' kodlu şube zaten var.");
        }
        return true;
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var branch = await db.Branches.FirstOrDefaultAsync(b => b.Id == id, ct);
        if (branch is null) return false;

        db.Branches.Remove(branch);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.ForeignKeyViolation })
        {
            // roadmap F1: composite FK Restrict → kullanımdaki şube silinemez (dostça hata).
            throw new ValidationException("Bu şube araç/gider/personel/kural kaydında kullanılıyor; önce bağı kaldırın.");
        }
        return true;
    }

    // ---- FAZ-23: şubeye özel ücretsiz hizmet ----

    public async Task<IReadOnlyList<SubeUcretsizHizmet>> ListHizmetlerAsync(Guid subeId, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.SubeUcretsizHizmetler.AsNoTracking()
            .Where(x => x.SubeId == subeId).OrderBy(x => x.HizmetAdi).ToListAsync(ct);
    }

    public async Task AddHizmetAsync(SubeUcretsizHizmet row, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        db.SubeUcretsizHizmetler.Add(row);
        await db.SaveChangesAsync(ct);
    }

    public async Task<bool> RemoveHizmetAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var row = await db.SubeUcretsizHizmetler.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (row is null) return false;
        db.SubeUcretsizHizmetler.Remove(row);
        await db.SaveChangesAsync(ct);
        return true;
    }

    // ---- FAZ-23: şube birleştirme ----
    //
    // KAPSAM NOTU: şubeye HEM FK (SubeId) HEM METİN (Sube) ile referans veren tablolar var.
    // Yalnız birkaçını taşımak, geri kalanları PASİFE ÇEKİLMİŞ bir şubeye bağlı bırakır ve
    // şube-kapsamı (BranchScope) o kayıtları kimseye göstermez — sessiz veri kaybı gibi davranır.
    // Bu yüzden AŞAĞIDAKİ LİSTE, şubeye referans veren TÜM tabloları kapsar. Yeni bir tablo
    // şube referansı eklerse buraya da eklenmelidir (SubeBirlestirmeKapsamTests bunu kilitler).

    public async Task<IReadOnlyList<(string Tablo, int Adet)>> BirlestirSayimAsync(
        Guid kaynakId, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var kaynak = await db.Branches.AsNoTracking().FirstOrDefaultAsync(b => b.Id == kaynakId, ct);
        if (kaynak is null) return [];

        var sonuc = new List<(string, int)>();
        async Task Say<T>(string etiket, IQueryable<T> q) where T : class
        {
            var n = await q.CountAsync(ct);
            if (n > 0) sonuc.Add((etiket, n));
        }

        await Say("Araç", db.Vehicles.Where(x => x.SubeId == kaynakId));
        await Say("Kira kuralı", db.RentalRules.Where(x => x.SubeId == kaynakId));
        await Say("Rezervasyon", db.Reservations.Where(x => x.CikisSubeId == kaynakId));
        await Say("Teklif", db.Quotations.Where(x => x.CikisSubeId == kaynakId));
        await Say("Personel", db.Personeller.Where(x => x.SubeId == kaynakId));
        await Say("Kira sözleşmesi", db.Rentals.Where(x => x.CikisSubeId == kaynakId));
        await Say("Lokasyon", db.Locations.Where(x => x.SubeId == kaynakId));
        await Say("Tarife matrisi", db.RateMatrices.Where(x => x.SubeId == kaynakId));
        await Say("Doluluk fiyat kuralı", db.DolulukFiyatKurallari.Where(x => x.SubeId == kaynakId));   // FAZ-73
        await Say("Araç (metin şube)", db.Vehicles.Where(x => x.Sube != null && x.Sube == kaynak.Ad));
        await Say("Kira kuralı (metin)", db.RentalRules.Where(x => x.Sube != null && x.Sube == kaynak.Ad));
        await Say("BAF (metin)", db.Baflar.Where(x => x.Sube != null && x.Sube == kaynak.Ad));
        // FAZ-18: dönüş şubesi de metin bir şube referansıdır → birleştirmede taşınmalı, yoksa
        // kaynak şube adı BAF dönüş alanında "hayalet" olarak kalır.
        await Say("BAF dönüş şubesi (metin)", db.Baflar.Where(x => x.DonusSube != null && x.DonusSube == kaynak.Ad));
        await Say("Personel (metin)", db.Personeller.Where(x => x.Sube != null && x.Sube == kaynak.Ad));
        await Say("Lokasyon (metin)", db.Locations.Where(x => x.Sube != null && x.Sube == kaynak.Ad));
        await Say("Hesap (metin)", db.FinancialAccounts.Where(x => x.Sube != null && x.Sube == kaynak.Ad));
        await Say("Tarife matrisi (metin)", db.RateMatrices.Where(x => x.Sube != null && x.Sube == kaynak.Ad));
        await Say("Doluluk fiyat kuralı (metin)", db.DolulukFiyatKurallari.Where(x => x.Sube != null && x.Sube == kaynak.Ad));   // FAZ-73
        await Say("Cari virman künyesi (metin)", db.CariVirmanBilgileri.Where(x => x.Sube != null && x.Sube == kaynak.Ad));
        // FAZ-50 — kasa/banka virman künyesi. Künye PARA TAŞIMAZ (mali belge değil) → gider gibi
        // "taşınmaz" değil, cari virman künyesiyle AYNI davranır: şube adı hedefe taşınır.
        await Say("Kasa virman künyesi (metin)", db.KasaVirmanBilgileri.Where(x => x.Sube != null && x.Sube == kaynak.Ad));
        await Say("Site talebi (metin)", db.SiteTalepleri.Where(x => x.Sube != null && x.Sube == kaynak.Ad));
        await Say("Drop tanımı (metin)", db.DropTanimlari.Where(x => x.Sube == kaynak.Ad));
        await Say("Kullanıcı (atanmış şube)", db.Users.Where(u => u.AtanmisSubeId == kaynakId));
        await Say("Şube ücretsiz hizmeti", db.SubeUcretsizHizmetler.Where(x => x.SubeId == kaynakId));
        await Say("Personel vardiyası", db.PersonelVardiyalari.Where(x => x.SubeId == kaynakId));   // FAZ-45
        await Say("Personel vardiyası (metin)", db.PersonelVardiyalari.Where(x => x.Sube != null && x.Sube == kaynak.Ad));
        // FAZ-60: ceza "İşlem Şube" metni. Ceza mali belge DEĞİL (başlık güncellenebilir), taşınır.
        await Say("Ceza (işlem şube metni)", db.Penalties.Where(x => x.IslemSube != null && x.IslemSube == kaynak.Ad));

        // GİDER TAŞINMAZ. Expense DEĞİŞMEZ bir mali belgedir: DB'de değişmezlik trigger'ı var ve
        // racar_app'in UPDATE yetkisi yok. Zaten olmamalı da — kesilmiş bir gider belgesinin şubesini
        // geriye dönük değiştirmek muhasebe kaydını tahrif etmek olurdu. Kullanıcı bunu ÖNİZLEMEDE
        // görür ve kaynak şube pasife çekilse de o giderler adıyla birlikte yerinde kalır.
        var gider = await db.Expenses.CountAsync(
            x => x.SubeId == kaynakId || (x.Sube != null && x.Sube == kaynak.Ad), ct);
        if (gider > 0) sonuc.Add(("Gider (TAŞINMAZ — değişmez mali belge)", gider));

        return sonuc;
    }

    public async Task<int> BirlestirAsync(Guid kaynakId, Guid hedefId, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var kaynak = await db.Branches.FirstOrDefaultAsync(b => b.Id == kaynakId, ct)
            ?? throw new ValidationException("Kaynak şube bulunamadı.");
        var hedef = await db.Branches.AsNoTracking().FirstOrDefaultAsync(b => b.Id == hedefId, ct)
            ?? throw new ValidationException("Hedef şube bulunamadı.");

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var toplam = 0;

            toplam += await db.Vehicles.Where(x => x.SubeId == kaynakId)
                .ExecuteUpdateAsync(u => u.SetProperty(x => x.SubeId, (Guid?)hedefId), ct);
            toplam += await db.RentalRules.Where(x => x.SubeId == kaynakId)
                .ExecuteUpdateAsync(u => u.SetProperty(x => x.SubeId, (Guid?)hedefId), ct);
            toplam += await db.Reservations.Where(x => x.CikisSubeId == kaynakId)
                .ExecuteUpdateAsync(u => u.SetProperty(x => x.CikisSubeId, (Guid?)hedefId), ct);
            toplam += await db.Quotations.Where(x => x.CikisSubeId == kaynakId)
                .ExecuteUpdateAsync(u => u.SetProperty(x => x.CikisSubeId, (Guid?)hedefId), ct);
            toplam += await db.Personeller.Where(x => x.SubeId == kaynakId)
                .ExecuteUpdateAsync(u => u.SetProperty(x => x.SubeId, (Guid?)hedefId), ct);
            toplam += await db.Rentals.Where(x => x.CikisSubeId == kaynakId)
                .ExecuteUpdateAsync(u => u.SetProperty(x => x.CikisSubeId, (Guid?)hedefId), ct);
            toplam += await db.Locations.Where(x => x.SubeId == kaynakId)
                .ExecuteUpdateAsync(u => u.SetProperty(x => x.SubeId, (Guid?)hedefId), ct);
            toplam += await db.RateMatrices.Where(x => x.SubeId == kaynakId)
                .ExecuteUpdateAsync(u => u.SetProperty(x => x.SubeId, (Guid?)hedefId), ct);
            toplam += await db.DolulukFiyatKurallari.Where(x => x.SubeId == kaynakId)   // FAZ-73
                .ExecuteUpdateAsync(u => u.SetProperty(x => x.SubeId, (Guid?)hedefId), ct);
            toplam += await db.PersonelVardiyalari.Where(x => x.SubeId == kaynakId)   // FAZ-45
                .ExecuteUpdateAsync(u => u.SetProperty(x => x.SubeId, (Guid?)hedefId), ct);
            toplam += await db.Vehicles.Where(x => x.Sube != null && x.Sube == kaynak.Ad)
                .ExecuteUpdateAsync(u => u.SetProperty(x => x.Sube, hedef.Ad), ct);
            toplam += await db.RentalRules.Where(x => x.Sube != null && x.Sube == kaynak.Ad)
                .ExecuteUpdateAsync(u => u.SetProperty(x => x.Sube, hedef.Ad), ct);
            toplam += await db.Baflar.Where(x => x.Sube != null && x.Sube == kaynak.Ad)
                .ExecuteUpdateAsync(u => u.SetProperty(x => x.Sube, hedef.Ad), ct);
            toplam += await db.Baflar.Where(x => x.DonusSube != null && x.DonusSube == kaynak.Ad)   // FAZ-18
                .ExecuteUpdateAsync(u => u.SetProperty(x => x.DonusSube, hedef.Ad), ct);
            toplam += await db.Personeller.Where(x => x.Sube != null && x.Sube == kaynak.Ad)
                .ExecuteUpdateAsync(u => u.SetProperty(x => x.Sube, hedef.Ad), ct);
            toplam += await db.Locations.Where(x => x.Sube != null && x.Sube == kaynak.Ad)
                .ExecuteUpdateAsync(u => u.SetProperty(x => x.Sube, hedef.Ad), ct);
            toplam += await db.FinancialAccounts.Where(x => x.Sube != null && x.Sube == kaynak.Ad)
                .ExecuteUpdateAsync(u => u.SetProperty(x => x.Sube, hedef.Ad), ct);
            toplam += await db.RateMatrices.Where(x => x.Sube != null && x.Sube == kaynak.Ad)
                .ExecuteUpdateAsync(u => u.SetProperty(x => x.Sube, hedef.Ad), ct);
            toplam += await db.DolulukFiyatKurallari.Where(x => x.Sube != null && x.Sube == kaynak.Ad)   // FAZ-73
                .ExecuteUpdateAsync(u => u.SetProperty(x => x.Sube, hedef.Ad), ct);
            toplam += await db.CariVirmanBilgileri.Where(x => x.Sube != null && x.Sube == kaynak.Ad)
                .ExecuteUpdateAsync(u => u.SetProperty(x => x.Sube, hedef.Ad), ct);
            toplam += await db.KasaVirmanBilgileri.Where(x => x.Sube != null && x.Sube == kaynak.Ad)   // FAZ-50
                .ExecuteUpdateAsync(u => u.SetProperty(x => x.Sube, hedef.Ad), ct);
            toplam += await db.SiteTalepleri.Where(x => x.Sube != null && x.Sube == kaynak.Ad)
                .ExecuteUpdateAsync(u => u.SetProperty(x => x.Sube, hedef.Ad), ct);
            toplam += await db.PersonelVardiyalari.Where(x => x.Sube != null && x.Sube == kaynak.Ad)   // FAZ-45
                .ExecuteUpdateAsync(u => u.SetProperty(x => x.Sube, hedef.Ad), ct);

        toplam += await db.DropTanimlari.Where(x => x.Sube == kaynak.Ad)
                .ExecuteUpdateAsync(u => u.SetProperty(x => x.Sube, hedef.Ad), ct);

        // FAZ-60 — ceza "İşlem Şube" metni (Penalties).
        toplam += await db.Penalties.Where(x => x.IslemSube != null && x.IslemSube == kaynak.Ad)
                .ExecuteUpdateAsync(u => u.SetProperty(x => x.IslemSube, hedef.Ad), ct);

        // Users PLATFORM tablosu (RLS yok, query filter yok) → tenant koşulu AÇIKÇA yazılır;
        // yoksa başka firmaların kullanıcıları da güncellenirdi.
        var tenant = db.TenantId;
        toplam += await db.Users.Where(u => u.TenantId == tenant && u.AtanmisSubeId == kaynakId)
            .ExecuteUpdateAsync(u => u
                .SetProperty(x => x.AtanmisSubeId, (Guid?)hedefId)
                .SetProperty(x => x.AtanmisSube, hedef.Ad), ct);

        // Child kayıtlar da taşınır (şube silinmiyor ama pasif şubede kalmaları anlamsız).
        toplam += await db.SubeUcretsizHizmetler.Where(x => x.SubeId == kaynakId)
            .ExecuteUpdateAsync(u => u.SetProperty(x => x.SubeId, hedefId), ct);

        // Kaynak şube SİLİNMEZ — pasife çekilir. Silmek geçmiş kayıtların adını çözümsüz bırakırdı.
        kaynak.Aktif = false;
        kaynak.UpdatedAtUtc = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return toplam;
    }

    // F11.1a — IVersionedRepository<Branch> (generic RowVersion helper).
    public Task<string?> GetVersionAsync(Guid id, CancellationToken ct = default) => RowVersion.ReadAsync<Branch>(_factory, id, ct);

    public Task<IReadOnlyDictionary<Guid, string>> GetVersionsAsync(CancellationToken ct = default) => RowVersion.ReadAllAsync<Branch>(_factory, ct);

    public Task<bool> UpdateAsync(Guid id, string expectedVersion, Action<Branch> apply, CancellationToken ct = default)
        => RowVersion.UpdateAsync(_factory, id, expectedVersion, apply, b => $"'{b.Kod}' kodlu şube zaten var.", ct);
}
