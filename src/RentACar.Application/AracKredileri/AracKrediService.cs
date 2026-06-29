using RentACar.Application.Authorization;
using RentACar.Application.Common;
using RentACar.Domain.Common;
using RentACar.Domain.Entities;
using RentACar.Domain.Enums;

namespace RentACar.Application.AracKredileri;

/// <summary>
/// Araç kredisi iş mantığı (roadmap L4): kredi oluştur/listele + taksit öde (kalan bakiye) + durum.
/// Banka entegrasyonu YOK, DEFTER POSTLAMAZ → salt kayıt/hesap; yazma OperationsWrite.
/// </summary>
public sealed class AracKrediService(IAracKrediRepository repository, ICurrentUser currentUser)
{
    private readonly IAracKrediRepository _repository = repository;
    private readonly ICurrentUser _currentUser = currentUser;

    public Task<IReadOnlyList<AracKredi>> ListAsync(CancellationToken ct = default)
        => _repository.ListAsync(ct);

    public Task<AracKredi?> GetAsync(Guid id, CancellationToken ct = default)
        => _repository.FindAsync(id, ct);

    public async Task<Guid> CreateAsync(AracKrediInput input, CancellationToken ct = default)
    {
        PermissionGuard.Require(_currentUser, Permission.OperationsWrite);
        if (string.IsNullOrWhiteSpace(input.BankaAdi)) throw new ValidationException("Banka adı zorunludur.");
        if (input.KrediTutari <= 0m) throw new ValidationException("Kredi tutarı pozitif olmalıdır.");
        if (input.TaksitSayisi is < 1 or > 360) throw new ValidationException("Taksit sayısı 1 ile 360 arasında olmalıdır.");
        if (input.FaizOran < 0m) throw new ValidationException("Faiz oranı negatif olamaz.");
        if (input.Kur <= 0m) throw new ValidationException("Kur pozitif olmalıdır.");

        var row = new AracKredi
        {
            BankaAdi = input.BankaAdi.Trim(),
            VehicleId = input.VehicleId,
            KrediTutari = input.KrediTutari,
            FaizOran = input.FaizOran,
            TaksitSayisi = input.TaksitSayisi,
            BaslangicTarihi = input.BaslangicTarihi ?? DateTimeOffset.UtcNow,
            OdenenTaksit = 0,
            Currency = string.IsNullOrWhiteSpace(input.Doviz) ? "TRY" : input.Doviz.Trim().ToUpperInvariant(),
            Kur = input.Kur,
            Durum = KrediDurum.Aktif,
            Aciklama = string.IsNullOrWhiteSpace(input.Aciklama) ? null : input.Aciklama.Trim()
        };
        await _repository.CreateAsync(row, ct);
        return row.Id;
    }

    public Task<bool> TaksitOdeAsync(Guid id, CancellationToken ct = default)
    {
        PermissionGuard.Require(_currentUser, Permission.OperationsWrite);
        return _repository.TaksitOdeAsync(id, ct);
    }

    public Task<bool> IptalAsync(Guid id, CancellationToken ct = default)
    {
        PermissionGuard.Require(_currentUser, Permission.OperationsWrite);
        return _repository.SetDurumAsync(id, KrediDurum.Iptal, ct);
    }

    /// <summary>Taksit planı + kalan bakiye (salt-hesap). Basit faiz: toplamFaiz = tutar × faiz × taksit/12;
    /// aylık taksit = toplam geri ödeme / taksit sayısı; kalan = toplam − ödenen.</summary>
    public static AracKrediOzet Hesapla(AracKredi k)
    {
        var toplamFaiz = R(k.KrediTutari * k.FaizOran * k.TaksitSayisi / 12m);
        var toplamGeriOdeme = k.KrediTutari + toplamFaiz;
        var aylikTaksit = R(toplamGeriOdeme / k.TaksitSayisi);

        var taksitler = new List<AracKrediTaksit>(k.TaksitSayisi);
        for (var i = 0; i < k.TaksitSayisi; i++)
            taksitler.Add(new AracKrediTaksit(i + 1, k.BaslangicTarihi.AddMonths(i), aylikTaksit, i < k.OdenenTaksit));

        var odenenAdet = Math.Clamp(k.OdenenTaksit, 0, k.TaksitSayisi);
        var odenenTutar = R(odenenAdet * aylikTaksit);
        var kalanBakiye = Math.Max(0m, toplamGeriOdeme - odenenTutar);

        return new AracKrediOzet(toplamFaiz, toplamGeriOdeme, aylikTaksit, odenenTutar, kalanBakiye, taksitler);
    }

    private static decimal R(decimal v) => Math.Round(v, 2, MidpointRounding.AwayFromZero);
}
