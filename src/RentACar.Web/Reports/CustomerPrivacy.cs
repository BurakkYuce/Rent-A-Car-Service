using Microsoft.EntityFrameworkCore;
using RentACar.Application.Customers;
using RentACar.Domain.Entities;
using RentACar.Infrastructure.Persistence;
using RentACar.Web.Api.Cari;

namespace RentACar.Web.Reports;

/// <summary>
/// KVKK — liste export'larında cari kişisel verisinin TEK kuralı; ekrandaki <c>CustomerView</c> /
/// <c>CustomerInputMapper.ToCard</c> ile aynı bayraklar:
/// <list type="bullet">
/// <item><c>AnonimAd</c> → ad yerine sabit etiket (<see cref="CustomerAnonymity.NameLabel"/>).</item>
/// <item><c>AnonimTelefon</c> → cep tel ve GSM2 boş. <c>AnonimMail</c> → e-posta boş.</item>
/// <item><c>AnonimAdres</c> → adres, il, ilçe boş.</item>
/// <item>Bireysel carinin vergi no'su TC olabilir → ekrandaki kartta olduğu gibi yazılmaz (<c>TaxNumberHidden</c>).</item>
/// </list>
/// Kimliği bilinmeyen satır (yalnız ad taşıyan DTO) anonim carilerin görünen ad kümesiyle maskelenir — güvenli yön.
/// <para><b>Çit:</b> <c>ListExportCatalogPrivacyTests</c> cari adı/iletişimi sütunu taşıyan her katalog projeksiyonunun bu
/// tipi parametre olarak aldığını kaynak taramasıyla kilitler.</para>
/// </summary>
public sealed class CustomerPrivacy
{
    public sealed record Flags(bool Name, bool Phone, bool Mail, bool Address);

    private readonly IReadOnlyDictionary<Guid, Flags> _flags;
    private readonly HashSet<string> _anonymousNames;

    public CustomerPrivacy(IReadOnlyDictionary<Guid, Flags> flags, IEnumerable<string> anonymousNames)
    {
        _flags = flags;
        _anonymousNames = anonymousNames.ToHashSet(StringComparer.Ordinal);
    }

    /// <summary>Anonim carisi olmayan küme (saf katalog testleri için).</summary>
    public static CustomerPrivacy None { get; } = new(new Dictionary<Guid, Flags>(), []);

    public static async Task<CustomerPrivacy> LoadAsync(IDbContextFactory<AppDbContext> dbf, CancellationToken ct)
    {
        await using var db = await dbf.CreateDbContextAsync(ct);
        var rows = await db.Customers.AsNoTracking()
            .Where(c => c.AnonimAd || c.AnonimTelefon || c.AnonimMail || c.AnonimAdres)
            .Select(c => new { c.Id, c.Tip, c.Unvan, c.Ad, c.Soyad, c.AnonimAd, c.AnonimTelefon, c.AnonimMail, c.AnonimAdres })
            .ToListAsync(ct);
        return new CustomerPrivacy(
            rows.ToDictionary(c => c.Id, c => new Flags(c.AnonimAd, c.AnonimTelefon, c.AnonimMail, c.AnonimAdres)),
            rows.Where(c => c.AnonimAd)
                .Select(c => new Customer { Tip = c.Tip, Unvan = c.Unvan, Ad = c.Ad, Soyad = c.Soyad }.DisplayName));
    }

    private Flags? Of(Guid? id) => id is { } i && _flags.TryGetValue(i, out var f) ? f : null;

    /// <summary>Görünen ad. Kimlik verilmişse carinin bayrağı, yoksa anonim ad kümesi.</summary>
    public string? Name(Guid? id, string? name)
    {
        if (name is null) return null;
        if (Of(id) is { } f) return f.Name ? CustomerAnonymity.NameLabel : name;
        return _anonymousNames.Contains(name) ? CustomerAnonymity.NameLabel : name;
    }

    public string? Phone(Guid? id, string? phone) => Of(id) is { Phone: true } ? null : phone;

    public string? Email(Guid? id, string? email) => Of(id) is { Mail: true } ? null : email;

    public string? Address(Guid? id, string? value) => Of(id) is { Address: true } ? null : value;

    /// <summary>Cari kartındaki vergi no kuralı (bireysel/TC biçimli → yazılmaz).</summary>
    public static string? TaxNumber(Customer c) => CustomerInputMapper.TaxNumberHidden(c) ? null : c.VergiNo;

    /// <summary>Kimlik → ad çözücüsünü maskeli hâle getirir (FK'den ad çözen export'lar).</summary>
    public Func<Guid, string?> Names(Func<Guid, string?> resolve) => id => Name(id, resolve(id));
}
