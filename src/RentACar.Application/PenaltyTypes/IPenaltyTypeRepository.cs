using RentACar.Domain.Entities;

namespace RentACar.Application.PenaltyTypes;

public interface IPenaltyTypeRepository
{
    /// <summary>Tüm ceza türleri (yönetim ekranı). Koda göre sıralı.</summary>
    Task<IReadOnlyList<PenaltyType>> ListAsync(CancellationToken ct = default);

    /// <summary>Yalnız aktif türler (form açılır listesi kaynağı).</summary>
    Task<IReadOnlyList<PenaltyType>> ListActiveAsync(CancellationToken ct = default);

    Task<PenaltyType?> FindAsync(Guid id, CancellationToken ct = default);

    /// <summary>Tenant içinde aynı kod (büyük/küçük harf duyarsız) başka kayıtta var mı?</summary>
    Task<bool> KodExistsAsync(string kod, Guid? excludeId = null, CancellationToken ct = default);

    Task CreateAsync(PenaltyType type, CancellationToken ct = default);

    Task<bool> UpdateAsync(Guid id, Action<PenaltyType> apply, CancellationToken ct = default);

    /// <summary>F11.1b — satır kilidi + iyimser sürüm karşılaştırması; uyuşmazlık <c>EszamanliDegisiklikException</c>.</summary>
    Task<bool> UpdateAsync(Guid id, string? expectedVersion, Action<PenaltyType> apply, CancellationToken ct = default);

    /// <summary>F11.1b — satır sürümü (Postgres <c>xmin</c>, opak). Yoksa <c>null</c>.</summary>
    Task<string?> RowVersionAsync(Guid id, CancellationToken ct = default);

    Task<bool> DeleteAsync(Guid id, CancellationToken ct = default);
}
