using RentACar.Application.Authorization;
using RentACar.Application.Common;
using RentACar.Domain.Common;
using RentACar.Domain.Entities;

namespace RentACar.Application.Baflar;

/// <summary>
/// BAF (personel araç tahsis) iş mantığı (roadmap L5): tahsis oluştur (çıkış) + teslim al (dönüş km/yakıt) + iptal.
/// DEFTER POSTLAMAZ (zimmet kaydı) → salt takip; yazma OperationsWrite.
/// </summary>
public sealed class BafService(IBafRepository repository, ICurrentUser currentUser)
{
    private readonly IBafRepository _repository = repository;
    private readonly ICurrentUser _currentUser = currentUser;

    public Task<IReadOnlyList<Baf>> ListAsync(CancellationToken ct = default)
        => _repository.ListAsync(BranchScope.EffectiveFilter(_currentUser), ct); // C3 (Baf FK'sız → metin dalı)

    /// <summary>
    /// FAZ-18 — filtreli liste (canlı baf_ara.aspx). Şube kapsamı filtreden BAĞIMSIZ uygulanır:
    /// Operatör "Ofis=Kadıköy" yazsa bile kendi şubesi dışını göremez (filtre kapsamı GENİŞLETEMEZ).
    /// </summary>
    public Task<IReadOnlyList<Baf>> SearchAsync(BafFilter? filtre = null, CancellationToken ct = default)
    {
        PermissionGuard.Require(_currentUser, Permission.OperationsWrite);
        return _repository.SearchAsync(BranchScope.EffectiveFilter(_currentUser), filtre ?? new BafFilter(), ct);
    }

    public async Task<Baf?> GetAsync(Guid id, CancellationToken ct = default)
    {
        var baf = await _repository.FindAsync(id, ct);
        if (baf is not null) BranchScope.RequireInScope(_currentUser, baf.Sube); // tekil kapsam (M3 deseni)
        return baf;
    }

    public async Task<Guid> CreateAsync(BafInput input, CancellationToken ct = default)
    {
        PermissionGuard.Require(_currentUser, Permission.OperationsWrite);
        if (input.PersonelId == Guid.Empty) throw new ValidationException("Personel seçilmelidir.");
        if (input.VehicleId == Guid.Empty) throw new ValidationException("Araç seçilmelidir.");
        if (input.CikisKm < 0) throw new ValidationException("Çıkış KM negatif olamaz.");

        var row = new Baf
        {
            Id = input.IslemAnahtari is { } ia && ia != Guid.Empty ? ia : Guid.NewGuid(), // F6.1b idempotent oluşturma
            PersonelId = input.PersonelId,
            VehicleId = input.VehicleId,
            CikisTarihi = input.CikisTarihi ?? DateTimeOffset.UtcNow,
            CikisKm = input.CikisKm,
            CikisYakit = input.CikisYakit,
            Sube = string.IsNullOrWhiteSpace(input.Sube) ? null : input.Sube.Trim(),
            Durum = Domain.Enums.BafDurum.Acik,
            Aciklama = string.IsNullOrWhiteSpace(input.Aciklama) ? null : input.Aciklama.Trim(),
            // FAZ-18 bilgi alanları — hiçbiri iş kuralı işletmez, defter postlamaz.
            KullanimAmaci = input.KullanimAmaci,
            Onaylayan = input.Onaylayan == Guid.Empty ? null : input.Onaylayan,
            KirayaVer = input.KirayaVer,
            CikisSaat = input.CikisSaat
        };
        await _repository.CreateAsync(row, ct);
        return row.Id;
    }

