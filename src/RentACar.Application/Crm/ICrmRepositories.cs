using RentACar.Domain.Entities;

namespace RentACar.Application.Crm;

public interface IAnketRepository
{
    Task<IReadOnlyList<Anket>> ListAsync(CancellationToken ct = default);
    /// <summary>FAZ-42 filtreli liste.</summary>
    Task<IReadOnlyList<Anket>> ListAsync(AnketFilter filtre, CancellationToken ct = default);
    Task<Anket?> FindAsync(Guid id, CancellationToken ct = default);
    /// <summary>FAZ-42 — anketin cevap satırları (soru sırasına göre).</summary>
    Task<IReadOnlyList<AnketCevap>> ListCevapAsync(Guid anketId, CancellationToken ct = default);
    /// <summary>FAZ-42 — anket + cevapları TEK transaction'da (yarım anket kalmasın).</summary>
    Task CreateWithCevapAsync(Anket anket, IReadOnlyList<AnketCevap> cevaplar, CancellationToken ct = default);
    /// <summary>FAZ-42 — cevapları TAMAMEN değiştirir (sil + yaz), anket alanlarıyla tek transaction.</summary>
    Task<bool> UpdateWithCevapAsync(Guid id, Action<Anket> apply, IReadOnlyList<AnketCevap> cevaplar,
        CancellationToken ct = default);
    Task CreateAsync(Anket row, CancellationToken ct = default);
    Task<bool> UpdateAsync(Guid id, Action<Anket> apply, CancellationToken ct = default);
    Task<bool> DeleteAsync(Guid id, CancellationToken ct = default);
}

public interface ISikayetRepository
{
    Task<IReadOnlyList<Sikayet>> ListAsync(CancellationToken ct = default);
    Task<Sikayet?> FindAsync(Guid id, CancellationToken ct = default);

    /// <summary>
    /// FAZ-43 — filtreli liste; sözleşme/araç/müşteri/personel adları ÇÖZÜLMÜŞ döner.
    /// <paramref name="filter"/> null → tüm kayıtlar.
    /// </summary>
    Task<IReadOnlyList<SikayetSatirDto>> SearchAsync(SikayetFilter? filter = null, CancellationToken ct = default);
    Task CreateAsync(Sikayet row, CancellationToken ct = default);
    Task<bool> UpdateAsync(Guid id, Action<Sikayet> apply, CancellationToken ct = default);
    Task<bool> DeleteAsync(Guid id, CancellationToken ct = default);
}
