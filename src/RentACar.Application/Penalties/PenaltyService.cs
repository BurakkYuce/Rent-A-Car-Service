using RentACar.Application.Authorization;
using RentACar.Application.Common;
using RentACar.Domain.Common;
using RentACar.Domain.Entities;
using RentACar.Domain.Enums;

namespace RentACar.Application.Penalties;

/// <summary>
/// Trafik cezası: kayıt (tebliğ→vade) + müşteriye yansıtma (Borç Cari / Alacak Gelir,
/// dengeli, idempotent) + durum (Öde/İptal). Yansıtma defter yazdığından FinanceWrite ister.
/// </summary>
public sealed class PenaltyService(IPenaltyRepository repository, ICurrentUser currentUser)
{
    private readonly IPenaltyRepository _repository = repository;
    private readonly ICurrentUser _currentUser = currentUser;

    public Task<IReadOnlyList<Penalty>> ListAsync(CancellationToken ct = default)
        => _repository.ListAsync(ct);
    public Task<Penalty?> GetAsync(Guid id, CancellationToken ct = default)
        => _repository.FindAsync(id, ct);

    public async Task<Guid> CreateAsync(PenaltyInput input, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(input.CezaTuru)) throw new ValidationException("Ceza türü zorunludur.");
        if (input.Tutar <= 0) throw new ValidationException("Ceza tutarı pozitif olmalıdır.");
        if (input.VadeGun < 0) throw new ValidationException("Vade günü negatif olamaz.");

        var teblig = input.TebligTarihi ?? DateTimeOffset.UtcNow;
        var penalty = new Penalty
        {
            CezaTuru = input.CezaTuru.Trim(),
            TebligTarihi = teblig,
            VadeTarihi = teblig.AddDays(input.VadeGun),
            VehicleId = input.VehicleId,
            CariId = input.CariId,
            RentalId = input.RentalId,
            Tutar = input.Tutar,
            Sebep = input.Sebep,
            Durum = CezaDurum.Yeni
        };
        await _repository.CreateAsync(penalty, ct);
        return penalty.Id;
    }

    /// <summary>Cezayı müşteriye yansıt (Borç Cari / Alacak Gelir). Yalnız Yeni ceza, cari zorunlu.</summary>
    public async Task<bool> YansitAsync(Guid id, CancellationToken ct = default)
    {
        PermissionGuard.Require(_currentUser, Permission.FinanceWrite);
        var penalty = await _repository.FindAsync(id, ct) ?? throw new ValidationException("Ceza bulunamadı.");
        if (penalty.Durum != CezaDurum.Yeni)
            throw new ValidationException("Yalnız 'Yeni' durumundaki ceza yansıtılabilir.");
        if (penalty.CariId is null || penalty.CariId == Guid.Empty)
            throw new ValidationException("Yansıtma için müşteri (cari) seçilmelidir.");

        return await _repository.ReflectAsync(id, p =>
        [
            new AccountLedgerEntry
            {
                EntryDateUtc = DateTimeOffset.UtcNow, AccountType = LedgerAccountType.Cari, AccountRef = p.CariId,
                Direction = LedgerDirection.Debit, Amount = new Money(p.Tutar, "TRY", 1m),
                SourceType = "Ceza", SourceId = p.Id, Description = $"Ceza {p.No}"
            },
            new AccountLedgerEntry
            {
                EntryDateUtc = DateTimeOffset.UtcNow, AccountType = LedgerAccountType.Gelir, AccountRef = null,
                Direction = LedgerDirection.Credit, Amount = new Money(p.Tutar, "TRY", 1m),
                SourceType = "Ceza", SourceId = p.Id, Description = $"Ceza {p.No}"
            }
        ], ct);
    }

    public Task<bool> OdeAsync(Guid id, CancellationToken ct = default)
        => _repository.UpdateAsync(id, p =>
        {
            if (p.Durum is CezaDurum.Iptal) throw new ValidationException("İptal ceza ödenemez.");
            p.Durum = CezaDurum.Odendi;
            p.UpdatedAtUtc = DateTimeOffset.UtcNow;
        }, ct);

    public Task<bool> IptalAsync(Guid id, CancellationToken ct = default)
        => _repository.UpdateAsync(id, p =>
        {
            if (p.Durum == CezaDurum.Yansitildi) throw new ValidationException("Yansıtılmış ceza iptal edilemez (ters kayıt gerekir).");
            p.Durum = CezaDurum.Iptal;
            p.UpdatedAtUtc = DateTimeOffset.UtcNow;
        }, ct);
}
