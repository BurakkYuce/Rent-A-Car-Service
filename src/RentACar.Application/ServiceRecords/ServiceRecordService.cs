using RentACar.Application.Common;
using RentACar.Domain.Entities;
using RentACar.Domain.Enums;

namespace RentACar.Application.ServiceRecords;

/// <summary>
/// Servis/bakım: kayıt (kalemlerle) + durum akışı (Açık → Serviste → Tamamlandi / Iptal) +
/// araç durumu kuplajı (servise alınınca Serviste, çıkınca Musait). İşçilik kalemleri eklenir.
/// Mali belge değildir (maliyet bilgilendirme); gerçek gider Gider dilimine bağlanır (follow-up).
/// </summary>
public sealed class ServiceRecordService(IServiceRecordRepository repository)
{
    private readonly IServiceRecordRepository _repository = repository;

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
}