    /// <summary>
    /// Teslim al (dönüş). FAZ-18: <paramref name="donusSube"/>/<paramref name="donusSaat"/> BİLGİ alanlarıdır —
    /// şube KAPSAMI hâlâ çıkış şubesinden (<c>baf.Sube</c>) işler; dönüş şubesi kapsamı değiştirmez
    /// (aksi hâlde kullanıcı kendi göremediği bir şubeye "dönüş" yazarak kaydı kapsamından çıkarabilirdi).
    /// </summary>
    public async Task<bool> TeslimAlAsync(Guid id, int donusKm, int? donusYakit, DateTimeOffset? donusTarihi = null,
        string? donusSube = null, TimeOnly? donusSaat = null, CancellationToken ct = default)
    {
        PermissionGuard.Require(_currentUser, Permission.OperationsWrite);
        var baf = await _repository.FindAsync(id, ct);
        if (baf is null) return false;
        BranchScope.RequireInScope(_currentUser, baf.Sube); // adversarial: tekil şube-kapsam
        if (baf.Durum != Domain.Enums.BafDurum.Acik) throw new ValidationException("Yalnız açık tahsis teslim alınabilir.");
        if (donusKm < baf.CikisKm) throw new ValidationException("Dönüş KM çıkış KM'den küçük olamaz.");
        return await _repository.TeslimAlAsync(id, donusKm, donusYakit, donusTarihi ?? DateTimeOffset.UtcNow,
            string.IsNullOrWhiteSpace(donusSube) ? null : donusSube.Trim(), donusSaat, ct);
    }

    public async Task<bool> IptalAsync(Guid id, CancellationToken ct = default)
    {
        PermissionGuard.Require(_currentUser, Permission.OperationsDelete); // inceltme
        var baf = await _repository.FindAsync(id, ct);
        if (baf is null) return false;
        BranchScope.RequireInScope(_currentUser, baf.Sube); // adversarial: tekil şube-kapsam
        return await _repository.IptalAsync(id, ct);
    }

    /// <summary>
    /// F6.1b — <see cref="TeslimAlAsync"/>'in KİLİTLİ karşılığı (/api/ui). Blazor yolu durumu kilitsiz okuyordu:
    /// eşzamanlı iki teslim (ya da teslim + iptal) ikisi de "Açık" görüp birbirini eziyordu. Kapsam ve durum çitleri
    /// satır kilidinin ALTINDA yeniden denetlenir (kapsam önce — başka şubenin kaydının durumu sızmasın).
    /// </summary>
    public async Task<bool> TeslimAlKilitliAsync(Guid id, int donusKm, int? donusYakit, DateTimeOffset? donusTarihi,
        string? donusSube, TimeOnly? donusSaat, CancellationToken ct = default)
    {
        PermissionGuard.Require(_currentUser, Permission.OperationsWrite);
        var sube = string.IsNullOrWhiteSpace(donusSube) ? null : donusSube.Trim();
        return await _repository.KilitliGuncelleAsync(id, row =>
        {
            BranchScope.RequireInScope(_currentUser, row.Sube);
            if (row.Durum != Domain.Enums.BafDurum.Acik) throw new ValidationException("Yalnız açık tahsis teslim alınabilir.");
            if (donusKm < row.CikisKm) throw new ValidationException("Dönüş KM çıkış KM'den küçük olamaz.");
            row.DonusTarihi = donusTarihi ?? DateTimeOffset.UtcNow;
            row.DonusKm = donusKm;
            row.DonusYakit = donusYakit;
            if (sube is not null) row.DonusSube = sube;
            if (donusSaat is { } ds) row.DonusSaat = ds;
            row.Durum = Domain.Enums.BafDurum.Kapandi;
            row.UpdatedAtUtc = DateTimeOffset.UtcNow;
        }, ct);
    }

    /// <summary>F6.1b — kilitli iptal: yalnız AÇIK tahsis iptal edilir (kapanmış tahsisin dönüş km/yakıt kaydı
    /// iptalle "yok" sayılmasın); zaten iptal olan → 400.</summary>
    public async Task<bool> IptalKilitliAsync(Guid id, CancellationToken ct = default)
    {
        PermissionGuard.Require(_currentUser, Permission.OperationsDelete);
        return await _repository.KilitliGuncelleAsync(id, row =>
        {
            BranchScope.RequireInScope(_currentUser, row.Sube);
            if (row.Durum != Domain.Enums.BafDurum.Acik) throw new ValidationException("Yalnız açık tahsis iptal edilebilir.");
            row.Durum = Domain.Enums.BafDurum.Iptal;
            row.UpdatedAtUtc = DateTimeOffset.UtcNow;
        }, ct);
    }
}
