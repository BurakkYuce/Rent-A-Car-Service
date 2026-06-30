using RentACar.Application.Authorization;
using RentACar.Application.Common;
using RentACar.Application.Periods;
using RentACar.Domain.Common;
using RentACar.Domain.Entities;
using RentACar.Domain.Enums;

namespace RentACar.Application.Regulation;

/// <summary>Sigorta/MTV/Muayene CRUD + doğrulama (araç zorunlu, tarih tutarlılığı) + MTV ödeme→defter (J1).</summary>
public sealed class RegulationService(IRegulationRepository repository, ICurrentUser currentUser, IPeriodLockGuard periodLock)
{
    private readonly IRegulationRepository _repository = repository;
    private readonly ICurrentUser _currentUser = currentUser;
    private readonly IPeriodLockGuard _lock = periodLock;

    public Task<IReadOnlyList<InsurancePolicy>> ListInsuranceAsync(CancellationToken ct = default)
        => _repository.ListInsuranceAsync(ct);
    public Task<IReadOnlyList<MtvRecord>> ListMtvAsync(CancellationToken ct = default)
        => _repository.ListMtvAsync(ct);
    public Task<IReadOnlyList<InspectionRecord>> ListInspectionAsync(CancellationToken ct = default)
        => _repository.ListInspectionAsync(ct);

    public async Task<Guid> AddInsuranceAsync(
        Guid vehicleId, InsuranceType tip, DateTimeOffset baslangic, DateTimeOffset bitis,
        decimal prim, string? policeNo, string? firma, string? acenta, CancellationToken ct = default)
    {
        RequireVehicle(vehicleId);
        if (bitis <= baslangic) throw new ValidationException("Bitiş başlangıçtan sonra olmalıdır.");
        if (prim < 0) throw new ValidationException("Prim negatif olamaz.");
        var p = new InsurancePolicy
        {
            VehicleId = vehicleId, Tip = tip, Baslangic = baslangic, Bitis = bitis,
            Prim = prim, PoliceNo = Trim(policeNo), Firma = Trim(firma), Acenta = Trim(acenta)
        };
        await _repository.AddInsuranceAsync(p, ct);
        return p.Id;
    }

    public async Task<Guid> AddMtvAsync(
        Guid vehicleId, string donem, decimal tutar, DateTimeOffset vade, CancellationToken ct = default)
    {
        RequireVehicle(vehicleId);
        if (string.IsNullOrWhiteSpace(donem)) throw new ValidationException("Dönem zorunludur.");
        if (tutar < 0) throw new ValidationException("Tutar negatif olamaz.");
        var m = new MtvRecord { VehicleId = vehicleId, Donem = donem.Trim(), Tutar = tutar, Vade = vade };
        await _repository.AddMtvAsync(m, ct);
        return m.Id;
    }

    public async Task<Guid> AddInspectionAsync(
        Guid vehicleId, DateTimeOffset muayeneTarihi, DateTimeOffset bitis, decimal ucret, CancellationToken ct = default)
    {
        RequireVehicle(vehicleId);
        if (bitis <= muayeneTarihi) throw new ValidationException("Bitiş muayene tarihinden sonra olmalıdır.");
        if (ucret < 0) throw new ValidationException("Ücret negatif olamaz.");
        var i = new InspectionRecord { VehicleId = vehicleId, MuayeneTarihi = muayeneTarihi, Bitis = bitis, Ucret = ucret };
        await _repository.AddInspectionAsync(i, ct);
        return i.Id;
    }

    /// <summary>
    /// MTV ödeme (roadmap J1): DENGELİ defter — Borç Gider / Alacak Kasa-Banka. FinanceWrite + dönem-kilidi +
    /// idempotency (SourceId=mtvId; çift-ödeme reddedilir). MtvRecord.Odendi=true (atomik, tek transaction).
    /// </summary>
    public async Task MtvOdeAsync(Guid mtvId, LedgerAccountType hesap, DateTimeOffset? odemeTarih = null,
        string? doviz = "TRY", decimal kur = 1m, CancellationToken ct = default)
    {
        PermissionGuard.Require(_currentUser, Permission.FinanceWrite);
        if (hesap is not (LedgerAccountType.Kasa or LedgerAccountType.Banka))
            throw new ValidationException("Ödeme hesabı Kasa veya Banka olmalıdır.");
        if (kur <= 0m) throw new ValidationException("Kur pozitif olmalıdır.");

        var rec = await _repository.FindMtvAsync(mtvId, ct) ?? throw new ValidationException("MTV kaydı bulunamadı.");
        if (rec.Odendi) throw new ValidationException("MTV zaten ödendi.");
        if (rec.Tutar <= 0m) throw new ValidationException("MTV tutarı pozitif olmalıdır.");

        var tarih = odemeTarih ?? DateTimeOffset.UtcNow;
        await _lock.EnsureOpenAsync(tarih, ct); // dönem kilidi: kapalı döneme MTV ödemesi YOK

        var money = new Money(rec.Tutar, (doviz ?? "TRY").Trim().ToUpperInvariant(), kur);
        var desc = $"MTV ödeme {rec.Donem}";
        await _repository.PostMtvOdemeAsync(mtvId,
        [
            new AccountLedgerEntry { EntryDateUtc = tarih, AccountType = LedgerAccountType.Gider, AccountRef = null,
                Direction = LedgerDirection.Debit, Amount = money, SourceType = "MtvOdeme", SourceId = mtvId, Description = desc },
            new AccountLedgerEntry { EntryDateUtc = tarih, AccountType = hesap, AccountRef = null,
                Direction = LedgerDirection.Credit, Amount = money, SourceType = "MtvOdeme", SourceId = mtvId, Description = desc }
        ], ct);
    }

