using RentACar.Domain.Entities;
using RentACar.Domain.Enums;

namespace RentACar.Application.Customers;

/// <summary>Dropdown/seçim satırı — PII TAŞIMAZ (yalnız Id + görünen ad).</summary>
public sealed record CariSecim(Guid Id, string Ad);

/// <summary>F1.6 typeahead satırı — PII TAŞIMAZ (Id + görünen ad + tip). TC/telefon/e-posta/adres YOK.
/// <c>AnonimAd</c>: KVKK bayrağı; <c>Ad</c> ham görünen addır, maskeyi TÜKETİCİ uygular
/// (<see cref="CustomerAnonymity.Name"/>; Web yüzeyinde <c>MusteriGorunumu</c> aynı sabiti kullanır).</summary>
public sealed record CariSecimSatiri(Guid Id, string Ad, CustomerType Tip, bool AnonimAd = false);

/// <summary>
/// KVKK <c>Customer.AnonimAd</c> görüntü kuralının Application katmanındaki TEK sabiti. Web'deki
/// <c>Api/Kira/MusteriGorunumu.AnonimAdEtiketi</c> bu sabite bağlıdır (iki ayrı metin olamaz); Application
/// servisleri (ör. <c>SecimService</c>) Web'e bağımlı olamadığı için sabit burada durur.
/// </summary>
public static class CustomerAnonymity
{
    /// <summary>Adı anonimleştirilmiş carinin her yüzeydeki görünen adı.</summary>
    public const string NameLabel = "Anonim müşteri";

    /// <summary>Anonimse sabit etiket, değilse verilen ad.</summary>
    public static string Name(string name, bool anonymousName) => anonymousName ? NameLabel : name;
}

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
    Task<IReadOnlyList<CariSecim>> ListForSelectAsync(CancellationToken ct = default);

    /// <summary>
    /// F1.6 SINIRLI seçim araması (typeahead): <paramref name="foldedTerm"/> (bkz. <c>TurkishText.Normalize</c>)
    /// ad/soyad/ünvanda aranır, en çok <paramref name="limit"/> satır döner. PII kolonlarına dokunmaz.
    /// <see cref="ListForSelectAsync"/>'ten farkı: tüm cari listesini DEĞİL, sorguya uyan ilk N'i döner.
    /// </summary>
    Task<IReadOnlyList<CariSecimSatiri>> SearchSelectionAsync(string foldedTerm, int limit, CancellationToken ct = default);

    /// <summary>
    /// F4.3b — kimlikle TEK seçim satırı (bağlantıdaki <c>?musteriId=</c> etiketi). <see cref="SearchSelectionAsync"/> gibi
    /// PII kolonlarına HİÇ dokunmaz (yalnız Id/Tip/Ünvan/Ad/Soyad). Yok/başka kiracı → <c>null</c> (RLS + filtre).
    /// </summary>
    Task<CariSecimSatiri?> GetSelectionAsync(Guid id, CancellationToken ct = default);

    /// <summary>Arama (ad/ünvan/TC/vergi) + sayfalama (liste ekranı).</summary>
    Task<Common.PagedResult<Customer>> SearchAsync(CustomerFilter filter, CancellationToken ct = default);

    /// <summary>Arama/filtre + sayfalama + kira agregaları (adet/ciro/son kira) — CRM liste ekranı.</summary>
    Task<Common.PagedResult<CustomerRow>> SearchRowsAsync(CustomerFilter filter, CancellationToken ct = default);

    Task<Customer?> FindAsync(Guid id, CancellationToken ct = default);

    /// <summary>Verilen kimliklerden kiracıda GERÇEKTEN var olanlar (tenant filtresi + RLS; başka kiracının
    /// ya da hiç olmayan kimlik sonuçta yer almaz). PII çözmez — yalnız varlık denetimi (toplu tahsilat/ödeme).</summary>
    Task<IReadOnlySet<Guid>> ExistingIdsAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct = default);

    /// <summary>TC blind-index özeti tenant içinde başka kayıtta var mı? (KVKK/F2 — düz metin yok.)</summary>
    Task<bool> NationalIdHashExistsAsync(string nationalIdHash, Guid? excludeId = null, CancellationToken ct = default);

    Task<bool> TaxNoExistsAsync(string taxNo, Guid? excludeId = null, CancellationToken ct = default);

    Task CreateAsync(Customer customer, CancellationToken ct = default);

    Task<bool> UpdateAsync(Guid id, Action<Customer> apply, CancellationToken ct = default);

    /// <summary>F7.1 — satır kilidi + iyimser sürüm karşılaştırması (<paramref name="expectedVersion"/> null → yalnız
    /// kilit). Sürüm farklı → <see cref="Common.ConcurrentModificationException"/>, hiçbir şey yazılmaz.</summary>
    Task<bool> UpdateAsync(Guid id, string? expectedVersion, Action<Customer> apply, CancellationToken ct = default);

    /// <summary>F7.1 — satır sürümü (opak); yok/başka kiracı → null.</summary>
    Task<string?> GetVersionAsync(Guid id, CancellationToken ct = default);

    Task<bool> DeleteAsync(Guid id, CancellationToken ct = default);
}
