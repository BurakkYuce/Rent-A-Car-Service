using RentACar.Application.Common;
using RentACar.Application.Integrations;
using RentACar.Domain.Enums;
using Ayar = RentACar.Domain.Entities.TenantSettings;

namespace RentACar.Application.Kabis;

/// <summary>Tenant ayarından KABİS kapısı ve kimliği (şifre yalnız bellekte çözülür).</summary>
public static class KabisCredentials
{
    public const string Missing = "KABİS kullanıcı adı ya da şifresi tanımlı değil (bağlı değil).";
    public const string Unreadable = "KABİS şifresi çözülemedi; Ayarlar > KABİS'ten şifreyi yeniden girin (bağlı değil).";

    public static KabisAuthority ParseAuthority(string? value)
        => Enum.TryParse<KabisAuthority>(value, ignoreCase: true, out var a) && Enum.IsDefined(a) ? a : KabisAuthority.Emniyet;

    public static KabisGate Gate(Ayar? s)
        => s is null || !s.KabisEtkin ? KabisGate.Kapali : new KabisGate(true, s.KabisTeslimiEngelle, ParseAuthority(s.KabisKurum));

    /// <summary>Kimlik çözümü: eksikse (null, neden). Şifre cipher'ı çözülemezse de "bağlı değil" — sahte deneme yapılmaz.</summary>
    public static (KabisKimlik? Kimlik, string? Neden) Resolve(Ayar? s, ISecretProtector secrets)
    {
        if (s is null || string.IsNullOrWhiteSpace(s.KabisKullanici) || string.IsNullOrEmpty(s.KabisSifreEnc))
            return (null, Missing);
        var password = secrets.Unprotect(s.KabisSifreEnc);
        if (string.IsNullOrEmpty(password)) return (null, Unreadable);
        return (new KabisKimlik(ParseAuthority(s.KabisKurum), s.KabisKullanici.Trim(), password), null);
    }
}
