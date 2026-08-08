using RentACar.Domain.Entities;

namespace RentACar.Application.Customers;

/// <summary>Dropdown/seçim satırı — PII TAŞIMAZ (yalnız Id + görünen ad).</summary>
public sealed record CariSecim(Guid Id, string Ad);

public interface ICustomerRepository
{
    Task<IReadOnlyList<Customer>> ListAsync(CancellationToken ct = default);

    /// <summary>
    /// Seçim listesi (dropdown). <b>PII ÇÖZMEZ</b> — yalnız Id/Tip/Ünvan/Ad/Soyad kolonlarını okur.
    /// <para>Neden ayrı: <see cref="ListAsync"/> her carinin TC/ehliyet/pasaport cipher'ını çözer;
    /// bir açılır liste için ne gerekli ne de masumdur — anahtar-halkası uyumsuz kayıtlarda her
    /// sayfa açılışında yüzlerce "Cipher çözülemedi" uyarısı üretip logu boğuyordu (FAZ-13 canlı
    /// duman testinde ölçüldü). Personel tarafındaki ListForSelectAsync ile aynı gerekçe.</para>
    /// </summary>
    Task<IReadOnlyList<CariSecim>> ListSecimAsync(CancellationToken ct = default);

    /// <summary>Arama (ad/ünvan/TC/vergi) + sayfalama (liste ekranı).</summary>
    Task<Common.PagedResult<Customer>> SearchAsync(CustomerFilter filter, CancellationToken ct = default);

    /// <summary>Arama/filtre + sayfalama + kira agregaları (adet/ciro/son kira) — CRM liste ekranı.</summary>
    Task<Common.PagedResult<CustomerRow>> SearchRowsAsync(CustomerFilter filter, CancellationToken ct = default);

    Task<Customer?> FindAsync(Guid id, CancellationToken ct = default);

    /// <summary>TC blind-index özeti tenant içinde başka kayıtta var mı? (KVKK/F2 — düz metin yok.)</summary>
    Task<bool> TcKimlikHashExistsAsync(string tcHash, Guid? excludeId = null, CancellationToken ct = default);

    Task<bool> VergiNoExistsAsync(string vergiNo, Guid? excludeId = null, CancellationToken ct = default);

    Task CreateAsync(Customer customer, CancellationToken ct = default);

    Task<bool> UpdateAsync(Guid id, Action<Customer> apply, CancellationToken ct = default);

    Task<bool> DeleteAsync(Guid id, CancellationToken ct = default);
}
