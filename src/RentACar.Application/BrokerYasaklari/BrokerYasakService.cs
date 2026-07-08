using RentACar.Application.Authorization;
using RentACar.Application.Common;
using RentACar.Domain.Common;
using RentACar.Domain.Entities;

namespace RentACar.Application.BrokerYasaklari;

/// <summary>
/// Broker/kaynak satış yasağı master iş mantığı: doğrulama + kod benzersizliği + CRUD. Yazma →
/// OperationsWrite. Okuma (<see cref="ListActiveAsync"/>) yetkisiz (rez/kira akışı çağırır). Tenant
/// izolasyonu/audit alt katmanda otomatik. Saf kural-tanım — deftere kayıt postlamaz.
/// </summary>
public sealed class BrokerYasakService(IBrokerYasakRepository repository, ICurrentUser currentUser)
{
    private readonly IBrokerYasakRepository _repository = repository;
    private readonly ICurrentUser _currentUser = currentUser;

    public Task<IReadOnlyList<BrokerYasak>> ListAsync(CancellationToken ct = default)
        => _repository.ListAsync(ct);

    public Task<IReadOnlyList<BrokerYasak>> ListActiveAsync(CancellationToken ct = default)
        => _repository.ListActiveAsync(ct);

    public Task<BrokerYasak?> GetAsync(Guid id, CancellationToken ct = default)
        => _repository.FindAsync(id, ct);

    public async Task<Guid> CreateAsync(BrokerYasakInput input, CancellationToken ct = default)
    {
        PermissionGuard.Require(_currentUser, Permission.OperationsWrite);
        var n = Normalize(input);
        Validate(n);
        if (await _repository.KodExistsAsync(n.Kod, excludeId: null, ct))
            throw new ValidationException($"'{n.Kod}' kodlu broker yasağı zaten var.");

        var row = new BrokerYasak();
        Apply(row, n);
        await _repository.CreateAsync(row, ct);
        return row.Id;
    }

    public async Task<bool> UpdateAsync(Guid id, BrokerYasakInput input, CancellationToken ct = default)
    {
        PermissionGuard.Require(_currentUser, Permission.OperationsWrite);
        var n = Normalize(input);
        Validate(n);
        if (await _repository.KodExistsAsync(n.Kod, excludeId: id, ct))
            throw new ValidationException($"'{n.Kod}' kodlu broker yasağı zaten var.");

        return await _repository.UpdateAsync(id, row =>
        {
            Apply(row, n);
            row.UpdatedAtUtc = DateTimeOffset.UtcNow;
        }, ct);
    }

    public Task<bool> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        PermissionGuard.Require(_currentUser, Permission.OperationsWrite);
        return _repository.DeleteAsync(id, ct);
    }

    private static void Validate(BrokerYasakInput n)
    {
        if (string.IsNullOrWhiteSpace(n.Kod)) throw new ValidationException("Yasak kodu zorunludur.");
        if (n.Kod.Length > 32) throw new ValidationException("Yasak kodu en çok 32 karakter olabilir.");
        if (string.IsNullOrWhiteSpace(n.Ad)) throw new ValidationException("Yasak adı zorunludur.");
        if (n.MinGun is < 0) throw new ValidationException("Min gün negatif olamaz.");
        if (n.GecerlilikBas is { } b && n.GecerlilikBit is { } t && t < b)
            throw new ValidationException("Geçerlilik bitişi başlangıçtan önce olamaz.");
        if (n.MinGun is null && !n.TumSatisKapali)
            throw new ValidationException("En az bir kısıt tanımlanmalı: Min Gün veya 'Tüm satış kapalı'.");
    }

    private static BrokerYasakInput Normalize(BrokerYasakInput input) => new()
    {
        Kod = (input.Kod ?? string.Empty).Trim().ToUpperInvariant(),
        Ad = (input.Ad ?? string.Empty).Trim(),
        Aciklama = TrimOrNull(input.Aciklama),
        Kaynak = TrimOrNull(input.Kaynak),
        AracGrupKod = string.IsNullOrWhiteSpace(input.AracGrupKod) ? null : input.AracGrupKod.Trim().ToUpperInvariant(),
        Bolge = TrimOrNull(input.Bolge),
        MinGun = input.MinGun,
        TumSatisKapali = input.TumSatisKapali,
        GecerlilikBas = input.GecerlilikBas,
        GecerlilikBit = input.GecerlilikBit,
        Aktif = input.Aktif
    };

    private static string? TrimOrNull(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    private static void Apply(BrokerYasak row, BrokerYasakInput n)
    {
        row.Kod = n.Kod;
        row.Ad = n.Ad;
        row.Aciklama = n.Aciklama;
        row.Kaynak = n.Kaynak;
        row.AracGrupKod = n.AracGrupKod;
        row.Bolge = n.Bolge;
        row.MinGun = n.MinGun;
        row.TumSatisKapali = n.TumSatisKapali;
        row.GecerlilikBas = n.GecerlilikBas;
        row.GecerlilikBit = n.GecerlilikBit;
        row.Aktif = n.Aktif;
    }
}
