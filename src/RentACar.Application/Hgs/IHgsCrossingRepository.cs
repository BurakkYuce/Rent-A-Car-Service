using RentACar.Application.Authorization;
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

    /// <summary>Plakalar + [from, to] aralığındaki mevcut geçişlerin özetleri (tam mükerrer ve olası mükerrer kararı).</summary>
    Task<IReadOnlyList<HgsCrossingRef>> ExistingAsync(
        IReadOnlyCollection<string> plates, DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default);

    /// <summary>
    /// Kilit altında mevcutları yeniden eler (tam mükerrer) ve olası mükerrer kararını YENİDEN verir (satırın
    /// <c>Durum</c>/<c>EslesmeNotu</c>'su yerinde güncellenir); yenileri ekler. Dönen: kilit altında "zaten var"
    /// çıkan satırların doğal anahtarları.
    /// </summary>
    Task<IReadOnlySet<string>> InsertAsync(IReadOnlyList<HgsCrossing> rows, CancellationToken ct = default);

    Task<IReadOnlyList<HgsCrossing>> ListAsync(HgsCrossingFilter filter, CancellationToken ct = default);

    Task<IReadOnlyList<HgsCrossing>> FindManyAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct = default);

    /// <summary>Yansıtılmamış geçişleri güncel araç/kira verisine ve olası mükerrer kuralına göre yeniden eşleştirir;
    /// değişen satır sayısı.</summary>
    Task<int> RematchAsync(CancellationToken ct = default);

    /// <summary>
    /// Kilit + satır kilidi (geçişler FOR UPDATE, kiraları FOR SHARE) altında: yalnız HÂLÂ eşleşmiş, kirası hâlâ geçiş
    /// anını kapsayan, olası mükerrer kümesinin ikincil üyesi olmayan satırlar için <paramref name="plan"/> çağrılır;
    /// defter kümesi ve satır durumu AYNI transaction'da yazılır. Kiranın şubesi <paramref name="scope"/> dışındaysa
    /// (kapsam kontrolünden sonra değişmiş) tüm istek reddedilir.
    /// </summary>
    /// <summary>
    /// Kilit + satır kilidi altında olası mükerrer notlu Belirsiz satırları, kirası hâlâ geçiş anını kapsıyorsa
    /// Eslesti yapar ve <c>OnayZamani</c>'nı yazar. Kiranın şubesi <paramref name="scope"/> dışındaysa tüm istek reddedilir.
    /// </summary>
    Task<HgsConfirmResult> ConfirmAsync(
        IReadOnlyCollection<Guid> ids, BranchScope.BranchFilter scope, DateTimeOffset now, CancellationToken ct = default);

    Task<HgsReflectResult> ReflectAsync(
        IReadOnlyCollection<Guid> ids, BranchScope.BranchFilter scope,
        Func<HgsCrossing, HgsRentalWindow, HgsReflectionPlan> plan, CancellationToken ct = default);
}
