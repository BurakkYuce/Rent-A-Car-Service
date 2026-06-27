using RentACar.Domain.Entities;
using RentACar.Domain.Enums;

namespace RentACar.Application.ServiceRecords;

public interface IServiceRecordRepository
{
    Task<IReadOnlyList<ServiceRecord>> ListAsync(CancellationToken ct = default);
    Task<ServiceRecord?> FindAsync(Guid id, CancellationToken ct = default);

    /// <summary>No boşluksuz tahsis edip kalemlerle birlikte ekler (SRV-000001).</summary>
    Task CreateAsync(ServiceRecord record, CancellationToken ct = default);

    /// <summary>
    /// Durum geçişi + (opsiyonel) araç durumu eşlemesi — TEK transaction. Araç durumu yalnız
    /// <paramref name="onlyWhenVehicleIs"/> verildiyse o duruma eşitse değiştirilir (idempotent kuplaj).
    /// </summary>
    Task<bool> TransitionAsync(
        Guid id, Action<ServiceRecord> apply,
        VehicleStatus? setVehicleTo, VehicleStatus? onlyWhenVehicleIs, CancellationToken ct = default);

    /// <summary>İşçilik/parça kalemi ekler ve ToplamIscilik'i yeniden hesaplar (kapanmamış serviste).</summary>
    Task<bool> AddLineAsync(Guid id, string aciklama, decimal tutar, CancellationToken ct = default);
}
