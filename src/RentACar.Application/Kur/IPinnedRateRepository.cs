using RentACar.Domain.Entities;

namespace RentACar.Application.Kur;

/// <summary>Tenant kur sabitleme CRUD (tenant-owned/RLS; izolasyon alt katmanda otomatik).</summary>
public interface IPinnedRateRepository
{
    Task<IReadOnlyList<SabitKur>> ListAsync(CancellationToken ct = default);
    Task<SabitKur?> FindAsync(Guid id, CancellationToken ct = default);

    /// <summary>Bu kod için AKTİF + tarih penceresinde geçerli sabit kur (yoksa null). Çözümleme kullanır.</summary>
    Task<SabitKur?> GetActiveAsync(string code, DateTimeOffset date, CancellationToken ct = default);

    Task<bool> CodeExistsAsync(string code, Guid? excludeId, CancellationToken ct = default);
    Task CreateAsync(SabitKur fixedValue, CancellationToken ct = default);
    Task<bool> UpdateAsync(SabitKur fixedValue, CancellationToken ct = default);
    Task<bool> DeleteAsync(Guid id, CancellationToken ct = default);

    /// <summary>F8.1a — satır sürümü (Postgres <c>xmin</c>, opak metin); satır yoksa null.</summary>
    Task<string?> GetVersionAsync(Guid id, CancellationToken ct = default);

    /// <summary>F8.1a — tam değiştirme: satır kilidi (<c>FOR UPDATE</c>) + kilit altında sürüm karşılaştırması +
    /// <paramref name="apply"/> TEK işlemde. Sürüm farklı → <c>EszamanliDegisiklikException</c>; satır yok → false.</summary>
    Task<bool> UpdateAsync(Guid id, string expectedVersion, Action<SabitKur> apply, CancellationToken ct = default);
}
