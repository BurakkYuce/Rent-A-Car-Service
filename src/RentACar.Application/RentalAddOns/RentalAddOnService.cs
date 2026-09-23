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
    IEkHizmetTanimRepository ekHizmetRepository,
    Bookings.IBookingRepository bookingRepository,
    ICurrentUser currentUser)
{
    private readonly IRentalAddOnRepository _repository = repository;
    private readonly IEkHizmetTanimRepository _ekHizmetRepository = ekHizmetRepository;
    private readonly Bookings.IBookingRepository _bookings = bookingRepository;
    private readonly ICurrentUser _currentUser = currentUser;

    public Task<IReadOnlyList<RentalAddOn>> ListAsync(Guid rentalId, CancellationToken ct = default)
        => _repository.ListForRentalAsync(rentalId, ct);

    public async Task<Guid> AddAsync(
        Guid rentalId, Guid ekHizmetTanimId, decimal miktar,
        decimal? birimNetOverride = null, decimal? kdvOraniOverride = null,
        bool sistem = false, CancellationToken ct = default, Guid? personelId = null, Guid? islemAnahtari = null)
    {
        PermissionGuard.Require(_currentUser, Permission.OperationsWrite);
        // FAZ 5-C4 adversarial bulgusu (önceden var olan açık): ek hizmet, şube-kapsam guard'sız TEK
        // booking-mutasyon yüzeyiydi — operatör GÖREMEDİĞİ çapraz-şube kiranın GenelToplam/Bakiye'sini
        // değiştirebiliyordu. Kural diğer kira guard'larıyla birebir: (CikisSubeId, CikisOfisi).
        // sistem=true atlanır: FeeLineService create/reprice içinden çağırır — create tarihsel olarak
        // kapsam-guard'sız, reprice yollarıysa üstte zaten guard'lı (çift sorguya gerek yok).
        if (!sistem && await _bookings.FindRentalAsync(rentalId, ct) is { } kira)
            BranchScope.RequireInScope(_currentUser, kira.CikisSubeId, kira.CikisOfisi);
        // Low-B idempotency (DEVIR §5 sırası): kapsamdan SONRA, tüm iş kuralı/durum çitlerinden ÖNCE "bu anahtarla
        // kayıt var mı". Önce: anahtarsız çift gönderim İKİ kalem yazıyordu; kaybolan yanıttan sonraki doğru tekrar,
        // arada kira faturalanınca 400 "faturalanmış" alırdı. Eşzamanlı yarışı repo kira kilidi altında yeniden
        // denetler; en son savunma kısmi unique index (IdempotencyKisiti → 409 mükerrer).
        if (!sistem && islemAnahtari is { } anahtar
            && await _repository.FindByIslemAnahtariAsync(anahtar, ct) is { } onceki)
            throw Mukerrer(onceki, rentalId, ekHizmetTanimId, miktar);
        if (miktar <= 0) throw new ValidationException("Miktar sıfırdan büyük olmalıdır.");
        // Taşma guard'ı (adversarial PR-B): devasa miktar round(birim×miktar)'da OverflowException → 500
        // üretiyordu; canlı-hesap ucuyla simetrik gerçekçi üst sınır.
        if (miktar > 100_000m) throw new ValidationException("Miktar gerçekçi değil (100.000 üstü).");

        var tanim = await _ekHizmetRepository.FindAsync(ekHizmetTanimId, ct)
            ?? throw new ValidationException("Ek hizmet tanımı bulunamadı.");
        // FAZ 3.A3a adversarial B4: SYS-* tanımları yalnız FeeLineService yazar — manuel/matris yolundan
        // eklenirse sistem satırının yanına ikinci satır biner (çift ücret) → temiz red.
        if (!sistem && tanim.Kod.StartsWith("SYS-", StringComparison.OrdinalIgnoreCase))
            throw new ValidationException("Sistem ücret kalemi manuel eklenemez (otomatik hesaplanır).");

        var birimNet = birimNetOverride ?? tanim.BirimUcret;
        var rate = kdvOraniOverride ?? tanim.KdvOrani;
        if (birimNet < 0) throw new ValidationException("Birim ücret negatif olamaz.");
        if (rate is < 0m or > 1m) throw new ValidationException("KDV oranı 0 ile 1 arasında olmalıdır.");

        if (await _repository.IsRentalInvoicedAsync(rentalId, ct))
            throw new ValidationException("Faturalanmış kiraya ek hizmet eklenemez.");

        // NET → KDV/brüt (kuruş tutarlı). Net = round(birim × miktar, 2).
        var net = Math.Round(birimNet * miktar, 2, MidpointRounding.AwayFromZero);
        var (kdv, gross) = KdvMath.FromNet(net, rate);

        var addOn = new RentalAddOn
        {
            RentalId = rentalId,
            EkHizmetTanimId = ekHizmetTanimId,
            Ad = tanim.Ad,
            Miktar = miktar,
            BirimNetFiyat = birimNet,
            KdvOrani = rate,
            NetTutar = net,
            KdvTutar = kdv,
            Toplam = gross,
            // FAZ-78: satan personel. Sistem kalemlerinde (SYS-*) daima boş — onları kimse satmaz.
            PersonelId = sistem ? null : personelId,
            IslemAnahtari = sistem ? null : islemAnahtari
        };
        await _repository.AddAsync(addOn, ct);
        return addOn.Id;
    }

    /// <summary>
    /// Low-B: aynı anahtarla ZATEN yazılmış kalem → 409 <c>mukerrer</c>. Kalem bu kiraya aitse <c>mevcut</c> döner
    /// (<c>AyniIcerik</c>: tanım + miktar birebir → kaybolan yanıttan sonraki kendi tekrarı; değilse gelen kalem
    /// YAZILMADI). Başka kiraya aitse <c>mevcut</c> dönmez (bilgi sızmaz) — farklı içerik 409'u.
    /// </summary>
    public static MukerrerIslemException Mukerrer(RentalAddOn onceki, Guid rentalId, Guid ekHizmetTanimId, decimal miktar)
    {
        if (onceki.RentalId != rentalId) return MukerrerIslemException.FarkliIcerik();
        var ayni = onceki.EkHizmetTanimId == ekHizmetTanimId
                   && Math.Round(onceki.Miktar, 4, MidpointRounding.AwayFromZero)
                      == Math.Round(miktar, 4, MidpointRounding.AwayFromZero);
        return new MukerrerIslemException(
            ayni
                ? $"Bu ek hizmet zaten eklendi ({onceki.Ad}, miktar {onceki.Miktar:0.####})."
                : MukerrerIslemException.FarkliIcerikMesaji,
            new MevcutIslem(onceki.Id, onceki.Ad, onceki.Toplam, "TRY", ayni));
    }

    public async Task<bool> RemoveAsync(Guid addOnId, CancellationToken ct = default)
    {
        PermissionGuard.Require(_currentUser, Permission.OperationsWrite);
        // Şube-kapsam guard'ı (bkz. AddAsync) — silme de web'e açık mutasyon yüzeyi.
        var addOn = await _repository.FindAsync(addOnId, ct);
        if (addOn is null) return false;
        if (await _bookings.FindRentalAsync(addOn.RentalId, ct) is { } kira)
            BranchScope.RequireInScope(_currentUser, kira.CikisSubeId, kira.CikisOfisi);
        return await _repository.RemoveAsync(addOnId, ct);
    }
}