    /// <summary>
    /// Muayene ödeme (roadmap J2): DENGELİ defter — Borç Gider / Alacak Kasa-Banka (Ucret + ceza). FinanceWrite +
    /// dönem-kilidi + idempotency (SourceId=inspectionId). InspectionRecord.Odendi=true + Ceza (atomik, tek tx).
    /// </summary>
    public async Task MuayeneOdeAsync(Guid inspectionId, LedgerAccountType hesap, decimal ceza = 0m,
        DateTimeOffset? odemeTarih = null, string? doviz = "TRY", decimal kur = 1m, CancellationToken ct = default)
    {
        PermissionGuard.Require(_currentUser, Permission.FinanceWrite);
        if (hesap is not (LedgerAccountType.Kasa or LedgerAccountType.Banka))
            throw new ValidationException("Ödeme hesabı Kasa veya Banka olmalıdır.");
        if (kur <= 0m) throw new ValidationException("Kur pozitif olmalıdır.");
        if (ceza < 0m) throw new ValidationException("Ceza negatif olamaz.");

        var rec = await _repository.FindInspectionAsync(inspectionId, ct) ?? throw new ValidationException("Muayene kaydı bulunamadı.");
        if (rec.Odendi) throw new ValidationException("Muayene zaten ödendi.");
        var toplam = rec.Ucret + ceza;
        if (toplam <= 0m) throw new ValidationException("Muayene ödeme tutarı pozitif olmalıdır.");

        var tarih = odemeTarih ?? DateTimeOffset.UtcNow;
        await _lock.EnsureOpenAsync(tarih, ct); // dönem kilidi

        var money = new Money(toplam, (doviz ?? "TRY").Trim().ToUpperInvariant(), kur);
        var desc = $"Muayene ödeme {rec.MuayeneTarihi.LocalDateTime:dd.MM.yyyy}" + (ceza > 0m ? $" (+ceza {ceza})" : "");
        await _repository.PostMuayeneOdemeAsync(inspectionId, ceza,
        [
            new AccountLedgerEntry { EntryDateUtc = tarih, AccountType = LedgerAccountType.Gider, AccountRef = null,
                Direction = LedgerDirection.Debit, Amount = money, SourceType = "MuayeneOdeme", SourceId = inspectionId, Description = desc },
            new AccountLedgerEntry { EntryDateUtc = tarih, AccountType = hesap, AccountRef = null,
                Direction = LedgerDirection.Credit, Amount = money, SourceType = "MuayeneOdeme", SourceId = inspectionId, Description = desc }
        ], ct);
    }

    /// <summary>
    /// Sigorta ödeme (roadmap J3): DENGELİ defter — Borç Gider / Alacak Kasa-Banka (Prim + zeyil ek prim).
    /// FinanceWrite + dönem-kilidi + idempotency (SourceId=policyId). InsurancePolicy.Odendi=true + ZeyilPrim
    /// (atomik, tek tx). Para birimi poliçenin Currency'si; kur ile baz tutara çevrilir.
    /// </summary>
    public async Task SigortaOdeAsync(Guid policyId, LedgerAccountType hesap, decimal zeyilEkPrim = 0m,
        DateTimeOffset? odemeTarih = null, decimal kur = 1m, CancellationToken ct = default)
    {
        PermissionGuard.Require(_currentUser, Permission.FinanceWrite);
        if (hesap is not (LedgerAccountType.Kasa or LedgerAccountType.Banka))
            throw new ValidationException("Ödeme hesabı Kasa veya Banka olmalıdır.");
        if (kur <= 0m) throw new ValidationException("Kur pozitif olmalıdır.");
        if (zeyilEkPrim < 0m) throw new ValidationException("Zeyil ek prim negatif olamaz.");

        var rec = await _repository.FindInsuranceAsync(policyId, ct) ?? throw new ValidationException("Sigorta poliçesi bulunamadı.");
        if (rec.Odendi) throw new ValidationException("Sigorta zaten ödendi.");
        var toplam = rec.Prim + zeyilEkPrim;
        if (toplam <= 0m) throw new ValidationException("Sigorta ödeme tutarı pozitif olmalıdır.");

        var tarih = odemeTarih ?? DateTimeOffset.UtcNow;
        await _lock.EnsureOpenAsync(tarih, ct); // dönem kilidi

        var money = new Money(toplam, (rec.Currency ?? "TRY").Trim().ToUpperInvariant(), kur);
        var desc = $"Sigorta ödeme {rec.Tip}" + (zeyilEkPrim > 0m ? $" (+zeyil {zeyilEkPrim})" : "");
        await _repository.PostSigortaOdemeAsync(policyId, zeyilEkPrim,
        [
            new AccountLedgerEntry { EntryDateUtc = tarih, AccountType = LedgerAccountType.Gider, AccountRef = null,
                Direction = LedgerDirection.Debit, Amount = money, SourceType = "SigortaOdeme", SourceId = policyId, Description = desc },
            new AccountLedgerEntry { EntryDateUtc = tarih, AccountType = hesap, AccountRef = null,
                Direction = LedgerDirection.Credit, Amount = money, SourceType = "SigortaOdeme", SourceId = policyId, Description = desc }
        ], ct);
    }

    private static void RequireVehicle(Guid vehicleId)
    {
        if (vehicleId == Guid.Empty) throw new ValidationException("Araç seçilmelidir.");
    }

    private static string? Trim(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
