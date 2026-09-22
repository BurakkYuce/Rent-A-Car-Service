using RentACar.Domain.Entities;
using RentACar.Domain.Enums;

namespace RentACar.Application.Customers;

/// <summary>Dropdown/seçim satırı — PII TAŞIMAZ (yalnız Id + görünen ad).</summary>
public sealed record CariSecim(Guid Id, string Ad);

/// <summary>F1.6 typeahead satırı — PII TAŞIMAZ (Id + görünen ad + tip). TC/telefon/e-posta/adres YOK.</summary>
public sealed record CariSecimSatiri(Guid Id, string Ad, CariType Tip);

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

    /// <summary>
    /// F1.6 SINIRLI seçim araması (typeahead): <paramref name="katlanmisTerim"/> (bkz. <c>TurkishText.Normalize</c>)
    /// ad/soyad/ünvanda aranır, en çok <paramref name="limit"/> satır döner. PII kolonlarına dokunmaz.
    /// <see cref="ListSecimAsync"/>'ten farkı: tüm cari listesini DEĞİL, sorguya uyan ilk N'i döner.
    /// </summary>
    Task<IReadOnlyList<CariSecimSatiri>> SecimAraAsync(string katlanmisTerim, int limit, CancellationToken ct = default);

    /// <summary>
    /// F4.3b — kimlikle TEK seçim satırı (bağlantıdaki <c>?musteriId=</c> etiketi). <see cref="SecimAraAsync"/> gibi
    /// PII kolonlarına HİÇ dokunmaz (yalnız Id/Tip/Ünvan/Ad/Soyad). Yok/başka kiracı → <c>null</c> (RLS + filtre).
    /// </summary>
    Task<CariSecimSatiri?> SecimGetirAsync(Guid id, CancellationToken ct = default);

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
