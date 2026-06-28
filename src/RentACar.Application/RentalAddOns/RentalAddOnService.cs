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
    ICurrentUser currentUser)
{
    private readonly IRentalAddOnRepository _repository = repository;
    private readonly IEkHizmetTanimRepository _ekHizmetRepository = ekHizmetRepository;
    private readonly ICurrentUser _currentUser = currentUser;

    public Task<IReadOnlyList<RentalAddOn>> ListAsync(Guid rentalId, CancellationToken ct = default)
        => _repository.ListForRentalAsync(rentalId, ct);

    public async Task<Guid> AddAsync(
        Guid rentalId, Guid ekHizmetTanimId, decimal miktar,
        decimal? birimNetOverride = null, decimal? kdvOraniOverride = null, CancellationToken ct = default)
    {
        PermissionGuard.Require(_currentUser, Permission.OperationsWrite);
        if (miktar <= 0) throw new ValidationException("Miktar sıfırdan büyük olmalıdır.");

        var tanim = await _ekHizmetRepository.FindAsync(ekHizmetTanimId, ct)
            ?? throw new ValidationException("Ek hizmet tanımı bulunamadı.");

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
            Toplam = gross
        };
        await _repository.AddAsync(addOn, ct);
        return addOn.Id;
    }

    public async Task<bool> RemoveAsync(Guid addOnId, CancellationToken ct = default)
    {
        PermissionGuard.Require(_currentUser, Permission.OperationsWrite);
        return await _repository.RemoveAsync(addOnId, ct);
    }
}
