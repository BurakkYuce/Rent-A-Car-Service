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
    public Task<IReadOnlyList<Baf>> SearchAsync(BafFilter? filter = null, CancellationToken ct = default)
    {
        PermissionGuard.Require(_currentUser, Permission.OperationsWrite);
        return _repository.SearchAsync(BranchScope.EffectiveFilter(_currentUser), filter ?? new BafFilter(), ct);
    }

    public async Task<Baf?> GetAsync(Guid id, CancellationToken ct = default)
    {
        var baf = await _repository.FindAsync(id, ct);
        if (baf is not null) BranchScope.RequireInScope(_currentUser, baf.Sube); // tekil kapsam (M3 deseni)
        return baf;
    }

    /// <summary>Yakıt TEK iç ölçekte 0–12 (<see cref="FuelScale"/>, Karar (3)); boş = girilmedi.</summary>
    private static void FuelRange(int? fuel, string label)
    {
        if (fuel is { } y && !FuelScale.IsValid(y))
            throw new ValidationException($"{label} yakıt 0-{FuelScale.Max} aralığında olmalıdır.");
    }

    public async Task<Guid> CreateAsync(BafInput input, CancellationToken ct = default)
    {
        PermissionGuard.Require(_currentUser, Permission.OperationsWrite);
        if (input.PersonelId == Guid.Empty) throw new ValidationException("Personel seçilmelidir.");
        if (input.VehicleId == Guid.Empty) throw new ValidationException("Araç seçilmelidir.");
        if (input.CikisKm < 0) throw new ValidationException("Çıkış KM negatif olamaz.");
        FuelRange(input.CikisYakit, "Çıkış");

        var row = new Baf
        {
            Id = input.IslemAnahtari is { } ia && ia != Guid.Empty ? ia : Guid.NewGuid(), // F6.1b idempotent oluşturma
            PersonelId = input.PersonelId,
            VehicleId = input.VehicleId,
            CikisTarihi = input.CikisTarihi ?? DateTimeOffset.UtcNow,
            CikisKm = input.CikisKm,
            CikisYakit = input.CikisYakit,
            Sube = string.IsNullOrWhiteSpace(input.Sube) ? null : input.Sube.Trim(),
            Durum = Domain.Enums.BafStatus.Acik,
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
    /// Teslim al (dönüş). FAZ-18: <paramref name="returnBranch"/>/<paramref name="returnHour"/> BİLGİ alanlarıdır —
    /// şube KAPSAMI hâlâ çıkış şubesinden (<c>baf.Sube</c>) işler; dönüş şubesi kapsamı değiştirmez
    /// (aksi hâlde kullanıcı kendi göremediği bir şubeye "dönüş" yazarak kaydı kapsamından çıkarabilirdi).
    /// </summary>
    public async Task<bool> ReceiveAsync(Guid id, int returnKm, int? returnFuel, DateTimeOffset? returnDate = null,
        string? returnBranch = null, TimeOnly? returnHour = null, CancellationToken ct = default)
    {
        PermissionGuard.Require(_currentUser, Permission.OperationsWrite);
        var baf = await _repository.FindAsync(id, ct);
        if (baf is null) return false;
        BranchScope.RequireInScope(_currentUser, baf.Sube); // adversarial: tekil şube-kapsam
        if (baf.Durum != Domain.Enums.BafStatus.Acik) throw new ValidationException("Yalnız açık tahsis teslim alınabilir.");
        FuelRange(returnFuel, "Dönüş");
        if (returnKm < baf.CikisKm) throw new ValidationException("Dönüş KM çıkış KM'den küçük olamaz.");
        return await _repository.ReceiveAsync(id, returnKm, returnFuel, returnDate ?? DateTimeOffset.UtcNow,
            string.IsNullOrWhiteSpace(returnBranch) ? null : returnBranch.Trim(), returnHour, ct);
    }

    public async Task<bool> CancelAsync(Guid id, CancellationToken ct = default)
    {
        PermissionGuard.Require(_currentUser, Permission.OperationsDelete); // inceltme
        var baf = await _repository.FindAsync(id, ct);
        if (baf is null) return false;
        BranchScope.RequireInScope(_currentUser, baf.Sube); // adversarial: tekil şube-kapsam
        return await _repository.CancelAsync(id, ct);
    }

    /// <summary>
    /// F6.1b — <see cref="ReceiveAsync"/>'in KİLİTLİ karşılığı (/api/ui). Blazor yolu durumu kilitsiz okuyordu:
    /// eşzamanlı iki teslim (ya da teslim + iptal) ikisi de "Açık" görüp birbirini eziyordu. Kapsam ve durum çitleri
    /// satır kilidinin ALTINDA yeniden denetlenir (kapsam önce — başka şubenin kaydının durumu sızmasın).
    /// </summary>
    public async Task<bool> ReceiveLockedAsync(Guid id, int returnKm, int? returnFuel, DateTimeOffset? returnDate,
        string? returnBranch, TimeOnly? returnHour, CancellationToken ct = default)
    {
        PermissionGuard.Require(_currentUser, Permission.OperationsWrite);
        FuelRange(returnFuel, "Dönüş");
        var branch = string.IsNullOrWhiteSpace(returnBranch) ? null : returnBranch.Trim();
        return await _repository.UpdateLockedAsync(id, row =>
        {
            BranchScope.RequireInScope(_currentUser, row.Sube);
            if (row.Durum != Domain.Enums.BafStatus.Acik) throw new ValidationException("Yalnız açık tahsis teslim alınabilir.");
            if (returnKm < row.CikisKm) throw new ValidationException("Dönüş KM çıkış KM'den küçük olamaz.");
            row.DonusTarihi = returnDate ?? DateTimeOffset.UtcNow;
            row.DonusKm = returnKm;
            row.DonusYakit = returnFuel;
            if (branch is not null) row.DonusSube = branch;
            if (returnHour is { } ds) row.DonusSaat = ds;
            row.Durum = Domain.Enums.BafStatus.Kapandi;
            row.UpdatedAtUtc = DateTimeOffset.UtcNow;
        }, ct);
    }

    /// <summary>F6.1b — kilitli iptal: yalnız AÇIK tahsis iptal edilir (kapanmış tahsisin dönüş km/yakıt kaydı
    /// iptalle "yok" sayılmasın); zaten iptal olan → 400.</summary>
    public async Task<bool> IsCancelLockedAsync(Guid id, CancellationToken ct = default)
    {
        PermissionGuard.Require(_currentUser, Permission.OperationsDelete);
        return await _repository.UpdateLockedAsync(id, row =>
        {
            BranchScope.RequireInScope(_currentUser, row.Sube);
            if (row.Durum != Domain.Enums.BafStatus.Acik) throw new ValidationException("Yalnız açık tahsis iptal edilebilir.");
            row.Durum = Domain.Enums.BafStatus.Iptal;
            row.UpdatedAtUtc = DateTimeOffset.UtcNow;
        }, ct);
    }
}
