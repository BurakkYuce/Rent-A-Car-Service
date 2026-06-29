using RentACar.Application.Authorization;
using RentACar.Application.Common;
using RentACar.Domain.Common;
using RentACar.Domain.Entities;
using RentACar.Domain.Enums;

namespace RentACar.Application.FiloKiralamalar;

/// <summary>
/// Filo (uzun-dönem) kiralama iş mantığı (roadmap L1): sözleşme oluştur/listele + taksit planı hesapla.
/// DEFTER POSTLAMAZ (gelir aylık faturalama ile tanınır) → salt sözleşme/hesap; yazma OperationsWrite.
/// </summary>
public sealed class FiloKiralamaService(IFiloKiralamaRepository repository, ICurrentUser currentUser)
{
    private readonly IFiloKiralamaRepository _repository = repository;
    private readonly ICurrentUser _currentUser = currentUser;

    public Task<IReadOnlyList<FiloKiralama>> ListAsync(CancellationToken ct = default)
        => _repository.ListAsync(ct);

    public Task<FiloKiralama?> GetAsync(Guid id, CancellationToken ct = default)
        => _repository.FindAsync(id, ct);

    public async Task<Guid> CreateAsync(FiloKiralamaInput input, CancellationToken ct = default)
    {
        PermissionGuard.Require(_currentUser, Permission.OperationsWrite);
        if (input.MusteriId == Guid.Empty) throw new ValidationException("Müşteri seçilmelidir.");
        if (input.VehicleId == Guid.Empty) throw new ValidationException("Araç seçilmelidir.");
        if (input.SureAy is < 1 or > 120) throw new ValidationException("Süre (ay) 1 ile 120 arasında olmalıdır.");
        if (input.AylikUcret <= 0m) throw new ValidationException("Aylık ücret pozitif olmalıdır.");
        if (input.KdvOrani is < 0m or > 1m) throw new ValidationException("KDV oranı 0-1 arası kesir olmalıdır.");
        if (input.Kur <= 0m) throw new ValidationException("Kur pozitif olmalıdır.");
        if (input.DamgaVergisi is < 0m) throw new ValidationException("Damga vergisi negatif olamaz.");

        var row = new FiloKiralama
        {
            MusteriId = input.MusteriId,
            VehicleId = input.VehicleId,
            BasTar = input.BasTar ?? DateTimeOffset.UtcNow,
            SureAy = input.SureAy,
            AylikUcret = input.AylikUcret,
            KdvOrani = input.KdvOrani,
            Currency = string.IsNullOrWhiteSpace(input.Doviz) ? "TRY" : input.Doviz.Trim().ToUpperInvariant(),
            Kur = input.Kur,
            ToplamKmLimiti = input.ToplamKmLimiti,
            DamgaVergisi = input.DamgaVergisi,
            Durum = FiloKiraDurum.Aktif,
            Aciklama = string.IsNullOrWhiteSpace(input.Aciklama) ? null : input.Aciklama.Trim()
        };
        await _repository.CreateAsync(row, ct);
        return row.Id;
    }

    public Task<bool> IptalAsync(Guid id, CancellationToken ct = default)
    {
        PermissionGuard.Require(_currentUser, Permission.OperationsWrite);
        return _repository.SetDurumAsync(id, FiloKiraDurum.Iptal, ct);
    }

    public Task<bool> TamamlaAsync(Guid id, CancellationToken ct = default)
    {
        PermissionGuard.Require(_currentUser, Permission.OperationsWrite);
        return _repository.SetDurumAsync(id, FiloKiraDurum.Tamamlandi, ct);
    }

    /// <summary>Taksit planı + mali özet (salt-hesap). Her ay: net = aylık ücret, kdv = net × oran;
    /// vade = BasTar + n ay. ToplamNet/Kdv = taksit toplamları (yuvarlama tutarlı).</summary>
    public static FiloKiraOzet TaksitPlani(FiloKiralama k)
    {
        var taksitler = new List<FiloKiraTaksit>(k.SureAy);
        for (var i = 0; i < k.SureAy; i++)
        {
            var net = Round(k.AylikUcret);
            var kdv = Round(k.AylikUcret * k.KdvOrani);
            taksitler.Add(new FiloKiraTaksit(i + 1, k.BasTar.AddMonths(i), net, kdv, net + kdv));
        }
        var toplamNet = taksitler.Sum(t => t.Net);
        var toplamKdv = taksitler.Sum(t => t.Kdv);
        var damga = k.DamgaVergisi ?? 0m;
        return new FiloKiraOzet(toplamNet, toplamKdv, damga, toplamNet + toplamKdv + damga, taksitler);
    }

    private static decimal Round(decimal x) => Math.Round(x, 2, MidpointRounding.AwayFromZero);
}
