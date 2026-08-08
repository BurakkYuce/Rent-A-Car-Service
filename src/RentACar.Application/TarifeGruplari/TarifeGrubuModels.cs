using RentACar.Domain.Entities;

namespace RentACar.Application.TarifeGruplari;

/// <summary>Tarife grubu oluştur/güncelle girişi. <see cref="Sifre"/> DÜZ gelir, serviste hash'lenir.</summary>
public sealed class TarifeGrubuInput
{
    public string Kod { get; set; } = string.Empty;
    public string Ad { get; set; } = string.Empty;
    public decimal Oran { get; set; }
    public string? KullaniciAdi { get; set; }
    /// <summary>Yeni şifre. BOŞ bırakılırsa mevcut şifre KORUNUR (güncellemede sıfırlanmaz).</summary>
    public string? Sifre { get; set; }
    public bool Aktif { get; set; } = true;
}

public interface ITarifeGrubuRepository
{
    Task<IReadOnlyList<TarifeGrubu>> ListAsync(CancellationToken ct = default);
    Task<IReadOnlyList<TarifeGrubu>> ListActiveAsync(CancellationToken ct = default);
    Task<TarifeGrubu?> FindAsync(Guid id, CancellationToken ct = default);
    Task<bool> KodExistsAsync(string kod, Guid? excludeId = null, CancellationToken ct = default);
    Task CreateAsync(TarifeGrubu row, CancellationToken ct = default);
    Task<bool> UpdateAsync(Guid id, Action<TarifeGrubu> apply, CancellationToken ct = default);
    Task<bool> DeleteAsync(Guid id, CancellationToken ct = default);
}
