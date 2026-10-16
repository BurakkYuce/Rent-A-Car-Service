using RentACar.Application.Authorization;
using RentACar.Application.Common;
using RentACar.Application.Integrations;
using RentACar.Application.TenantSettings;
using RentACar.Domain.Common;
using RentACar.Domain.Enums;

namespace RentACar.Application.Kabis;

/// <summary>
/// KABİS ayarları (Ayarlar satırının KABİS kolonları) — yalnız <see cref="Permission.ManageUsers"/> + "ayarlar" ekran
/// kapısı (<see cref="TenantSettingsService"/> ile aynı kapı). Şifre at-rest ŞİFRELİ (<see cref="ISecretProtector"/>),
/// görünüm yalnız "tanımlı mı" bayrağını taşır; boş şifre gelirse mevcut korunur. Tam değiştirme kaydı iyimser
/// eşzamanlılıkla (<see cref="ITenantSettingsVersionStore"/>: satır kilidi + sürüm).
/// </summary>
public sealed class KabisSettingsService(
    ITenantSettingsRepository repository, ITenantSettingsVersionStore versions, ISecretProtector secrets,
    ICurrentUser currentUser, ScreenPermissionService screens, IKabisService port)
{
    public const int UserMaxLength = 128;
    public const int PasswordMaxLength = 256;

    public async Task<(KabisSettingsView View, string? Surum)> GetAsync(CancellationToken ct = default)
    {
        await GuardAsync(ct);
        var surum = await versions.VersionAsync(ct); // alanlardan ÖNCE (bayat alan "güncel" görünmesin)
        var s = await repository.GetAsync(ct);
        var view = s is null
            ? new KabisSettingsView(false, KabisAuthority.Emniyet, null, false, false)
            : new KabisSettingsView(s.KabisEtkin, KabisCredentials.ParseAuthority(s.KabisKurum), s.KabisKullanici,
                !string.IsNullOrEmpty(s.KabisSifreEnc), s.KabisTeslimiEngelle);
        return (view, surum);
    }

    public async Task SaveAsync(KabisSettingsInput input, string? expectedVersion, CancellationToken ct = default)
    {
        await GuardAsync(ct);
        if (!Enum.IsDefined(input.Kurum)) throw new ValidationException("Kurum Emniyet ya da Jandarma olmalıdır.", "kurum");
        var user = string.IsNullOrWhiteSpace(input.Kullanici) ? null : input.Kullanici.Trim();
        if (user is { Length: > UserMaxLength })
            throw new ValidationException($"KABİS kullanıcı adı en fazla {UserMaxLength} karakter olabilir.", "kullanici");
        var password = string.IsNullOrWhiteSpace(input.Sifre) ? null : input.Sifre.Trim();
        if (password is { Length: > PasswordMaxLength })
            throw new ValidationException($"KABİS şifresi en fazla {PasswordMaxLength} karakter olabilir.", "sifre");
        if (input.TeslimiEngelle && !input.Etkin)
            throw new ValidationException("Teslimi engelleme yalnız KABİS otomatik bildirimi açıkken seçilebilir.", "teslimiEngelle");

        await versions.UpsertAsync(s =>
        {
            // M3 deseni (TenantSettingsService): sırrın GİTTİĞİ hedef değişirse kayıtlı şifre yeni hedefe taşınmaz.
            var targetChanged =
                !string.Equals(s.KabisKullanici ?? "", user ?? "", StringComparison.OrdinalIgnoreCase)
                || KabisCredentials.ParseAuthority(s.KabisKurum) != input.Kurum;
            if (!string.IsNullOrEmpty(s.KabisSifreEnc) && password is null && !input.SifreTemizle && user is not null && targetChanged)
                throw new ValidationException("KABİS şifresi: kullanıcı ya da kurum değişince şifre yeniden girilmelidir.", "sifre");

            s.KabisEtkin = input.Etkin;
            s.KabisKurum = input.Kurum.ToString();
            s.KabisKullanici = user;
            s.KabisSifreEnc = password is not null ? secrets.Protect(password) : input.SifreTemizle ? null : s.KabisSifreEnc;
            s.KabisTeslimiEngelle = input.TeslimiEngelle;
            s.UpdatedAtUtc = DateTimeOffset.UtcNow;
        }, expectedVersion, ct);
    }

    /// <summary>
    /// Kayıtlı kimlikle bağlantı sınaması (karşı sistemde sahte bir referans SORGULANIR, bildirim yapılmaz).
    /// Dürüst: adaptör ya da kimlik yoksa Ok=false; yalnız karşı sistem yanıt verdiyse Ok=true.
    /// </summary>
    public async Task<KabisConnectionTest> TestConnectionAsync(CancellationToken ct = default)
    {
        await GuardAsync(ct);
        var (credentials, missing) = KabisCredentials.Resolve(await repository.GetAsync(ct), secrets);
        if (credentials is null) return new KabisConnectionTest(false, missing!);
        try
        {
            var r = await port.QueryAsync(credentials, "BAGLANTI-TESTI", ct);
            return r.Tur == KabisSonucTuru.Basarili
                ? new KabisConnectionTest(true, $"KABİS ({credentials.Kurum}) bağlantısı doğrulandı.")
                : new KabisConnectionTest(false,
                    KabisErrorText.Sanitize(r.Hata, [credentials.Sifre]) ?? "KABİS bağlantısı doğrulanamadı.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            return new KabisConnectionTest(false,
                KabisErrorText.Sanitize($"KABİS bağlantısı kurulamadı ({ex.GetType().Name}): {ex.Message}", [credentials.Sifre])!);
        }
    }

    private async Task GuardAsync(CancellationToken ct)
    {
        PermissionGuard.Require(currentUser, Permission.ManageUsers);
        await screens.EnsureScreenAccessAsync("ayarlar", Permission.ManageUsers, ct);
    }
}
