using RentACar.Application.Authorization;
using RentACar.Application.Common;
using RentACar.Application.Periods;
using RentACar.Domain.Common;
using RentACar.Domain.Entities;
using RentACar.Domain.Enums;

namespace RentACar.Application.ServiceRecords;

/// <summary>
/// Servis/bakım: kayıt (kalemlerle) + durum akışı (Açık → Serviste → Tamamlandi / Iptal) +
/// araç durumu kuplajı (servise alınınca Serviste, çıkınca Musait). İşçilik kalemleri eklenir.
/// Mali belge değildir (maliyet bilgilendirme); gerçek gider Gider dilimine bağlanır (follow-up).
/// Hasar rücu: tamamlanmış servis maliyeti kusur-oranıyla cari'ye yansıtılır (J4).
/// </summary>
public sealed class ServiceRecordService(
    IServiceRecordRepository repository, ICurrentUser currentUser, IPeriodLockGuard periodLock)
{
    private readonly IServiceRecordRepository _repository = repository;
    private readonly ICurrentUser _currentUser = currentUser;
    private readonly IPeriodLockGuard _lock = periodLock;

    public Task<IReadOnlyList<ServiceRecord>> ListAsync(CancellationToken ct = default)
        => _repository.ListAsync(ct);
    public Task<ServiceRecord?> GetAsync(Guid id, CancellationToken ct = default)
        => _repository.FindAsync(id, ct);

    public async Task<Guid> CreateAsync(ServiceRecordInput input, CancellationToken ct = default)
    {
        if (input.VehicleId == Guid.Empty) throw new ValidationException("Araç seçilmelidir.");
        if (input.GirisKm < 0) throw new ValidationException("Giriş KM negatif olamaz.");
        if (input.KusurOrani is < 0 or > 1) throw new ValidationException("Kusur oranı 0 ile 1 arasında olmalıdır.");
        foreach (var l in input.Lines)
        {
            if (string.IsNullOrWhiteSpace(l.Aciklama)) throw new ValidationException("Kalem açıklaması zorunludur.");
            if (l.Tutar < 0) throw new ValidationException("Kalem tutarı negatif olamaz.");
        }

        var record = new ServiceRecord
        {
            VehicleId = input.VehicleId,
            Tip = input.Tip,
            Durum = ServisDurum.Acik,
            AtolyeAdi = input.AtolyeAdi,
            GirisTarihi = input.GirisTarihi ?? DateTimeOffset.UtcNow,
            GirisKm = input.GirisKm,
            HasarSorumlu = input.HasarSorumlu,
            KusurOrani = input.KusurOrani,
            Aciklama = input.Aciklama,
            Lines = input.Lines.Select(l => new ServiceLine { Aciklama = l.Aciklama.Trim(), Tutar = l.Tutar }).ToList()
        };
        await _repository.CreateAsync(record, ct);
        return record.Id;
    }

    /// <summary>Açık → Serviste; araç Serviste'ye geçer.</summary>
    public Task<bool> BaslatAsync(Guid id, CancellationToken ct = default)
        => _repository.TransitionAsync(id, r =>
        {
            if (r.Durum != ServisDurum.Acik)
                throw new ValidationException("Yalnız 'Açık' servis başlatılabilir.");
            r.Durum = ServisDurum.Serviste;
        }, setVehicleTo: VehicleStatus.Serviste, onlyWhenVehicleIs: null, ct);

    /// <summary>Serviste → Tamamlandi; çıkış KM/tarih, sonraki bakım KM; araç Musait'e döner.</summary>
    public Task<bool> TamamlaAsync(Guid id, int cikisKm, int? sonrakiBakimKm = null, CancellationToken ct = default)
        => _repository.TransitionAsync(id, r =>
        {
            if (r.Durum != ServisDurum.Serviste)
                throw new ValidationException("Yalnız 'Serviste' kayıt tamamlanabilir.");
            if (cikisKm < r.GirisKm)
                throw new ValidationException("Çıkış KM giriş KM'den küçük olamaz.");
            r.Durum = ServisDurum.Tamamlandi;
            r.CikisKm = cikisKm;
            r.CikisTarihi = DateTimeOffset.UtcNow;
            r.SonrakiBakimKm = sonrakiBakimKm;
        }, setVehicleTo: VehicleStatus.Musait, onlyWhenVehicleIs: VehicleStatus.Serviste, ct);

    /// <summary>Açık/Serviste → Iptal; araç Serviste'den çıktıysa Musait'e döner.</summary>
    public Task<bool> IptalAsync(Guid id, CancellationToken ct = default)
        => _repository.TransitionAsync(id, r =>
        {
            if (r.Durum is ServisDurum.Tamamlandi or ServisDurum.Iptal)
                throw new ValidationException("Kapanmış servis iptal edilemez.");
            r.Durum = ServisDurum.Iptal;
        }, setVehicleTo: VehicleStatus.Musait, onlyWhenVehicleIs: VehicleStatus.Serviste, ct);

    public async Task<bool> KalemEkleAsync(Guid id, string aciklama, decimal tutar, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(aciklama)) throw new ValidationException("Kalem açıklaması zorunludur.");
        if (tutar < 0) throw new ValidationException("Kalem tutarı negatif olamaz.");
        return await _repository.AddLineAsync(id, aciklama.Trim(), tutar, ct);
    }

    /// <summary>
    /// Servis maliyetini hasar rücu olarak cari'ye yansıt (roadmap J4): DENGELİ defter — Borç Cari /
    /// Alacak Gelir. Yansıtılan = ToplamIscilik × KusurOrani. Yalnız Tamamlanmış + sorumlusu Müşteri/Sigorta +
    /// kusur>0 + henüz yansıtılmamış. FinanceWrite + dönem-kilidi + idempotency (SourceId=serviceId).
    /// </summary>
    public async Task YansitAsync(Guid serviceId, Guid cariId, DateTimeOffset? tarih = null,
        string? doviz = "TRY", decimal kur = 1m, CancellationToken ct = default)
    {
        PermissionGuard.Require(_currentUser, Permission.FinanceWrite);
        if (cariId == Guid.Empty) throw new ValidationException("Yansıtılacak cari seçilmelidir.");
        if (kur <= 0m) throw new ValidationException("Kur pozitif olmalıdır.");

        var rec = await _repository.FindAsync(serviceId, ct) ?? throw new ValidationException("Servis kaydı bulunamadı.");
        if (rec.Yansitildi) throw new ValidationException("Servis maliyeti zaten yansıtıldı.");
        if (rec.Durum != ServisDurum.Tamamlandi) throw new ValidationException("Yalnız tamamlanmış servis yansıtılabilir.");
        if (rec.HasarSorumlu is not (HasarSorumlu.Musteri or HasarSorumlu.Sigorta))
            throw new ValidationException("Yansıtma yalnız sorumlusu Müşteri/Sigorta olan hasarda yapılır.");
        if (rec.KusurOrani is not > 0m) throw new ValidationException("Kusur oranı pozitif olmalıdır.");

        var yansitilan = decimal.Round(rec.ToplamIscilik * rec.KusurOrani.Value, 2, MidpointRounding.AwayFromZero);
        if (yansitilan <= 0m) throw new ValidationException("Yansıtılacak tutar pozitif olmalıdır.");

        var entryDate = tarih ?? DateTimeOffset.UtcNow;
        await _lock.EnsureOpenAsync(entryDate, ct); // dönem kilidi

        var money = new Money(yansitilan, (doviz ?? "TRY").Trim().ToUpperInvariant(), kur);
        var desc = $"Servis rücu {rec.No} (kusur %{rec.KusurOrani.Value * 100m:0.##})";
        await _repository.PostYansitmaAsync(serviceId, cariId, yansitilan,
        [
            new AccountLedgerEntry { EntryDateUtc = entryDate, AccountType = LedgerAccountType.Cari, AccountRef = cariId,
                Direction = LedgerDirection.Debit, Amount = money, SourceType = "ServisYansitma", SourceId = serviceId, Description = desc },
            new AccountLedgerEntry { EntryDateUtc = entryDate, AccountType = LedgerAccountType.Gelir, AccountRef = null,
                Direction = LedgerDirection.Credit, Amount = money, SourceType = "ServisYansitma", SourceId = serviceId, Description = desc }
        ], ct);
    }
}
