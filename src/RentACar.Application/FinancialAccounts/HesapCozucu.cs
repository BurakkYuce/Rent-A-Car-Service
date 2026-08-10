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
public sealed class HesapCozucu(IFinancialAccountRepository repository)
{
    private readonly IFinancialAccountRepository _repository = repository;

    /// <summary>
    /// <paramref name="hesapId"/> boşsa null (legacy). Doluysa: tenant içinde var mı, aktif mi ve
    /// türü <paramref name="tur"/> ile çelişiyor mu diye bakar; sorun varsa <see cref="ValidationException"/>.
    /// <paramref name="tur"/> null verilirse tür kontrolü ATLANIR (para hangi taraftan geçtiği
    /// belirsiz olan kayıtlar için — ör. açık hesap gideri hesabı yalnız belge notudur).
    /// </summary>
    public async Task<Guid?> CozAsync(Guid? hesapId, LedgerAccountType? tur, CancellationToken ct = default)
    {
        if (hesapId is not { } id || id == Guid.Empty) return null;

        var hesap = await _repository.FindAsync(id, ct)
            ?? throw new ValidationException("Seçilen kasa/banka hesabı bulunamadı.");
        if (!hesap.Aktif)
            throw new ValidationException($"'{hesap.Ad}' hesabı pasif — işlem yapılamaz.");

        // Tür çelişkisi yalnız EMİN olduğumuzda reddedilir: FinancialAccount.Tur serbest metindir
        // ("Kasa"/"Banka"/"POS"/boş olabilir). Çözülemeyen metin YOK SAYILIR — yoksa kullanıcının
        // meşru "Ziraat TL Vadesiz" gibi bir türü uydurma bir hataya dönerdi.
        var cozulen = TuruCoz(hesap.Tur);
        if (tur is { } beklenen && cozulen is { } t && t != beklenen)
            throw new ValidationException(
                $"'{hesap.Ad}' bir {(t == LedgerAccountType.Kasa ? "Kasa" : "Banka")} hesabı; " +
                $"işlem {(beklenen == LedgerAccountType.Kasa ? "Kasa" : "Banka")} olarak seçilmiş.");

        return id;
    }

    /// <summary>
    /// Serbest metin hesap türünü defter türüne çevirir. Tanınmayan/boş metin → null ("bilinmiyor",
    /// çelişki sayılmaz). Saf fonksiyon — ekran ve servis AYNI kuralı kullansın diye public.
    /// </summary>
    public static LedgerAccountType? TuruCoz(string? tur)
    {
        if (string.IsNullOrWhiteSpace(tur)) return null;
        var t = tur.Trim();
        if (t.StartsWith("kasa", StringComparison.OrdinalIgnoreCase)) return LedgerAccountType.Kasa;
        if (t.StartsWith("banka", StringComparison.OrdinalIgnoreCase)) return LedgerAccountType.Banka;
        return null;
    }
}
