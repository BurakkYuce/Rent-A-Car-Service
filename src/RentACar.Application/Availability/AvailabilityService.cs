using RentACar.Application.Authorization;
using RentACar.Application.Common;
using RentACar.Domain.Common;
using RentACar.Domain.Entities;

namespace RentACar.Application.Availability;

/// <summary>
/// Araç müsaitlik arama: tarih aralığı (+ opsiyonel grup/şube) → kiralanabilir araçlar.
/// Rol bazlı şube kapsamı uygulanır (operatör yalnız kendi şubesi). Rezervasyon açmanın
/// doğal girişi.
/// </summary>
public sealed class AvailabilityService(IAvailabilityRepository repository, ICurrentUser currentUser)
{
    private readonly IAvailabilityRepository _repository = repository;
    private readonly ICurrentUser _currentUser = currentUser;

    public async Task<IReadOnlyList<Vehicle>> FindAvailableAsync(
        DateTimeOffset from, DateTimeOffset to, string? grup = null, string? sube = null, CancellationToken ct = default)
    {
        if (to <= from) throw new ValidationException("Bitiş tarihi başlangıçtan sonra olmalıdır.");

        // C3: kapsam FK-farkındalı zorlanır. MEVCUT SEMANTİK korunur: kapsamlı operatörün UI şube
        // seçimi YOK SAYILIR (kendi şubesi gösterilir — kesişim değil; AvailabilityTests kilitli davranış).
        var kapsam = BranchScope.EffectiveFilter(_currentUser);
        var effectiveSube = kapsam.Unrestricted
            ? (string.IsNullOrWhiteSpace(sube) ? null : sube.Trim())
            : null;
        var effectiveGrup = string.IsNullOrWhiteSpace(grup) ? null : grup.Trim();

        return await _repository.GetAvailableAsync(from, to, effectiveGrup, effectiveSube, kapsam, ct);
    }
}
