using RentACar.Application.Common;
using RentACar.Domain.Common;
using RentACar.Domain.Entities;

namespace RentACar.Application.ReservationSources;

/// <summary>
/// Rezervasyon kaynağı master tanımı — <see cref="MasterTanimService{T}"/> ince alt sınıfı (O12d): doğrulama,
/// kod benzersizliği, CRUD, OperationsWrite guard ve liste cache ("reservation-sources") tabandan gelir;
/// burada yalnız <see cref="ReservationSourceInput"/> (kod, ad, aktif) üçlüsüne açılır. Dış yüzey değişmedi.
/// </summary>
public sealed class ReservationSourceService(IReservationSourceRepository repository, ICurrentUser currentUser, ITenantCache cache)
    : MasterTanimService<ReservationSource>(repository, currentUser, cache, "reservation-sources", "rezervasyon kaynağı")
{
    public Task<Guid> CreateAsync(ReservationSourceInput input, CancellationToken ct = default)
        => CreateCoreAsync(input.Kod, input.Ad, input.Aktif, ct);

    public Task<bool> UpdateAsync(Guid id, ReservationSourceInput input, CancellationToken ct = default)
        => UpdateCoreAsync(id, input.Kod, input.Ad, input.Aktif, ct);
}
