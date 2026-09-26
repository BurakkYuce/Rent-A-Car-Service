using RentACar.Application.Authorization;
using RentACar.Application.Common;
using RentACar.Domain.Common;
using RentACar.Domain.Entities;

namespace RentACar.Application.Crm;

/// <summary>
/// Müşteri şikayeti iş mantığı (roadmap C3): CRUD + Konu doğrulama + durum/çözüm takibi. Yazma →
/// OperationsWrite. Tenant izolasyonu/audit alt katmanda.
/// </summary>
public sealed class ComplaintService(IComplaintRepository repository, ICurrentUser currentUser, CrmScopeGuard scope)
{
    private readonly IComplaintRepository _repository = repository;
    private readonly ICurrentUser _currentUser = currentUser;

    /// <summary>r317 M1: güncellemede mevcut kaydın ve hedefin şube kapsamı (her iki arayüz için tek yer).</summary>
    private async Task<bool> ScopedUpdateAsync(Guid id, SikayetInput input, CancellationToken ct)
    {
        if (await _repository.FindAsync(id, ct) is not { } current) return false;
        await scope.RequireUpdateAsync(current.RentalId, current.CikisOfisi, input.RentalId, input.CikisOfisi, ct);
        return true;
    }

    public Task<IReadOnlyList<Sikayet>> ListAsync(CancellationToken ct = default) => _repository.ListAsync(ct);
    public Task<Sikayet?> GetAsync(Guid id, CancellationToken ct = default) => _repository.FindAsync(id, ct);

    /// <summary>FAZ-43 — filtreli liste (sözleşme/araç/müşteri/personel adları çözülmüş).</summary>
    public Task<IReadOnlyList<SikayetSatirDto>> SearchAsync(
        SikayetFilter? filter = null, CancellationToken ct = default)
        => _repository.SearchAsync(filter, ct);

    public async Task<Guid> CreateAsync(SikayetInput input, CancellationToken ct = default)
    {
        PermissionGuard.Require(_currentUser, Permission.OperationsWrite);
        Validate(input);
        await scope.RequireTargetAsync(input.RentalId, input.CikisOfisi, creating: true, ct);
        var row = new Sikayet();
        Apply(row, input);
        await _repository.CreateAsync(row, ct);
        return row.Id;
    }

    public async Task<bool> UpdateAsync(Guid id, SikayetInput input, CancellationToken ct = default)
    {
        PermissionGuard.Require(_currentUser, Permission.OperationsWrite);
        if (!await ScopedUpdateAsync(id, input, ct)) return false;
        Validate(input);
        return await _repository.UpdateAsync(id, row =>
        {
            Apply(row, input);
            row.UpdatedAtUtc = DateTimeOffset.UtcNow;
        }, ct);
    }

    /// <summary>F7.1 — tam değiştirme, iyimser eşzamanlılıkla (satır kilidi altında sürüm; farklı → 409).</summary>
    public async Task<bool> UpdateAsync(Guid id, SikayetInput input, string expectedVersion, CancellationToken ct = default)
    {
        PermissionGuard.Require(_currentUser, Permission.OperationsWrite);
        if (!await ScopedUpdateAsync(id, input, ct)) return false;
        Validate(input);
        return await _repository.UpdateAsync(id, expectedVersion, row =>
        {
            Apply(row, input);
            row.UpdatedAtUtc = DateTimeOffset.UtcNow;
        }, ct);
    }

    /// <summary>F7.1 — satır sürümü (PUT'un <c>surum</c>'u); yok/başka kiracı → null.</summary>
    public Task<string?> GetVersionAsync(Guid id, CancellationToken ct = default) => _repository.GetVersionAsync(id, ct);

    public async Task<bool> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        PermissionGuard.Require(_currentUser, Permission.OperationsWrite);
        if (await _repository.FindAsync(id, ct) is not { } current) return false;
        await scope.RequireRecordAsync(current.RentalId, current.CikisOfisi, ct); // r317 M1
        return await _repository.DeleteAsync(id, ct);
    }

    private static void Validate(SikayetInput n)
    {
        if (string.IsNullOrWhiteSpace(n.Konu)) throw new ValidationException("Konu zorunludur.");
        if (n.Konu!.Trim().Length > 256) throw new ValidationException("Konu en çok 256 karakter olabilir.");
    }

    private static void Apply(Sikayet row, SikayetInput n)
    {
        row.CariId = n.CariId;
        row.Konu = n.Konu!.Trim();
        row.Detay = string.IsNullOrWhiteSpace(n.Detay) ? null : n.Detay.Trim();
        row.Durum = n.Durum;
        row.Tarih = n.Tarih ?? DateTimeOffset.UtcNow;
        row.Cozum = string.IsNullOrWhiteSpace(n.Cozum) ? null : n.Cozum.Trim();
        // FAZ-43 teslim/dönüş bağı
        row.RentalId = n.RentalId;
        row.TeslimAlanPersonelId = n.TeslimAlanPersonelId;
        row.TeslimEdenPersonelId = n.TeslimEdenPersonelId;
        row.Puan = n.Puan;
        row.SikayetKanali = string.IsNullOrWhiteSpace(n.SikayetKanali) ? null : n.SikayetKanali.Trim();
        row.SikayetYeri = n.SikayetYeri;
        row.CikisOfisi = string.IsNullOrWhiteSpace(n.CikisOfisi) ? null : n.CikisOfisi.Trim();
    }
}
