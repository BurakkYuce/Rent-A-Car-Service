using RentACar.Application.Authorization;
using RentACar.Application.Common;
using RentACar.Domain.Common;
using RentACar.Domain.Entities;

namespace RentACar.Application.RateMatrices;

/// <summary>
/// Tarife matrisi master iş mantığı: doğrulama + kod benzersizliği + CRUD. Yazma operasyonel
/// fiyat yapılandırmasıdır → <see cref="Permission.OperationsWrite"/>. Açılır liste/fiyat motoru
/// okuması (<see cref="ListActiveAsync"/>) yetkisizdir. Tenant izolasyonu/audit alt katmanda otomatik.
/// Saf fiyat-tanım — deftere kayıt postlamaz.
/// </summary>
public sealed class RateMatrixService(IRateMatrixRepository repository, ICurrentUser currentUser)
{
    private readonly IRateMatrixRepository _repository = repository;
    private readonly ICurrentUser _currentUser = currentUser;

    public Task<IReadOnlyList<RateMatrix>> ListAsync(CancellationToken ct = default)
        => _repository.ListAsync(ct);

    /// <summary>Fiyat motoru / açılır liste kaynağı (yalnız aktif). Yetki gerektirmez.</summary>
    public Task<IReadOnlyList<RateMatrix>> ListActiveAsync(CancellationToken ct = default)
        => _repository.ListActiveAsync(ct);

    public Task<RateMatrix?> GetAsync(Guid id, CancellationToken ct = default)
        => _repository.FindAsync(id, ct);

    public async Task<Guid> CreateAsync(RateMatrixInput input, CancellationToken ct = default)
    {
        PermissionGuard.Require(_currentUser, Permission.OperationsWrite);
        var n = Normalize(input);
        Validate(n);
        if (await _repository.KodExistsAsync(n.Kod, excludeId: null, ct))
            throw new ValidationException($"'{n.Kod}' kodlu tarife matrisi zaten var.");

        var row = new RateMatrix();
        Apply(row, n);
        await _repository.CreateAsync(row, ct);
        return row.Id;
    }

    public async Task<bool> UpdateAsync(Guid id, RateMatrixInput input, CancellationToken ct = default)
    {
        PermissionGuard.Require(_currentUser, Permission.OperationsWrite);
        var n = Normalize(input);
        Validate(n);
        if (await _repository.KodExistsAsync(n.Kod, excludeId: id, ct))
            throw new ValidationException($"'{n.Kod}' kodlu tarife matrisi zaten var.");

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

    private static void Validate(RateMatrixInput n)
    {
        if (string.IsNullOrWhiteSpace(n.Kod)) throw new ValidationException("Tarife kodu zorunludur.");
        if (n.Kod.Length > 32) throw new ValidationException("Tarife kodu en çok 32 karakter olabilir.");
        if (string.IsNullOrWhiteSpace(n.Ad)) throw new ValidationException("Tarife adı zorunludur.");

        Pos(n.Gun1, "Gün 1 fiyatı"); Pos(n.Gun2, "Gün 2 fiyatı"); Pos(n.Gun3, "Gün 3 fiyatı");
        Pos(n.Gun4, "Gün 4 fiyatı"); Pos(n.Gun5, "Gün 5 fiyatı"); Pos(n.Gun6, "Gün 6 fiyatı");
        Pos(n.Gun7, "Gün 7 fiyatı");
        if (n.MaxEsneklik is < 0m or > 100m)
            throw new ValidationException("Esneklik (indirim) oranı 0 ile 100 arasında olmalıdır (%).");
        if (n.BasTar is { } b && n.BitTar is { } t && t < b)
            throw new ValidationException("Bitiş tarihi başlangıçtan önce olamaz.");
    }

    private static void Pos(decimal? v, string label)
    {
        if (v is < 0m) throw new ValidationException($"{label} negatif olamaz.");
    }

    private static RateMatrixInput Normalize(RateMatrixInput input) => new()
    {
        Kod = (input.Kod ?? string.Empty).Trim().ToUpperInvariant(),
        Ad = (input.Ad ?? string.Empty).Trim(),
        Aciklama = TrimOrNull(input.Aciklama),
        Kanal = TrimOrNull(input.Kanal),
        Sube = TrimOrNull(input.Sube),
        Lokasyon = TrimOrNull(input.Lokasyon),
        AracGrupKod = string.IsNullOrWhiteSpace(input.AracGrupKod) ? null : input.AracGrupKod.Trim().ToUpperInvariant(),
        ParaBirimi = string.IsNullOrWhiteSpace(input.ParaBirimi) ? null : input.ParaBirimi.Trim().ToUpperInvariant(),
        BasTar = input.BasTar,
        BitTar = input.BitTar,
        Gun1 = input.Gun1, Gun2 = input.Gun2, Gun3 = input.Gun3, Gun4 = input.Gun4,
        Gun5 = input.Gun5, Gun6 = input.Gun6, Gun7 = input.Gun7,
        MaxEsneklik = input.MaxEsneklik,
        OnayDurumu = input.OnayDurumu,
        Onaylayan = TrimOrNull(input.Onaylayan),
        OnayZaman = input.OnayZaman,
        Aktif = input.Aktif
    };

    private static string? TrimOrNull(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    private static void Apply(RateMatrix row, RateMatrixInput n)
    {
        row.Kod = n.Kod;
        row.Ad = n.Ad;
        row.Aciklama = n.Aciklama;
        row.Kanal = n.Kanal;
        row.Sube = n.Sube;
        row.Lokasyon = n.Lokasyon;
        row.AracGrupKod = n.AracGrupKod;
        row.ParaBirimi = n.ParaBirimi;
        row.BasTar = n.BasTar;
        row.BitTar = n.BitTar;
        row.Gun1 = n.Gun1; row.Gun2 = n.Gun2; row.Gun3 = n.Gun3; row.Gun4 = n.Gun4;
        row.Gun5 = n.Gun5; row.Gun6 = n.Gun6; row.Gun7 = n.Gun7;
        row.MaxEsneklik = n.MaxEsneklik;
        row.OnayDurumu = n.OnayDurumu;
        row.Onaylayan = n.Onaylayan;
        row.OnayZaman = n.OnayZaman;
        row.Aktif = n.Aktif;
    }
}
