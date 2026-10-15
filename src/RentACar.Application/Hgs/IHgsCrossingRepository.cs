using RentACar.Domain.Entities;

namespace RentACar.Application.Hgs;

/// <summary>Yansıtılacak geçiş için defter planı (servis kurar; depo kilit altında uygular).</summary>
public sealed record HgsReflectionPlan(IReadOnlyList<AccountLedgerEntry> Entries, decimal Amount, decimal Rate);

/// <summary>
/// HGS geçiş kalıcılığı. Yazan yolların hepsi aynı kiracı-kapsamlı danışma kilidini alır (içe aktarma, yeniden
/// eşleştirme, yansıtma birbirini sıraya sokar).
/// </summary>
public interface IHgsCrossingRepository
{
    Task<IReadOnlyList<HgsVehicleRef>> ListVehiclesAsync(CancellationToken ct = default);

    /// <summary>Verilen araçların [from, to] ile kesişen, iptal edilmemiş kiraları.</summary>
    Task<IReadOnlyList<HgsRentalWindow>> ListRentalWindowsAsync(
        IReadOnlyCollection<Guid> vehicleIds, DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default);

    /// <summary>Plakalar + zaman aralığındaki mevcut geçişlerin doğal anahtarları (önizleme "mükerrer" sayısı).</summary>
    Task<IReadOnlySet<string>> ExistingKeysAsync(
        IReadOnlyCollection<string> plates, DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default);

    /// <summary>
    /// Kilit altında mevcutları yeniden eler ve yenileri ekler. Dönen: kilit altında "zaten var" çıkan satırların
    /// doğal anahtarları (önizlemeden sonra başka bir yükleme aynı geçişleri eklemiş olabilir).
    /// </summary>
    Task<IReadOnlySet<string>> InsertAsync(IReadOnlyList<HgsCrossing> rows, CancellationToken ct = default);

    Task<IReadOnlyList<HgsCrossing>> ListAsync(HgsCrossingFilter filter, CancellationToken ct = default);

    Task<IReadOnlyList<HgsCrossing>> FindManyAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct = default);

    /// <summary>Yansıtılmamış geçişleri güncel araç/kira verisine göre yeniden eşleştirir; değişen satır sayısı.</summary>
    Task<int> RematchAsync(CancellationToken ct = default);

    /// <summary>
    /// Kilit + satır kilidi (FOR UPDATE) altında: yalnız HÂLÂ eşleşmiş ve kirası hâlâ geçiş anını kapsayan satırlar
    /// için <paramref name="plan"/> çağrılır; defter kümesi ve satır durumu AYNI transaction'da yazılır.
    /// </summary>
    Task<HgsReflectResult> ReflectAsync(
        IReadOnlyCollection<Guid> ids, Func<HgsCrossing, HgsRentalWindow, HgsReflectionPlan> plan,
        CancellationToken ct = default);
}
