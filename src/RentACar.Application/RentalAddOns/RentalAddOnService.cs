using RentACar.Application.Authorization;
using RentACar.Application.Common;
using RentACar.Application.EkHizmetler;
using RentACar.Application.Finance;
using RentACar.Domain.Common;
using RentACar.Domain.Entities;

namespace RentACar.Application.RentalAddOns;

/// <summary>
/// Kira ek hizmet kalemleri iş mantığı. Tanımdan tutar/oran SNAPSHOT alır, KDV'yi NET'ten
/// hesaplar (KdvMath.FromNet — kuruş tutarlı), kalemi ekler/çıkarır ve parent kira tutarlarını
/// repo içinde (tek transaction) yeniden hesaplar. Yazma operasyonel → OperationsWrite.
/// Faturalanmış kirada değişiklik engellenir (defter snapshot'ı ile tutarsızlık olmasın).
/// </summary>
public sealed class RentalAddOnService(
    IRentalAddOnRepository repository,
    IAddOnDefinitionRepository addOnRepository,
    Bookings.IBookingRepository bookingRepository,
    ICurrentUser currentUser)
{
    private readonly IRentalAddOnRepository _repository = repository;
    private readonly IAddOnDefinitionRepository _addOnRepository = addOnRepository;
    private readonly Bookings.IBookingRepository _bookings = bookingRepository;
    private readonly ICurrentUser _currentUser = currentUser;

    public Task<IReadOnlyList<RentalAddOn>> ListAsync(Guid rentalId, CancellationToken ct = default)
        => _repository.ListForRentalAsync(rentalId, ct);

    public async Task<Guid> AddAsync(
        Guid rentalId, Guid addOnDefinitionId, decimal quantity,
        decimal? unitNetOverride = null, decimal? vatRateOverride = null,
        bool system = false, CancellationToken ct = default, Guid? staffId = null, Guid? operationKey = null)
    {
        PermissionGuard.Require(_currentUser, Permission.OperationsWrite);
        // FAZ 5-C4 adversarial bulgusu (önceden var olan açık): ek hizmet, şube-kapsam guard'sız TEK
        // booking-mutasyon yüzeyiydi — operatör GÖREMEDİĞİ çapraz-şube kiranın GenelToplam/Bakiye'sini
        // değiştirebiliyordu. Kural diğer kira guard'larıyla birebir: (CikisSubeId, CikisOfisi).
        // sistem=true atlanır: FeeLineService create/reprice içinden çağırır — create tarihsel olarak
        // kapsam-guard'sız, reprice yollarıysa üstte zaten guard'lı (çift sorguya gerek yok).
        if (!system && await _bookings.FindRentalAsync(rentalId, ct) is { } rental)
            BranchScope.RequireInScope(_currentUser, rental.CikisSubeId, rental.CikisOfisi);
        // Low-B idempotency (DEVIR §5 sırası): kapsamdan SONRA, tüm iş kuralı/durum çitlerinden ÖNCE "bu anahtarla
        // kayıt var mı". Önce: anahtarsız çift gönderim İKİ kalem yazıyordu; kaybolan yanıttan sonraki doğru tekrar,
        // arada kira faturalanınca 400 "faturalanmış" alırdı. Eşzamanlı yarışı repo kira kilidi altında yeniden
        // denetler; en son savunma kısmi unique index (IdempotencyKisiti → 409 mükerrer).
        if (!system && operationKey is { } key
            && await _repository.FindByOperationKeyAsync(key, ct) is { } previous)
            throw Duplicate(previous, rentalId, addOnDefinitionId, quantity);
        if (quantity <= 0) throw new ValidationException("Miktar sıfırdan büyük olmalıdır.");
        // Taşma guard'ı (adversarial PR-B): devasa miktar round(birim×miktar)'da OverflowException → 500
        // üretiyordu; canlı-hesap ucuyla simetrik gerçekçi üst sınır.
        if (quantity > 100_000m) throw new ValidationException("Miktar gerçekçi değil (100.000 üstü).");

        var definition = await _addOnRepository.FindAsync(addOnDefinitionId, ct)
            ?? throw new ValidationException("Ek hizmet tanımı bulunamadı.");
        // FAZ 3.A3a adversarial B4: SYS-* tanımları yalnız FeeLineService yazar — manuel/matris yolundan
        // eklenirse sistem satırının yanına ikinci satır biner (çift ücret) → temiz red.
        if (!system && definition.Kod.StartsWith("SYS-", StringComparison.OrdinalIgnoreCase))
            throw new ValidationException("Sistem ücret kalemi manuel eklenemez (otomatik hesaplanır).");

        var unitNet = unitNetOverride ?? definition.BirimUcret;
        var rate = vatRateOverride ?? definition.KdvOrani;
        if (unitNet < 0) throw new ValidationException("Birim ücret negatif olamaz.");
        if (rate is < 0m or > 1m) throw new ValidationException("KDV oranı 0 ile 1 arasında olmalıdır.");

        if (await _repository.IsRentalInvoicedAsync(rentalId, ct))
            throw new ValidationException("Faturalanmış kiraya ek hizmet eklenemez.");

        // NET → KDV/brüt (kuruş tutarlı). Net = round(birim × miktar, 2).
        var net = Math.Round(unitNet * quantity, 2, MidpointRounding.AwayFromZero);
        var (vat, gross) = VatMath.FromNet(net, rate);

        var addOn = new RentalAddOn
        {
            RentalId = rentalId,
            EkHizmetTanimId = addOnDefinitionId,
            Ad = definition.Ad,
            Miktar = quantity,
            BirimNetFiyat = unitNet,
            KdvOrani = rate,
            NetTutar = net,
            KdvTutar = vat,
            Toplam = gross,
            // FAZ-78: satan personel. Sistem kalemlerinde (SYS-*) daima boş — onları kimse satmaz.
            PersonelId = system ? null : staffId,
            IslemAnahtari = system ? null : operationKey
        };
        await _repository.AddAsync(addOn, ct);
        return addOn.Id;
    }

    /// <summary>
    /// Low-B: aynı anahtarla ZATEN yazılmış kalem → 409 <c>mukerrer</c>. Kalem bu kiraya aitse <c>mevcut</c> döner
    /// (<c>AyniIcerik</c>: tanım + miktar birebir → kaybolan yanıttan sonraki kendi tekrarı; değilse gelen kalem
    /// YAZILMADI). Başka kiraya aitse <c>mevcut</c> dönmez (bilgi sızmaz) — farklı içerik 409'u.
    /// </summary>
    public static DuplicateOperationException Duplicate(RentalAddOn previous, Guid rentalId, Guid addOnDefinitionId, decimal quantity)
    {
        if (previous.RentalId != rentalId) return DuplicateOperationException.DifferentContent();
        var same = previous.EkHizmetTanimId == addOnDefinitionId
                   && Math.Round(previous.Miktar, 4, MidpointRounding.AwayFromZero)
                      == Math.Round(quantity, 4, MidpointRounding.AwayFromZero);
        return new DuplicateOperationException(
            same
                ? $"Bu ek hizmet zaten eklendi ({previous.Ad}, miktar {previous.Miktar:0.####})."
                : DuplicateOperationException.DifferentContentMessage,
            new MevcutIslem(previous.Id, previous.Ad, previous.Toplam, "TRY", same));
    }

    public async Task<bool> RemoveAsync(Guid addOnId, CancellationToken ct = default)
    {
        PermissionGuard.Require(_currentUser, Permission.OperationsWrite);
        // Şube-kapsam guard'ı (bkz. AddAsync) — silme de web'e açık mutasyon yüzeyi.
        var addOn = await _repository.FindAsync(addOnId, ct);
        if (addOn is null) return false;
        if (await _bookings.FindRentalAsync(addOn.RentalId, ct) is { } rental)
            BranchScope.RequireInScope(_currentUser, rental.CikisSubeId, rental.CikisOfisi);
        return await _repository.RemoveAsync(addOnId, ct);
    }
}
