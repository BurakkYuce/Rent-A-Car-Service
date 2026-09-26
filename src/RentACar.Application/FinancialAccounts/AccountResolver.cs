using RentACar.Application.Common;
using RentACar.Domain.Enums;

namespace RentACar.Application.FinancialAccounts;

/// <summary>
/// FAZ-50 — <b>hangi SPESİFİK kasa/banka hesabı</b> sorusunun tek çözüm noktası.
///
/// <para>Defterde para bacağının <c>AccountRef</c>'i bugüne kadar Kasa/Banka satırlarında HER ZAMAN
/// null'du: "Kasa"dan geçti biliniyordu ama HANGİ kasadan geçtiği bilinmiyordu. Bu sınıf, forma
/// gelen <c>hesapId</c>'yi doğrulayıp defter satırına yazılacak <c>AccountRef</c>'e çevirir.</para>
///
/// <para><b>Boş geçilebilir (bilinçli):</b> <c>hesapId</c> verilmezse null döner ve eski davranış
/// aynen sürer (legacy kova). Zorunlu kılmak, hesap tanımı olmayan tenant'ların tahsilat yapamaz
/// hâle gelmesi demekti.</para>
///
/// <para><b>Tenant güvenliği:</b> arama tenant-filtreli repo üzerinden yapılır — başka tenant'ın
/// hesap kimliği uydurulup gönderilse bile bulunamaz ve gürültülü red alır (sessizce null'a
/// düşürmek, parayı yanlış kovaya yazardı).</para>
/// </summary>
public sealed class AccountResolver(IFinancialAccountRepository repository)
{
    private readonly IFinancialAccountRepository _repository = repository;

    /// <summary>
    /// <paramref name="accountId"/> boşsa null (legacy). Doluysa: tenant içinde var mı, aktif mi ve
    /// türü <paramref name="type"/> ile çelişiyor mu diye bakar; sorun varsa <see cref="ValidationException"/>.
    /// <paramref name="type"/> null verilirse tür kontrolü ATLANIR (para hangi taraftan geçtiği
    /// belirsiz olan kayıtlar için — ör. açık hesap gideri hesabı yalnız belge notudur).
    /// </summary>
    public async Task<Guid?> ResolveAsync(
        Guid? accountId, LedgerAccountType? type, CancellationToken ct = default, string? currency = null)
    {
        if (accountId is not { } id || id == Guid.Empty) return null;

        var account = await _repository.FindAsync(id, ct)
            ?? throw new ValidationException("Seçilen kasa/banka hesabı bulunamadı.");
        if (!account.Aktif)
            throw new ValidationException($"'{account.Ad}' hesabı pasif — işlem yapılamaz.");

        // ADVERSARIAL H1 — tür ÇÖZÜLEBİLİR OLMAK ZORUNDA. Önce çözülemeyen metin ("POS", boş)
        // yok sayılıyordu; sonuç: AYNI hesap hem Kasa hem Banka bacağında kullanılabiliyor ve
        // hesap-bazlı özet onu İKİ AYRI satıra bölüyordu (100 Kasa + 200 Banka), birleşik bakiyeyi
        // hiçbir ekran göstermiyordu. Artık tür belirsizse işlem gürültülü reddedilir; tanım
        // formu da Kasa/Banka seçimini zorunlu kılar (FinancialAccountService.Validate).
        var resolved = ResolveType(account.Tur)
            ?? throw new ValidationException(
                $"'{account.Ad}' hesabının türü belirsiz. Hesap tanımında türü Kasa ya da Banka olarak seçin.");
        if (type is { } expected && resolved != expected)
            throw new ValidationException(
                $"'{account.Ad}' bir {(resolved == LedgerAccountType.Kasa ? "Kasa" : "Banka")} hesabı; " +
                $"işlem {(expected == LedgerAccountType.Kasa ? "Kasa" : "Banka")} olarak seçilmiş.");

        // ADVERSARIAL M4 — hesabın kendi dövizi ile işlem dövizi çelişkisi. Hesap-bazlı bakiye
        // gerçek banka ekstresiyle mutabakat için kullanılacak; USD hesaba TRY yazmak o mutabakatı
        // anlamsız kılar. Hesabın dövizi TANIMSIZSA karışmayız (eski hesaplar).
        // F6.1b adversarial L2: iki taraf da KurService.NormalizeKod'dan geçer — "TRL"/"TL" tanımlı hesap TRY
        // işlemini (ya da tersi) çelişki sanıp reddediyordu.
        if (currency is { Length: > 0 } d && !string.IsNullOrWhiteSpace(account.Doviz)
            && !string.Equals(Kur.ExchangeRateService.NormalizeCode(account.Doviz), Kur.ExchangeRateService.NormalizeCode(d), StringComparison.Ordinal))
            throw new ValidationException(
                $"'{account.Ad}' hesabı {account.Doviz} tanımlı; işlem {d.Trim().ToUpperInvariant()} olarak giriliyor.");

        return id;
    }

    /// <summary>
    /// Serbest metin hesap türünü defter türüne çevirir. Tanınmayan/boş metin → null ("bilinmiyor",
    /// çelişki sayılmaz). Saf fonksiyon — ekran ve servis AYNI kuralı kullansın diye public.
    /// </summary>
    public static LedgerAccountType? ResolveType(string? type)
    {
        if (string.IsNullOrWhiteSpace(type)) return null;
        var t = type.Trim();
        // ADVERSARIAL L1 — "Banka Kasası" gerçekte bir KASA'dır; salt ön ek bakışı onu Banka
        // sanıp meşru işlemi reddediyordu. Metin "kasa" ile BİTİYORSA kasa kazanır.
        if (t.EndsWith("kasa", StringComparison.OrdinalIgnoreCase)
            || t.EndsWith("kasası", StringComparison.OrdinalIgnoreCase)
            || t.EndsWith("kasasi", StringComparison.OrdinalIgnoreCase)) return LedgerAccountType.Kasa;
        if (t.StartsWith("kasa", StringComparison.OrdinalIgnoreCase)) return LedgerAccountType.Kasa;
        if (t.StartsWith("banka", StringComparison.OrdinalIgnoreCase)) return LedgerAccountType.Banka;
        return null;
    }
}
