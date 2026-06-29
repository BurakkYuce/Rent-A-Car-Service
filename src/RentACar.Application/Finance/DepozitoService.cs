using RentACar.Application.Authorization;
using RentACar.Application.Common;
using RentACar.Application.Periods;
using RentACar.Domain.Common;
using RentACar.Domain.Entities;
using RentACar.Domain.Enums;

namespace RentACar.Application.Finance;

/// <summary>
/// Depozito (nakit emanet) yaşam döngüsü (roadmap I3). DENGELİ çift-taraflı defter:
///   Al:     Borç Kasa/Banka / Alacak Depozito(cari)   → nakit ↑, yükümlülük ↑
///   İade:   Borç Depozito(cari) / Alacak Kasa/Banka    → yükümlülük ↓, nakit ↓ (tutulanı aşamaz)
///   Mahsup: Borç Depozito(cari) / Alacak Cari(cari)     → yükümlülüğü cari borcuna mahsup (tutulanı aşamaz)
/// Dönem-kilidi + opsiyonel idempotency (IslemAnahtari → deterministik SourceId; çift-submit yutulur).
/// </summary>
public sealed class DepozitoService(
    ILedgerPoster ledger, ICashRepository repository, ICurrentUser currentUser, IPeriodLockGuard periodLock)
{
    private readonly ILedgerPoster _ledger = ledger;
    private readonly ICashRepository _repository = repository;
    private readonly ICurrentUser _currentUser = currentUser;
    private readonly IPeriodLockGuard _lock = periodLock;

    public Task<decimal> GetBakiyeAsync(Guid cariId, CancellationToken ct = default)
        => _repository.GetDepozitoBakiyeAsync(cariId, ct);

    /// <summary>Depozito al: Borç Kasa/Banka / Alacak Depozito(cari).</summary>
    public Task<Guid> AlAsync(Guid cariId, decimal tutar, LedgerAccountType hesap, string? doviz = "TRY",
        decimal kur = 1m, DateTimeOffset? tarih = null, Guid? islemAnahtari = null, CancellationToken ct = default)
        => PostAsync(cariId, tutar, hesap, doviz, kur, tarih, islemAnahtari, "DepozitoAl", "Depozito al",
            borc: hesap, borcRef: null, alacak: LedgerAccountType.Depozito, alacakRef: cariId, kontrolEt: false, ct);

    /// <summary>Depozito iade: Borç Depozito(cari) / Alacak Kasa/Banka. Tutulan depozitoyu aşamaz.</summary>
    public Task<Guid> IadeAsync(Guid cariId, decimal tutar, LedgerAccountType hesap, string? doviz = "TRY",
        decimal kur = 1m, DateTimeOffset? tarih = null, Guid? islemAnahtari = null, CancellationToken ct = default)
        => PostAsync(cariId, tutar, hesap, doviz, kur, tarih, islemAnahtari, "DepozitoIade", "Depozito iade",
            borc: LedgerAccountType.Depozito, borcRef: cariId, alacak: hesap, alacakRef: null, kontrolEt: true, ct);

    /// <summary>Depozito mahsup (cari borcuna): Borç Depozito(cari) / Alacak Cari(cari). Tutulanı aşamaz.</summary>
    public Task<Guid> MahsupAsync(Guid cariId, decimal tutar, string? doviz = "TRY",
        decimal kur = 1m, DateTimeOffset? tarih = null, Guid? islemAnahtari = null, CancellationToken ct = default)
        => PostAsync(cariId, tutar, LedgerAccountType.Cari, doviz, kur, tarih, islemAnahtari, "DepozitoMahsup", "Depozito mahsup",
            borc: LedgerAccountType.Depozito, borcRef: cariId, alacak: LedgerAccountType.Cari, alacakRef: cariId, kontrolEt: true, ct);

    private async Task<Guid> PostAsync(
        Guid cariId, decimal tutar, LedgerAccountType hesap, string? doviz, decimal kur,
        DateTimeOffset? tarih, Guid? islemAnahtari, string sourceType, string aciklama,
        LedgerAccountType borc, Guid? borcRef, LedgerAccountType alacak, Guid? alacakRef, bool kontrolEt,
        CancellationToken ct)
    {
        PermissionGuard.Require(_currentUser, Permission.FinanceWrite);
        if (cariId == Guid.Empty) throw new ValidationException("Cari seçilmelidir.");
        if (tutar <= 0m) throw new ValidationException("Tutar pozitif olmalıdır.");
        if (kur <= 0m) throw new ValidationException("Kur pozitif olmalıdır.");

        var money = new Money(tutar, (doviz ?? "TRY").Trim().ToUpperInvariant(), kur);

        if (kontrolEt)
        {
            var tutulan = await _repository.GetDepozitoBakiyeAsync(cariId, ct);
            if (money.AmountInBase > tutulan)
                throw new ValidationException($"İşlem tutarı ({money.AmountInBase}) tutulan depozitoyu ({tutulan}) aşamaz.");
        }

        var entryDate = tarih ?? DateTimeOffset.UtcNow;
        await _lock.EnsureOpenAsync(entryDate, ct); // dönem kilidi: kapalı tarihe depozito işlemi YOK

        var sourceId = islemAnahtari is { } k && k != Guid.Empty ? k : Guid.NewGuid();
        await _ledger.PostAsync(
        [
            new AccountLedgerEntry { EntryDateUtc = entryDate, AccountType = borc, AccountRef = borcRef,
                Direction = LedgerDirection.Debit, Amount = money, SourceType = sourceType, SourceId = sourceId, Description = aciklama },
            new AccountLedgerEntry { EntryDateUtc = entryDate, AccountType = alacak, AccountRef = alacakRef,
                Direction = LedgerDirection.Credit, Amount = money, SourceType = sourceType, SourceId = sourceId, Description = aciklama }
        ], ct);
        return sourceId;
    }
}
