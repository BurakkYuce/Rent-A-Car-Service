using System.Text.RegularExpressions;
using RentACar.Application.Integrations;

namespace RentACar.Application.EInvoicing;

/// <summary>
/// Entegratörün AYAR düzeyinde "bağlanabilir" olup olmadığı — saf kural, ağa çıkmaz. Fatura kesilirken (kuyruğa
/// alınsın mı?), ayar ekranında (durum satırı) ve gönderim işinde (her koşu) AYNI kural kullanılır.
///
/// <para>Bağlı değilse fatura YİNE kesilir, e-belge kaydı <c>Taslak</c> kalır ve sebebi yazılır. Sahte ETTN onayı
/// ya da "gönderildi" işareti üretilmez (dürüst stub kuralı).</para>
/// </summary>
public static class EInvoiceReadiness
{
    /// <summary>Kullanıcıya gösterilen "bağlı değil" cümlesinin sabit başı (testler ve ekran bunu arar).</summary>
    public const string NotConnectedPrefix = "e-Fatura gönderilmedi: entegratör bağlı değil";

    public const string BasicScenario = "TEMELFATURA";
    public const string CommercialScenario = "TICARIFATURA";
    public static readonly IReadOnlyList<string> Scenarios = [BasicScenario, CommercialScenario];

    private static readonly Regex AliasPattern = new(@"^urn:mail:\S{1,240}$", RegexOptions.CultureInvariant);
    private static readonly Regex KeyPattern = new("^[a-z0-9-]{1,32}$", RegexOptions.CultureInvariant);

    /// <summary>GİB etiketi (alias) biçimi: <c>urn:mail:…</c>, boşluksuz.</summary>
    public static bool IsValidAlias(string? alias) => alias is not null && AliasPattern.IsMatch(alias);

    /// <summary>Adaptör anahtarı biçimi.</summary>
    public static bool IsValidKey(string? key) => key is not null && KeyPattern.IsMatch(key);

    /// <summary>
    /// Ayarlardaki ilk eksik (null = ayarlar tamam). <paramref name="catalog"/> verilirse seçili entegratörün
    /// adaptörünün bu sürümde kayıtlı olduğu da denetlenir; verilmezse (fatura kesimi transaction'ı, DI yok) yalnız
    /// ayarlar — adaptör yoksa gönderim işi belgeyi aynı sebeple Taslak'a çeker.
    /// </summary>
    public static string? Gap(Domain.Entities.TenantSettings? s, EInvoiceIntegratorCatalog? catalog = null)
    {
        if (s is null || string.IsNullOrWhiteSpace(s.EFaturaEntegrator)) return "entegratör seçilmedi";
        if (catalog is not null && catalog.Find(s.EFaturaEntegrator) is null)
            return "seçili entegratörün adaptörü bu sürümde yok";
        if (string.IsNullOrWhiteSpace(s.EFaturaKullanici)) return "entegratör kullanıcı adı girilmedi";
        if (string.IsNullOrWhiteSpace(s.EFaturaSifreEnc)) return "entegratör şifresi / API anahtarı girilmedi";
        if (string.IsNullOrWhiteSpace(s.FirmaUnvan)) return "firma unvanı girilmedi";
        // Boşluklar yok sayılır ("123 456 7890"); belgeye de yalnız rakamlar yazılır (EDocumentInputFactory.Seller).
        var taxNo = string.Concat((s.FirmaVergiNo ?? string.Empty).Where(c => !char.IsWhiteSpace(c)));
        if (taxNo.Length == 11 && taxNo.All(char.IsAsciiDigit))
            return "şahıs firması (11 haneli satıcı kimliği) henüz desteklenmiyor — yetkili ad/soyad alanı yok";
        if (taxNo.Length != 10 || !taxNo.All(char.IsAsciiDigit)) return "firma vergi kimlik numarası (10 hane) girilmedi";
        if (string.IsNullOrWhiteSpace(s.FirmaIl) || string.IsNullOrWhiteSpace(s.FirmaIlce)) return "firma il/ilçe girilmedi";
        return null;
    }

    /// <summary>"e-Fatura gönderilmedi: entegratör bağlı değil (sebep)."</summary>
    public static string NotConnected(string gap) => $"{NotConnectedPrefix} ({gap}).";

    /// <summary>Alıcı e-Fatura kullanıcısıysa kullanılacak senaryo (varsayılan TEMELFATURA).</summary>
    public static UblProfile EInvoiceProfile(Domain.Entities.TenantSettings s)
        => string.Equals(s.EFaturaSenaryo, CommercialScenario, StringComparison.Ordinal) ? UblProfile.TicariFatura : UblProfile.TemelFatura;
}

/// <summary>
/// Uygulamada kayıtlı entegratör adaptörleri (DI'daki tüm <see cref="IEInvoiceIntegrator"/>). Bu sürümde üretimde
/// HİÇ adaptör yoktur — liste boştur ve her tenant "bağlı değil"dir; gerçek adaptör kimlik bilgisi gelince eklenir.
/// </summary>
public sealed class EInvoiceIntegratorCatalog
{
    private readonly Dictionary<string, IEInvoiceIntegrator> _byKey;

    public EInvoiceIntegratorCatalog(IEnumerable<IEInvoiceIntegrator> integrators)
    {
        _byKey = new Dictionary<string, IEInvoiceIntegrator>(StringComparer.Ordinal);
        foreach (var i in integrators)
        {
            if (!EInvoiceReadiness.IsValidKey(i.Key))
                throw new InvalidOperationException($"Geçersiz entegratör anahtarı: '{i.Key}' (^[a-z0-9-]{{1,32}}$).");
            if (!_byKey.TryAdd(i.Key, i))
                throw new InvalidOperationException($"Entegratör anahtarı iki kez kayıtlı: '{i.Key}'.");
        }
    }

    public IReadOnlyList<IEInvoiceIntegrator> All => [.. _byKey.Values.OrderBy(i => i.Key, StringComparer.Ordinal)];

    public IEInvoiceIntegrator? Find(string? key)
        => key is not null && _byKey.TryGetValue(key, out var i) ? i : null;
}
