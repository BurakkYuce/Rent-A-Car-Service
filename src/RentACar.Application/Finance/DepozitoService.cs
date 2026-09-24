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
    ILedgerPoster ledger, ICashRepository repository, ICurrentUser currentUser, IPeriodLockGuard periodLock,
    RentACar.Application.Kur.KurCozucu kurCozucu, RentACar.Application.FinancialAccounts.HesapCozucu hesapCozucu,
    RentACar.Application.Customers.ICustomerRepository customers)
{
    private readonly ILedgerPoster _ledger = ledger;
    private readonly ICashRepository _repository = repository;
    private readonly ICurrentUser _currentUser = currentUser;
    private readonly IPeriodLockGuard _lock = periodLock;
    private readonly RentACar.Application.Kur.KurCozucu _kurCozucu = kurCozucu;
    private readonly RentACar.Application.FinancialAccounts.HesapCozucu _hesapCozucu = hesapCozucu;
    private readonly RentACar.Application.Customers.ICustomerRepository _customers = customers;

    public Task<decimal> GetBakiyeAsync(Guid cariId, CancellationToken ct = default)
        => _repository.GetDepozitoBakiyeAsync(cariId, ct);

    /// <summary>F8.1a — tutulan depozitolar (bakiyesi sıfır olmayan cariler; tek sorgu). Para bilgisi →
    /// <see cref="Permission.FinanceWrite"/> (Blazor <c>/depozito</c> ekranı gibi).</summary>
    public Task<Dictionary<Guid, decimal>> GetBakiyelerAsync(CancellationToken ct = default)
    {
        PermissionGuard.Require(_currentUser, Permission.FinanceWrite);
        return _repository.GetDepozitoBakiyeleriAsync(ct);
    }

    /// <summary>Depozito al: Borç Kasa/Banka / Alacak Depozito(cari).
    /// FAZ-50: <paramref name="hesapId"/> verilirse nakit bacağı o spesifik hesaba yazılır.</summary>
    public async Task<Guid> AlAsync(Guid cariId, decimal tutar, LedgerAccountType hesap, string? doviz = "TRY",
        decimal? kur = null, DateTimeOffset? tarih = null, Guid? islemAnahtari = null,
        Guid? hesapId = null, CancellationToken ct = default)
    {
        // Guard GİRİŞ noktasında: hesap çözümü PostAsync'ten önce çalışıyor, yetkisiz kullanıcı
        // hesap varlığını yoklayamamalı.
        PermissionGuard.Require(_currentUser, Permission.FinanceWrite);
        await CariVarAsync(cariId, ct);
        // F4.4a adversarial L1: hesap-döviz çiti tahsilattaki gibi (FAZ-50 M4) — USD depozito TRY hesaba yazılmaz.
        return await PostAsync(cariId, tutar, hesap, doviz, kur, tarih, islemAnahtari, "DepozitoAl", "Depozito al",
            borc: hesap, borcRef: await _hesapCozucu.CozAsync(hesapId, hesap, ct, RentACar.Application.Kur.KurService.NormalizeKod(doviz)),
            alacak: LedgerAccountType.Depozito, alacakRef: cariId, kontrolEt: false, ct);
    }

    /// <summary>Depozito iade: Borç Depozito(cari) / Alacak Kasa/Banka. Tutulan depozitoyu aşamaz.
    /// FAZ-50: <paramref name="hesapId"/> verilirse nakit bacağı o spesifik hesaba yazılır.</summary>
    public async Task<Guid> IadeAsync(Guid cariId, decimal tutar, LedgerAccountType hesap, string? doviz = "TRY",
        decimal? kur = null, DateTimeOffset? tarih = null, Guid? islemAnahtari = null,
        Guid? hesapId = null, CancellationToken ct = default)
    {
        PermissionGuard.Require(_currentUser, Permission.FinanceWrite); // bkz. AlAsync notu
        await CariVarAsync(cariId, ct);
        return await PostAsync(cariId, tutar, hesap, doviz, kur, tarih, islemAnahtari, "DepozitoIade", "Depozito iade",
            borc: LedgerAccountType.Depozito, borcRef: cariId,
            alacak: hesap, alacakRef: await _hesapCozucu.CozAsync(hesapId, hesap, ct, RentACar.Application.Kur.KurService.NormalizeKod(doviz)),
            kontrolEt: true, ct);
    }

    /// <summary>Depozito mahsup (cari borcuna): Borç Depozito(cari) / Alacak Cari(cari). Tutulanı aşamaz.</summary>
    public async Task<Guid> MahsupAsync(Guid cariId, decimal tutar, string? doviz = "TRY",
        decimal? kur = null, DateTimeOffset? tarih = null, Guid? islemAnahtari = null, CancellationToken ct = default)
    {
        // F8.1a: al/iade/irat ile simetri — guard giriş noktasında, cari kiracıda var olmalı (yetim AccountRef yok).
        PermissionGuard.Require(_currentUser, Permission.FinanceWrite);
        await CariVarAsync(cariId, ct);
        return await PostAsync(cariId, tutar, LedgerAccountType.Cari, doviz, kur, tarih, islemAnahtari, "DepozitoMahsup", "Depozito mahsup",
            borc: LedgerAccountType.Depozito, borcRef: cariId, alacak: LedgerAccountType.Cari, alacakRef: cariId, kontrolEt: true, ct);
    }

    /// <summary>Depozito İRAT (FAZ 1.2): iade edilmeyen depozito GELİR olur — Borç Depozito(cari) /
    /// Alacak Gelir. Tutulan depozitoyu aşamaz (mevcut bakiye guard'ı). rentalId verilirse gelir o
    /// kiranın aracına atfedilir (karne/Karlilik); kira carisi eşleşmezse repo çiti reddeder.
    /// Çift-submit sessiz idempotent (Depozito% kısmi unique index — I3 sözleşmesi).</summary>
    public async Task<Guid> IratAsync(Guid cariId, decimal tutar, string? doviz = "TRY",
        decimal? kur = null, Guid? rentalId = null, DateTimeOffset? tarih = null,
        Guid? islemAnahtari = null, string? aciklama = null, CancellationToken ct = default)
    {
        PermissionGuard.Require(_currentUser, Permission.FinanceWrite);
        if (cariId == Guid.Empty) throw new ValidationException("Cari seçilmelidir.");
        if (tutar <= 0m) throw new ValidationException("Tutar pozitif olmalıdır.");
        await CariVarAsync(cariId, ct);

        var cozulenKur = await _kurCozucu.CozAsync(doviz, kur, tarih, ct); // 1.1 sözleşmesi
        var money = new Money(tutar, RentACar.Application.Kur.KurService.NormalizeKodStrict(doviz), cozulenKur);
        // Bakiye kontrolü repo tx'inde, kilidin arkasında (TOCTOU çiti — tek otorite).

        var entryDate = tarih ?? DateTimeOffset.UtcNow;
        await _lock.EnsureOpenAsync(entryDate, ct); // dönem kilidi

        var sourceId = islemAnahtari is { } k && k != Guid.Empty ? k : Guid.NewGuid();
        var desc = string.IsNullOrWhiteSpace(aciklama) ? "Depozito irat (gelir)" : aciklama.Trim();
        var kayit = new DepozitoIrat
        {
            Id = sourceId, CariId = cariId, RentalId = rentalId,
            Tutar = tutar, Currency = money.Currency, Kur = cozulenKur,
            Tarih = entryDate, Aciklama = desc
        };
        await _repository.PostDepozitoIslemAsync(cariId, kontrolEt: true, kayit,
        [
            new AccountLedgerEntry { EntryDateUtc = entryDate, AccountType = LedgerAccountType.Depozito, AccountRef = cariId,
                Direction = LedgerDirection.Debit, Amount = money, SourceType = "DepozitoIrat", SourceId = sourceId, Description = desc },
            new AccountLedgerEntry { EntryDateUtc = entryDate, AccountType = LedgerAccountType.Gelir, AccountRef = null,
                Direction = LedgerDirection.Credit, Amount = money, SourceType = "DepozitoIrat", SourceId = sourceId, Description = desc }
        ], ct);
        return sourceId;
    }

    /// <summary>F4.4a adversarial MEDIUM-2: cari kiracı içinde var olmalı (RLS + sorgu filtresi; yoksa/başka
    /// kiracınınsa null) — yoksa depozito, hiçbir ekstrede görünmeyen yetim bir AccountRef'e yazılırdı.
    /// Boş kimlik PostAsync'in "Cari seçilmelidir." mesajına bırakılır.</summary>
    private async Task CariVarAsync(Guid cariId, CancellationToken ct)
    {
        if (cariId != Guid.Empty && await _customers.FindAsync(cariId, ct) is null)
            throw new ValidationException("Cari bulunamadı.", "cariId");
    }

    private async Task<Guid> PostAsync(
        Guid cariId, decimal tutar, LedgerAccountType hesap, string? doviz, decimal? kur,
        DateTimeOffset? tarih, Guid? islemAnahtari, string sourceType, string aciklama,
        LedgerAccountType borc, Guid? borcRef, LedgerAccountType alacak, Guid? alacakRef, bool kontrolEt,
        CancellationToken ct)
    {
        PermissionGuard.Require(_currentUser, Permission.FinanceWrite);
        if (cariId == Guid.Empty) throw new ValidationException("Cari seçilmelidir.");
        if (tutar <= 0m) throw new ValidationException("Tutar pozitif olmalıdır.");

        // Kur çözümü (1.1): açık kur (>0) aynen; boş → TRY=1 / döviz KurService (yoksa net red — sessiz 1 YOK).
        var cozulenKur = await _kurCozucu.CozAsync(doviz, kur, tarih, ct);
        var money = new Money(tutar, RentACar.Application.Kur.KurService.NormalizeKodStrict(doviz), cozulenKur);
        // Bakiye kontrolü repo tx'inde, kilidin arkasında (TOCTOU çiti — adversarial 1.2:
        // eşzamanlı iki iade/irat pre-check'i birlikte geçip tutulanı aşabiliyordu).

        var entryDate = tarih ?? DateTimeOffset.UtcNow;
        await _lock.EnsureOpenAsync(entryDate, ct); // dönem kilidi: kapalı tarihe depozito işlemi YOK

        var sourceId = islemAnahtari is { } k && k != Guid.Empty ? k : Guid.NewGuid();
        await _repository.PostDepozitoIslemAsync(cariId, kontrolEt, izKaydi: null,
        [
            new AccountLedgerEntry { EntryDateUtc = entryDate, AccountType = borc, AccountRef = borcRef,
                Direction = LedgerDirection.Debit, Amount = money, SourceType = sourceType, SourceId = sourceId, Description = aciklama },
            new AccountLedgerEntry { EntryDateUtc = entryDate, AccountType = alacak, AccountRef = alacakRef,
                Direction = LedgerDirection.Credit, Amount = money, SourceType = sourceType, SourceId = sourceId, Description = aciklama }
        ], ct);
        return sourceId;
    }
}
