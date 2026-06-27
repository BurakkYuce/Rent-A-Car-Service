using RentACar.Domain.Entities;

namespace RentACar.Application.Penalties;

public interface IPenaltyRepository
{
    Task<IReadOnlyList<Penalty>> ListAsync(CancellationToken ct = default);
    Task<Penalty?> FindAsync(Guid id, CancellationToken ct = default);

    /// <summary>No boşluksuz tahsis edip ekler.</summary>
    Task CreateAsync(Penalty penalty, CancellationToken ct = default);

    /// <summary>Durum geçişi (Öde/İptal vb.). Yansıtma için ReflectAsync kullan.</summary>
    Task<bool> UpdateAsync(Guid id, Action<Penalty> apply, CancellationToken ct = default);

    /// <summary>
    /// Yansıtma: cezayı Yansitildi'ye çevirir ve DENGELİ defter kümesini yazar — TEK
    /// transaction. Satır kilidiyle (FOR UPDATE) idempotenttir (zaten yansıtılmışsa false).
    /// </summary>
    Task<bool> ReflectAsync(
        Guid id, Func<Penalty, IReadOnlyList<AccountLedgerEntry>> buildEntries, CancellationToken ct = default);
}
