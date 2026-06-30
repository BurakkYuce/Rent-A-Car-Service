using RentACar.Application.Authorization;
using RentACar.Application.Common;
using RentACar.Domain.Common;
using RentACar.Domain.Entities;

namespace RentACar.Application.Branches;

/// <summary>
/// Şube master iş mantığı: doğrulama + kod benzersizliği + CRUD. Şube yönetimi yönetsel
/// yapılandırmadır → <see cref="Permission.ManageUsers"/> (Admin) ile korunur. Tenant
/// izolasyonu ve audit alt katmanda otomatik.
/// </summary>
public sealed class BranchService(IBranchRepository repository, ICurrentUser currentUser)
{
    private readonly IBranchRepository _repository = repository;
    private readonly ICurrentUser _currentUser = currentUser;

    public Task<IReadOnlyList<Branch>> ListAsync(CancellationToken ct = default)
        => _repository.ListAsync(ct);

    /// <summary>Açılır liste kaynağı (yalnız aktif). Okuma — yetki gerektirmez.</summary>
    public Task<IReadOnlyList<Branch>> ListActiveAsync(CancellationToken ct = default)
        => _repository.ListActiveAsync(ct);

    public Task<Branch?> GetAsync(Guid id, CancellationToken ct = default)
        => _repository.FindAsync(id, ct);

    public async Task<Guid> CreateAsync(BranchInput input, CancellationToken ct = default)
    {
        PermissionGuard.Require(_currentUser, Permission.ManageUsers);
        var n = Normalize(input);
        Validate(n);
        if (await _repository.KodExistsAsync(n.Kod, excludeId: null, ct))
            throw new ValidationException($"'{n.Kod}' kodlu şube zaten var.");

        var branch = new Branch();
        Apply(branch, n);
        await _repository.CreateAsync(branch, ct);
        return branch.Id;
    }

    public async Task<bool> UpdateAsync(Guid id, BranchInput input, CancellationToken ct = default)
    {
        PermissionGuard.Require(_currentUser, Permission.ManageUsers);
        var n = Normalize(input);
        Validate(n);
        if (await _repository.KodExistsAsync(n.Kod, excludeId: id, ct))
            throw new ValidationException($"'{n.Kod}' kodlu şube zaten var.");

        return await _repository.UpdateAsync(id, b =>
        {
            Apply(b, n);
            b.UpdatedAtUtc = DateTimeOffset.UtcNow;
        }, ct);
    }

    public Task<bool> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        PermissionGuard.Require(_currentUser, Permission.ManageUsers);
        return _repository.DeleteAsync(id, ct);
    }

    private static void Validate(BranchInput n)
    {
        if (string.IsNullOrWhiteSpace(n.Kod))
            throw new ValidationException("Şube kodu zorunludur.");
        if (n.Kod.Length > 32)
            throw new ValidationException("Şube kodu en çok 32 karakter olabilir.");
        if (string.IsNullOrWhiteSpace(n.Ad))
            throw new ValidationException("Şube adı zorunludur.");
    }

    private static BranchInput Normalize(BranchInput input) => new()
    {
        // Kod büyük harfe normalize edilir (benzersizlik tutarlılığı + tipik kullanım).
        Kod = (input.Kod ?? string.Empty).Trim().ToUpperInvariant(),
        Ad = (input.Ad ?? string.Empty).Trim(),
        Adres = Trim(input.Adres),
        Telefon = Trim(input.Telefon),
        Eposta = Trim(input.Eposta),
        Il = Trim(input.Il),
        Ilce = Trim(input.Ilce),
        Yetkili = Trim(input.Yetkili),
        CalismaSaatleri = Trim(input.CalismaSaatleri),
        KomisyonOran = input.KomisyonOran,
        EvrakNoOnek = Trim(input.EvrakNoOnek),
        Aktif = input.Aktif
    };

    private static void Apply(Branch b, BranchInput n)
    {
        b.Kod = n.Kod;
        b.Ad = n.Ad;
        b.Adres = n.Adres;
        b.Telefon = n.Telefon;
        b.Eposta = n.Eposta;
        b.Il = n.Il;
        b.Ilce = n.Ilce;
        b.Yetkili = n.Yetkili;
        b.CalismaSaatleri = n.CalismaSaatleri;
        b.KomisyonOran = n.KomisyonOran;
        b.EvrakNoOnek = n.EvrakNoOnek;
        b.Aktif = n.Aktif;
    }

    private static string? Trim(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
