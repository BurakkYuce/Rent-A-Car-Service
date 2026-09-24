using RentACar.Domain.Entities;

namespace RentACar.Application.Branches;

public interface IBranchRepository : Common.IVersionedRepository<Branch>
{
    /// <summary>Tüm şubeler (yönetim ekranı). Koda göre sıralı.</summary>
    Task<IReadOnlyList<Branch>> ListAsync(CancellationToken ct = default);

    /// <summary>Yalnız aktif şubeler (açılır liste kaynağı).</summary>
    Task<IReadOnlyList<Branch>> ListActiveAsync(CancellationToken ct = default);

    Task<Branch?> FindAsync(Guid id, CancellationToken ct = default);

    /// <summary>Ada göre (tenant içi, case-insensitive) şube bul — F1 forward-resolution. Yoksa null.</summary>
    Task<Branch?> FindByAdAsync(string ad, CancellationToken ct = default);

    /// <summary>Tenant içinde aynı kod (büyük/küçük harf duyarsız) başka kayıtta var mı?</summary>
    Task<bool> KodExistsAsync(string kod, Guid? excludeId = null, CancellationToken ct = default);

    Task CreateAsync(Branch branch, CancellationToken ct = default);

    Task<bool> UpdateAsync(Guid id, Action<Branch> apply, CancellationToken ct = default);

    Task<bool> DeleteAsync(Guid id, CancellationToken ct = default);

    // ---- FAZ-23: şubeye özel ücretsiz hizmet (child) ----
    Task<IReadOnlyList<Domain.Entities.SubeUcretsizHizmet>> ListHizmetlerAsync(Guid subeId, CancellationToken ct = default);
    Task AddHizmetAsync(Domain.Entities.SubeUcretsizHizmet row, CancellationToken ct = default);
    Task<bool> RemoveHizmetAsync(Guid id, CancellationToken ct = default);

    // ---- FAZ-23: şube birleştirme ----
    /// <summary>Birleştirme ÖNİZLEMESİ: tablo başına etkilenecek kayıt sayısı (yazma YAPMAZ).</summary>
    Task<IReadOnlyList<(string Tablo, int Adet)>> BirlestirSayimAsync(Guid kaynakId, CancellationToken ct = default);

    /// <summary>
    /// Kaynak şubenin TÜM referanslarını hedefe taşır ve kaynağı PASİFE çeker (silmez).
    /// Hepsi TEK transaction. Dönen değer taşınan kayıt sayısıdır.
    /// </summary>
    Task<int> BirlestirAsync(Guid kaynakId, Guid hedefId, CancellationToken ct = default);
}
