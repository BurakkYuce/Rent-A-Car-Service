using RentACar.Application.Authorization;
using RentACar.Application.Common;
using RentACar.Domain.Common;
using RentACar.Domain.Entities;
using RentACar.Domain.Enums;

namespace RentACar.Application.AracSiparisleri;

/// <summary>
/// Araç sipariş/tedarik iş mantığı (roadmap L3): sipariş oluştur/listele + durum geçişi (onayla/teslim al/iptal).
/// DEFTER POSTLAMAZ (teslim/satınalma faturalama ayrı) → salt sipariş takibi; yazma OperationsWrite.
/// </summary>
public sealed class AracSiparisService(IAracSiparisRepository repository, ICurrentUser currentUser)
{
    private readonly IAracSiparisRepository _repository = repository;
    private readonly ICurrentUser _currentUser = currentUser;

    public Task<IReadOnlyList<AracSiparis>> ListAsync(CancellationToken ct = default)
        => _repository.ListAsync(ct);

    public Task<AracSiparis?> GetAsync(Guid id, CancellationToken ct = default)
        => _repository.FindAsync(id, ct);

    public async Task<Guid> CreateAsync(AracSiparisInput input, CancellationToken ct = default)
    {
        PermissionGuard.Require(_currentUser, Permission.OperationsWrite);
        if (string.IsNullOrWhiteSpace(input.Tedarikci)) throw new ValidationException("Tedarikçi zorunludur.");
        if (input.Adet is < 1 or > 10000) throw new ValidationException("Adet 1 ile 10000 arasında olmalıdır.");
        if (input.BirimFiyat < 0m) throw new ValidationException("Birim fiyat negatif olamaz.");
        if (input.Kur <= 0m) throw new ValidationException("Kur pozitif olmalıdır.");

        var row = new AracSiparis
        {
            Tedarikci = input.Tedarikci.Trim(),
            SiparisTarihi = input.SiparisTarihi ?? DateTimeOffset.UtcNow,
            BeklenenTeslim = input.BeklenenTeslim,
            Marka = Trim(input.Marka),
            Tip = Trim(input.Tip),
            Grup = Trim(input.Grup),
            Adet = input.Adet,
            BirimFiyat = input.BirimFiyat,
            Currency = string.IsNullOrWhiteSpace(input.Doviz) ? "TRY" : input.Doviz.Trim().ToUpperInvariant(),
            Kur = input.Kur,
            Durum = SiparisDurum.Bekliyor,
            Aciklama = Trim(input.Aciklama)
        };
        await _repository.CreateAsync(row, ct);
        return row.Id;
    }

    public Task<bool> OnaylaAsync(Guid id, CancellationToken ct = default) => SetDurum(id, SiparisDurum.Onaylandi, ct);
    public Task<bool> TeslimAlAsync(Guid id, CancellationToken ct = default) => SetDurum(id, SiparisDurum.TeslimAlindi, ct);
    public Task<bool> IptalAsync(Guid id, CancellationToken ct = default) => SetDurum(id, SiparisDurum.Iptal, ct);

    private Task<bool> SetDurum(Guid id, SiparisDurum durum, CancellationToken ct)
    {
        PermissionGuard.Require(_currentUser, Permission.OperationsWrite);
        return _repository.SetDurumAsync(id, durum, ct);
    }

    private static string? Trim(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
